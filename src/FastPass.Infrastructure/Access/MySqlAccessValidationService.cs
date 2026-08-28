using System.Text.Json;
using FastPass.Application.Access;
using FastPass.Domain.Enums;
using FastPass.Infrastructure.Database;
using MySqlConnector;

namespace FastPass.Infrastructure.Access;

public sealed class MySqlAccessValidationService : IAccessValidationService
{
    private readonly FastPassDbConnectionFactory _connectionFactory;
    private readonly IAccessMessageService _messageService;

    public MySqlAccessValidationService(
        FastPassDbConnectionFactory connectionFactory,
        IAccessMessageService messageService)
    {
        _connectionFactory = connectionFactory;
        _messageService = messageService;
    }

    public async Task<AccessValidationResult> ValidateAsync(
        ValidateAccessCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        var channel = ParseChannel(command.Channel);
        var direction = ParseDirection(command.Direction);
        var now = DateTime.UtcNow;

        if (channel == AccessChannel.App && direction != AccessDirection.Entry)
        {
            throw new ArgumentException("Validação por APP sempre usa a direção Entry.");
        }

        if (channel == AccessChannel.App && command.DeviceId.HasValue)
        {
            throw new ArgumentException("Validação por APP não aceita DeviceId.");
        }

        if (channel == AccessChannel.Turnstile && !command.DeviceId.HasValue)
        {
            throw new ArgumentException("DeviceId é obrigatório para validação por catraca.");
        }

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var replay = await ReadExistingAttemptAsync(connection, transaction, command.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay with { IdempotentReplay = true };
        }

        var gate = await ReadGateAsync(connection, transaction, command.EventId, command.GateId, cancellationToken);
        if (gate is null)
        {
            throw new ArgumentException("Portaria não encontrada, inativa ou não associada ao evento.");
        }

        if (channel == AccessChannel.Turnstile && !await IsActiveDeviceAtGateAsync(
                connection,
                transaction,
                command.EventId,
                command.GateId,
                command.DeviceId!.Value,
                cancellationToken))
        {
            throw new ArgumentException("DeviceId não encontrado, inativo ou pertencente a outra portaria.");
        }

        if (channel == AccessChannel.Turnstile
            && direction == AccessDirection.Exit
            && gate.OperationMode == GateOperationMode.EntryValidatedExitFree)
        {
            var resolvedFreeExitMessage = await _messageService.ResolveAsync(
                command.EventId,
                "ACESSO_CONCEDIDO",
                null,
                true,
                cancellationToken);
            var freeExit = BuildResult(
                command,
                channel,
                direction,
                approved: true,
                credentialType: "FreeExit",
                reason: null,
                attemptId: Guid.NewGuid(),
                ticketId: null,
                maximumEntries: null,
                entriesUsed: null,
                peopleInside: null,
                armAction: ArmAction.Unlock,
                pictogram: AccessPictogram.GreenArrowExit,
                resolvedMessage: resolvedFreeExitMessage);

            await InsertAttemptAsync(
                connection,
                transaction,
                command,
                channel,
                direction,
                "FreeExit",
                null,
                null,
                true,
                null,
                resolvedFreeExitMessage.Code,
                resolvedFreeExitMessage.Message,
                JsonSerializer.Serialize(resolvedFreeExitMessage.Presentation),
                freeExit.AttemptId,
                now,
                null,
                null,
                null,
                null,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return freeExit;
        }

        if (command.SectorId.HasValue && !await HasActiveSectorAsync(
                connection,
                transaction,
                command.EventId,
                command.SectorId.Value,
                cancellationToken))
        {
            throw new ArgumentException("SectorId não encontrado, inativo ou incompatível com o evento.");
        }

        var credential = string.IsNullOrWhiteSpace(command.CredentialCode)
            ? null
            : await ReadCredentialAsync(connection, transaction, command.CredentialCode.Trim(), cancellationToken);

        DecisionData decision;
        if (credential is not null)
        {
            decision = await ValidateStaffAsync(
                connection,
                transaction,
                command,
                channel,
                direction,
                credential,
                now,
                cancellationToken);
        }
        else
        {
            decision = await ValidateTicketAsync(
                connection,
                transaction,
                command,
                channel,
                direction,
                gate.OperationMode,
                now,
                cancellationToken);
        }

        var messageCode = ResolveMessageCode(decision.Reason, decision.Approved);
        var resolvedMessage = await _messageService.ResolveAsync(
            command.EventId,
            messageCode,
            decision.Reason,
            decision.Approved,
            cancellationToken);
        var result = BuildResult(
            command,
            channel,
            direction,
            decision.Approved,
            decision.CredentialType,
            decision.Reason,
            Guid.NewGuid(),
            decision.TicketId,
            decision.MaximumEntries,
            decision.EntriesUsedAfter,
            decision.PeopleInsideAfter,
            channel == AccessChannel.Turnstile && decision.Approved ? ArmAction.Unlock : (channel == AccessChannel.Turnstile ? ArmAction.KeepLocked : ArmAction.None),
            channel != AccessChannel.Turnstile
                ? AccessPictogram.None
                : decision.Approved
                    ? direction == AccessDirection.Entry ? AccessPictogram.GreenArrowEntry : AccessPictogram.GreenArrowExit
                    : AccessPictogram.RedCross,
            resolvedMessage: resolvedMessage) with
        {
            StaffCredentialId = credential?.CredentialId,
            StaffMemberId = credential?.StaffMemberId,
            StaffName = credential?.StaffName
        };

        try
        {
            await InsertAttemptAsync(
                connection,
                transaction,
                command,
                channel,
                direction,
                decision.CredentialType,
                decision.TicketId,
                credential?.CredentialId,
                decision.Approved,
                decision.Reason,
                resolvedMessage.Code,
                resolvedMessage.Message,
                JsonSerializer.Serialize(resolvedMessage.Presentation),
                result.AttemptId,
                now,
                decision.EntriesUsedBefore,
                decision.EntriesUsedAfter,
                decision.PeopleInsideBefore,
                decision.PeopleInsideAfter,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            var concurrentResult = await ReadExistingAttemptAsync(connection, null, command.IdempotencyKey, cancellationToken);
            if (concurrentResult is not null)
            {
                return concurrentResult with { IdempotentReplay = true };
            }

            throw;
        }
        catch (MySqlException exception) when (exception.Number == 1452)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ArgumentException("Evento, portaria, setor ou dispositivo não encontrado.", exception);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return result;
    }

    private static async Task<DecisionData> ValidateStaffAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ValidateAccessCommand command,
        AccessChannel channel,
        AccessDirection direction,
        CredentialData credential,
        DateTime now,
        CancellationToken cancellationToken)
    {
        string? reason = null;
        var approved = false;

        if (!credential.Active || !credential.StaffActive)
        {
            reason = "Crachá ou funcionário inativo.";
        }
        else if (credential.ValidFrom.HasValue && credential.ValidFrom.Value > now)
        {
            reason = "Crachá ainda não está válido.";
        }
        else if (credential.ValidUntil.HasValue && credential.ValidUntil.Value < now)
        {
            reason = "Crachá expirado.";
        }
        else if (!await HasAuthorizationAsync(
                     connection,
                     transaction,
                     command,
                     credential.StaffMemberId,
                     direction,
                     now,
                     cancellationToken))
        {
            reason = "Crachá sem autorização para este evento, portaria, setor ou direção.";
        }
        else
        {
            approved = true;
        }

        return new DecisionData(
            approved,
            CredentialType.StaffBadge.ToString(),
            null,
            null,
            null,
            null,
            null,
            null,
            reason);
    }

    private static async Task<DecisionData> ValidateTicketAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ValidateAccessCommand command,
        AccessChannel channel,
        AccessDirection direction,
        GateOperationMode operationMode,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var ticket = await ReadTicketAsync(
            connection,
            transaction,
            command.EventId,
            command.CredentialCode.Trim(),
            cancellationToken);

        if (ticket is null)
        {
            return new DecisionData(
                false,
                CredentialType.Ticket.ToString(),
                null,
                null,
                null,
                null,
                null,
                null,
                "Ingresso não encontrado.");
        }

        var entriesBefore = ticket.EntriesUsed;
        var peopleInsideBefore = ticket.PeopleInside;
        string? reason = null;
        var approved = false;
        var entriesAfter = entriesBefore;
        var peopleInsideAfter = peopleInsideBefore;

        if (!string.Equals(ticket.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Ingresso inativo.";
        }
        else if (ticket.SectorId.HasValue && !await HasActiveGateSectorAsync(
                     connection,
                     transaction,
                     command.EventId,
                     command.GateId,
                     ticket.SectorId.Value,
                     direction,
                     now,
                     cancellationToken))
        {
            reason = "Portaria sem autorização para o setor deste ingresso.";
        }
        else if (command.SectorId.HasValue && command.SectorId != ticket.SectorId && !await HasActiveGateSectorAsync(
                     connection,
                     transaction,
                     command.EventId,
                     command.GateId,
                     command.SectorId.Value,
                     direction,
                     now,
                     cancellationToken))
        {
            reason = "Ingresso sem associação ativa entre portaria, setor e direção.";
        }
        else if (!await HasTicketAuthorizationAsync(
                     connection,
                     transaction,
                     command,
                     ticket,
                     direction,
                     cancellationToken))
        {
            reason = "Ingresso sem autorização para este evento, portaria, setor ou direção.";
        }
        else if (direction == AccessDirection.Entry && ticket.EntriesUsed >= ticket.MaximumEntries)
        {
            reason = "Limite de entradas do ingresso atingido.";
        }
        else if (direction == AccessDirection.Exit && operationMode == GateOperationMode.EntryAndExitValidated && ticket.PeopleInside <= 0)
        {
            reason = "Não há presença registrada para este ingresso.";
        }
        else
        {
            var trackPresence = channel == AccessChannel.Turnstile
                && operationMode == GateOperationMode.EntryAndExitValidated;

            if (direction == AccessDirection.Entry)
            {
                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = trackPresence
                    ? """
                      UPDATE fp_tickets
                      SET entries_used = entries_used + 1,
                          uses = uses + 1,
                          people_inside = people_inside + 1,
                          updated_at = @updated_at
                      WHERE id = @id
                        AND status = 'active'
                        AND entries_used < maximum_entries;
                      """
                    : """
                      UPDATE fp_tickets
                      SET entries_used = entries_used + 1,
                          uses = uses + 1,
                          updated_at = @updated_at
                      WHERE id = @id
                        AND status = 'active'
                        AND entries_used < maximum_entries;
                      """;
                update.Parameters.AddWithValue("@updated_at", now);
                update.Parameters.AddWithValue("@id", ticket.TicketId.ToString());

                if (await update.ExecuteNonQueryAsync(cancellationToken) == 1)
                {
                    approved = true;
                    entriesAfter++;
                    if (trackPresence)
                    {
                        peopleInsideAfter++;
                    }
                }
                else
                {
                    reason = "Ingresso atingiu o limite de entradas durante a validação.";
                }
            }
            else
            {
                var updatePeopleInside = trackPresence;
                if (updatePeopleInside)
                {
                    await using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = """
                        UPDATE fp_tickets
                        SET people_inside = people_inside - 1,
                            updated_at = @updated_at
                        WHERE id = @id
                          AND status = 'active'
                          AND people_inside > 0;
                        """;
                    update.Parameters.AddWithValue("@updated_at", now);
                    update.Parameters.AddWithValue("@id", ticket.TicketId.ToString());
                    approved = await update.ExecuteNonQueryAsync(cancellationToken) == 1;
                }
                else
                {
                    approved = true;
                }

                if (approved && updatePeopleInside)
                {
                    peopleInsideAfter--;
                }
                else if (!approved)
                {
                    reason = "Não há presença registrada para este ingresso durante a saída.";
                }
            }
        }

        return new DecisionData(
            approved,
            CredentialType.Ticket.ToString(),
            ticket.TicketId,
            ticket.MaximumEntries,
            entriesBefore,
            entriesAfter,
            peopleInsideBefore,
            peopleInsideAfter,
            reason);
    }

    private static async Task<GateData?> ReadGateAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid eventId,
        Guid gateId,
        CancellationToken cancellationToken)
    {
        await using var sql = connection.CreateCommand();
        sql.Transaction = transaction;
        sql.CommandText = """
            SELECT eg.operation_mode
            FROM fp_event_gates eg
            INNER JOIN fp_gates g ON g.id = eg.gate_id
            WHERE eg.event_id = @event_id
              AND eg.gate_id = @gate_id
              AND eg.active = 1
              AND g.active = 1
            LIMIT 1;
            """;
        sql.Parameters.AddWithValue("@event_id", eventId.ToString());
        sql.Parameters.AddWithValue("@gate_id", gateId.ToString());

        var value = await sql.ExecuteScalarAsync(cancellationToken);
        if (value is null || value is DBNull)
        {
            return null;
        }

        if (!Enum.TryParse<GateOperationMode>(value.ToString(), true, out var operationMode))
        {
            throw new ArgumentException("Modo operacional inválido configurado para a portaria.");
        }

        return new GateData(operationMode);
    }

