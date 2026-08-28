using FastPass.Domain.Enums;

namespace FastPass.Domain.Entities;

public sealed class TicketType
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool AllowsReentry { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class TicketBatch
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public Guid TicketTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? MaximumQuantity { get; set; }
    public DateTimeOffset? SalesStartsAt { get; set; }
    public DateTimeOffset? SalesEndsAt { get; set; }
}

public sealed class Ticket
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public Guid? BatchId { get; set; }
    public Guid TicketTypeId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int MaximumUses { get; set; } = 1;
    public int Uses { get; set; }
    public string Status { get; set; } = "active";
}

public sealed class AccessPolicy
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public Guid? TicketTypeId { get; set; }
    public Guid? BatchId { get; set; }
    public Guid? GateId { get; set; }
    public Guid? SectorId { get; set; }
    public AccessDirection Direction { get; set; }
    public int Priority { get; set; }
    public bool Allows { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class AccessAttempt
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public Guid? TicketId { get; set; }
    public Guid? StaffCredentialId { get; set; }
    public CredentialType CredentialType { get; set; }
    public Guid GateId { get; set; }
    public Guid? DeviceId { get; set; }
    public string CredentialCode { get; set; } = string.Empty;
    public AccessDirection Direction { get; set; }
    public AccessDecision Decision { get; set; }
    public string? Reason { get; set; }
    public AccessAttemptStatus Status { get; set; } = AccessAttemptStatus.Decided;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
}
