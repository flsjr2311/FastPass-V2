namespace FastPass.Application.Audit;

/// <summary>Um registro da trilha de auditoria de ações no sistema.</summary>
public sealed record AuditTrailEntry(
    Guid Id,
    Guid? UserId,
    string? UserName,
    string Action,
    string Method,
    string Path,
    string? TargetId,
    int StatusCode,
    string? Summary,
    string? Ip,
    string? UserAgent,
    DateTimeOffset CreatedAt);

public sealed record AuditTrailPage(
    int Page,
    int PageSize,
    int Total,
    IReadOnlyList<AuditTrailEntry> Data);

/// <summary>Dados de uma ação a ser registrada na trilha.</summary>
public sealed record AuditTrailRecord(
    Guid? UserId,
    string? UserName,
    string Action,
    string Method,
    string Path,
    string? TargetId,
    int StatusCode,
    string? Summary,
    string? Ip,
    string? UserAgent);

public interface IAuditTrailService
{
    /// <summary>Persiste uma ação na trilha. Best-effort: não deve lançar em caso de falha.</summary>
    Task RecordAsync(AuditTrailRecord record, CancellationToken cancellationToken = default);

    /// <summary>Leitura paginada da trilha, com filtros opcionais.</summary>
    Task<AuditTrailPage> ListAsync(
        int page, int pageSize, string? userName, string? action,
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default);
}
