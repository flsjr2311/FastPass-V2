using FastPass.Application.Access;
using FastPass.Application.Catalog;
using FastPass.Infrastructure.Access;
using FastPass.Infrastructure.Catalog;
using FastPass.Infrastructure.Database;
using FastPass.Worker;
using FastPass.Worker.Mqtt;
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

// Serviços de domínio necessários para validar acessos vindos das catracas.
builder.Services.AddSingleton<ICatalogService, MySqlCatalogService>();
builder.Services.AddSingleton<IAccessPolicyService, MySqlAccessPolicyService>();
builder.Services.AddSingleton<IAccessMessageService, MySqlAccessMessageService>();
builder.Services.AddSingleton<IAccessValidationService, MySqlAccessValidationService>();

// Configuração e serviço MQTT das catracas.
builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection(MqttOptions.SectionName));

builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<MqttTurnstileService>();

var host = builder.Build();
host.Run();
