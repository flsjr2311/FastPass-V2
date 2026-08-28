namespace FastPass.Application.Access;

public sealed record ValidateAccessCommand(
    string CredentialCode,
    Guid EventId,
    Guid GateId,
    Guid? SectorId,
    string? Direction,
    string IdempotencyKey,
    Guid? DeviceId = null,
    string Channel = "Turnstile");

public sealed record AccessValidationResult(
    Guid AttemptId,
    bool Approved,
    string Decision,
    string CredentialType,
    string? Reason,
    Guid? StaffCredentialId,
    Guid? StaffMemberId,
    string? StaffName,
    bool IdempotentReplay,
    string Channel = "Turnstile",
    string Direction = "Entry",
    Guid? TicketId = null,
    int? MaximumEntries = null,
    int? EntriesUsed = null,
    int? PeopleInside = null,
    string ArmAction = "None",
    string Pictogram = "None",
    string? ReasonCode = null,
    string? Message = null,
    AccessMessagePresentation? Presentation = null);

public interface IAccessValidationService
{
    Task<AccessValidationResult> ValidateAsync(
        ValidateAccessCommand command,
        CancellationToken cancellationToken = default);
}
