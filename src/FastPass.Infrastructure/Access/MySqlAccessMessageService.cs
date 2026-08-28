using System.Text.RegularExpressions;
using FastPass.Application.Access;
using FastPass.Infrastructure.Database;
using MySqlConnector;

namespace FastPass.Infrastructure.Access;

/// <summary>
/// Mensagens de validação seguem um padrão GLOBAL (fp_access_message_templates), compartilhado
/// por todos os eventos. Cada evento pode opcionalmente customizar um código específico —
/// esse override fica em fp_access_messages e tem prioridade sobre o template padrão.
/// Editar o template padrão afeta automaticamente todos os eventos que não tiverem override.
/// </summary>
public sealed class MySqlAccessMessageService : IAccessMessageService
{
    // Fallback de última instância em memória — só usado se a tabela de templates estiver vazia
    // (não deveria acontecer em condições normais, pois a migration 014 semeia os 14 códigos).
    private static readonly IReadOnlyDictionary<string, MessageDefaults> Defaults =
        new Dictionary<string, MessageDefaults>(StringComparer.OrdinalIgnoreCase)
        {
            ["ACESSO_CONCEDIDO"] = new("Acesso liberado", "Acesso liberado com sucesso.", "#11998E", "#38EF7D"),
            ["ACESSO_NEGADO"] = new("Acesso negado", "Acesso não autorizado.", "#EB3349", "#F45C43"),
            ["CRACHA_INATIVO"] = new("Acesso negado", "Crachá ou funcionário inativo.", "#EB3349", "#F45C43"),
            ["CRACHA_AINDA_NAO_VALIDO"] = new("Acesso negado", "Crachá ainda não está válido.", "#EB3349", "#F45C43"),
            ["CRACHA_EXPIRADO"] = new("Acesso negado", "Crachá expirado.", "#EB3349", "#F45C43"),
            ["CRACHA_SEM_AUTORIZACAO"] = new("Acesso negado", "Crachá sem autorização para este evento, portaria, setor ou direção.", "#EB3349", "#F45C43"),
            ["INGRESSO_NAO_ENCONTRADO"] = new("Acesso negado", "Ingresso não encontrado.", "#EB3349", "#F45C43"),
            ["INGRESSO_INATIVO"] = new("Acesso negado", "Ingresso inativo.", "#EB3349", "#F45C43"),
            ["MATRIZ_NAO_AUTORIZADA"] = new("Acesso negado", "Ingresso sem associação ativa entre portaria, setor e direção.", "#EB3349", "#F45C43"),
            ["INGRESSO_SEM_AUTORIZACAO"] = new("Acesso negado", "Ingresso sem autorização para este evento, portaria, setor ou direção.", "#EB3349", "#F45C43"),
            ["LIMITE_ENTRADAS_ATINGIDO"] = new("Acesso negado", "Limite de entradas do ingresso atingido.", "#EB3349", "#F45C43"),
            ["PRESENCA_NAO_REGISTRADA"] = new("Acesso negado", "Não há presença registrada para este ingresso.", "#EB3349", "#F45C43"),
            ["LIMITE_ENTRADAS_CONCORRENTE"] = new("Acesso negado", "Ingresso atingiu o limite de entradas durante a validação.", "#EB3349", "#F45C43"),
            ["SAIDA_SEM_PRESENCA"] = new("Acesso negado", "Não há presença registrada para este ingresso durante a saída.", "#EB3349", "#F45C43"),
        };

    private readonly FastPassDbConnectionFactory _connectionFactory;

