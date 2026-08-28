using MySqlConnector;

namespace FastPass.Infrastructure.Database;

public sealed class FastPassDbConnectionFactory
{
    private readonly string _connectionString;

    public FastPassDbConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A connection string do FastPass não foi configurada.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public MySqlConnection Create() => new(_connectionString);
}
