namespace FastPass.Domain.Enums;

public enum EventStatus
{
    Draft,
    Preparing,
    Published,
    Running,
    Closed,
    Archived
}

public enum CredentialType
{
    Ticket,
    StaffBadge
}

public enum AccessDirection
{
    Entry,
    Exit
}

public enum AccessDecision
{
    Approved,
    Rejected
}

public enum AccessAttemptStatus
{
    Decided,
    CommandPending,
    DeviceConfirmed,
    DeviceFailed,
    SyncPending,
    Reconciled
}

public enum DeviceType
{
    Legacy,
    Serial,
    Vcom,
    Mqtt,
    Simulator
}

public enum SyncStatus
{
    Pending,
    Processing,
    Sent,
    Failed,
    Reconciled
}

public enum AccessChannel
{
    App,
    Turnstile,
    Api,
    Manual
}

public enum GateOperationMode
{
    EntryValidatedExitFree,
    EntryAndExitValidated
}

public enum TurnstileOperationMode
{
    /// <summary>Catraca ativa: lê ingresso, valida e libera se autorizado.</summary>
    Active,
    /// <summary>Catraca liberada: gira livremente para ambos os lados (sem validação).</summary>
    Free,
    /// <summary>Catraca bloqueada: não gira para nenhum lado (totalmente travada).</summary>
    Blocked
}

public enum ArmAction
{
    None,
    Unlock,
    KeepLocked
}

public enum AccessPictogram
{
    None,
    GreenArrowEntry,
    GreenArrowExit,
    RedCross,
    Error
}
