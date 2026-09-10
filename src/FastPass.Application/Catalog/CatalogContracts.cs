using FastPass.Domain.Enums;

namespace FastPass.Application.Catalog;

// ── Clientes ──────────────────────────────────────────────────────────────────

public sealed record CreateClientCommand(
    string Name,
    string? Document = null,
    string? Email = null,
    string? Phone = null,
    string? Notes = null);

public sealed record UpdateClientCommand(
    string Name,
    string? Document = null,
    string? Email = null,
    string? Phone = null,
    string? Notes = null,
    bool Active = true);

public sealed record ClientView(
    Guid Id,
    string Name,
    string? Document,
    string? Email,
    string? Phone,
    bool Active,
    string? Notes,
    int EventCount,
    DateTimeOffset CreatedAt);

// ── Venues ────────────────────────────────────────────────────────────────────

public sealed record CreateVenueCommand(
    string Name,
    string? Address = null,
    string? City = null,
    string? State = null,
    int? Capacity = null);

public sealed record VenueView(
    Guid Id,
    string Name,
    string? Address,
    string? City,
    string? State,
    int? Capacity);

public sealed record CreateEventCommand(
    Guid VenueId,
    string Name,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Organizer = null,
    string Status = "Draft",
    Guid? ClientId = null);

public sealed record EventView(
    Guid Id,
    Guid VenueId,
    string Name,
    string? Organizer,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    Guid? ClientId = null,
    string? ClientName = null);

public sealed record CreateGateCommand(
    string Name,
    string? Code = null);

public sealed record GateView(
    Guid Id,
    Guid VenueId,
    string Name,
    string? Code,
    bool Active,
    string OperationMode = "EntryAndExitValidated",
    string TurnstileMode = "Active");

public sealed record SetGateOperationModeCommand(
    string OperationMode);

public sealed record SetGateTurnstileModeCommand(
    string TurnstileMode);

public sealed record CreateDeviceCommand(
    string Name,
    string? Identifier = null,
    string DeviceType = "Simulator",
    string? ConfigurationJson = null);

public sealed record UpdateDeviceCommand(
    string Name,
    string? Identifier = null,
    string DeviceType = "Simulator",
    string? ConfigurationJson = null,
    bool Active = true);

public sealed record SetDeviceOperationModeCommand(
    string OperationMode);

public sealed record DeviceView(
    Guid Id,
    Guid EventId,
    Guid GateId,
    string GateName,
    string? GateCode,
    string Name,
    string? Identifier,
    string DeviceType,
    bool Active,
    DateTimeOffset? LastSeenAt,
    string? ConfigurationJson,
    string OperationMode = "Active",
    string? OperationModeOverride = null);

/// <summary>
/// Resolução de um dispositivo (catraca) a partir do seu identifier — o "nome"
/// que a placa envia via MQTT. Traz o vínculo device → portaria → evento,
/// necessário para validar o acesso e comandar a catraca correta.
/// </summary>
public sealed record TurnstileDeviceResolution(
    Guid DeviceId,
    string DeviceName,
    string Identifier,
    string DeviceType,
    string OperationMode,
    string? OperationModeOverride,
    Guid GateId,
    string GateName,
    Guid EventId,
    string EventName,
    string EventStatus);

public sealed record CreateSectorCommand(
    string Name,
    int? Capacity = null);

public sealed record SectorView(
    Guid Id,
    Guid EventId,
    string Name,
    int? Capacity,
    bool Active);

public sealed record CreateGateSectorCommand(
    Guid GateId,
    Guid SectorId,
    string Direction,
    DateTimeOffset? ActiveFrom = null,
    DateTimeOffset? ActiveUntil = null,
    bool Active = true);

public sealed record GateSectorView(
    Guid Id,
    Guid EventId,
    Guid GateId,
    string GateName,
    Guid SectorId,
    string SectorName,
    string Direction,
    DateTimeOffset? ActiveFrom,
    DateTimeOffset? ActiveUntil,
    bool Active);

public sealed record CreateTicketTypeCommand(
    string Name,
    bool AllowsReentry = false);

public sealed record TicketTypeView(
    Guid Id,
    Guid EventId,
    string Name,
    bool AllowsReentry,
    bool Active);

public sealed record CreateTicketBatchCommand(
    Guid TicketTypeId,
    string Name,
    int? MaximumQuantity = null,
    DateTimeOffset? SalesStartsAt = null,
    DateTimeOffset? SalesEndsAt = null);

public sealed record TicketBatchView(
    Guid Id,
    Guid EventId,
    Guid TicketTypeId,
    string Name,
    int? MaximumQuantity,
    DateTimeOffset? SalesStartsAt,
    DateTimeOffset? SalesEndsAt);

public sealed record IssueTicketCommand(
    Guid TicketTypeId,
    Guid? BatchId,
    string ExternalId,
    string Code,
    int MaximumUses = 1,
    string? MetadataJson = null,
    int? MaximumEntries = null);

