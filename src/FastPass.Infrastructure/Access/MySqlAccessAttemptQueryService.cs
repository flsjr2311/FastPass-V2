using FastPass.Application.Access;
using FastPass.Domain.Enums;
using FastPass.Infrastructure.Database;
using MySqlConnector;

namespace FastPass.Infrastructure.Access;

public sealed class MySqlAccessAttemptQueryService : IAccessAttemptQueryService
{
    private const string FromSql = """
        FROM fp_access_attempts a
        LEFT JOIN fp_tickets t ON t.id = a.ticket_id
        LEFT JOIN fp_staff_credentials c ON c.id = a.staff_credential_id
        LEFT JOIN fp_staff_members s ON s.id = c.staff_member_id
        LEFT JOIN fp_gates g ON g.id = a.gate_id
        LEFT JOIN fp_sectors sec ON sec.id = a.sector_id
        LEFT JOIN fp_sectors tsec ON tsec.id = t.sector_id
        LEFT JOIN fp_devices d ON d.id = a.device_id
        """;

    private readonly FastPassDbConnectionFactory _connectionFactory;

    public MySqlAccessAttemptQueryService(FastPassDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AccessAttemptPage> ListAsync(
        Guid eventId,
        AccessAttemptAuditFilter filter,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(eventId, filter);
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        await using var countCommand = CreateCommand(
            connection,
            $"SELECT COUNT(*) {FromSql} WHERE {WhereSql}",
            normalized);
        var total = Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken));

        await using var dataCommand = CreateCommand(
            connection,
            $"""
            SELECT a.id, a.event_id, a.ticket_id, a.staff_credential_id, c.staff_member_id,
                   a.gate_id, a.sector_id, a.device_id, a.credential_type, a.direction, a.decision,
                   a.reason, a.status, a.credential_code, t.external_id, s.name,
                   g.name, sec.name, d.name, a.requested_at, a.created_at, tsec.name
            {FromSql}
            WHERE {WhereSql}
            ORDER BY a.requested_at DESC, a.id DESC
            LIMIT @limit OFFSET @offset;
            """,
            normalized);
        dataCommand.Parameters.AddWithValue("@limit", normalized.PageSize);
        dataCommand.Parameters.AddWithValue("@offset", (normalized.Page - 1) * normalized.PageSize);

        var data = new List<AccessAttemptAuditView>();
        await using var reader = await dataCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            data.Add(new AccessAttemptAuditView(
                ReadGuid(reader, 0),
                ReadGuid(reader, 1),
                ReadNullableGuid(reader, 2),
                ReadNullableGuid(reader, 3),
                ReadNullableGuid(reader, 4),
                ReadGuid(reader, 5),
                ReadNullableGuid(reader, 6),
                ReadNullableGuid(reader, 7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.GetString(12),
                MaskCredentialCode(reader.GetString(13)),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.IsDBNull(17) ? null : reader.GetString(17),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                ReadDateTimeOffset(reader, 19),
                ReadDateTimeOffset(reader, 20),
                reader.IsDBNull(21) ? null : reader.GetString(21)));
        }

