using MySqlConnector;

namespace FastPass.Infrastructure.Database;

public sealed class DatabaseMigrator
{
    private readonly FastPassDbConnectionFactory _connectionFactory;

    public DatabaseMigrator(FastPassDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS fp_schema_migrations (
                version VARCHAR(100) NOT NULL,
                applied_at DATETIME(6) NOT NULL,
                PRIMARY KEY (version)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            cancellationToken);

        foreach (var migration in GetMigrations())
        {
            if (await IsAppliedAsync(connection, migration.Version, cancellationToken))
            {
                continue;
            }

            var script = await ReadMigrationScriptAsync(migration.ResourceName, cancellationToken);
            foreach (var statement in SplitStatements(script))
            {
                await ExecuteAsync(connection, statement, cancellationToken);
            }

            await MarkAsAppliedAsync(connection, migration.Version, cancellationToken);
        }

        await BackfillReadableCodesAsync(connection, cancellationToken);
    }

    /// <summary>
    /// Popula os códigos legíveis (code/code_num) para registros que ainda estão NULL.
    /// - fp_events.code: sequencial global por ordem de criação
    /// - fp_gates.code_num / fp_sectors.code_num: sequencial por evento
    /// Idempotente: só atualiza linhas com código NULL.
    /// </summary>
    private static async Task BackfillReadableCodesAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            // Eventos — sequencial global (linha a linha, sem variável de sessão)
            await BackfillEventsAsync(connection, cancellationToken);

            // Portarias — sequencial por evento (via fp_event_gates)
            await BackfillPerEventAsync(connection, "fp_gates", "code_num",
                """
                SELECT g.id, eg.event_id
                FROM fp_gates g
                INNER JOIN fp_event_gates eg ON eg.gate_id = g.id
                WHERE g.code_num IS NULL
                ORDER BY eg.event_id, g.created_at, g.id;
                """,
                cancellationToken);

