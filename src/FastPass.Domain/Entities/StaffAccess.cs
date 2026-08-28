using FastPass.Domain.Enums;

namespace FastPass.Domain.Entities;

public sealed class StaffMember
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string EmployeeCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class StaffCredential
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid StaffMemberId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string CredentialType { get; set; } = "employee_badge";
    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class StaffEventAccess
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public Guid StaffMemberId { get; set; }
    public Guid? GateId { get; set; }
    public Guid? SectorId { get; set; }
    public string Profile { get; set; } = "operator";
    public AccessDirection Direction { get; set; } = AccessDirection.Entry;
    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public bool Active { get; set; } = true;
}
