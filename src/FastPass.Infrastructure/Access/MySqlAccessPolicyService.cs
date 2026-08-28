using System.Text.Json;
using FastPass.Application.Access;
using FastPass.Infrastructure.Database;
using MySqlConnector;

namespace FastPass.Infrastructure.Access;

public sealed class MySqlAccessPolicyService : IAccessPolicyService
{
    private readonly FastPassDbConnectionFactory _connectionFactory;

    public MySqlAccessPolicyService(FastPassDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AccessPolicyView> CreateAsync(
        Guid eventId,
        CreateAccessPolicyCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateIds(eventId, command);
        var direction = ParseDirection(command.Direction);
        var conditionsJson = ValidateConditions(command.ConditionsJson);
        var policyId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            if (!await ExistsAsync(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM fp_events WHERE id = @event_id;",
                    cancellationToken,
                    ("@event_id", eventId.ToString())))
            {
                throw new ArgumentException("Evento não encontrado.");
            }

            if (command.BatchId.HasValue && !command.TicketTypeId.HasValue)
            {
                throw new ArgumentException("TicketTypeId é obrigatório quando BatchId é informado.");
            }

            if (command.TicketTypeId.HasValue && !await ExistsAsync(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM fp_ticket_types WHERE id = @type_id AND event_id = @event_id AND active = 1;",
                    cancellationToken,
                    ("@type_id", command.TicketTypeId.Value.ToString()),
                    ("@event_id", eventId.ToString())))
            {
                throw new ArgumentException("TicketTypeId não encontrado, inativo ou incompatível com o evento.");
            }

            if (command.BatchId.HasValue && !await ExistsAsync(
                    connection,
                    transaction,
                    """
                    SELECT COUNT(*)
                    FROM fp_ticket_batches
                    WHERE id = @batch_id
                      AND event_id = @event_id
                      AND ticket_type_id = @type_id;
                    """,
                    cancellationToken,
                    ("@batch_id", command.BatchId.Value.ToString()),
                    ("@event_id", eventId.ToString()),
                    ("@type_id", command.TicketTypeId!.Value.ToString())))
            {
                throw new ArgumentException("BatchId não encontrado ou incompatível com evento/tipo.");
            }

            if (command.GateId.HasValue && !await ExistsAsync(
                    connection,
                    transaction,
                    """
                    SELECT COUNT(*)
                    FROM fp_event_gates eg
                    INNER JOIN fp_gates g ON g.id = eg.gate_id
                    WHERE eg.event_id = @event_id
                      AND eg.gate_id = @gate_id
                      AND eg.active = 1
                      AND g.active = 1;
                    """,
                    cancellationToken,
                    ("@event_id", eventId.ToString()),
                    ("@gate_id", command.GateId.Value.ToString())))
            {
                throw new ArgumentException("GateId não é uma portaria ativa associada ao evento.");
            }

            if (command.SectorId.HasValue && !await ExistsAsync(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM fp_sectors WHERE id = @sector_id AND event_id = @event_id AND active = 1;",
                    cancellationToken,
                    ("@sector_id", command.SectorId.Value.ToString()),
                    ("@event_id", eventId.ToString())))
            {
                throw new ArgumentException("SectorId não encontrado, inativo ou incompatível com o evento.");
            }

            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO fp_access_policies
                    (id, event_id, ticket_type_id, batch_id, gate_id, sector_id, direction,
                     priority, allows, active, conditions_json, created_at, updated_at)
                VALUES
                    (@id, @event_id, @ticket_type_id, @batch_id, @gate_id, @sector_id, @direction,
                     @priority, @allows, @active, @conditions_json, @created_at, @updated_at);
                """,
                cancellationToken,
                ("@id", policyId.ToString()),
                ("@event_id", eventId.ToString()),
                ("@ticket_type_id", NullableGuid(command.TicketTypeId)),
                ("@batch_id", NullableGuid(command.BatchId)),
                ("@gate_id", NullableGuid(command.GateId)),
                ("@sector_id", NullableGuid(command.SectorId)),
                ("@direction", direction),
                ("@priority", command.Priority),
                ("@allows", command.Allows ? 1 : 0),
                ("@active", command.Active ? 1 : 0),
                ("@conditions_json", (object?)conditionsJson ?? DBNull.Value),
                ("@created_at", now),
                ("@updated_at", now));

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return new AccessPolicyView(
            policyId,
            eventId,
            command.TicketTypeId,
            command.BatchId,
            command.GateId,
            command.SectorId,
            direction,
            command.Priority,
            command.Allows,
            command.Active,
            conditionsJson,
            new DateTimeOffset(now, TimeSpan.Zero),
            new DateTimeOffset(now, TimeSpan.Zero));
    }

    public async Task<IReadOnlyList<AccessPolicyView>> ListAsync(
        Guid eventId,
        Guid? ticketTypeId,
        Guid? batchId,
        Guid? gateId,
        Guid? sectorId,
        string? direction,
        bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty || ticketTypeId == Guid.Empty || batchId == Guid.Empty || gateId == Guid.Empty || sectorId == Guid.Empty)
        {
            throw new ArgumentException("EventId e filtros de política devem ser UUIDs válidos.");
        }

        var normalizedDirection = string.IsNullOrWhiteSpace(direction) ? null : ParseDirection(direction);
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, ticket_type_id, batch_id, gate_id, sector_id, direction,
                   priority, allows, active, conditions_json, created_at, updated_at
            FROM fp_access_policies
            WHERE event_id = @event_id
              AND (@ticket_type_id IS NULL OR ticket_type_id = @ticket_type_id)
              AND (@batch_id IS NULL OR batch_id = @batch_id)
              AND (@gate_id IS NULL OR gate_id = @gate_id)
              AND (@sector_id IS NULL OR sector_id = @sector_id)
              AND (@direction IS NULL OR direction = @direction)
              AND (@active_only = 0 OR active = 1)
            ORDER BY priority DESC, created_at DESC, id;
            """;
        command.Parameters.AddWithValue("@event_id", eventId.ToString());
        command.Parameters.AddWithValue("@ticket_type_id", NullableGuid(ticketTypeId));
        command.Parameters.AddWithValue("@batch_id", NullableGuid(batchId));
        command.Parameters.AddWithValue("@gate_id", NullableGuid(gateId));
        command.Parameters.AddWithValue("@sector_id", NullableGuid(sectorId));
        command.Parameters.AddWithValue("@direction", (object?)normalizedDirection ?? DBNull.Value);
        command.Parameters.AddWithValue("@active_only", activeOnly ? 1 : 0);

        var result = new List<AccessPolicyView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AccessPolicyView(
                ReadGuid(reader, 0),
                eventId,
                ReadNullableGuid(reader, 1),
                ReadNullableGuid(reader, 2),
                ReadNullableGuid(reader, 3),
                ReadNullableGuid(reader, 4),
                reader.GetString(5),
                Convert.ToInt32(reader.GetValue(6)),
                Convert.ToBoolean(reader.GetValue(7)),
                Convert.ToBoolean(reader.GetValue(8)),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                ReadDateTimeOffset(reader, 10),
                ReadDateTimeOffset(reader, 11)));
        }

