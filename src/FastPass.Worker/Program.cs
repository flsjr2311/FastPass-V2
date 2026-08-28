using FastPass.Infrastructure.Database;
using FastPass.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("FastPass");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:FastPass não foi configurada.");
}

builder.Services.AddSingleton(new FastPassDbConnectionFactory(connectionString));
builder.Services.AddSingleton<DatabaseProbe>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
