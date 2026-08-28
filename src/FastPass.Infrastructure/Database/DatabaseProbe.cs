using MySqlConnector;

namespace FastPass.Infrastructure.Database;

public sealed record DatabaseProbeResult(
    bool Connected,
    string? Database,
    string? ServerVersion,
    string? Error);

public sealed class DatabaseProbe
{
    private readonly FastPassDbConnectionFactory _connectionFactory;

    public DatabaseProbe(FastPassDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<DatabaseProbeResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = _connectionFactory.Create();
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT DATABASE(), VERSION();";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return new DatabaseProbeResult(false, null, null, "O banco não retornou informações de diagnóstico.");
            }

            return new DatabaseProbeResult(
                true,
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                null);
        }
        catch (Exception exception)
        {
            return new DatabaseProbeResult(false, null, null, exception.Message);
        }
    }
}