    private static async Task<bool> IsActiveDeviceAtGateAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid eventId,
        Guid gateId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        await using var sql = connection.CreateCommand();
        sql.Transaction = transaction;
        sql.CommandText = """
            SELECT COUNT(*)
            FROM fp_devices d
            INNER JOIN fp_event_gates eg
                ON eg.gate_id = d.gate_id
               AND eg.event_id = @event_id
            INNER JOIN fp_gates g ON g.id = d.gate_id
            WHERE d.id = @device_id
              AND d.gate_id = @gate_id
              AND d.active = 1
              AND eg.active = 1
              AND g.active = 1;
            """;
        sql.Parameters.AddWithValue("@event_id", eventId.ToString());
        sql.Parameters.AddWithValue("@gate_id", gateId.ToString());
        sql.Parameters.AddWithValue("@device_id", deviceId.ToString());
        return Convert.ToInt32(await sql.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<TicketData?> ReadTicketAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid eventId,
        string code,
        CancellationToken cancellationToken)
    {
        await using var sql = connection.CreateCommand();
        sql.Transaction = transaction;
        sql.CommandText = """
            SELECT id, ticket_type_id, batch_id, status, maximum_uses, uses,
                   maximum_entries, entries_used, people_inside, sector_id
            FROM fp_tickets
            WHERE event_id = @event_id
              AND code = @code
            LIMIT 1
            FOR UPDATE;
            """;
        sql.Parameters.AddWithValue("@event_id", eventId.ToString());
        sql.Parameters.AddWithValue("@code", code);

        await using var reader = await sql.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new TicketData(
            ReadGuid(reader, 0),
            ReadGuid(reader, 1),
            ReadNullableGuid(reader, 2),
            reader.GetString(3),
            Convert.ToInt32(reader.GetValue(4)),
            Convert.ToInt32(reader.GetValue(5)),
            Convert.ToInt32(reader.GetValue(6)),
            Convert.ToInt32(reader.GetValue(7)),
            Convert.ToInt32(reader.GetValue(8)),
            ReadNullableGuid(reader, 9));
    }