    public MySqlAccessMessageService(FastPassDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // ── Templates globais ───────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AccessMessageTemplateView>> ListTemplatesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, code, title, message, background_start, background_end, title_color, message_color,
                   title_size, message_size, title_bold, message_bold, active, updated_at
            FROM fp_access_message_templates
            ORDER BY code;
            """;
        var result = new List<AccessMessageTemplateView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AccessMessageTemplateView(
                ReadGuid(reader, 0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetString(8), reader.GetString(9), Convert.ToBoolean(reader.GetValue(10)),
                Convert.ToBoolean(reader.GetValue(11)), Convert.ToBoolean(reader.GetValue(12)),
                ReadDateTimeOffset(reader, 13)));
        }
        return result;
    }

    public async Task<AccessMessageTemplateView> UpdateTemplateAsync(
        string code,
        UpdateAccessMessageTemplateCommand command,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeCode(code, true);
        ValidateTemplateCommand(command);
        var now = DateTime.UtcNow;
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        // Verifica existência ANTES do UPDATE — o MySqlConnector usa UseAffectedRows=true por
        // padrão, então um UPDATE que não altera nenhum valor retorna 0 linhas mesmo que o
        // registro exista, o que quebraria a checagem "não encontrado" se feita depois.
        if (!await ExistsAsync(connection, null, "SELECT COUNT(*) FROM fp_access_message_templates WHERE code = @code;",
                cancellationToken, ("@code", normalizedCode)))
            throw new KeyNotFoundException($"Template padrão '{normalizedCode}' não encontrado.");

        await ExecuteAsync(connection, null, """
            UPDATE fp_access_message_templates
            SET title = @title, message = @message,
                background_start = @background_start, background_end = @background_end,
                title_color = @title_color, message_color = @message_color,
                title_size = @title_size, message_size = @message_size,
                title_bold = @title_bold, message_bold = @message_bold,
                active = @active, updated_at = @updated_at
            WHERE code = @code;
            """, cancellationToken,
            ("@title", command.Title.Trim()),
            ("@message", command.Message.Trim()),
            ("@background_start", command.BackgroundStart.Trim().ToUpperInvariant()),
            ("@background_end", command.BackgroundEnd.Trim().ToUpperInvariant()),
            ("@title_color", command.TitleColor.Trim().ToUpperInvariant()),
            ("@message_color", command.MessageColor.Trim().ToUpperInvariant()),
            ("@title_size", command.TitleSize.Trim()),
            ("@message_size", command.MessageSize.Trim()),
            ("@title_bold", command.TitleBold ? 1 : 0),
            ("@message_bold", command.MessageBold ? 1 : 0),
            ("@active", command.Active ? 1 : 0),
            ("@updated_at", now),
            ("@code", normalizedCode));

        var result = (await ListTemplatesAsync(cancellationToken)).SingleOrDefault(t => t.Code == normalizedCode);
        return result ?? throw new KeyNotFoundException($"Template padrão '{normalizedCode}' não encontrado.");
    }

    // ── Mensagens por evento (herdam do template, exceto onde houver override) ──

    public async Task<IReadOnlyList<AccessMessageView>> ListAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        ValidateEventId(eventId);
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        return await ReadListAsync(connection, eventId, cancellationToken);
    }

    public async Task<AccessMessageView> UpdateAsync(
        Guid eventId,
        string code,
        UpdateAccessMessageCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateEventId(eventId);
        var normalizedCode = NormalizeCode(code, true);
        ValidateCommand(command);
        var now = DateTime.UtcNow;
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await ExistsAsync(connection, transaction, "SELECT COUNT(*) FROM fp_events WHERE id = @event_id;", cancellationToken, ("@event_id", eventId.ToString())))
                throw new ArgumentException("Evento não encontrado.");

            if (!await ExistsAsync(connection, transaction, "SELECT COUNT(*) FROM fp_access_message_templates WHERE code = @code;", cancellationToken, ("@code", normalizedCode)))
                throw new ArgumentException($"Código de mensagem '{normalizedCode}' não é reconhecido.");

            // Upsert do override deste evento — cria a customização ou atualiza a existente.
            await ExecuteAsync(connection, transaction, """
                INSERT INTO fp_access_messages
                    (id, event_id, code, title, message, background_start, background_end,
                     title_color, message_color, title_size, message_size, title_bold, message_bold,
                     active, system_message, created_at, updated_at)
                VALUES
                    (@id, @event_id, @code, @title, @message, @background_start, @background_end,
                     @title_color, @message_color, @title_size, @message_size, @title_bold, @message_bold,
                     @active, 1, @created_at, @updated_at)
                ON DUPLICATE KEY UPDATE
                    title = @title, message = @message,
                    background_start = @background_start, background_end = @background_end,
                    title_color = @title_color, message_color = @message_color,
                    title_size = @title_size, message_size = @message_size,
                    title_bold = @title_bold, message_bold = @message_bold,
                    active = @active, updated_at = @updated_at;
                """, cancellationToken,
                ("@id", Guid.NewGuid().ToString()),
                ("@event_id", eventId.ToString()),
                ("@code", normalizedCode),
                ("@title", command.Title.Trim()),
                ("@message", command.Message.Trim()),
                ("@background_start", command.BackgroundStart.Trim().ToUpperInvariant()),
                ("@background_end", command.BackgroundEnd.Trim().ToUpperInvariant()),
                ("@title_color", command.TitleColor.Trim().ToUpperInvariant()),
                ("@message_color", command.MessageColor.Trim().ToUpperInvariant()),
                ("@title_size", command.TitleSize.Trim()),
                ("@message_size", command.MessageSize.Trim()),
                ("@title_bold", command.TitleBold ? 1 : 0),
                ("@message_bold", command.MessageBold ? 1 : 0),
                ("@active", command.Active ? 1 : 0),
                ("@created_at", now),
                ("@updated_at", now));
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        var result = (await ListAsync(eventId, cancellationToken)).SingleOrDefault(item => item.Code == normalizedCode);
        return result ?? throw new KeyNotFoundException("Mensagem de validação não encontrada.");
    }

    public async Task RestoreDefaultAsync(Guid eventId, string code, CancellationToken cancellationToken = default)
    {
        ValidateEventId(eventId);
        var normalizedCode = NormalizeCode(code, true);
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, null, """
            DELETE FROM fp_access_messages WHERE event_id = @event_id AND code = @code;
            """, cancellationToken, ("@event_id", eventId.ToString()), ("@code", normalizedCode));
    }

    public async Task<ResolvedAccessMessage> ResolveAsync(Guid eventId, string code, string? fallback, bool approved, CancellationToken cancellationToken = default)
    {
        ValidateEventId(eventId);
        var normalizedCode = NormalizeCode(code, approved);
        var defaults = Defaults.TryGetValue(normalizedCode, out var definition)
            ? definition
            : Defaults[approved ? "ACESSO_CONCEDIDO" : "ACESSO_NEGADO"];

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Prioridade: override do evento > template padrão global > fallback em memória.
        command.CommandText = """
            SELECT COALESCE(m.title, t.title), COALESCE(m.message, t.message),
                   COALESCE(m.background_start, t.background_start), COALESCE(m.background_end, t.background_end),
                   COALESCE(m.title_color, t.title_color), COALESCE(m.message_color, t.message_color),
                   COALESCE(m.title_size, t.title_size), COALESCE(m.message_size, t.message_size),
                   COALESCE(m.title_bold, t.title_bold), COALESCE(m.message_bold, t.message_bold),
                   COALESCE(m.active, t.active)
            FROM fp_access_message_templates t
            LEFT JOIN fp_access_messages m ON m.event_id = @event_id AND m.code = t.code
            WHERE t.code = @code
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@event_id", eventId.ToString());
        command.Parameters.AddWithValue("@code", normalizedCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var title = defaults.Title;
        var message = defaults.Message;
        var backgroundStart = defaults.BackgroundStart;
        var backgroundEnd = defaults.BackgroundEnd;
        var titleColor = "#FFFFFF";
        var messageColor = "#FFFFFF";
        var titleSize = "28dp";
        var messageSize = "18dp";
        var titleBold = true;
        var messageBold = true;
        if (await reader.ReadAsync(cancellationToken))
        {
            var active = Convert.ToBoolean(reader.GetValue(10));
            if (active)
            {
                title = reader.GetString(0);
                message = reader.GetString(1);
            }
            backgroundStart = ValidColor(reader.GetString(2)) ? reader.GetString(2) : defaults.BackgroundStart;
            backgroundEnd = ValidColor(reader.GetString(3)) ? reader.GetString(3) : defaults.BackgroundEnd;
            titleColor = ValidColor(reader.GetString(4)) ? reader.GetString(4) : "#FFFFFF";
            messageColor = ValidColor(reader.GetString(5)) ? reader.GetString(5) : "#FFFFFF";
            titleSize = reader.GetString(6);
            messageSize = reader.GetString(7);
            titleBold = Convert.ToBoolean(reader.GetValue(8));
            messageBold = Convert.ToBoolean(reader.GetValue(9));
        }

        var resolvedMessage = ReplacePlaceholders(message, approved, fallback);
        var resolvedTitle = ReplacePlaceholders(title, approved, null);
        var presentation = new AccessMessagePresentation(
            resolvedTitle,
            resolvedMessage,
            backgroundStart,
            backgroundEnd,
            titleColor,
            messageColor,
            titleSize,
            messageSize,
            titleBold,
            messageBold);
        return new ResolvedAccessMessage(normalizedCode, resolvedMessage, presentation);
    }

    private static async Task<IReadOnlyList<AccessMessageView>> ReadListAsync(MySqlConnection connection, Guid eventId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(m.id, t.id), t.code,
                   COALESCE(m.title, t.title), COALESCE(m.message, t.message),
                   COALESCE(m.background_start, t.background_start), COALESCE(m.background_end, t.background_end),
                   COALESCE(m.title_color, t.title_color), COALESCE(m.message_color, t.message_color),
                   COALESCE(m.title_size, t.title_size), COALESCE(m.message_size, t.message_size),
                   COALESCE(m.title_bold, t.title_bold), COALESCE(m.message_bold, t.message_bold),
                   COALESCE(m.active, t.active), COALESCE(m.updated_at, t.updated_at),
                   (m.id IS NOT NULL) is_customized
            FROM fp_access_message_templates t
            LEFT JOIN fp_access_messages m ON m.event_id = @event_id AND m.code = t.code
            ORDER BY t.code;
            """;
        command.Parameters.AddWithValue("@event_id", eventId.ToString());
        var result = new List<AccessMessageView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AccessMessageView(
                ReadGuid(reader, 0), eventId, reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetString(8), reader.GetString(9), Convert.ToBoolean(reader.GetValue(10)),
                Convert.ToBoolean(reader.GetValue(11)), Convert.ToBoolean(reader.GetValue(12)),
                true, ReadDateTimeOffset(reader, 13), Convert.ToBoolean(reader.GetValue(14))));
        }
        return result;
    }

    private static void ValidateEventId(Guid eventId)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("EventId é obrigatório.");
    }

    private static void ValidateCommand(UpdateAccessMessageCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Title) || command.Title.Trim().Length > 120) throw new ArgumentException("Title é obrigatório e deve ter até 120 caracteres.");
        if (string.IsNullOrWhiteSpace(command.Message) || command.Message.Trim().Length > 500) throw new ArgumentException("Message é obrigatório e deve ter até 500 caracteres.");
        foreach (var color in new[] { command.BackgroundStart, command.BackgroundEnd, command.TitleColor, command.MessageColor })
        {
            if (!ValidColor(color)) throw new ArgumentException("As cores devem estar no formato #RRGGBB.");
        }
        foreach (var size in new[] { command.TitleSize, command.MessageSize })
        {
            if (!Regex.IsMatch(size ?? string.Empty, "^\\d{1,3}(px|dp|sp|rem|em|%)$", RegexOptions.IgnoreCase)) throw new ArgumentException("Os tamanhos devem usar unidades como 28dp ou 18px.");
        }
    }

    private static void ValidateTemplateCommand(UpdateAccessMessageTemplateCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Title) || command.Title.Trim().Length > 120) throw new ArgumentException("Title é obrigatório e deve ter até 120 caracteres.");
        if (string.IsNullOrWhiteSpace(command.Message) || command.Message.Trim().Length > 500) throw new ArgumentException("Message é obrigatório e deve ter até 500 caracteres.");
        foreach (var color in new[] { command.BackgroundStart, command.BackgroundEnd, command.TitleColor, command.MessageColor })
        {
            if (!ValidColor(color)) throw new ArgumentException("As cores devem estar no formato #RRGGBB.");
        }
        foreach (var size in new[] { command.TitleSize, command.MessageSize })
        {
            if (!Regex.IsMatch(size ?? string.Empty, "^\\d{1,3}(px|dp|sp|rem|em|%)$", RegexOptions.IgnoreCase)) throw new ArgumentException("Os tamanhos devem usar unidades como 28dp ou 18px.");
        }
    }

    private static bool ValidColor(string? value) => value is not null && Regex.IsMatch(value.Trim(), "^#[0-9A-Fa-f]{6}$");
    private static string NormalizeCode(string? code, bool approved) => string.IsNullOrWhiteSpace(code) ? (approved ? "ACESSO_CONCEDIDO" : "ACESSO_NEGADO") : code.Trim().ToUpperInvariant();
    private static string ReplacePlaceholders(string value, bool approved, string? fallback) => value.Replace("{status}", approved ? "Approved" : "Rejected", StringComparison.OrdinalIgnoreCase).Replace("{situacao}", approved ? "Acesso liberado" : "Acesso negado", StringComparison.OrdinalIgnoreCase);
    private static Guid ReadGuid(MySqlDataReader reader, int ordinal) => Guid.Parse(reader.GetValue(ordinal).ToString()!);
    private static DateTimeOffset ReadDateTimeOffset(MySqlDataReader reader, int ordinal) => new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private static async Task<bool> ExistsAsync(MySqlConnection connection, MySqlTransaction? transaction, string sqlText, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand(); if (transaction is not null) command.Transaction = transaction; command.CommandText = sqlText; AddParameters(command, parameters); return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task ExecuteAsync(MySqlConnection connection, MySqlTransaction? transaction, string sqlText, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand(); if (transaction is not null) command.Transaction = transaction; command.CommandText = sqlText; AddParameters(command, parameters); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameters(MySqlCommand command, params (string Name, object Value)[] parameters)
    {
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
    }

    private sealed record MessageDefaults(string Title, string Message, string BackgroundStart, string BackgroundEnd);
}
