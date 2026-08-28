using System.Text.Json;
using FastPass.Application.Catalog;
using FastPass.Domain.Enums;
using FastPass.Infrastructure.Database;
using MySqlConnector;

namespace FastPass.Infrastructure.Catalog;

public sealed class MySqlCatalogService : ICatalogService
{
    private readonly FastPassDbConnectionFactory _connectionFactory;

    public MySqlCatalogService(FastPassDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Clientes
    // ══════════════════════════════════════════════════════════════════════════

    public async Task<ClientView> CreateClientAsync(
        CreateClientCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ArgumentException("Name é obrigatório.");

        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await ExistsAsync(conn, tx,
                "SELECT COUNT(*) FROM fp_clients WHERE name=@n;",
                cancellationToken, ("@n", command.Name.Trim())))
                throw new CatalogConflictException("Já existe um cliente com esse nome.");

            await ExecuteAsync(conn, tx, """
                INSERT INTO fp_clients (id,name,document,email,phone,active,notes,created_at,updated_at)
                VALUES (@id,@n,@doc,@email,@phone,1,@notes,@now,@now);
                """, cancellationToken,
                ("@id", id.ToString()), ("@n", command.Name.Trim()),
                ("@doc", NullableValue(command.Document)), ("@email", NullableValue(command.Email)),
                ("@phone", NullableValue(command.Phone)), ("@notes", NullableValue(command.Notes)),
                ("@now", now));
            await tx.CommitAsync(cancellationToken);
        }
        catch { await tx.RollbackAsync(cancellationToken); throw; }