            // Setores — sequencial por evento
            await BackfillPerEventAsync(connection, "fp_sectors", "code_num",
                """
                SELECT id, event_id
                FROM fp_sectors
                WHERE code_num IS NULL
                ORDER BY event_id, created_at, id;
                """,
                cancellationToken);
        }
        catch (MySqlException)
        {
            // Coluna inexistente ou schema antigo — ignora.
        }
    }

    private static async Task BackfillEventsAsync(
        MySqlConnection connection, CancellationToken cancellationToken)
    {
        var pending = new List<string>();
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT id FROM fp_events WHERE code IS NULL ORDER BY created_at, id;";
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                pending.Add(reader.GetValue(0).ToString()!);
        }
        if (pending.Count == 0) return;

        int current;
        await using (var maxCmd = connection.CreateCommand())
        {
            maxCmd.CommandText = "SELECT COALESCE(MAX(code),0) FROM fp_events;";
            current = Convert.ToInt32(await maxCmd.ExecuteScalarAsync(cancellationToken));
        }

        foreach (var id in pending)
        {
            current++;
            await using var upd = connection.CreateCommand();
            upd.CommandText = "UPDATE fp_events SET code = @n WHERE id = @id;";
            upd.Parameters.AddWithValue("@n", current);
            upd.Parameters.AddWithValue("@id", id);
            await upd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Popula uma coluna sequencial por evento: para cada evento, numera os registros
    /// a partir de (máximo atual daquele evento + 1).
    /// </summary>
    private static async Task BackfillPerEventAsync(
        MySqlConnection connection,
        string table,
        string column,
        string selectSql,
        CancellationToken cancellationToken)
    {
        var pending = new List<(string Id, string EventId)>();
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = selectSql;
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                pending.Add((reader.GetValue(0).ToString()!, reader.GetValue(1).ToString()!));
            }
        }

        if (pending.Count == 0) return;

        var counters = new Dictionary<string, int>();
        foreach (var (id, eventId) in pending)
        {
            if (!counters.TryGetValue(eventId, out var current))
            {
                await using var maxCmd = connection.CreateCommand();
                maxCmd.CommandText = table == "fp_gates"
                    ? $"SELECT COALESCE(MAX({column}),0) FROM fp_gates g INNER JOIN fp_event_gates eg ON eg.gate_id = g.id WHERE eg.event_id = @eid;"
                    : $"SELECT COALESCE(MAX({column}),0) FROM {table} WHERE event_id = @eid;";
                maxCmd.Parameters.AddWithValue("@eid", eventId);
                current = Convert.ToInt32(await maxCmd.ExecuteScalarAsync(cancellationToken));
            }
            current++;
            counters[eventId] = current;

            await using var upd = connection.CreateCommand();
            upd.CommandText = $"UPDATE {table} SET {column} = @n WHERE id = @id;";
            upd.Parameters.AddWithValue("@n", current);
            upd.Parameters.AddWithValue("@id", id);
            await upd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static IEnumerable<(string Version, string ResourceName)> GetMigrations()
    {
        return typeof(DatabaseMigrator)
            .Assembly
            .GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => (name[..^4].Split('.').Last(), name));
    }

    private static async Task<bool> IsAppliedAsync(
        MySqlConnection connection,
        string version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM fp_schema_migrations WHERE version = @version;";
        command.Parameters.AddWithValue("@version", version);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) > 0;
    }

    private static async Task MarkAsAppliedAsync(
        MySqlConnection connection,
        string version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO fp_schema_migrations (version, applied_at) VALUES (@version, UTC_TIMESTAMP(6));";
        command.Parameters.AddWithValue("@version", version);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        MySqlConnection connection,
        string statement,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(statement))
        {
            return;
        }

        // Validar migration ANTES de executar para evitar erros conhecidos
        MigrationValidation.ValidateDeleteStatement(statement);
        MigrationValidation.ValidateLoginImpact(statement);
        MigrationValidation.ValidateWhereClause(statement);

        await using var command = connection.CreateCommand();
        command.CommandText = statement;
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (MySqlException ex)
        {
            // Log the error for debugging
            var upper = statement.TrimStart().ToUpperInvariant();
            System.Console.WriteLine($"MySqlException #{ex.Number}: {ex.Message}");
            System.Console.WriteLine($"Statement: {upper[..Math.Min(100, upper.Length)]}");
            
            if (!IsTolerantError(statement, ex))
            {
                throw;
            }
            // Ignora erros esperados em migrations idempotentes
        }
    }

    /// <summary>
    /// Erros que podem ocorrer em re-execuções parciais de migrations e são seguros de ignorar.
    /// </summary>
    private static bool IsTolerantError(string statement, MySqlException ex)
    {
        // Remove leading comments for checking
        var upper = statement.TrimStart().ToUpperInvariant();
        while (upper.StartsWith("--", StringComparison.Ordinal))
        {
            // Skip to next line
            var newlineIndex = upper.IndexOf('\n');
            if (newlineIndex == -1) return false; // Only comments, no actual statement
            upper = upper[(newlineIndex + 1)..].TrimStart();
        }

        return ex.Number switch
        {
            1060 => upper.StartsWith("ALTER TABLE", StringComparison.Ordinal),  // Duplicate column
            1061 => upper.StartsWith("ALTER TABLE", StringComparison.Ordinal),  // Duplicate key name
            1091 => upper.StartsWith("ALTER TABLE", StringComparison.Ordinal),  // Can't DROP; check column/key/FK exists
            1050 => upper.StartsWith("CREATE TABLE", StringComparison.Ordinal), // Table already exists
            1136 => upper.StartsWith("INSERT", StringComparison.Ordinal),       // Column count doesn't match value count (idempotent INSERT)
            1146 => upper.StartsWith("TRUNCATE", StringComparison.Ordinal) || upper.StartsWith("DELETE", StringComparison.Ordinal), // Table doesn't exist (idempotent DELETE/TRUNCATE)
            _ => false
        };
    }

    private static async Task<string> ReadMigrationScriptAsync(
        string resourceName,
        CancellationToken cancellationToken)
    {
        var assembly = typeof(DatabaseMigrator).Assembly;
        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Migration SQL não encontrada: {resourceName}");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static IEnumerable<string> SplitStatements(string script)
    {
        return script
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split(";\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(statement => statement.Trim());
    }
}
