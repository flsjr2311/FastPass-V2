using System.Security.Cryptography;
using System.Text;
using FastPass.Application.Staff;
using FastPass.Domain.Enums;
using FastPass.Infrastructure.Database;
using MySqlConnector;

namespace FastPass.Infrastructure.Staff;

public sealed class MySqlStaffService : IStaffService
{
    private readonly FastPassDbConnectionFactory _connectionFactory;

    public MySqlStaffService(FastPassDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<StaffCredentialView> RegisterAsync(
        RegisterStaffCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);

        var staffId = Guid.NewGuid();
        var credentialId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using (var staffCommand = connection.CreateCommand())
            {
                staffCommand.Transaction = transaction;
                staffCommand.CommandText = """
                    INSERT INTO fp_staff_members
                        (id, employee_code, name, department, job_title, active, created_at, updated_at)
                    VALUES
                        (@id, @employee_code, @name, @department, @job_title, 1, @created_at, @updated_at);
                    """;
                staffCommand.Parameters.AddWithValue("@id", staffId.ToString());
                staffCommand.Parameters.AddWithValue("@employee_code", (object?)command.EmployeeCode?.Trim() ?? DBNull.Value);
                staffCommand.Parameters.AddWithValue("@name", command.Name.Trim());
                staffCommand.Parameters.AddWithValue("@department", (object?)command.Department?.Trim() ?? DBNull.Value);
                staffCommand.Parameters.AddWithValue("@job_title", (object?)command.JobTitle?.Trim() ?? DBNull.Value);
                staffCommand.Parameters.AddWithValue("@created_at", now);
                staffCommand.Parameters.AddWithValue("@updated_at", now);
                await staffCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var credentialCommand = connection.CreateCommand())
            {
                credentialCommand.Transaction = transaction;
                credentialCommand.CommandText = """
                    INSERT INTO fp_staff_credentials
                        (id, staff_member_id, code, credential_type, valid_from, valid_until, active, created_at, updated_at)
                    VALUES
                        (@id, @staff_member_id, @code, @credential_type, @valid_from, @valid_until, 1, @created_at, @updated_at);
                    """;
                credentialCommand.Parameters.AddWithValue("@id", credentialId.ToString());
                credentialCommand.Parameters.AddWithValue("@staff_member_id", staffId.ToString());
                credentialCommand.Parameters.AddWithValue("@code", command.BadgeCode.Trim());
                credentialCommand.Parameters.AddWithValue("@credential_type", command.CredentialType.Trim());
                credentialCommand.Parameters.AddWithValue("@valid_from", (object?)command.ValidFrom?.UtcDateTime ?? DBNull.Value);
                credentialCommand.Parameters.AddWithValue("@valid_until", (object?)command.ValidUntil?.UtcDateTime ?? DBNull.Value);
                credentialCommand.Parameters.AddWithValue("@created_at", now);
                credentialCommand.Parameters.AddWithValue("@updated_at", now);
                await credentialCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new StaffConflictException("O código do funcionário ou do crachá já está cadastrado.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return new StaffCredentialView(
            staffId,
            credentialId,
            command.EmployeeCode?.Trim() ?? string.Empty,
            command.Name.Trim(),
            command.Department?.Trim(),
            command.JobTitle?.Trim(),
            command.BadgeCode.Trim(),
            command.CredentialType.Trim(),
            true,
            command.ValidFrom,
            command.ValidUntil);
    }

    public async Task<IReadOnlyList<StaffCredentialView>> ListAsync(
        bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                s.id,
                c.id,
                s.employee_code,
                s.name,
                s.department,
                s.job_title,
                c.code,
                c.credential_type,
                s.active AND c.active,
                c.valid_from,
                c.valid_until
            FROM fp_staff_members s
            INNER JOIN fp_staff_credentials c ON c.staff_member_id = s.id
            WHERE (@active_only = 0 OR (s.active = 1 AND c.active = 1))
            ORDER BY s.name, s.employee_code;
            """;
        command.Parameters.AddWithValue("@active_only", activeOnly ? 1 : 0);

        var result = new List<StaffCredentialView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new StaffCredentialView(
                Guid.Parse(reader.GetValue(0).ToString()!),
                Guid.Parse(reader.GetValue(1).ToString()!),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetBoolean(8),
                reader.IsDBNull(9) ? null : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(9), DateTimeKind.Utc)),
                reader.IsDBNull(10) ? null : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(10), DateTimeKind.Utc))));
        }

        return result;
    }

    public async Task<StaffAccessView> GrantAccessAsync(
        Guid staffId,
        Guid eventId,
        GrantStaffAccessCommand command,
        CancellationToken cancellationToken = default)
    {
        if (staffId == Guid.Empty || eventId == Guid.Empty)
        {
            throw new ArgumentException("StaffId e EventId são obrigatórios.");
        }

        if (string.IsNullOrWhiteSpace(command.Profile))
        {
            throw new ArgumentException("Profile é obrigatório.");
        }

        if (!Enum.TryParse<AccessDirection>(command.Direction, true, out var direction))
        {
            throw new ArgumentException("Direction deve ser Entry ou Exit.");
        }

        if (command.ValidFrom.HasValue && command.ValidUntil.HasValue && command.ValidFrom > command.ValidUntil)
        {
            throw new ArgumentException("ValidFrom não pode ser posterior a ValidUntil.");
        }

        var profile = command.Profile.Trim();
        var accessKey = BuildAccessKey(eventId, staffId, command.GateId, command.SectorId, profile, direction);
        var accessId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using (var staffCommand = connection.CreateCommand())
            {
                staffCommand.Transaction = transaction;
                staffCommand.CommandText = "SELECT COUNT(*) FROM fp_staff_members WHERE id = @staff_id AND active = 1;";
                staffCommand.Parameters.AddWithValue("@staff_id", staffId.ToString());
                var staffExists = Convert.ToInt32(await staffCommand.ExecuteScalarAsync(cancellationToken)) > 0;
                if (!staffExists)
                {
                    throw new ArgumentException("Funcionário não encontrado ou inativo.");
                }
            }

            await using var commandSql = connection.CreateCommand();
            commandSql.Transaction = transaction;
            commandSql.CommandText = """
                INSERT INTO fp_staff_event_access
                    (id, access_key, event_id, staff_member_id, gate_id, sector_id, profile, direction, valid_from, valid_until, active, created_at, updated_at)
                VALUES
                    (@id, @access_key, @event_id, @staff_member_id, @gate_id, @sector_id, @profile, @direction, @valid_from, @valid_until, 1, @created_at, @updated_at);
                """;
            commandSql.Parameters.AddWithValue("@id", accessId.ToString());
            commandSql.Parameters.AddWithValue("@access_key", accessKey);
            commandSql.Parameters.AddWithValue("@event_id", eventId.ToString());
            commandSql.Parameters.AddWithValue("@staff_member_id", staffId.ToString());
            commandSql.Parameters.AddWithValue("@gate_id", (object?)command.GateId?.ToString() ?? DBNull.Value);
            commandSql.Parameters.AddWithValue("@sector_id", (object?)command.SectorId?.ToString() ?? DBNull.Value);
            commandSql.Parameters.AddWithValue("@profile", profile);
            commandSql.Parameters.AddWithValue("@direction", direction.ToString());
            commandSql.Parameters.AddWithValue("@valid_from", (object?)command.ValidFrom?.UtcDateTime ?? DBNull.Value);
            commandSql.Parameters.AddWithValue("@valid_until", (object?)command.ValidUntil?.UtcDateTime ?? DBNull.Value);
            commandSql.Parameters.AddWithValue("@created_at", now);
            commandSql.Parameters.AddWithValue("@updated_at", now);
            await commandSql.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new StaffConflictException("Essa autorização já está cadastrada para o funcionário.");
        }
        catch (MySqlException exception) when (exception.Number == 1452)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ArgumentException("Evento, portaria ou setor não encontrado.", exception);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return new StaffAccessView(
            accessId,
            staffId,
            eventId,
            command.GateId,
            command.SectorId,
            profile,
            direction.ToString(),
            true,
            command.ValidFrom,
            command.ValidUntil);
    }

    public async Task<IReadOnlyList<StaffAccessView>> ListAccessAsync(
        Guid staffId,
        Guid? eventId,
        CancellationToken cancellationToken = default)
    {
        if (staffId == Guid.Empty)
        {
            throw new ArgumentException("StaffId é obrigatório.");
        }

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, event_id, gate_id, sector_id, profile, direction, active, valid_from, valid_until
            FROM fp_staff_event_access
            WHERE staff_member_id = @staff_id
              AND (@event_id IS NULL OR event_id = @event_id)
            ORDER BY event_id, profile, direction;
            """;
        command.Parameters.AddWithValue("@staff_id", staffId.ToString());
        command.Parameters.AddWithValue("@event_id", (object?)eventId?.ToString() ?? DBNull.Value);

        var result = new List<StaffAccessView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new StaffAccessView(
                ReadGuid(reader, 0),
                staffId,
                ReadGuid(reader, 1),
                ReadNullableGuid(reader, 2),
                ReadNullableGuid(reader, 3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetBoolean(6),
                ReadNullableDateTimeOffset(reader, 7),
                ReadNullableDateTimeOffset(reader, 8)));
        }

        return result;
    }

