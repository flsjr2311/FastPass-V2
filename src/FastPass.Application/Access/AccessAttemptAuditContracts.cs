namespace FastPass.Application.Access;

public sealed record AccessAttemptAuditFilter(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    Guid? GateId = null,
    Guid? DeviceId = null,
    Guid? TicketId = null,
    Guid? StaffCredentialId = null,
    string? CredentialType = null,
    string? Direction = null,
    string? Decision = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 50,
    Guid? SectorId = null);

public sealed record AccessAttemptAuditView(
    Guid AttemptId,
    Guid EventId,
    Guid? TicketId,
    Guid? StaffCredentialId,
    Guid? StaffMemberId,
    Guid GateId,
    Guid? SectorId,
    Guid? DeviceId,
    string CredentialType,
    string Direction,
    string Decision,
    string? Reason,
    string Status,
    string CredentialCodeMasked,
    string? TicketExternalId,
    string? StaffName,
    string? GateName,
    string? SectorName,
    string? DeviceName,
    DateTimeOffset RequestedAt,
    DateTimeOffset CreatedAt,
    string? TicketSectorName = null,
    string? Channel = null,
    string? AppDeviceLabel = null);

public sealed record AccessAttemptPage(
    IReadOnlyList<AccessAttemptAuditView> Data,
    int Page,
    int PageSize,
    long Total,
    bool HasNext);

public sealed record AccessAttemptCountView(
    string Key,
    long Count);

public sealed record AccessAttemptSummaryView(
    Guid EventId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    long TotalAttempts,
    long Approved,
    long Rejected,
    double ApprovalRate,
    IReadOnlyList<AccessAttemptCountView> ByCredentialType,
    IReadOnlyList<AccessAttemptCountView> ByDirection,
    IReadOnlyList<AccessAttemptCountView> ByDecision,
    IReadOnlyList<AccessAttemptCountView> ByGate,
    IReadOnlyList<AccessAttemptCountView> ByReason,
    IReadOnlyList<AccessAttemptCountView> BySector);

public interface IAccessAttemptQueryService
{
    Task<AccessAttemptPage> ListAsync(
        Guid eventId,
        AccessAttemptAuditFilter filter,
        CancellationToken cancellationToken = default);

    Task<AccessAttemptSummaryView> SummaryAsync(
        Guid eventId,
        AccessAttemptAuditFilter filter,
        CancellationToken cancellationToken = default);
}
