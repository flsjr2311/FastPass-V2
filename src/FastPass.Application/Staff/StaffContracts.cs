namespace FastPass.Application.Staff;

public sealed record RegisterStaffCommand(
    string Name,
    string BadgeCode,
    string? EmployeeCode = null,
    string? Department = null,
    string? JobTitle = null,
    string CredentialType = "employee_badge",
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidUntil = null);

public sealed record UpdateStaffCommand(
    string Name,
    string BadgeCode,
    string? EmployeeCode = null,
    string? Department = null,
    string? JobTitle = null,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidUntil = null,
    bool Active = true);

public sealed record StaffCredentialView(
    Guid StaffId,
    Guid CredentialId,
    string EmployeeCode,
    string Name,
    string? Department,
    string? JobTitle,
    string BadgeCode,
    string CredentialType,
    bool Active,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil);

public sealed class StaffConflictException : Exception
{
    public StaffConflictException(string message)
        : base(message)
    {
    }
}

public interface IStaffService
{
    Task<StaffCredentialView> RegisterAsync(
        RegisterStaffCommand command,
        CancellationToken cancellationToken = default);

    Task<StaffCredentialView> UpdateAsync(
        Guid staffId,
        UpdateStaffCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StaffCredentialView>> ListAsync(
        bool activeOnly,
        CancellationToken cancellationToken = default);

    Task<StaffAccessView> GrantAccessAsync(
        Guid staffId,
        Guid eventId,
        GrantStaffAccessCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StaffAccessView>> ListAccessAsync(
        Guid staffId,
        Guid? eventId,
        CancellationToken cancellationToken = default);
}

public sealed record GrantStaffAccessCommand(
    Guid? GateId,
    Guid? SectorId,
    string Profile = "operator",
    string Direction = "Entry",
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidUntil = null);

public sealed record StaffAccessView(
    Guid Id,
    Guid StaffId,
    Guid EventId,
    Guid? GateId,
    Guid? SectorId,
    string Profile,
    string Direction,
    bool Active,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil);
