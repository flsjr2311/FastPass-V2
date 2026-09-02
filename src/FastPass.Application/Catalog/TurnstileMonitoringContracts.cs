namespace FastPass.Application.Catalog;

/// <summary>Dados de presença reportados por uma catraca via MQTT.</summary>
public sealed record TurnstilePresenceUpdate(
    string DeviceId,
    string? Status,
    string? Firmware,
    string? BoardId,
    string? SerialId,
    string? IpLocal,
    string? Media);

/// <summary>
/// Linha do painel de monitoramento: junta a presença MQTT (o que a placa reporta)
/// com o cadastro em fp_devices (se existir) e a portaria/evento atribuídos.
/// </summary>
public sealed record TurnstileMonitorView(
    string DeviceId,
    string Status,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    bool Online,
    string? Firmware,
    string? BoardId,
    string? SerialId,
    string? IpLocal,
    string? Media,
    // Vínculo com o cadastro (null quando a catraca ainda não foi cadastrada):
    Guid? DeviceRegistrationId,
    string? DeviceName,
    bool? DeviceActive,
    Guid? GateId,
    string? GateName,
    Guid? EventId,
    string? EventName);

/// <summary>Registro e leitura da presença/monitoramento das catracas MQTT.</summary>
public interface ITurnstileMonitoringService
{
    /// <summary>Upsert da presença AO VIVO de uma catraca (renova status + last_seen).</summary>
    Task RecordPresenceAsync(
        TurnstilePresenceUpdate update,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atualiza apenas os metadados (firmware/IP/serial) de uma catraca já conhecida,
    /// sem alterar status nem last_seen. Usado para mensagens retidas (histórico).
    /// Não cria registro novo.
    /// </summary>
    Task RecordMetadataAsync(
        TurnstilePresenceUpdate update,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista todas as catracas já vistas via MQTT, cruzando com o cadastro/atribuição.
    /// </summary>
    /// <param name="onlineWindowSeconds">Janela (s) para considerar a catraca online pelo last_seen.</param>
    Task<IReadOnlyList<TurnstileMonitorView>> ListAsync(
        int onlineWindowSeconds,
        CancellationToken cancellationToken = default);
}