        return result;
    }

    private static void ValidateIds(Guid eventId, CreateAccessPolicyCommand command)
    {
        if (eventId == Guid.Empty || command.TicketTypeId == Guid.Empty || command.BatchId == Guid.Empty || command.GateId == Guid.Empty || command.SectorId == Guid.Empty)
        {
            throw new ArgumentException("EventId e escopos de política devem ser UUIDs válidos.");
        }

        if (command.Priority < 0)
        {
            throw new ArgumentException("Priority não pode ser negativo.");
        }
    }

    private static string ParseDirection(string direction)
    {
        if (string.Equals(direction, "Entry", StringComparison.OrdinalIgnoreCase))
        {
            return "Entry";
        }

        if (string.Equals(direction, "Exit", StringComparison.OrdinalIgnoreCase))
        {
            return "Exit";
        }

        throw new ArgumentException("Direction deve ser Entry ou Exit.");
    }

    private static string? ValidateConditions(string? conditionsJson)
    {
        if (string.IsNullOrWhiteSpace(conditionsJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(conditionsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("ConditionsJson deve conter um objeto JSON.");
            }

            return conditionsJson.Trim();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("ConditionsJson deve conter um JSON válido.", exception);
        }
    }

    private static async Task<bool> ExistsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string sqlText,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sqlText;
        AddParameters(command, parameters);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task ExecuteAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string sqlText,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sqlText;
        AddParameters(command, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameters(MySqlCommand command, params (string Name, object Value)[] parameters)
    {
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }
    }

    private static object NullableGuid(Guid? value) => (object?)value?.ToString() ?? DBNull.Value;

    private static Guid ReadGuid(MySqlDataReader reader, int ordinal) => Guid.Parse(reader.GetValue(ordinal).ToString()!);

    private static Guid? ReadNullableGuid(MySqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ReadGuid(reader, ordinal);

    private static DateTimeOffset ReadDateTimeOffset(MySqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
}
