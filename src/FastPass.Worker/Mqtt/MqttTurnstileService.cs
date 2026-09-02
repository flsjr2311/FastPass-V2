using System.Text;
using FastPass.Application.Access;
using FastPass.Application.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;

namespace FastPass.Worker.Mqtt;

/// <summary>
/// Serviço que conecta no broker MQTT, escuta as catracas e comanda a liberação.
///
/// Fluxo por leitura:
///   1. Placa publica em "&lt;Prefix&gt;/&lt;DeviceId&gt;/from/&lt;verbo&gt;".
///   2. Telemetria (status/keepalive/info) → atualiza last_seen do device.
///   3. Leitura de credencial → resolve device→portaria→evento pelo identifier (DeviceId),
///      chama IAccessValidationService (canal Turnstile — infere direção e decide),
///      e publica o comando (liberar/negar + sentido + display) em ".../to/&lt;verbo&gt;".
///
/// A catraca só envia {código, nome da catraca}. A relação catraca↔portaria e o
/// sentido a liberar são decididos aqui/no serviço de validação.
/// </summary>
public sealed class MqttTurnstileService : BackgroundService
{
    private readonly ILogger<MqttTurnstileService> _logger;
    private readonly MqttOptions _options;
    private readonly IServiceProvider _services;
    private readonly TurnstileTopics _topics;
    private readonly TurnstileMessageCodec _codec = new();

    private IMqttClient? _client;

    public MqttTurnstileService(
        ILogger<MqttTurnstileService> logger,
        IOptions<MqttOptions> options,
        IServiceProvider services)
    {
        _logger = logger;
        _options = options.Value;
        _services = services;
        _topics = new TurnstileTopics(_options.TopicPrefix);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("MQTT desabilitado (Mqtt:Enabled=false). Serviço de catracas inativo.");
            return;
        }

        var factory = new MqttFactory();
        _client = factory.CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnMessageAsync;

        var clientOptions = new MqttClientOptionsBuilder()
            .WithClientId(_options.ClientId)
            .WithTcpServer(_options.Host, _options.Port)
            .WithCleanSession()
            .Build();

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            clientOptions = new MqttClientOptionsBuilder()
                .WithClientId(_options.ClientId)
                .WithTcpServer(_options.Host, _options.Port)
                .WithCredentials(_options.Username, _options.Password)
                .WithCleanSession()
                .Build();
        }

        // Loop de conexão com reconexão automática.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_client.IsConnected)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                _logger.LogInformation("Conectando ao broker MQTT {Host}:{Port}...", _options.Host, _options.Port);
                await _client.ConnectAsync(clientOptions, stoppingToken);

                await _client.SubscribeAsync(
                    new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(_topics.SubscribeFromAll)
                        .Build(),
                    stoppingToken);

                _logger.LogInformation("Conectado. Assinando '{Filter}'.", _topics.SubscribeFromAll);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao conectar/assinar no broker MQTT. Retry em {Delay}s.",
                    _options.ReconnectDelaySeconds);
                try { await Task.Delay(TimeSpan.FromSeconds(_options.ReconnectDelaySeconds), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        try { if (_client.IsConnected) await _client.DisconnectAsync(); } catch { /* ignore */ }
    }

    private async Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        var payload = e.ApplicationMessage.PayloadSegment.Count > 0
            ? Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment)
            : string.Empty;

        if (!_topics.TryParseFrom(topic, out var deviceId, out var verb))
        {
            _logger.LogDebug("Tópico ignorado (fora do padrão): {Topic}", topic);
            return;
        }

        try
        {
            var kind = _codec.Decode(verb, payload, out var read);
            switch (kind)
            {
                case TurnstileInboundKind.Telemetry:
                    await TouchDeviceAsync(deviceId);
                    break;

                case TurnstileInboundKind.CredentialRead when read is not null:
                    await HandleReadAsync(deviceId, read);
                    break;

                default:
                    _logger.LogInformation(
                        "Mensagem não reconhecida de '{Device}' (verbo '{Verb}'): {Payload}",
                        deviceId, verb, Truncate(payload, 300));
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar mensagem de '{Device}' (verbo '{Verb}').", deviceId, verb);
        }
    }

    private async Task HandleReadAsync(string deviceId, TurnstileRead read)
    {
        using var scope = _services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var validation = scope.ServiceProvider.GetRequiredService<IAccessValidationService>();

        // O "nome da catraca" (deviceId MQTT) é o identifier cadastrado em fp_devices.
        var device = await catalog.ResolveTurnstileDeviceAsync(deviceId);
        if (device is null)
        {
            _logger.LogWarning(
                "Leitura de catraca '{Device}' sem cadastro (identifier não encontrado / evento inativo). Código: {Code}",
                deviceId, read.CredentialCode);
            return;
        }

        var command = new ValidateAccessCommand(
            CredentialCode: read.CredentialCode,
            EventId: device.EventId,
            GateId: device.GateId,
            SectorId: null,
            Direction: null,                 // canal Turnstile infere o sentido
            IdempotencyKey: Guid.NewGuid().ToString(),
            DeviceId: device.DeviceId,
            Channel: "Turnstile");

        var result = await validation.ValidateAsync(command);

        _logger.LogInformation(
            "Catraca '{Device}' ({Gate}/{Event}): código {Code} → {Decision} ({Direction}), arm={Arm}",
            deviceId, device.GateName, device.EventName, read.CredentialCode,
            result.Decision, result.Direction, result.ArmAction);

        var (verb, responsePayload) = _codec.EncodeCommand(result);
        await PublishAsync(_topics.To(deviceId, verb), responsePayload);
    }

    private async Task TouchDeviceAsync(string deviceId)
    {
        try
        {
            using var scope = _services.CreateScope();
            var catalog = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            await catalog.TouchDeviceAsync(deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao atualizar last_seen de '{Device}'.", deviceId);
        }
    }

    private async Task PublishAsync(string topic, string payload)
    {
        if (_client is null || !_client.IsConnected)
        {
            _logger.LogWarning("Sem conexão MQTT para publicar em {Topic}.", topic);
            return;
        }
        var msg = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .Build();
        await _client.PublishAsync(msg);
        _logger.LogDebug("Publicado em {Topic}: {Payload}", topic, payload);
    }

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] : s;
}
