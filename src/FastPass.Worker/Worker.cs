using FastPass.Infrastructure.Database;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FastPass.Worker;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly DatabaseProbe _databaseProbe;

    public Worker(ILogger<Worker> logger, DatabaseProbe databaseProbe)
    {
        _logger = logger;
        _databaseProbe = databaseProbe;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        do
        {
            var result = await _databaseProbe.CheckAsync(stoppingToken);
            if (result.Connected)
            {
                _logger.LogInformation(
                    "Banco DEV conectado: {Database} / MySQL {ServerVersion}",
                    result.Database,
                    result.ServerVersion);
            }
            else
            {
                _logger.LogWarning("Banco DEV indisponível: {Error}", result.Error);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