        return new ClientView(id, command.Name.Trim(), TrimOrNull(command.Document),
            TrimOrNull(command.Email), TrimOrNull(command.Phone), true, TrimOrNull(command.Notes),
            0, new DateTimeOffset(now, TimeSpan.Zero));
    }

    public async Task<ClientView> UpdateClientAsync(
        Guid clientId,
        UpdateClientCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ArgumentException("Name é obrigatório.");

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await ExistsAsync(conn, tx, "SELECT COUNT(*) FROM fp_clients WHERE id=@id;",
                cancellationToken, ("@id", clientId.ToString())))
                throw new ArgumentException("Cliente não encontrado.");

            await ExecuteAsync(conn, tx, """
                UPDATE fp_clients
                SET name=@n,document=@doc,email=@email,phone=@phone,
                    active=@active,notes=@notes,updated_at=@now
                WHERE id=@id;
                """, cancellationToken,
                ("@n", command.Name.Trim()), ("@doc", NullableValue(command.Document)),
                ("@email", NullableValue(command.Email)), ("@phone", NullableValue(command.Phone)),
                ("@active", command.Active ? 1 : 0), ("@notes", NullableValue(command.Notes)),
                ("@now", DateTime.UtcNow), ("@id", clientId.ToString()));
            await tx.CommitAsync(cancellationToken);
        }
        catch { await tx.RollbackAsync(cancellationToken); throw; }

        return (await GetClientViewAsync(conn, clientId, cancellationToken))!;
    }

    public async Task<IReadOnlyList<ClientView>> ListClientsAsync(
        bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.id, c.name, c.document, c.email, c.phone, c.active, c.notes, c.created_at,
                   (SELECT COUNT(*) FROM fp_events e WHERE e.client_id = c.id) event_count
            FROM fp_clients c
            WHERE (@ao=0 OR c.active=1)
            ORDER BY c.name;
            """;
        cmd.Parameters.AddWithValue("@ao", activeOnly ? 1 : 0);
        var result = new List<ClientView>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
            result.Add(ReadClientView(r));
        return result;
    }

    private static async Task<ClientView?> GetClientViewAsync(
        MySqlConnection conn, Guid clientId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.id, c.name, c.document, c.email, c.phone, c.active, c.notes, c.created_at,
                   (SELECT COUNT(*) FROM fp_events e WHERE e.client_id = c.id) event_count
            FROM fp_clients c WHERE c.id=@id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@id", clientId.ToString());
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadClientView(r);
    }

    private static ClientView ReadClientView(MySqlDataReader r) =>
        new(ReadGuid(r, 0), r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4),
            Convert.ToBoolean(r.GetValue(5)),
            r.IsDBNull(6) ? null : r.GetString(6),
            Convert.ToInt32(r.GetValue(8)),
            new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(7), DateTimeKind.Utc)));

    // ══════════════════════════════════════════════════════════════════════════
    // Venues
    // ══════════════════════════════════════════════════════════════════════════

    public async Task<VenueView> CreateVenueAsync(
        CreateVenueCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            throw new ArgumentException("Name é obrigatório.");
        }

        if (command.Capacity is < 1)
        {
            throw new ArgumentException("Capacity deve ser maior que zero.");
        }

        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO fp_venues
                    (id, name, address, city, state, capacity, created_at, updated_at)
                VALUES
                    (@id, @name, @address, @city, @state, @capacity, @created_at, @updated_at);
                """, cancellationToken,
                ("@id", id.ToString()),
                ("@name", command.Name.Trim()),
                ("@address", NullableValue(command.Address)),
                ("@city", NullableValue(command.City)),
                ("@state", NullableValue(command.State)),
                ("@capacity", (object?)command.Capacity ?? DBNull.Value),
                ("@created_at", now),
                ("@updated_at", now));
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return new VenueView(id, command.Name.Trim(), TrimOrNull(command.Address), TrimOrNull(command.City), TrimOrNull(command.State), command.Capacity);
    }

    public async Task<IReadOnlyList<VenueView>> ListVenuesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, address, city, state, capacity
            FROM fp_venues
            ORDER BY name, id;
            """;

        var result = new List<VenueView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new VenueView(
                ReadGuid(reader, 0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : Convert.ToInt32(reader.GetValue(5))));
        }

        return result;
    }

    public async Task<EventView> CreateEventAsync(
        CreateEventCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.VenueId == Guid.Empty)
        {
            throw new ArgumentException("VenueId é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            throw new ArgumentException("Name é obrigatório.");
        }

        if (command.StartsAt >= command.EndsAt)
        {
            throw new ArgumentException("StartsAt deve ser anterior a EndsAt.");
        }

        var status = ParseEventStatus(command.Status);
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            if (!await ExistsAsync(connection, transaction, "SELECT COUNT(*) FROM fp_venues WHERE id = @id;", cancellationToken, ("@id", command.VenueId.ToString())))
            {
                throw new ArgumentException("Venue não encontrado.");
            }

            await ExecuteAsync(connection, transaction, """
                INSERT INTO fp_events
                    (id, venue_id, client_id, name, organizer, starts_at, ends_at, status, created_at, updated_at)
                VALUES
                    (@id, @venue_id, @client_id, @name, @organizer, @starts_at, @ends_at, @status, @created_at, @updated_at);
                """, cancellationToken,
                ("@id", id.ToString()),
                ("@venue_id", command.VenueId.ToString()),
                ("@client_id", (object?)command.ClientId?.ToString() ?? DBNull.Value),
                ("@name", command.Name.Trim()),
                ("@organizer", NullableValue(command.Organizer)),
                ("@starts_at", command.StartsAt.UtcDateTime),
                ("@ends_at", command.EndsAt.UtcDateTime),
                ("@status", status.ToString()),
                ("@created_at", now),
                ("@updated_at", now));
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException exception) when (exception.Number == 1452)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ArgumentException("Venue não encontrado.", exception);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return new EventView(id, command.VenueId, command.Name.Trim(), TrimOrNull(command.Organizer), command.StartsAt.ToUniversalTime(), command.EndsAt.ToUniversalTime(), status.ToString(), command.ClientId);
    }

    public async Task<IReadOnlyList<EventView>> ListEventsAsync(
        Guid? venueId,
        string? status,
        CancellationToken cancellationToken = default)
    {
        if (venueId == Guid.Empty)
        {
            throw new ArgumentException("VenueId inválido.");
        }

        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : ParseEventStatus(status).ToString();
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.id, e.venue_id, e.name, e.organizer, e.starts_at, e.ends_at, e.status,
                   e.client_id, c.name
            FROM fp_events e
            LEFT JOIN fp_clients c ON c.id = e.client_id
            WHERE (@venue_id IS NULL OR e.venue_id = @venue_id)
              AND (@status IS NULL OR e.status = @status)
            ORDER BY e.starts_at, e.name;
            """;
        command.Parameters.AddWithValue("@venue_id", (object?)venueId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("@status", (object?)normalizedStatus ?? DBNull.Value);

        var result = new List<EventView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new EventView(
                ReadGuid(reader, 0),
                ReadGuid(reader, 1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                ReadDateTimeOffset(reader, 4),
                ReadDateTimeOffset(reader, 5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : ReadGuid(reader, 7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return result;
    }

    public async Task<SectorView> CreateSectorAsync(
        Guid eventId,
        CreateSectorCommand command,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty || string.IsNullOrWhiteSpace(command.Name))
        {
            throw new ArgumentException("EventId e Name são obrigatórios.");
        }

        if (command.Capacity is < 1)
        {
            throw new ArgumentException("Capacity deve ser maior que zero.");
        }

        var normalizedName = command.Name.Trim();
        var now = DateTime.UtcNow;
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        Guid id;
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

            if (await ExistsAsync(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM fp_sectors WHERE event_id = @event_id AND name = @name AND active = 1;",
                    cancellationToken,
                    ("@event_id", eventId.ToString()),
                    ("@name", normalizedName)))
            {
                throw new CatalogConflictException("Já existe esse setor no evento.");
            }

            var existingId = await ReadGuidOrNullAsync(
                connection,
                transaction,
                "SELECT id FROM fp_sectors WHERE event_id = @event_id AND name = @name LIMIT 1;",
                cancellationToken,
                ("@event_id", eventId.ToString()),
                ("@name", normalizedName));

            if (existingId.HasValue)
            {
                id = existingId.Value;
                await ExecuteAsync(connection, transaction, """
                    UPDATE fp_sectors
                    SET capacity = @capacity, active = 1, updated_at = @updated_at
                    WHERE id = @id AND event_id = @event_id;
                    """, cancellationToken,
                    ("@id", id.ToString()),
                    ("@event_id", eventId.ToString()),
                    ("@capacity", (object?)command.Capacity ?? DBNull.Value),
                    ("@updated_at", now));
            }
            else
            {
                id = Guid.NewGuid();
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO fp_sectors
                        (id, event_id, name, capacity, active, created_at, updated_at)
                    VALUES
                        (@id, @event_id, @name, @capacity, 1, @created_at, @updated_at);
                    """, cancellationToken,
                    ("@id", id.ToString()),
                    ("@event_id", eventId.ToString()),
                    ("@name", normalizedName),
                    ("@capacity", (object?)command.Capacity ?? DBNull.Value),
                    ("@created_at", now),
                    ("@updated_at", now));
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new CatalogConflictException("Já existe esse setor no evento.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return new SectorView(id, eventId, command.Name.Trim(), command.Capacity, true);
    }

    public async Task<IReadOnlyList<SectorView>> ListSectorsAsync(
        Guid eventId,
        bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("EventId é obrigatório.");
        }

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, capacity, active
            FROM fp_sectors
            WHERE event_id = @event_id
              AND (@active_only = 0 OR active = 1)
            ORDER BY name, id;
            """;
        command.Parameters.AddWithValue("@event_id", eventId.ToString());
        command.Parameters.AddWithValue("@active_only", activeOnly ? 1 : 0);

        var result = new List<SectorView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SectorView(
                ReadGuid(reader, 0),
                eventId,
                reader.GetString(1),
                reader.IsDBNull(2) ? null : Convert.ToInt32(reader.GetValue(2)),
                Convert.ToBoolean(reader.GetValue(3))));
        }

        return result;
    }

    public async Task<GateSectorView> CreateGateSectorAsync(
        Guid eventId,
        CreateGateSectorCommand command,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty || command.GateId == Guid.Empty || command.SectorId == Guid.Empty)
        {
            throw new ArgumentException("EventId, GateId e SectorId são obrigatórios.");
        }

        if (!Enum.TryParse<AccessDirection>(command.Direction, true, out var direction))
        {
            throw new ArgumentException("Direction deve ser Entry ou Exit.");
        }

        if (command.ActiveFrom.HasValue && command.ActiveUntil.HasValue && command.ActiveFrom > command.ActiveUntil)
        {
            throw new ArgumentException("ActiveFrom deve ser anterior ou igual a ActiveUntil.");
        }

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        Guid id;
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

            if (!await ExistsAsync(
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
                    ("@gate_id", command.GateId.ToString())))
            {
                throw new ArgumentException("Portaria não encontrada, inativa ou não associada ao evento.");
            }

            if (!await ExistsAsync(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM fp_sectors WHERE id = @sector_id AND event_id = @event_id AND active = 1;",
                    cancellationToken,
                    ("@sector_id", command.SectorId.ToString()),
                    ("@event_id", eventId.ToString())))
            {
                throw new ArgumentException("Setor não encontrado, inativo ou incompatível com o evento.");
            }

            var existingId = await ReadGuidOrNullAsync(
                connection,
                transaction,
                """
                SELECT id
                FROM fp_gate_sectors
                WHERE event_id = @event_id
                  AND gate_id = @gate_id
                  AND sector_id = @sector_id
                  AND direction = @direction
                LIMIT 1;
                """,
                cancellationToken,
                ("@event_id", eventId.ToString()),
                ("@gate_id", command.GateId.ToString()),
                ("@sector_id", command.SectorId.ToString()),
                ("@direction", direction.ToString()));

            if (existingId.HasValue)
            {
                id = existingId.Value;
                await ExecuteAsync(connection, transaction, """
                    UPDATE fp_gate_sectors
                    SET active_from = @active_from,
                        active_until = @active_until,
                        active = @active
                    WHERE id = @id;
                    """, cancellationToken,
                    ("@id", id.ToString()),
                    ("@active_from", (object?)command.ActiveFrom?.UtcDateTime ?? DBNull.Value),
                    ("@active_until", (object?)command.ActiveUntil?.UtcDateTime ?? DBNull.Value),
                    ("@active", command.Active ? 1 : 0));
            }
            else
            {
                id = Guid.NewGuid();
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO fp_gate_sectors
                        (id, event_id, gate_id, sector_id, direction, active_from, active_until, active)
                    VALUES
                        (@id, @event_id, @gate_id, @sector_id, @direction, @active_from, @active_until, @active);
                    """, cancellationToken,
                    ("@id", id.ToString()),
                    ("@event_id", eventId.ToString()),
                    ("@gate_id", command.GateId.ToString()),
                    ("@sector_id", command.SectorId.ToString()),
                    ("@direction", direction.ToString()),
                    ("@active_from", (object?)command.ActiveFrom?.UtcDateTime ?? DBNull.Value),
                    ("@active_until", (object?)command.ActiveUntil?.UtcDateTime ?? DBNull.Value),
                    ("@active", command.Active ? 1 : 0));
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new CatalogConflictException("Já existe essa associação de portaria, setor e direção no evento.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        var created = await ListGateSectorsAsync(eventId, false, cancellationToken);
        return created.Single(item => item.Id == id);
    }

    public async Task<IReadOnlyList<GateSectorView>> ListGateSectorsAsync(
        Guid eventId,
        bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("EventId é obrigatório.");
        }

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT gs.id, gs.gate_id, g.name, gs.sector_id, s.name,
                   gs.direction, gs.active_from, gs.active_until, gs.active
            FROM fp_gate_sectors gs
            INNER JOIN fp_gates g ON g.id = gs.gate_id
            INNER JOIN fp_sectors s ON s.id = gs.sector_id
            WHERE gs.event_id = @event_id
              AND (@active_only = 0 OR gs.active = 1)
            ORDER BY g.name, s.name, gs.direction, gs.id;
            """;
        command.Parameters.AddWithValue("@event_id", eventId.ToString());
        command.Parameters.AddWithValue("@active_only", activeOnly ? 1 : 0);

        var result = new List<GateSectorView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new GateSectorView(
                ReadGuid(reader, 0),
                eventId,
                ReadGuid(reader, 1),
                reader.GetString(2),
                ReadGuid(reader, 3),
                reader.GetString(4),
                reader.GetString(5),
                ReadNullableDateTimeOffset(reader, 6),
                ReadNullableDateTimeOffset(reader, 7),
                Convert.ToBoolean(reader.GetValue(8))));
        }

        return result;
    }

    public async Task<bool> RemoveGateSectorAsync(
        Guid eventId,
        Guid gateSectorId,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty || gateSectorId == Guid.Empty)
        {
            throw new ArgumentException("EventId e GateSectorId são obrigatórios.");
        }

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE fp_gate_sectors
            SET active = 0
            WHERE id = @id
              AND event_id = @event_id
              AND active = 1;
            """;
        command.Parameters.AddWithValue("@id", gateSectorId.ToString());
        command.Parameters.AddWithValue("@event_id", eventId.ToString());
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> RemoveGateAsync(
        Guid eventId,
        Guid gateId,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty || gateId == Guid.Empty)
        {
            throw new ArgumentException("EventId e GateId são obrigatórios.");
        }

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            if (await ExistsAsync(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM fp_gate_sectors WHERE event_id = @event_id AND gate_id = @gate_id AND active = 1;",
                    cancellationToken,
                    ("@event_id", eventId.ToString()),
                    ("@gate_id", gateId.ToString())))
            {
                throw new CatalogConflictException("Não é possível remover a portaria porque ela possui associações ativas na matriz.");
            }

            var removed = await ExecuteAsync(
                connection,
                transaction,
                "UPDATE fp_event_gates SET active = 0 WHERE event_id = @event_id AND gate_id = @gate_id AND active = 1;",
                cancellationToken,
                ("@event_id", eventId.ToString()),
                ("@gate_id", gateId.ToString()));
            await transaction.CommitAsync(cancellationToken);
            return removed == 1;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> RemoveSectorAsync(
        Guid eventId,
        Guid sectorId,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty || sectorId == Guid.Empty)
        {
            throw new ArgumentException("EventId e SectorId são obrigatórios.");
        }

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            if (await ExistsAsync(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM fp_gate_sectors WHERE event_id = @event_id AND sector_id = @sector_id AND active = 1;",
                    cancellationToken,
                    ("@event_id", eventId.ToString()),
                    ("@sector_id", sectorId.ToString())))
            {
                throw new CatalogConflictException("Não é possível remover o setor porque ele possui associações ativas na matriz.");
            }

            var removed = await ExecuteAsync(
                connection,
                transaction,
                "UPDATE fp_sectors SET active = 0, updated_at = @updated_at WHERE event_id = @event_id AND id = @sector_id AND active = 1;",
                cancellationToken,
                ("@updated_at", DateTime.UtcNow),
                ("@event_id", eventId.ToString()),
                ("@sector_id", sectorId.ToString()));
            await transaction.CommitAsync(cancellationToken);
            return removed == 1;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<GateView> CreateGateAsync(
        Guid eventId,
        CreateGateCommand command,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty || string.IsNullOrWhiteSpace(command.Name))
        {
            throw new ArgumentException("EventId e Name são obrigatórios.");
        }

        var normalizedName = command.Name.Trim();
        var normalizedCode = TrimOrNull(command.Code);
        var now = DateTime.UtcNow;
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var venueId = await ReadGuidOrNullAsync(connection, transaction, "SELECT venue_id FROM fp_events WHERE id = @id;", cancellationToken, ("@id", eventId.ToString()));
            if (!venueId.HasValue)
            {
                throw new ArgumentException("Evento não encontrado.");
            }

            var existingGateId = normalizedCode is null
                ? null
                : await ReadGuidOrNullAsync(
                    connection,
                    transaction,
                    "SELECT id FROM fp_gates WHERE venue_id = @venue_id AND code = @code LIMIT 1;",
                    cancellationToken,
                    ("@venue_id", venueId.Value.ToString()),
                    ("@code", normalizedCode));

            Guid id;
            if (existingGateId.HasValue)
            {
                id = existingGateId.Value;
                if (await ExistsAsync(
                        connection,
                        transaction,
                        "SELECT COUNT(*) FROM fp_event_gates WHERE event_id = @event_id AND gate_id = @gate_id AND active = 1;",
                        cancellationToken,
                        ("@event_id", eventId.ToString()),
                        ("@gate_id", id.ToString())))
                {
                    throw new CatalogConflictException("Já existe uma portaria com esse código neste evento.");
                }

                await ExecuteAsync(connection, transaction, """
                    UPDATE fp_gates
                    SET name = @name, active = 1, updated_at = @updated_at
                    WHERE id = @gate_id AND venue_id = @venue_id;
                    """, cancellationToken,
                    ("@name", normalizedName),
                    ("@updated_at", now),
                    ("@gate_id", id.ToString()),
                    ("@venue_id", venueId.Value.ToString()));

                var reactivated = await ExecuteAsync(connection, transaction, """
                    UPDATE fp_event_gates
                    SET active = 1, operation_mode = @operation_mode
                    WHERE event_id = @event_id AND gate_id = @gate_id;
                    """, cancellationToken,
                    ("@operation_mode", GateOperationMode.EntryAndExitValidated.ToString()),
                    ("@event_id", eventId.ToString()),
                    ("@gate_id", id.ToString()));
                if (reactivated == 0)
                {
                    await ExecuteAsync(connection, transaction, """
                        INSERT INTO fp_event_gates (event_id, gate_id, active, operation_mode)
                        VALUES (@event_id, @gate_id, 1, @operation_mode);
                        """, cancellationToken,
                        ("@event_id", eventId.ToString()),
                        ("@gate_id", id.ToString()),
                        ("@operation_mode", GateOperationMode.EntryAndExitValidated.ToString()));
                }
            }
            else
            {
                id = Guid.NewGuid();
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO fp_gates
                        (id, venue_id, name, code, active, created_at, updated_at)
                    VALUES
                        (@id, @venue_id, @name, @code, 1, @created_at, @updated_at);
                    """, cancellationToken,
                    ("@id", id.ToString()),
                    ("@venue_id", venueId.Value.ToString()),
                    ("@name", normalizedName),
                    ("@code", (object?)normalizedCode ?? DBNull.Value),
                    ("@created_at", now),
                    ("@updated_at", now));

                await ExecuteAsync(connection, transaction, """
                    INSERT INTO fp_event_gates (event_id, gate_id, active, operation_mode)
                    VALUES (@event_id, @gate_id, 1, @operation_mode);
                    """, cancellationToken,
                    ("@event_id", eventId.ToString()),
                    ("@gate_id", id.ToString()),
                    ("@operation_mode", GateOperationMode.EntryAndExitValidated.ToString()));
            }

            await transaction.CommitAsync(cancellationToken);
            return new GateView(id, venueId.Value, normalizedName, normalizedCode, true, GateOperationMode.EntryAndExitValidated.ToString());
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new CatalogConflictException("Já existe uma portaria com esse código no local.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<GateView>> ListGatesAsync(
        Guid eventId,
        bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("EventId é obrigatório.");
        }

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT g.id, g.venue_id, g.name, g.code,
                   (g.active = 1 AND eg.active = 1) AS active,
                   eg.operation_mode
            FROM fp_event_gates eg
            INNER JOIN fp_gates g ON g.id = eg.gate_id
            WHERE eg.event_id = @event_id
              AND (@active_only = 0 OR (g.active = 1 AND eg.active = 1))
            ORDER BY g.name, g.id;
            """;
        command.Parameters.AddWithValue("@event_id", eventId.ToString());
        command.Parameters.AddWithValue("@active_only", activeOnly ? 1 : 0);

        var result = new List<GateView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new GateView(
                ReadGuid(reader, 0),
                ReadGuid(reader, 1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                Convert.ToBoolean(reader.GetValue(4)),
                reader.IsDBNull(5) ? GateOperationMode.EntryAndExitValidated.ToString() : reader.GetString(5)));
        }

        return result;
    }

    public async Task<GateView> SetGateOperationModeAsync(
        Guid eventId,
        Guid gateId,
        SetGateOperationModeCommand command,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty || gateId == Guid.Empty)
        {
            throw new ArgumentException("EventId e GateId são obrigatórios.");
        }

        if (!Enum.TryParse<GateOperationMode>(command.OperationMode, true, out var operationMode))
        {
            throw new ArgumentException("OperationMode deve ser EntryValidatedExitFree ou EntryAndExitValidated.");
        }

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var update = connection.CreateCommand();
        update.CommandText = """
            UPDATE fp_event_gates
            SET operation_mode = @operation_mode
            WHERE event_id = @event_id
              AND gate_id = @gate_id
              AND active = 1;
            """;
        update.Parameters.AddWithValue("@operation_mode", operationMode.ToString());
        update.Parameters.AddWithValue("@event_id", eventId.ToString());
        update.Parameters.AddWithValue("@gate_id", gateId.ToString());

        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new ArgumentException("Portaria não encontrada, inativa ou não associada ao evento.");
        }

        var gates = await ListGatesAsync(eventId, false, cancellationToken);
        return gates.Single(item => item.Id == gateId);
    }

    public async Task<DeviceView> CreateDeviceAsync(
        Guid eventId,
        Guid gateId,
        CreateDeviceCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateDeviceFields(eventId, gateId, command.Name, command.Identifier, command.DeviceType, command.ConfigurationJson);
        var deviceType = Enum.Parse<DeviceType>(command.DeviceType, true).ToString();
        var identifier = TrimOrNull(command.Identifier);
        var configurationJson = ValidateConfiguration(command.ConfigurationJson);
        var deviceId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            if (!await ExistsAsync(
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
                    ("@gate_id", gateId.ToString())))
            {
                throw new ArgumentException("Portaria não encontrada, inativa ou não associada ao evento.");
            }

            await ExecuteAsync(connection, transaction, """
                INSERT INTO fp_devices
                    (id, gate_id, name, identifier, device_type, active, last_seen_at,
                     configuration_json, created_at, updated_at)
                VALUES
                    (@id, @gate_id, @name, @identifier, @device_type, 1, NULL,
                     @configuration_json, @created_at, @updated_at);
                """, cancellationToken,
                ("@id", deviceId.ToString()),
                ("@gate_id", gateId.ToString()),
                ("@name", command.Name.Trim()),
                ("@identifier", (object?)identifier ?? DBNull.Value),
                ("@device_type", deviceType),
                ("@configuration_json", (object?)configurationJson ?? DBNull.Value),
                ("@created_at", now),
                ("@updated_at", now));
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new CatalogConflictException("Já existe um dispositivo com esse identifier.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return (await ListDevicesAsync(eventId, gateId, false, cancellationToken)).Single(item => item.Id == deviceId);
    }

    public async Task<IReadOnlyList<DeviceView>> ListDevicesAsync(
        Guid eventId,
        Guid? gateId,
        bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty || (gateId.HasValue && gateId.Value == Guid.Empty))
        {
            throw new ArgumentException("EventId e GateId devem ser válidos.");
        }

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.id, d.gate_id, g.name, g.code, d.name, d.identifier,
                   d.device_type, d.active, d.last_seen_at, d.configuration_json
            FROM fp_devices d
            INNER JOIN fp_gates g ON g.id = d.gate_id
            INNER JOIN fp_event_gates eg ON eg.gate_id = d.gate_id
            WHERE eg.event_id = @event_id
              AND eg.active = 1
              AND g.active = 1
              AND (@gate_id IS NULL OR d.gate_id = @gate_id)
              AND (@active_only = 0 OR d.active = 1)
            ORDER BY g.name, d.name, d.id;
            """;
        command.Parameters.AddWithValue("@event_id", eventId.ToString());
        command.Parameters.AddWithValue("@gate_id", (object?)gateId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("@active_only", activeOnly ? 1 : 0);

        var result = new List<DeviceView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DeviceView(
                ReadGuid(reader, 0),
                eventId,
                ReadGuid(reader, 1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                Convert.ToBoolean(reader.GetValue(7)),
                reader.IsDBNull(8) ? null : ReadDateTimeOffset(reader, 8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return result;
    }

    public async Task<DeviceView> UpdateDeviceAsync(
        Guid eventId,
        Guid gateId,
        Guid deviceId,
        UpdateDeviceCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateDeviceFields(eventId, gateId, command.Name, command.Identifier, command.DeviceType, command.ConfigurationJson);
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("DeviceId é obrigatório.");
        }

        var deviceType = Enum.Parse<DeviceType>(command.DeviceType, true).ToString();
        var identifier = TrimOrNull(command.Identifier);
        var configurationJson = ValidateConfiguration(command.ConfigurationJson);

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            if (!await ExistsAsync(
                    connection,
                    transaction,
                    """
                    SELECT COUNT(*)
                    FROM fp_devices d
                    INNER JOIN fp_event_gates eg
                        ON eg.gate_id = d.gate_id
                       AND eg.event_id = @event_id
                    WHERE d.id = @device_id
                      AND d.gate_id = @gate_id
                      AND eg.active = 1;
                    """,
                    cancellationToken,
                    ("@event_id", eventId.ToString()),
                    ("@gate_id", gateId.ToString()),
                    ("@device_id", deviceId.ToString())))
            {
                throw new ArgumentException("Dispositivo não encontrado ou não pertence à portaria/evento informado.");
            }

            await ExecuteAsync(connection, transaction, """
                UPDATE fp_devices
                SET name = @name,
                    identifier = @identifier,
                    device_type = @device_type,
                    active = @active,
                    configuration_json = @configuration_json,
                    updated_at = @updated_at
                WHERE id = @device_id
                  AND gate_id = @gate_id;
                """, cancellationToken,
                ("@name", command.Name.Trim()),
                ("@identifier", (object?)identifier ?? DBNull.Value),
                ("@device_type", deviceType),
                ("@active", command.Active ? 1 : 0),
                ("@configuration_json", (object?)configurationJson ?? DBNull.Value),
                ("@updated_at", DateTime.UtcNow),
                ("@device_id", deviceId.ToString()),
                ("@gate_id", gateId.ToString()));
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new CatalogConflictException("Já existe um dispositivo com esse identifier.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return (await ListDevicesAsync(eventId, gateId, false, cancellationToken)).Single(item => item.Id == deviceId);
    }

    public async Task<TicketTypeView> CreateTicketTypeAsync(
        Guid eventId,
        CreateTicketTypeCommand command,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty || string.IsNullOrWhiteSpace(command.Name))
        {
            throw new ArgumentException("EventId e Name são obrigatórios.");
        }

        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            if (!await ExistsAsync(connection, transaction, "SELECT COUNT(*) FROM fp_events WHERE id = @id;", cancellationToken, ("@id", eventId.ToString())))
            {
                throw new ArgumentException("Evento não encontrado.");
            }

            await ExecuteAsync(connection, transaction, """
                INSERT INTO fp_ticket_types
                    (id, event_id, name, allows_reentry, active, created_at, updated_at)
                VALUES
                    (@id, @event_id, @name, @allows_reentry, 1, @created_at, @updated_at);
                """, cancellationToken,
                ("@id", id.ToString()),
                ("@event_id", eventId.ToString()),
                ("@name", command.Name.Trim()),
                ("@allows_reentry", command.AllowsReentry ? 1 : 0),
                ("@created_at", now),
                ("@updated_at", now));
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new CatalogConflictException("Já existe esse tipo de ingresso no evento.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return new TicketTypeView(id, eventId, command.Name.Trim(), command.AllowsReentry, true);
    }

    public async Task<TicketBatchView> CreateTicketBatchAsync(
        Guid eventId,
        CreateTicketBatchCommand command,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty || command.TicketTypeId == Guid.Empty || string.IsNullOrWhiteSpace(command.Name))
        {
            throw new ArgumentException("EventId, TicketTypeId e Name são obrigatórios.");
        }

        if (command.MaximumQuantity is < 1)
        {
            throw new ArgumentException("MaximumQuantity deve ser maior que zero.");
        }

        if (command.SalesStartsAt.HasValue && command.SalesEndsAt.HasValue && command.SalesStartsAt > command.SalesEndsAt)
        {
            throw new ArgumentException("SalesStartsAt não pode ser posterior a SalesEndsAt.");
        }

        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            if (!await ExistsAsync(connection, transaction, "SELECT COUNT(*) FROM fp_ticket_types WHERE id = @type_id AND event_id = @event_id AND active = 1;", cancellationToken,
                    ("@type_id", command.TicketTypeId.ToString()), ("@event_id", eventId.ToString())))
            {
                throw new ArgumentException("Tipo de ingresso não encontrado ou não pertence ao evento.");
            }

            await ExecuteAsync(connection, transaction, """
                INSERT INTO fp_ticket_batches
                    (id, event_id, ticket_type_id, name, maximum_quantity, sales_starts_at, sales_ends_at, created_at, updated_at)
                VALUES
                    (@id, @event_id, @ticket_type_id, @name, @maximum_quantity, @sales_starts_at, @sales_ends_at, @created_at, @updated_at);
                """, cancellationToken,
                ("@id", id.ToString()),
                ("@event_id", eventId.ToString()),
                ("@ticket_type_id", command.TicketTypeId.ToString()),
                ("@name", command.Name.Trim()),
                ("@maximum_quantity", (object?)command.MaximumQuantity ?? DBNull.Value),
                ("@sales_starts_at", (object?)command.SalesStartsAt?.UtcDateTime ?? DBNull.Value),
                ("@sales_ends_at", (object?)command.SalesEndsAt?.UtcDateTime ?? DBNull.Value),
                ("@created_at", now),
                ("@updated_at", now));
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new CatalogConflictException("Já existe esse lote no evento.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return new TicketBatchView(id, eventId, command.TicketTypeId, command.Name.Trim(), command.MaximumQuantity, command.SalesStartsAt?.ToUniversalTime(), command.SalesEndsAt?.ToUniversalTime());
    }

    public async Task<TicketView> IssueTicketAsync(
        Guid eventId,
        IssueTicketCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateTicketCommand(eventId, command);
        var metadata = ValidateMetadata(command.MetadataJson);
        var maximumEntries = command.MaximumEntries ?? command.MaximumUses;
        var ticketId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            if (!await ExistsAsync(connection, transaction, "SELECT COUNT(*) FROM fp_ticket_types WHERE id = @type_id AND event_id = @event_id AND active = 1;", cancellationToken,
                    ("@type_id", command.TicketTypeId.ToString()), ("@event_id", eventId.ToString())))
            {
                throw new ArgumentException("Tipo de ingresso não encontrado ou não pertence ao evento.");
            }

            if (command.BatchId.HasValue)
            {
                await ValidateBatchForIssueAsync(connection, transaction, eventId, command, now, cancellationToken);
            }

            await ExecuteAsync(connection, transaction, """
                INSERT INTO fp_tickets
                    (id, event_id, batch_id, ticket_type_id, external_id, code, maximum_uses,
                     uses, maximum_entries, entries_used, people_inside, status, metadata_json,
                     created_at, updated_at)
                VALUES
                    (@id, @event_id, @batch_id, @ticket_type_id, @external_id, @code, @maximum_uses,
                     0, @maximum_entries, 0, 0, 'active', @metadata_json, @created_at, @updated_at);
                """, cancellationToken,
                ("@id", ticketId.ToString()),
                ("@event_id", eventId.ToString()),
                ("@batch_id", (object?)command.BatchId?.ToString() ?? DBNull.Value),
                ("@ticket_type_id", command.TicketTypeId.ToString()),
                ("@external_id", command.ExternalId.Trim()),
                ("@code", command.Code.Trim()),
                ("@maximum_uses", maximumEntries),
                ("@maximum_entries", maximumEntries),
                ("@metadata_json", (object?)metadata ?? DBNull.Value),
                ("@created_at", now),
                ("@updated_at", now));
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new CatalogConflictException("O external ID ou código do ingresso já está cadastrado neste evento.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return new TicketView(
            ticketId,
            eventId,
            command.BatchId,
            command.TicketTypeId,
            command.ExternalId.Trim(),
            command.Code.Trim(),
            maximumEntries,
            0,
            "active",
            metadata,
            new DateTimeOffset(now, TimeSpan.Zero),
            maximumEntries,
            0,
            0);
    }

    public async Task<IReadOnlyList<TicketView>> ListTicketsAsync(
        Guid eventId,
        Guid? ticketTypeId,
        Guid? batchId,
        string? externalId,
        string? code,
        string? status,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty || ticketTypeId == Guid.Empty || batchId == Guid.Empty)
        {
            throw new ArgumentException("EventId, TicketTypeId e BatchId devem ser válidos.");
        }

        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, batch_id, ticket_type_id, external_id, code, maximum_uses, uses,
                   maximum_entries, entries_used, people_inside, status, metadata_json, created_at
            FROM fp_tickets
            WHERE event_id = @event_id
              AND (@ticket_type_id IS NULL OR ticket_type_id = @ticket_type_id)
              AND (@batch_id IS NULL OR batch_id = @batch_id)
              AND (@external_id IS NULL OR external_id = @external_id)
              AND (@code IS NULL OR code = @code)
              AND (@status IS NULL OR status = @status)
            ORDER BY created_at, external_id;
            """;
        command.Parameters.AddWithValue("@event_id", eventId.ToString());
        command.Parameters.AddWithValue("@ticket_type_id", (object?)ticketTypeId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("@batch_id", (object?)batchId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("@external_id", NullableValue(externalId));
        command.Parameters.AddWithValue("@code", NullableValue(code));
        command.Parameters.AddWithValue("@status", (object?)normalizedStatus ?? DBNull.Value);

        var result = new List<TicketView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new TicketView(
                ReadGuid(reader, 0),
                eventId,
                ReadNullableGuid(reader, 1),
                ReadGuid(reader, 2),
                reader.GetString(3),
                reader.GetString(4),
                Convert.ToInt32(reader.GetValue(5)),
                Convert.ToInt32(reader.GetValue(6)),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                ReadDateTimeOffset(reader, 12),
                Convert.ToInt32(reader.GetValue(7)),
                Convert.ToInt32(reader.GetValue(8)),
                Convert.ToInt32(reader.GetValue(9))));
        }

        return result;
    }

    private static async Task ValidateBatchForIssueAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid eventId,
        IssueTicketCommand command,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var batchCommand = connection.CreateCommand();
        batchCommand.Transaction = transaction;
        batchCommand.CommandText = """
            SELECT maximum_quantity, sales_starts_at, sales_ends_at
            FROM fp_ticket_batches
            WHERE id = @batch_id
              AND event_id = @event_id
              AND ticket_type_id = @ticket_type_id
            FOR UPDATE;
            """;
        batchCommand.Parameters.AddWithValue("@batch_id", command.BatchId!.Value.ToString());
        batchCommand.Parameters.AddWithValue("@event_id", eventId.ToString());
        batchCommand.Parameters.AddWithValue("@ticket_type_id", command.TicketTypeId.ToString());

        await using var reader = await batchCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new ArgumentException("Lote não encontrado ou não pertence ao evento/tipo informado.");
        }

        int? maximumQuantity = reader.IsDBNull(0) ? null : Convert.ToInt32(reader.GetValue(0));
        DateTime? salesStartsAt = reader.IsDBNull(1) ? null : DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc);
        DateTime? salesEndsAt = reader.IsDBNull(2) ? null : DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc);
        await reader.DisposeAsync();

        if (salesStartsAt.HasValue && now < salesStartsAt.Value)
        {
            throw new ArgumentException("O lote ainda não está disponível para emissão.");
        }

        if (salesEndsAt.HasValue && now > salesEndsAt.Value)
        {
            throw new ArgumentException("A janela de emissão do lote foi encerrada.");
        }

        if (maximumQuantity.HasValue)
        {
            await using var countCommand = connection.CreateCommand();
            countCommand.Transaction = transaction;
            countCommand.CommandText = "SELECT COUNT(*) FROM fp_tickets WHERE batch_id = @batch_id;";
            countCommand.Parameters.AddWithValue("@batch_id", command.BatchId.Value.ToString());
            var issued = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
            if (issued >= maximumQuantity.Value)
            {
                throw new ArgumentException("A quantidade máxima do lote já foi emitida.");
            }
        }
    }

    private static void ValidateDeviceFields(
        Guid eventId,
        Guid gateId,
        string name,
        string? identifier,
        string deviceType,
        string? configurationJson)
    {
        if (eventId == Guid.Empty || gateId == Guid.Empty)
        {
            throw new ArgumentException("EventId e GateId são obrigatórios.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name é obrigatório.");
        }

        if (name.Trim().Length > 120)
        {
            throw new ArgumentException("Name deve ter no máximo 120 caracteres.");
        }

        if (!string.IsNullOrWhiteSpace(identifier) && identifier.Trim().Length > 120)
        {
            throw new ArgumentException("Identifier deve ter no máximo 120 caracteres.");
        }

        if (!Enum.TryParse<DeviceType>(deviceType, true, out _))
        {
            throw new ArgumentException("DeviceType deve ser Legacy, Serial, Vcom, Mqtt ou Simulator.");
        }

        ValidateConfiguration(configurationJson);
    }

    private static string? ValidateConfiguration(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(configurationJson);
            return configurationJson.Trim();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("ConfigurationJson deve conter um JSON válido.", exception);
        }
    }

    private static void ValidateTicketCommand(Guid eventId, IssueTicketCommand command)
    {
        if (eventId == Guid.Empty || command.TicketTypeId == Guid.Empty)
        {
            throw new ArgumentException("EventId e TicketTypeId são obrigatórios.");
        }

        if (string.IsNullOrWhiteSpace(command.ExternalId) || string.IsNullOrWhiteSpace(command.Code))
        {
            throw new ArgumentException("ExternalId e Code são obrigatórios.");
        }

        if (command.MaximumUses < 1)
        {
            throw new ArgumentException("MaximumUses deve ser maior que zero.");
        }

        if (command.MaximumEntries is < 1)
        {
            throw new ArgumentException("MaximumEntries deve ser maior que zero.");
        }
    }

    private static string? ValidateMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            return metadataJson.Trim();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("MetadataJson deve conter um JSON válido.", exception);
        }
    }

    private static EventStatus ParseEventStatus(string status)
    {
        if (Enum.TryParse<EventStatus>(status, true, out var result))
        {
            return result;
        }

        throw new ArgumentException("Status deve ser Draft, Preparing, Published, Running, Closed ou Archived.");
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

    private static async Task<int> ExecuteAsync(
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
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Guid?> ReadGuidOrNullAsync(
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
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null || value is DBNull ? null : Guid.Parse(value.ToString()!);
    }

    private static void AddParameters(MySqlCommand command, params (string Name, object Value)[] parameters)
    {
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }
    }

    private static object NullableValue(string? value) => (object?)TrimOrNull(value) ?? DBNull.Value;

    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Guid ReadGuid(MySqlDataReader reader, int ordinal) => Guid.Parse(reader.GetValue(ordinal).ToString()!);

    private static Guid? ReadNullableGuid(MySqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ReadGuid(reader, ordinal);

    private static DateTimeOffset? ReadNullableDateTimeOffset(MySqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadDateTimeOffset(reader, ordinal);

    private static DateTimeOffset ReadDateTimeOffset(MySqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    public async Task<BulkStatusResult> BulkUpdateTicketStatusAsync(
        Guid eventId,
        IReadOnlyList<Guid> ticketIds,
        string newStatus,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("EventId é obrigatório.");
        if (ticketIds is null || ticketIds.Count == 0) throw new ArgumentException("Informe ao menos um ticket.");
        var allowedStatuses = new[] { "active", "cancelled", "revoked" };
        if (!allowedStatuses.Contains(newStatus, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Status inválido '{newStatus}'. Use: active, cancelled, revoked.");

        var normalizedStatus = newStatus.ToLowerInvariant();
        var now = DateTime.UtcNow;
        int updated = 0, notFound = 0;

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var ticketId in ticketIds)
            {
                await using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = """
                    UPDATE fp_tickets SET status=@status,updated_at=@now
                    WHERE id=@id AND event_id=@eid;
                    """;
                cmd.Parameters.AddWithValue("@status", normalizedStatus);
                cmd.Parameters.AddWithValue("@now", now);
                cmd.Parameters.AddWithValue("@id", ticketId.ToString());
                cmd.Parameters.AddWithValue("@eid", eventId.ToString());
                var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
                if (rows == 1) updated++; else notFound++;
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }

        return new BulkStatusResult(updated, notFound);
    }

    public async Task<TicketSummaryView> GetTicketSummaryAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("EventId é obrigatório.");
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT
                COUNT(*) total,
                COALESCE(SUM(CASE WHEN status='active'    THEN 1 ELSE 0 END),0) active_count,
                COALESCE(SUM(CASE WHEN status='active' AND entries_used > 0 THEN 1 ELSE 0 END),0) used_count,
                COALESCE(SUM(CASE WHEN status='cancelled' THEN 1 ELSE 0 END),0) cancelled_count,
                COALESCE(SUM(CASE WHEN status='revoked'   THEN 1 ELSE 0 END),0) revoked_count,
                COALESCE(SUM(people_inside),0) people_inside
            FROM fp_tickets
            WHERE event_id=@eid;
            """;
        cmd.Parameters.AddWithValue("@eid", eventId.ToString());
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
            return new TicketSummaryView(0, 0, 0, 0, 0, 0);
        return new TicketSummaryView(
            Convert.ToInt32(r.GetValue(0)),
            Convert.ToInt32(r.GetValue(1)),
            Convert.ToInt32(r.GetValue(2)),
            Convert.ToInt32(r.GetValue(3)),
            Convert.ToInt32(r.GetValue(4)),
            Convert.ToInt32(r.GetValue(5)));
    }
}
