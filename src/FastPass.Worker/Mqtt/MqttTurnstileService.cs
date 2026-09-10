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

        // Ao (re)conectar, re-assina o filtro — essencial após qualquer queda/reconexão.
        _client.ConnectedAsync += async _ =>
        {
            try
            {
                await _client.SubscribeAsync(
                    new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(_topics.SubscribeFromAll)
                        .Build());
                _logger.LogInformation("Conectado ao broker. Assinando '{Filter}'.", _topics.SubscribeFromAll);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao assinar após conectar.");
            }
        };

        _client.DisconnectedAsync += e =>
        {
            _logger.LogWarning("Desconectado do broker MQTT ({Reason}). Reconectando...", e.Reason);
            return Task.CompletedTask;
        };

        // ClientId único por execução evita colisão/derrubada de sessão no broker.
        var clientId = $"{_options.ClientId}-{Guid.NewGuid():N}";
        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId(clientId)
            .WithTcpServer(_options.Host, _options.Port)
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            optionsBuilder = optionsBuilder.WithCredentials(_options.Username, _options.Password);
        }

        var clientOptions = optionsBuilder.Build();

        // Loop supervisor: garante a conexão viva; reconecta sempre que cair.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_client.IsConnected)
                {
                    _logger.LogInformation("Conectando ao broker MQTT {Host}:{Port}...", _options.Host, _options.Port);
                    await _client.ConnectAsync(clientOptions, stoppingToken);
                    // A assinatura acontece no handler ConnectedAsync.
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao conectar no broker MQTT. Retry em {Delay}s.",
                    _options.ReconnectDelaySeconds);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(_options.ReconnectDelaySeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
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

        // Mensagens RETIDAS são histórico que o broker re-entrega ao (re)assinar —
        // não representam a placa "ao vivo". Ex.: o Last Will "disconnected" fica retido
        // e reapareceria a cada reconexão, marcando a catraca como offline indevidamente.
        var retained = e.ApplicationMessage.Retain;

        // Log de diagnóstico: torna visível cada mensagem recebida de uma catraca,
        // útil para diagnosticar intermitência de conexão da placa.
        _logger.LogInformation(
            "MQTT ← '{Device}' /from/{Verb}{Retained}", deviceId, verb, retained ? " (retida)" : "");

        try
        {
            var kind = _codec.Decode(verb, payload, out var read, out var telemetry);
            switch (kind)
            {
                case TurnstileInboundKind.Telemetry:
                    // Só telemetria AO VIVO conta como presença. Retida atualiza apenas
                    // os metadados conhecidos (firmware/IP), sem mexer em status/last_seen.
                    if (!retained)
                    {
                        await RecordPresenceAsync(deviceId, telemetry);
                        await TouchDeviceAsync(deviceId);
                    }
                    else
                    {
                        await RecordMetadataOnlyAsync(deviceId, telemetry);
                    }
                    // Envia mensagem inicial do modo operacional (PASSE SEU INGRESSO, CATRACA LIBERADA, etc)
                    // SEMPRE enviar, mesmo se retida, para garantir que o display mostra o modo correto
                    await SendInitialModeMessageAsync(deviceId);
                    break;

                case TurnstileInboundKind.CredentialRead when read is not null:
                    // Uma leitura também prova que a placa está viva/online.
                    await RecordPresenceAsync(deviceId, null);
                    await HandleReadAsync(deviceId, read);
                    break;

                default:
                    _logger.LogInformation(
                        "Mensagem não reconhecida de '{Device}' (verbo '{Verb}', retida={Retained}): {Payload}",
                        deviceId, verb, retained, Truncate(payload, 300));
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

        // ── Determina o modo efetivo ──────────────────────────────────────────
        // Se o device tem override, usa ele; senão, usa o da portaria
        var effectiveMode = device.OperationModeOverride ?? device.OperationMode;
        
        // Verifica o modo de operação da catraca/portaria
        if (effectiveMode == "Blocked")
        {
            _logger.LogInformation(
                "Catraca '{Device}' está BLOQUEADA (modo: {Mode}, override: {Override}). Leitura rejeitada (código: {Code})",
                deviceId, device.OperationMode, device.OperationModeOverride, read.CredentialCode);
            
            // Envia comando: CATRACA BLOQUEADA (usa 2 linhas completas)
            var blockedResult = new AccessValidationResult(
                AttemptId: Guid.NewGuid(),
                Approved: false,
                Decision: "Rejected",
                CredentialType: "Unknown",
                Reason: "Catraca bloqueada.",
                StaffCredentialId: null,
                StaffMemberId: null,
                StaffName: null,
                IdempotentReplay: false,
                Channel: "Turnstile",
                Direction: "Entry",
                TicketId: null,
                MaximumEntries: null,
                EntriesUsed: null,
                PeopleInside: null,
                ArmAction: "KeepLocked",
                Pictogram: "RedCross",
                ReasonCode: "CATRACA_BLOQUEADA",
                Message: "BLOQUEADA\nCADEADO");
            
            var (verb, payload) = _codec.EncodeCommand(blockedResult);
            await PublishAsync(_topics.To(deviceId, verb), payload);
            return;
        }

        if (effectiveMode == "Free")
        {
            _logger.LogInformation(
                "Catraca '{Device}' em modo LIVRE (modo: {Mode}, override: {Override}). Liberando entrada e saída.",
                deviceId, device.OperationMode, device.OperationModeOverride);
            
            // Envia comando: CATRACA LIBERADA (usa 2 linhas completas)
            var freeResult = new AccessValidationResult(
                AttemptId: Guid.NewGuid(),
                Approved: true,
                Decision: "Approved",
                CredentialType: "Unknown",
                Reason: null,
                StaffCredentialId: null,
                StaffMemberId: null,
                StaffName: null,
                IdempotentReplay: false,
                Channel: "Turnstile",
                Direction: "Entry",
                TicketId: null,
                MaximumEntries: null,
                EntriesUsed: null,
                PeopleInside: null,
                ArmAction: "Unlock",
                Pictogram: "GreenArrowEntry",
                ReasonCode: "CATRACA_LIVRE",
                Message: "LIBERADA\nENTRE");
            
            var (verb, payload) = _codec.EncodeCommand(freeResult);
            await PublishAsync(_topics.To(deviceId, verb), payload);
            return;
        }

        // Mode == "Active": mostra mensagem inicial e valida depois
        _logger.LogInformation(
            "Catraca '{Device}' em modo ATIVO. Aguardando leitura de ingresso.",
            deviceId);
        
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

        var (verb2, responsePayload) = _codec.EncodeCommand(result);
        await PublishAsync(_topics.To(deviceId, verb2), responsePayload);
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

    /// <summary>Registra a presença AO VIVO da catraca (renova status + last_seen). Best-effort.</summary>
    private async Task RecordPresenceAsync(string deviceId, TurnstileTelemetry? telemetry)
    {
        try
        {
            using var scope = _services.CreateScope();
            var monitoring = scope.ServiceProvider.GetRequiredService<ITurnstileMonitoringService>();
            await monitoring.RecordPresenceAsync(new TurnstilePresenceUpdate(
                DeviceId: deviceId,
                Status: telemetry?.Status,
                Firmware: telemetry?.Firmware,
                BoardId: telemetry?.BoardId,
                SerialId: telemetry?.SerialId,
                IpLocal: telemetry?.IpLocal,
                Media: telemetry?.Media));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao registrar presença de '{Device}'.", deviceId);
        }
    }

    /// <summary>
    /// Envia a mensagem inicial do modo operacional da catraca.
    /// Otimizado para usar 2 linhas completas sem quebrar palavras.
    /// Display sem mudança de cor - apenas pictogramas.
    /// Best-effort.
    /// </summary>
    private async Task SendInitialModeMessageAsync(string deviceId)
    {
        try
        {
            using var scope = _services.CreateScope();
            var catalog = scope.ServiceProvider.GetRequiredService<ICatalogService>();

            var device = await catalog.ResolveTurnstileDeviceAsync(deviceId);
            if (device is null) return;

            var effectiveMode = device.OperationModeOverride ?? device.OperationMode;
            
            _logger.LogInformation("📌 SendInitialMode para '{Device}': operationMode={OperationMode}, override={Override}, effective={Effective}", 
                deviceId, device.OperationMode, device.OperationModeOverride, effectiveMode);

            AccessValidationResult modeMessage;

            if (effectiveMode == "Blocked")
            {
                modeMessage = new AccessValidationResult(
                    AttemptId: Guid.NewGuid(),
                    Approved: false,
                    Decision: "Rejected",
                    CredentialType: "Unknown",
                    Reason: "Catraca bloqueada.",
                    StaffCredentialId: null,
                    StaffMemberId: null,
                    StaffName: null,
                    IdempotentReplay: false,
                    Channel: "Turnstile",
                    Direction: "Entry",
                    TicketId: null,
                    MaximumEntries: null,
                    EntriesUsed: null,
                    PeopleInside: null,
                    ArmAction: "KeepLocked",
                    Pictogram: "RedCross",
                    ReasonCode: "CATRACA_BLOQUEADA",
                    Message: "BLOQUEADA\nCADEADO");
            }
            else if (effectiveMode == "Free")
            {
                modeMessage = new AccessValidationResult(
                    AttemptId: Guid.NewGuid(),
                    Approved: true,
                    Decision: "Approved",
                    CredentialType: "Unknown",
                    Reason: null,
                    StaffCredentialId: null,
                    StaffMemberId: null,
                    StaffName: null,
                    IdempotentReplay: false,
                    Channel: "Turnstile",
                    Direction: "Entry",
                    TicketId: null,
                    MaximumEntries: null,
                    EntriesUsed: null,
                    PeopleInside: null,
                    ArmAction: "Unlock",
                    Pictogram: "GreenArrowEntry",
                    ReasonCode: "CATRACA_LIVRE",
                    Message: "LIBERADA\nENTRE");
            }
            else  // Active
            {
                modeMessage = new AccessValidationResult(
                    AttemptId: Guid.NewGuid(),
                    Approved: false,
                    Decision: "Pending",
                    CredentialType: "Unknown",
                    Reason: "Aguardando ingresso.",
                    StaffCredentialId: null,
                    StaffMemberId: null,
                    StaffName: null,
                    IdempotentReplay: false,
                    Channel: "Turnstile",
                    Direction: "Entry",
                    TicketId: null,
                    MaximumEntries: null,
                    EntriesUsed: null,
                    PeopleInside: null,
                    ArmAction: "None",
                    Pictogram: "None",
                    ReasonCode: "CATRACA_ATIVA",
                    Message: "PASSE SEU\nINGRESSO");
            }

            var (verb, payload) = _codec.EncodeCommand(modeMessage);
            await PublishAsync(_topics.To(deviceId, verb), payload);

            _logger.LogDebug("Enviada mensagem inicial de modo para '{Device}': {Mode}", deviceId, effectiveMode);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao enviar mensagem inicial de modo para '{Device}'.", deviceId);
        }
    }

    /// <summary>
    /// Atualiza SOMENTE os metadados (firmware/IP/serial) a partir de uma mensagem
    /// retida, sem mexer em status/last_seen (retida não é sinal de vida). Best-effort.
    /// </summary>
    private async Task RecordMetadataOnlyAsync(string deviceId, TurnstileTelemetry? telemetry)
    {
        if (telemetry is null) return;
        try
        {
            using var scope = _services.CreateScope();
            var monitoring = scope.ServiceProvider.GetRequiredService<ITurnstileMonitoringService>();
            await monitoring.RecordMetadataAsync(new TurnstilePresenceUpdate(
                DeviceId: deviceId,
                Status: null,
                Firmware: telemetry.Firmware,
                BoardId: telemetry.BoardId,
                SerialId: telemetry.SerialId,
                IpLocal: telemetry.IpLocal,
                Media: telemetry.Media));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao registrar metadados de '{Device}'.", deviceId);
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
        _logger.LogInformation("MQTT → {Topic}: {Payload}", topic, payload);
    }

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] : s;
}
