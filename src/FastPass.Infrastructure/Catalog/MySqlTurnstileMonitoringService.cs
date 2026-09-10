using FastPass.Application.Catalog;
using FastPass.Infrastructure.Database;
using MySqlConnector;

namespace FastPass.Infrastructure.Catalog;

/// <summary>
/// Presença/monitoramento das catracas MQTT (tabela fp_turnstile_presence),
/// cruzada com o cadastro em fp_devices e a portaria/evento atribuídos.
/// </summary>
public sealed class MySqlTurnstileMonitoringService : ITurnstileMonitoringService
{
    private readonly FastPassDbConnectionFactory _connectionFactory;

    public MySqlTurnstileMonitoringService(FastPassDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task RecordPresenceAsync(
        TurnstilePresenceUpdate update,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.DeviceId)) return;

        var now = DateTime.UtcNow;
        var status = string.IsNullOrWhiteSpace(update.Status)
            ? "connected"
            : update.Status!.Trim().ToLowerInvariant();

        // Uma mensagem de "disconnected" é o Last Will do broker: a placa CAIU.
        // Nesse caso marcamos o status como desconectado, mas NÃO renovamos o
        // last_seen_at (senão a catraca "morta" apareceria como online).
        var isDisconnect = status == "disconnected";

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        // Upsert: cria na primeira vez, atualiza depois. Campos de metadado só
        // sobrescrevem quando vêm preenchidos (COALESCE mantém o último conhecido).
        // last_seen_at só avança em mensagens de "vida" (não em disconnect).
        command.CommandText = """
            INSERT INTO fp_turnstile_presence
                (device_id, status, first_seen_at, last_seen_at, firmware, board_id, serial_id, ip_local, media)
            VALUES
                (@id, @status, @now, @now, @firmware, @board, @serial, @ip, @media)
            ON DUPLICATE KEY UPDATE
                status = @status,
                last_seen_at = IF(@is_disconnect = 1, last_seen_at, @now),
                firmware = COALESCE(@firmware, firmware),
                board_id = COALESCE(@board, board_id),
                serial_id = COALESCE(@serial, serial_id),
                ip_local = COALESCE(@ip, ip_local),
                media = COALESCE(@media, media);
            """;
        command.Parameters.AddWithValue("@id", update.DeviceId.Trim());
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@now", now);
        command.Parameters.AddWithValue("@is_disconnect", isDisconnect ? 1 : 0);
        command.Parameters.AddWithValue("@firmware", (object?)Trim(update.Firmware) ?? DBNull.Value);
        command.Parameters.AddWithValue("@board", (object?)Trim(update.BoardId) ?? DBNull.Value);
        command.Parameters.AddWithValue("@serial", (object?)Trim(update.SerialId) ?? DBNull.Value);
        command.Parameters.AddWithValue("@ip", (object?)Trim(update.IpLocal) ?? DBNull.Value);
        command.Parameters.AddWithValue("@media", (object?)Trim(update.Media) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordMetadataAsync(
        TurnstilePresenceUpdate update,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.DeviceId)) return;

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Só atualiza metadados de um registro existente; não cria, não toca status/last_seen.
        command.CommandText = """
            UPDATE fp_turnstile_presence
            SET firmware = COALESCE(@firmware, firmware),
                board_id = COALESCE(@board, board_id),
                serial_id = COALESCE(@serial, serial_id),
                ip_local = COALESCE(@ip, ip_local),
                media = COALESCE(@media, media)
            WHERE device_id = @id;
            """;
        command.Parameters.AddWithValue("@id", update.DeviceId.Trim());
        command.Parameters.AddWithValue("@firmware", (object?)Trim(update.Firmware) ?? DBNull.Value);
        command.Parameters.AddWithValue("@board", (object?)Trim(update.BoardId) ?? DBNull.Value);
        command.Parameters.AddWithValue("@serial", (object?)Trim(update.SerialId) ?? DBNull.Value);
        command.Parameters.AddWithValue("@ip", (object?)Trim(update.IpLocal) ?? DBNull.Value);
        command.Parameters.AddWithValue("@media", (object?)Trim(update.Media) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TurnstileMonitorView>> ListAsync(
        int onlineWindowSeconds,
        CancellationToken cancellationToken = default)
    {
        if (onlineWindowSeconds < 5) onlineWindowSeconds = 90;

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        // Cruza a presença (por device_id) com o cadastro (fp_devices.identifier),
        // trazendo portaria e — quando associada a um evento — o evento.
        // Prioriza o evento em andamento (Running) / mais recente, igual à resolução do Worker.
        // Filtra catracas offline há mais de 24 horas (não aparecem na lista).
        const int offlineThresholdSeconds = 86400; // 24 horas
        command.CommandText = """
            SELECT p.device_id, p.status, p.first_seen_at, p.last_seen_at,
                   (p.status <> 'disconnected'
                     AND p.last_seen_at >= (UTC_TIMESTAMP() - INTERVAL @win SECOND)) AS online,
                   p.firmware, p.board_id, p.serial_id, p.ip_local, p.media,
                   d.id, d.name, d.active, d.gate_id,
                   g.name, e.id, e.name
            FROM fp_turnstile_presence p
            LEFT JOIN fp_devices d ON d.identifier = p.device_id
            LEFT JOIN fp_gates g ON g.id = d.gate_id
            LEFT JOIN fp_event_gates eg ON eg.gate_id = d.gate_id AND eg.active = 1
            LEFT JOIN fp_events e ON e.id = eg.event_id
            WHERE p.last_seen_at >= (UTC_TIMESTAMP() - INTERVAL @offline_threshold SECOND)
            ORDER BY online DESC, p.last_seen_at DESC, p.device_id;
            """;
        command.Parameters.AddWithValue("@win", onlineWindowSeconds);
        command.Parameters.AddWithValue("@offline_threshold", offlineThresholdSeconds);

        var result = new List<TurnstileMonitorView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new TurnstileMonitorView(
                DeviceId: reader.GetString(0),
                Status: reader.GetString(1),
                FirstSeenAt: ReadDto(reader, 2),
                LastSeenAt: ReadDto(reader, 3),
                Online: Convert.ToInt64(reader.GetValue(4)) != 0,
                Firmware: reader.IsDBNull(5) ? null : reader.GetString(5),
                BoardId: reader.IsDBNull(6) ? null : reader.GetString(6),
                SerialId: reader.IsDBNull(7) ? null : reader.GetString(7),
                IpLocal: reader.IsDBNull(8) ? null : reader.GetString(8),
                Media: reader.IsDBNull(9) ? null : reader.GetString(9),
                DeviceRegistrationId: reader.IsDBNull(10) ? null : Guid.Parse(reader.GetValue(10).ToString()!),
                DeviceName: reader.IsDBNull(11) ? null : reader.GetString(11),
                DeviceActive: reader.IsDBNull(12) ? null : Convert.ToBoolean(reader.GetValue(12)),
                GateId: reader.IsDBNull(13) ? null : Guid.Parse(reader.GetValue(13).ToString()!),
                GateName: reader.IsDBNull(14) ? null : reader.GetString(14),
                EventId: reader.IsDBNull(15) ? null : Guid.Parse(reader.GetValue(15).ToString()!),
                EventName: reader.IsDBNull(16) ? null : reader.GetString(16)));
        }
        return result;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static DateTimeOffset ReadDto(MySqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
}
