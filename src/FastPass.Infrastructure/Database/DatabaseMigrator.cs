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

        await using var command = connection.CreateCommand();
        command.CommandText = statement;
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (MySqlException ex) when (IsTolerantError(statement, ex))
        {
            // Ignora erros esperados em migrations idempotentes:
            // 1060 = Duplicate column name (ADD COLUMN que já existe)
            // 1050 = Table already exists (CREATE TABLE sem IF NOT EXISTS)
        }
    }

    /// <summary>
    /// Erros que podem ocorrer em re-execuções parciais de migrations e são seguros de ignorar.
    /// </summary>
    private static bool IsTolerantError(string statement, MySqlException ex)
    {
        var upper = statement.TrimStart().ToUpperInvariant();
        return ex.Number switch
        {
            1060 => upper.StartsWith("ALTER TABLE", StringComparison.Ordinal),  // Duplicate column
            1061 => upper.StartsWith("ALTER TABLE", StringComparison.Ordinal),  // Duplicate key name
            1050 => upper.StartsWith("CREATE TABLE", StringComparison.Ordinal), // Table already exists
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
