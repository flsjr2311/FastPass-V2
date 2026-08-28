using FastPass.Domain.Enums;

namespace FastPass.Domain.Entities;

public sealed class Event
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Organizer { get; set; }
    public Guid VenueId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public EventStatus Status { get; set; } = EventStatus.Draft;
}

public sealed class Venue
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public int? Capacity { get; set; }
}

public sealed class Sector
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class Gate
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid VenueId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class Device
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid GateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Identifier { get; set; }
    public DeviceType Type { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class GateSectorAccess
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public Guid GateId { get; set; }
    public Guid SectorId { get; set; }
    public AccessDirection Direction { get; set; }
    public DateTimeOffset? ActiveFrom { get; set; }
    public DateTimeOffset? ActiveUntil { get; set; }
    public bool Active { get; set; } = true;
}