        return new AccessAttemptPage(
            data,
            normalized.Page,
            normalized.PageSize,
            total,
            (long)normalized.Page * normalized.PageSize < total);
    }

    public async Task<AccessAttemptSummaryView> SummaryAsync(
        Guid eventId,
        AccessAttemptAuditFilter filter,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(eventId, filter);
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        await using var totalsCommand = CreateCommand(
            connection,
            $"""
            SELECT COUNT(*),
                   COALESCE(SUM(CASE WHEN a.decision = 'Approved' THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN a.decision = 'Rejected' THEN 1 ELSE 0 END), 0)
            {FromSql}
            WHERE {WhereSql};
            """,
            normalized);
        await using var totalsReader = await totalsCommand.ExecuteReaderAsync(cancellationToken);
        await totalsReader.ReadAsync(cancellationToken);
        var total = Convert.ToInt64(totalsReader.GetValue(0));
        var approved = Convert.ToInt64(totalsReader.GetValue(1));
        var rejected = Convert.ToInt64(totalsReader.GetValue(2));
        await totalsReader.DisposeAsync();

        var byCredentialType = await GroupAsync(connection, normalized, "a.credential_type", cancellationToken);
        var byDirection = await GroupAsync(connection, normalized, "a.direction", cancellationToken);
        var byDecision = await GroupAsync(connection, normalized, "a.decision", cancellationToken);
        var byGate = await GroupAsync(connection, normalized, "COALESCE(g.name, a.gate_id)", cancellationToken);
        var byReason = await GroupAsync(connection, normalized, "COALESCE(NULLIF(a.reason, ''), '(none)')", cancellationToken);
        var bySector = await GroupAsync(connection, normalized, "COALESCE(sec.name, a.sector_id)", cancellationToken);

        return new AccessAttemptSummaryView(
            eventId,
            normalized.From,
            normalized.To,
            total,
            approved,
            rejected,
            total == 0 ? 0 : approved * 100d / total,
            byCredentialType,
            byDirection,
            byDecision,
            byGate,
            byReason,
            bySector);
    }

    private static async Task<IReadOnlyList<AccessAttemptCountView>> GroupAsync(
        MySqlConnection connection,
        NormalizedFilter filter,
        string expression,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            $"""
            SELECT {expression} AS grouping_key, COUNT(*) AS grouping_count
            {FromSql}
            WHERE {WhereSql}
            GROUP BY {expression}
            ORDER BY grouping_count DESC, grouping_key;
            """,
            filter);

        var result = new List<AccessAttemptCountView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AccessAttemptCountView(
                reader.IsDBNull(0) ? "(none)" : reader.GetString(0),
                Convert.ToInt64(reader.GetValue(1))));
        }

        return result;
    }

    private static MySqlCommand CreateCommand(
        MySqlConnection connection,
        string commandText,
        NormalizedFilter filter)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddWithValue("@event_id", filter.EventId.ToString());
        command.Parameters.AddWithValue("@from", (object?)filter.From?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("@to", (object?)filter.To?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("@gate_id", NullableGuid(filter.GateId));
        command.Parameters.AddWithValue("@sector_id", NullableGuid(filter.SectorId));
        command.Parameters.AddWithValue("@device_id", NullableGuid(filter.DeviceId));
        command.Parameters.AddWithValue("@ticket_id", NullableGuid(filter.TicketId));
        command.Parameters.AddWithValue("@staff_credential_id", NullableGuid(filter.StaffCredentialId));
        command.Parameters.AddWithValue("@credential_type", (object?)filter.CredentialType ?? DBNull.Value);
        command.Parameters.AddWithValue("@direction", (object?)filter.Direction ?? DBNull.Value);
        command.Parameters.AddWithValue("@decision", (object?)filter.Decision ?? DBNull.Value);
        command.Parameters.AddWithValue("@status", (object?)filter.Status ?? DBNull.Value);
        return command;
    }

    private static string WhereSql => """
        a.event_id = @event_id
        AND (@from IS NULL OR a.requested_at >= @from)
        AND (@to IS NULL OR a.requested_at < @to)
        AND (@gate_id IS NULL OR a.gate_id = @gate_id)
        AND (@sector_id IS NULL OR a.sector_id = @sector_id)
        AND (@device_id IS NULL OR a.device_id = @device_id)
        AND (@ticket_id IS NULL OR a.ticket_id = @ticket_id)
        AND (@staff_credential_id IS NULL OR a.staff_credential_id = @staff_credential_id)
        AND (@credential_type IS NULL OR a.credential_type = @credential_type)
        AND (@direction IS NULL OR a.direction = @direction)
        AND (@decision IS NULL OR a.decision = @decision)
        AND (@status IS NULL OR a.status = @status)
        """;

    private static NormalizedFilter Normalize(Guid eventId, AccessAttemptAuditFilter filter)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("EventId é obrigatório.");
        }

        if (filter.Page < 1 || filter.PageSize < 1 || filter.PageSize > 200)
        {
            throw new ArgumentException("Page deve ser maior que zero e PageSize deve estar entre 1 e 200.");
        }

        var from = filter.From?.ToUniversalTime();
        var to = filter.To?.ToUniversalTime();
        if (from.HasValue && to.HasValue && from >= to)
        {
            throw new ArgumentException("From deve ser anterior a To.");
        }

        ValidateOptionalGuid(filter.GateId, nameof(filter.GateId));
        ValidateOptionalGuid(filter.SectorId, nameof(filter.SectorId));
        ValidateOptionalGuid(filter.DeviceId, nameof(filter.DeviceId));
        ValidateOptionalGuid(filter.TicketId, nameof(filter.TicketId));
        ValidateOptionalGuid(filter.StaffCredentialId, nameof(filter.StaffCredentialId));

        return new NormalizedFilter(
            eventId,
            from,
            to,
            filter.GateId,
            filter.SectorId,
            filter.DeviceId,
            filter.TicketId,
            filter.StaffCredentialId,
            NormalizeEnum(filter.CredentialType, "Ticket", "StaffBadge"),
            NormalizeEnum(filter.Direction, "Entry", "Exit"),
            NormalizeEnum(filter.Decision, "Approved", "Rejected"),
            NormalizeEnum(filter.Status, "Decided", "CommandPending", "DeviceConfirmed", "DeviceFailed", "SyncPending", "Reconciled"),
            filter.Page,
            filter.PageSize);
    }

    private static string? NormalizeEnum(string? value, params string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = allowed.FirstOrDefault(item => string.Equals(item, value.Trim(), StringComparison.OrdinalIgnoreCase));
        if (normalized is null)
        {
            throw new ArgumentException($"Valor de filtro inválido: {value}.");
        }

        return normalized;
    }

    private static void ValidateOptionalGuid(Guid? value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{name} inválido.");
        }
    }

    private static object NullableGuid(Guid? value) => (object?)value?.ToString() ?? DBNull.Value;

    private static string MaskCredentialCode(string value)
    {
        if (value.Length <= 4)
        {
            return new string('*', value.Length);
        }

        return $"{value[..2]}{new string('*', value.Length - 4)}{value[^2..]}";
    }

    private static Guid ReadGuid(MySqlDataReader reader, int ordinal) => Guid.Parse(reader.GetValue(ordinal).ToString()!);

    private static Guid? ReadNullableGuid(MySqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ReadGuid(reader, ordinal);

    private static DateTimeOffset ReadDateTimeOffset(MySqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private sealed record NormalizedFilter(
        Guid EventId,
        DateTimeOffset? From,
        DateTimeOffset? To,
        Guid? GateId,
        Guid? SectorId,
        Guid? DeviceId,
        Guid? TicketId,
        Guid? StaffCredentialId,
        string? CredentialType,
        string? Direction,
        string? Decision,
        string? Status,
        int Page,
        int PageSize);
}