public sealed record TicketView(
    Guid Id,
    Guid EventId,
    Guid? BatchId,
    Guid TicketTypeId,
    string ExternalId,
    string Code,
    int MaximumUses,
    int Uses,
    string Status,
    string? MetadataJson,
    DateTimeOffset CreatedAt,
    int? MaximumEntries = null,
    int? EntriesUsed = null,
    int? PeopleInside = null,
    Guid? SectorId = null,
    string? SectorName = null,
    string? BatchName = null);

public sealed class CatalogConflictException : Exception
{
    public CatalogConflictException(string message)
        : base(message)
    {
    }
}

public interface ICatalogService
{
    // ── Clientes ─────────────────────────────────────────────────────────────
    Task<ClientView> CreateClientAsync(CreateClientCommand command, CancellationToken cancellationToken = default);
    Task<ClientView> UpdateClientAsync(Guid clientId, UpdateClientCommand command, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClientView>> ListClientsAsync(bool activeOnly, CancellationToken cancellationToken = default);

    // ── Venues ────────────────────────────────────────────────────────────────
    Task<VenueView> CreateVenueAsync(
        CreateVenueCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VenueView>> ListVenuesAsync(
        CancellationToken cancellationToken = default);

    Task<EventView> CreateEventAsync(
        CreateEventCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EventView>> ListEventsAsync(
        Guid? venueId,
        string? status,
        CancellationToken cancellationToken = default);

    Task<SectorView> CreateSectorAsync(
        Guid eventId,
        CreateSectorCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SectorView>> ListSectorsAsync(
        Guid eventId,
        bool activeOnly,
        CancellationToken cancellationToken = default);

    Task<GateSectorView> CreateGateSectorAsync(
        Guid eventId,
        CreateGateSectorCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GateSectorView>> ListGateSectorsAsync(
        Guid eventId,
        bool activeOnly,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveGateSectorAsync(
        Guid eventId,
        Guid gateSectorId,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveGateAsync(
        Guid eventId,
        Guid gateId,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveSectorAsync(
        Guid eventId,
        Guid sectorId,
        CancellationToken cancellationToken = default);

    Task<GateView> CreateGateAsync(
        Guid eventId,
        CreateGateCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GateView>> ListGatesAsync(
        Guid eventId,
        bool activeOnly,
        CancellationToken cancellationToken = default);

    Task<GateView> SetGateOperationModeAsync(
        Guid eventId,
        Guid gateId,
        SetGateOperationModeCommand command,
        CancellationToken cancellationToken = default);

    Task<GateView> SetGateTurnstileModeAsync(
        Guid eventId,
        Guid gateId,
        SetGateTurnstileModeCommand command,
        CancellationToken cancellationToken = default);

    Task<DeviceView> CreateDeviceAsync(
        Guid eventId,
        Guid gateId,
        CreateDeviceCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceView>> ListDevicesAsync(
        Guid eventId,
        Guid? gateId,
        bool activeOnly,
        CancellationToken cancellationToken = default);

    Task<DeviceView> UpdateDeviceAsync(
        Guid eventId,
        Guid gateId,
        Guid deviceId,
        UpdateDeviceCommand command,
        CancellationToken cancellationToken = default);

    Task<DeviceView> SetDeviceOperationModeAsync(
        Guid eventId,
        Guid gateId,
        Guid deviceId,
        SetDeviceOperationModeCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve um dispositivo pelo identifier (o "nome" que a catraca envia via MQTT),
    /// trazendo portaria e evento vinculados. Retorna null se não houver device ativo
    /// com esse identifier associado a um evento/portaria ativos.
    /// </summary>
    Task<TurnstileDeviceResolution?> ResolveTurnstileDeviceAsync(
        string identifier,
        CancellationToken cancellationToken = default);

    /// <summary>Atualiza o last_seen_at do dispositivo (heartbeat). Silencioso se não existir.</summary>
    Task TouchDeviceAsync(
        string identifier,
        CancellationToken cancellationToken = default);

    Task<TicketTypeView> CreateTicketTypeAsync(
        Guid eventId,
        CreateTicketTypeCommand command,
        CancellationToken cancellationToken = default);

    Task<TicketBatchView> CreateTicketBatchAsync(
        Guid eventId,
        CreateTicketBatchCommand command,
        CancellationToken cancellationToken = default);

    Task<TicketView> IssueTicketAsync(
        Guid eventId,
        IssueTicketCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TicketView>> ListTicketsAsync(
        Guid eventId,
        Guid? ticketTypeId,
        Guid? batchId,
        string? externalId,
        string? code,
        string? status,
        CancellationToken cancellationToken = default);

    Task<BulkStatusResult> BulkUpdateTicketStatusAsync(
        Guid eventId,
        IReadOnlyList<Guid> ticketIds,
        string newStatus,
        CancellationToken cancellationToken = default);

    Task<TicketSummaryView> GetTicketSummaryAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);
}

public sealed record BulkStatusResult(int Updated, int NotFound);

public sealed record TicketSummaryView(
    int Total,
    int Active,
    int Used,
    int Cancelled,
    int Revoked,
    int PeopleInside);
