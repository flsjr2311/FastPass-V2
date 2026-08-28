namespace FastPass.Application.Access;

public sealed record CreateAccessPolicyCommand(
    Guid? TicketTypeId = null,
    Guid? BatchId = null,
    Guid? GateId = null,
    Guid? SectorId = null,
    string Direction = "Entry",
    int Priority = 0,
    bool Allows = true,
    bool Active = true,
    string? ConditionsJson = null);

public sealed record AccessPolicyView(
    Guid Id,
    Guid EventId,
    Guid? TicketTypeId,
    Guid? BatchId,
    Guid? GateId,
    Guid? SectorId,
    string Direction,
    int Priority,
    bool Allows,
    bool Active,
    string? ConditionsJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public interface IAccessPolicyService
{
    Task<AccessPolicyView> CreateAsync(
        Guid eventId,
        CreateAccessPolicyCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccessPolicyView>> ListAsync(
        Guid eventId,
        Guid? ticketTypeId,
        Guid? batchId,
        Guid? gateId,
        Guid? sectorId,
        string? direction,
        bool activeOnly,
        CancellationToken cancellationToken = default);
}