    private static async Task<bool> HasActiveGateSectorAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid eventId,
        Guid gateId,
        Guid sectorId,
        AccessDirection direction,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var sql = connection.CreateCommand();
        sql.Transaction = transaction;
        sql.CommandText = """
            SELECT COUNT(*)
            FROM fp_gate_sectors gs
            INNER JOIN fp_event_gates eg
                ON eg.event_id = gs.event_id
               AND eg.gate_id = gs.gate_id
            INNER JOIN fp_gates g ON g.id = gs.gate_id
            INNER JOIN fp_sectors s ON s.id = gs.sector_id
            WHERE gs.event_id = @event_id
              AND gs.gate_id = @gate_id
              AND gs.sector_id = @sector_id
              AND gs.direction = @direction
              AND gs.active = 1
              AND eg.active = 1
              AND g.active = 1
              AND s.active = 1
              AND (gs.active_from IS NULL OR gs.active_from <= @now)
              AND (gs.active_until IS NULL OR gs.active_until >= @now);
            """;
        sql.Parameters.AddWithValue("@event_id", eventId.ToString());
        sql.Parameters.AddWithValue("@gate_id", gateId.ToString());
        sql.Parameters.AddWithValue("@sector_id", sectorId.ToString());
        sql.Parameters.AddWithValue("@direction", direction.ToString());
        sql.Parameters.AddWithValue("@now", now);
        return Convert.ToInt32(await sql.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<bool> HasTicketAuthorizationAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ValidateAccessCommand command,
        TicketData ticket,
        AccessDirection direction,
        CancellationToken cancellationToken)
    {
        await using var sql = connection.CreateCommand();
        sql.Transaction = transaction;
        sql.CommandText = """
            SELECT allows
            FROM fp_access_policies
            WHERE event_id = @event_id
              AND active = 1
              AND direction = @direction
              AND (gate_id IS NULL OR gate_id = @gate_id)
              AND (sector_id IS NULL OR (@sector_id IS NOT NULL AND sector_id = @sector_id))
              AND (ticket_type_id IS NULL OR ticket_type_id = @ticket_type_id)
              AND (batch_id IS NULL OR (@batch_id IS NOT NULL AND batch_id = @batch_id))
            ORDER BY priority DESC
            LIMIT 1;
            """;
        sql.Parameters.AddWithValue("@event_id", command.EventId.ToString());
        sql.Parameters.AddWithValue("@direction", direction.ToString());
        sql.Parameters.AddWithValue("@gate_id", command.GateId.ToString());
        sql.Parameters.AddWithValue("@sector_id", (object?)command.SectorId?.ToString() ?? DBNull.Value);
        sql.Parameters.AddWithValue("@ticket_type_id", ticket.TicketTypeId.ToString());
        sql.Parameters.AddWithValue("@batch_id", (object?)ticket.BatchId?.ToString() ?? DBNull.Value);

        var value = await sql.ExecuteScalarAsync(cancellationToken);
        if (value is not null && value is not DBNull)
        {
            return Convert.ToBoolean(value);
        }

        // Quando a matriz física está ativa, ela é a autorização operacional padrão
        // para tickets com setor. Políticas explícitas continuam podendo permitir ou negar.
        return command.SectorId.HasValue;
    }