    private static string BuildAccessKey(
        Guid eventId,
        Guid staffId,
        Guid? gateId,
        Guid? sectorId,
        string profile,
        AccessDirection direction)
    {
        var canonical = string.Join(
            ':',
            eventId,
            staffId,
            gateId?.ToString() ?? "*",
            sectorId?.ToString() ?? "*",
            profile.ToUpperInvariant(),
            direction.ToString().ToUpperInvariant());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static Guid ReadGuid(MySqlDataReader reader, int ordinal) =>
        Guid.Parse(reader.GetValue(ordinal).ToString()!);

    private static Guid? ReadNullableGuid(MySqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadGuid(reader, ordinal);

    private static DateTimeOffset? ReadNullableDateTimeOffset(MySqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private static void Validate(RegisterStaffCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ArgumentException("Name é obrigatório.");
        if (string.IsNullOrWhiteSpace(command.BadgeCode))
            throw new ArgumentException("BadgeCode (código do crachá/QR) é obrigatório.");
    }

    public async Task<StaffCredentialView> UpdateAsync(
        Guid staffId,
        UpdateStaffCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ArgumentException("Name é obrigatório.");
        if (string.IsNullOrWhiteSpace(command.BadgeCode))
            throw new ArgumentException("BadgeCode (código do crachá/QR) é obrigatório.");

        var now = DateTime.UtcNow;
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            // Atualiza o colaborador
            await using var staffCmd = connection.CreateCommand();
            staffCmd.Transaction = transaction;
            staffCmd.CommandText = """
                UPDATE fp_staff_members
                SET employee_code=@ec, name=@name, department=@dep,
                    job_title=@jt, active=@active, updated_at=@now
                WHERE id=@id;
                """;
            staffCmd.Parameters.AddWithValue("@ec", (object?)command.EmployeeCode?.Trim() ?? DBNull.Value);
            staffCmd.Parameters.AddWithValue("@name", command.Name.Trim());
            staffCmd.Parameters.AddWithValue("@dep", (object?)command.Department?.Trim() ?? DBNull.Value);
            staffCmd.Parameters.AddWithValue("@jt", (object?)command.JobTitle?.Trim() ?? DBNull.Value);
            staffCmd.Parameters.AddWithValue("@active", command.Active ? 1 : 0);
            staffCmd.Parameters.AddWithValue("@now", now);
            staffCmd.Parameters.AddWithValue("@id", staffId.ToString());
            if (await staffCmd.ExecuteNonQueryAsync(cancellationToken) == 0)
                throw new ArgumentException("Colaborador não encontrado.");

            // Atualiza o crachá (primeiro crachá do membro)
            await using var credCmd = connection.CreateCommand();
            credCmd.Transaction = transaction;
            credCmd.CommandText = """
                UPDATE fp_staff_credentials
                SET code=@code, active=@active,
                    valid_from=@vf, valid_until=@vu, updated_at=@now
                WHERE staff_member_id=@id
                ORDER BY created_at ASC
                LIMIT 1;
                """;
            credCmd.Parameters.AddWithValue("@code", command.BadgeCode.Trim());
            credCmd.Parameters.AddWithValue("@active", command.Active ? 1 : 0);
            credCmd.Parameters.AddWithValue("@vf", (object?)command.ValidFrom?.UtcDateTime ?? DBNull.Value);
            credCmd.Parameters.AddWithValue("@vu", (object?)command.ValidUntil?.UtcDateTime ?? DBNull.Value);
            credCmd.Parameters.AddWithValue("@now", now);
            credCmd.Parameters.AddWithValue("@id", staffId.ToString());
            await credCmd.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new StaffConflictException("Esse código de crachá já está em uso por outro colaborador.");
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }

        // Relê o resultado atualizado
        var updated = await ListAsync(false, cancellationToken);
        return updated.FirstOrDefault(s => s.StaffId == staffId)
               ?? throw new ArgumentException("Colaborador não encontrado após atualização.");
    }
}