    private static async Task<bool> HasActiveSectorAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid eventId,
        Guid sectorId,
        CancellationToken cancellationToken)
    {
        await using var sql = connection.CreateCommand();
        sql.Transaction = transaction;
        sql.CommandText = "SELECT COUNT(*) FROM fp_sectors WHERE id = @sector_id AND event_id = @event_id AND active = 1;";
        sql.Parameters.AddWithValue("@sector_id", sectorId.ToString());
        sql.Parameters.AddWithValue("@event_id", eventId.ToString());
        return Convert.ToInt32(await sql.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<CredentialData?> ReadCredentialAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string code,
        CancellationToken cancellationToken)
    {
        await using var sql = connection.CreateCommand();
        sql.Transaction = transaction;
        sql.CommandText = """
            SELECT c.id, s.id, s.name, c.active, s.active, c.valid_from, c.valid_until
            FROM fp_staff_credentials c
            INNER JOIN fp_staff_members s ON s.id = c.staff_member_id
            WHERE c.code = @code
            LIMIT 1;
            """;
        sql.Parameters.AddWithValue("@code", code);

        await using var reader = await sql.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CredentialData(
            ReadGuid(reader, 0),
            ReadGuid(reader, 1),
            reader.GetString(2),
            Convert.ToBoolean(reader.GetValue(3)),
            Convert.ToBoolean(reader.GetValue(4)),
            ReadNullableDateTime(reader, 5),
            ReadNullableDateTime(reader, 6));
    }

    private static async Task<bool> HasAuthorizationAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ValidateAccessCommand command,
        Guid staffMemberId,
        AccessDirection direction,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var sql = connection.CreateCommand();
        sql.Transaction = transaction;
        sql.CommandText = """
            SELECT COUNT(*)
            FROM fp_staff_event_access
            WHERE event_id = @event_id
              AND staff_member_id = @staff_member_id
              AND active = 1
              AND (gate_id IS NULL OR gate_id = @gate_id)
              AND (sector_id IS NULL OR (@sector_id IS NOT NULL AND sector_id = @sector_id))
              AND direction = @direction
              AND (valid_from IS NULL OR valid_from <= @now)
              AND (valid_until IS NULL OR valid_until >= @now);
            """;
        sql.Parameters.AddWithValue("@event_id", command.EventId.ToString());
        sql.Parameters.AddWithValue("@staff_member_id", staffMemberId.ToString());
        sql.Parameters.AddWithValue("@gate_id", command.GateId.ToString());
        sql.Parameters.AddWithValue("@sector_id", (object?)command.SectorId?.ToString() ?? DBNull.Value);
        sql.Parameters.AddWithValue("@direction", direction.ToString());
        sql.Parameters.AddWithValue("@now", now);
        return Convert.ToInt32(await sql.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task InsertAttemptAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ValidateAccessCommand command,
        AccessChannel channel,
        AccessDirection direction,
        string credentialType,
        Guid? ticketId,
        Guid? staffCredentialId,
        bool approved,
        string? reason,
        string reasonCode,
        string message,
        string presentationJson,
        Guid attemptId,
        DateTime now,
        int? entriesBefore,
        int? entriesAfter,
        int? peopleInsideBefore,
        int? peopleInsideAfter,
        CancellationToken cancellationToken)
    {
        await using var sql = connection.CreateCommand();
        sql.Transaction = transaction;
        sql.CommandText = """
            INSERT INTO fp_access_attempts
                (id, event_id, ticket_id, staff_credential_id, gate_id, sector_id, device_id,
                 credential_code, credential_type, direction, decision, reason, reason_code, message, message_presentation_json, status,
                 idempotency_key, requested_at, created_at, channel, arm_action, pictogram,
                 entries_before, entries_after, people_inside_before, people_inside_after)
            VALUES
                (@id, @event_id, @ticket_id, @staff_credential_id, @gate_id, @sector_id, @device_id,
                 @credential_code, @credential_type, @direction, @decision, @reason, @reason_code, @message, @message_presentation_json, @status,
                 @idempotency_key, @requested_at, @created_at, @channel, @arm_action, @pictogram,
                 @entries_before, @entries_after, @people_inside_before, @people_inside_after);
            """;
        sql.Parameters.AddWithValue("@id", attemptId.ToString());
        sql.Parameters.AddWithValue("@event_id", command.EventId.ToString());
        sql.Parameters.AddWithValue("@ticket_id", (object?)ticketId?.ToString() ?? DBNull.Value);
        sql.Parameters.AddWithValue("@staff_credential_id", (object?)staffCredentialId?.ToString() ?? DBNull.Value);
        sql.Parameters.AddWithValue("@gate_id", command.GateId.ToString());
        sql.Parameters.AddWithValue("@sector_id", (object?)command.SectorId?.ToString() ?? DBNull.Value);
        sql.Parameters.AddWithValue("@device_id", (object?)command.DeviceId?.ToString() ?? DBNull.Value);
        sql.Parameters.AddWithValue("@credential_code", command.CredentialCode?.Trim() ?? string.Empty);
        sql.Parameters.AddWithValue("@credential_type", credentialType);
        sql.Parameters.AddWithValue("@direction", direction.ToString());
        sql.Parameters.AddWithValue("@decision", approved ? AccessDecision.Approved.ToString() : AccessDecision.Rejected.ToString());
        sql.Parameters.AddWithValue("@reason", (object?)reason ?? DBNull.Value);
        sql.Parameters.AddWithValue("@reason_code", reasonCode);
        sql.Parameters.AddWithValue("@message", message);
        sql.Parameters.AddWithValue("@message_presentation_json", presentationJson);
        sql.Parameters.AddWithValue("@status", AccessAttemptStatus.Decided.ToString());
        sql.Parameters.AddWithValue("@idempotency_key", command.IdempotencyKey.Trim());
        sql.Parameters.AddWithValue("@requested_at", now);
        sql.Parameters.AddWithValue("@created_at", now);
        sql.Parameters.AddWithValue("@channel", channel.ToString());
        sql.Parameters.AddWithValue("@arm_action", channel == AccessChannel.Turnstile
            ? approved ? ArmAction.Unlock.ToString() : ArmAction.KeepLocked.ToString()
            : ArmAction.None.ToString());
        sql.Parameters.AddWithValue("@pictogram", channel == AccessChannel.Turnstile
            ? approved
                ? direction == AccessDirection.Entry ? AccessPictogram.GreenArrowEntry.ToString() : AccessPictogram.GreenArrowExit.ToString()
                : AccessPictogram.RedCross.ToString()
            : AccessPictogram.None.ToString());
        sql.Parameters.AddWithValue("@entries_before", (object?)entriesBefore ?? DBNull.Value);
        sql.Parameters.AddWithValue("@entries_after", (object?)entriesAfter ?? DBNull.Value);
        sql.Parameters.AddWithValue("@people_inside_before", (object?)peopleInsideBefore ?? DBNull.Value);
        sql.Parameters.AddWithValue("@people_inside_after", (object?)peopleInsideAfter ?? DBNull.Value);
        await sql.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<AccessValidationResult?> ReadExistingAttemptAsync(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var sql = connection.CreateCommand();
        sql.Transaction = transaction;
        sql.CommandText = """
            SELECT a.id, a.decision, a.reason, a.credential_type, a.staff_credential_id,
                   s.id, s.name, a.channel, a.direction, a.ticket_id,
                   a.entries_after, a.people_inside_after, a.arm_action, a.pictogram,
                   t.maximum_entries, a.reason_code, a.message, a.message_presentation_json
            FROM fp_access_attempts a
            LEFT JOIN fp_staff_credentials c ON c.id = a.staff_credential_id
            LEFT JOIN fp_staff_members s ON s.id = c.staff_member_id
            LEFT JOIN fp_tickets t ON t.id = a.ticket_id
            WHERE a.idempotency_key = @idempotency_key
            LIMIT 1;
            """;
        sql.Parameters.AddWithValue("@idempotency_key", idempotencyKey.Trim());

        await using var reader = await sql.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var decision = reader.GetString(1);
        return new AccessValidationResult(
            ReadGuid(reader, 0),
            string.Equals(decision, AccessDecision.Approved.ToString(), StringComparison.OrdinalIgnoreCase),
            decision,
            reader.GetString(3),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            ReadNullableGuid(reader, 4),
            ReadNullableGuid(reader, 5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            false,
            reader.IsDBNull(7) ? AccessChannel.Turnstile.ToString() : reader.GetString(7),
            reader.IsDBNull(8) ? AccessDirection.Entry.ToString() : reader.GetString(8),
            ReadNullableGuid(reader, 9),
            reader.IsDBNull(14) ? null : Convert.ToInt32(reader.GetValue(14)),
            reader.IsDBNull(10) ? null : Convert.ToInt32(reader.GetValue(10)),
            reader.IsDBNull(11) ? null : Convert.ToInt32(reader.GetValue(11)),
            reader.IsDBNull(12) ? ArmAction.None.ToString() : reader.GetString(12),
            reader.IsDBNull(13) ? AccessPictogram.None.ToString() : reader.GetString(13),
            reader.IsDBNull(15) ? null : reader.GetString(15),
            reader.IsDBNull(16) ? null : reader.GetString(16),
            reader.IsDBNull(17) ? null : JsonSerializer.Deserialize<AccessMessagePresentation>(reader.GetString(17)));
    }

    private static AccessValidationResult BuildResult(
        ValidateAccessCommand command,
        AccessChannel channel,
        AccessDirection direction,
        bool approved,
        string credentialType,
        string? reason,
        Guid attemptId,
        Guid? ticketId,
        int? maximumEntries,
        int? entriesUsed,
        int? peopleInside,
        ArmAction armAction,
        AccessPictogram pictogram,
        ResolvedAccessMessage resolvedMessage)
    {
        return new AccessValidationResult(
            attemptId,
            approved,
            approved ? AccessDecision.Approved.ToString() : AccessDecision.Rejected.ToString(),
            credentialType,
            reason,
            null,
            null,
            null,
            false,
            channel.ToString(),
            direction.ToString(),
            ticketId,
            maximumEntries,
            entriesUsed,
            peopleInside,
            armAction.ToString(),
            pictogram.ToString(),
            resolvedMessage.Code,
            resolvedMessage.Message,
            resolvedMessage.Presentation);
    }

    private static string ResolveMessageCode(string? reason, bool approved)
    {
        if (approved) return "ACESSO_CONCEDIDO";
        return reason switch
        {
            "Crachá ou funcionário inativo." => "CRACHA_INATIVO",
            "Crachá ainda não está válido." => "CRACHA_AINDA_NAO_VALIDO",
            "Crachá expirado." => "CRACHA_EXPIRADO",
            "Crachá sem autorização para este evento, portaria, setor ou direção." => "CRACHA_SEM_AUTORIZACAO",
            "Ingresso não encontrado." => "INGRESSO_NAO_ENCONTRADO",
            "Ingresso inativo." => "INGRESSO_INATIVO",
            "Ingresso sem associação ativa entre portaria, setor e direção." => "MATRIZ_NAO_AUTORIZADA",
            "Ingresso sem autorização para este evento, portaria, setor ou direção." => "INGRESSO_SEM_AUTORIZACAO",
            "Limite de entradas do ingresso atingido." => "LIMITE_ENTRADAS_ATINGIDO",
            "Não há presença registrada para este ingresso." => "PRESENCA_NAO_REGISTRADA",
            "Ingresso atingiu o limite de entradas durante a validação." => "LIMITE_ENTRADAS_CONCORRENTE",
            "Não há presença registrada para este ingresso durante a saída." => "SAIDA_SEM_PRESENCA",
            _ => "ACESSO_NEGADO"
        };
    }

    private static void Validate(ValidateAccessCommand command)
    {
        if (command.EventId == Guid.Empty || command.GateId == Guid.Empty)
        {
            throw new ArgumentException("EventId e GateId são obrigatórios.");
        }

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new ArgumentException("IdempotencyKey é obrigatório.");
        }

        var channel = ParseChannel(command.Channel);
        var direction = ParseDirection(command.Direction);
        if (channel != AccessChannel.Turnstile
            || direction != AccessDirection.Exit)
        {
            if (string.IsNullOrWhiteSpace(command.CredentialCode))
            {
                throw new ArgumentException("CredentialCode é obrigatório para esta validação.");
            }
        }
    }

    private static AccessChannel ParseChannel(string channel)
    {
        if (Enum.TryParse<AccessChannel>(channel, true, out var result))
        {
            return result == AccessChannel.Api ? AccessChannel.Turnstile : result;
        }

        throw new ArgumentException("Channel deve ser App ou Turnstile.");
    }

    private static AccessDirection ParseDirection(string direction)
    {
        if (Enum.TryParse<AccessDirection>(direction, true, out var result))
        {
            return result;
        }

        throw new ArgumentException("Direction deve ser Entry ou Exit.");
    }

    private static Guid ReadGuid(MySqlDataReader reader, int ordinal) =>
        Guid.Parse(reader.GetValue(ordinal).ToString()!);

    private static Guid? ReadNullableGuid(MySqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadGuid(reader, ordinal);

    private static DateTime? ReadNullableDateTime(MySqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);

    private sealed record GateData(GateOperationMode OperationMode);

    private sealed record TicketData(
        Guid TicketId,
        Guid TicketTypeId,
        Guid? BatchId,
        string Status,
        int MaximumUses,
        int Uses,
        int MaximumEntries,
        int EntriesUsed,
        int PeopleInside,
        Guid? SectorId);

    private sealed record CredentialData(
        Guid CredentialId,
        Guid StaffMemberId,
        string StaffName,
        bool Active,
        bool StaffActive,
        DateTime? ValidFrom,
        DateTime? ValidUntil);

    private sealed record DecisionData(
        bool Approved,
        string CredentialType,
        Guid? TicketId,
        int? MaximumEntries,
        int? EntriesUsedBefore,
        int? EntriesUsedAfter,
        int? PeopleInsideBefore,
        int? PeopleInsideAfter,
        string? Reason);
}
