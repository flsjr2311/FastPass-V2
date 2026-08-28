namespace FastPass.Application.Reports;

// ── Filtros ───────────────────────────────────────────────────────────────────
public sealed record ValidationReportFilter(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    Guid? GateId = null,
    Guid? SectorId = null,
    string? Direction = null,
    string? TimeZone = null);

// ── Métricas de resumo ────────────────────────────────────────────────────────
public sealed record ValidationSummary(
    int TotalAttempts,
    int Approved,
    int Rejected,
    double ApprovalRate,
    int UniqueTickets,
    int TotalTickets,
    double CoverageRate,
    int PeopleInside,
    int EntriesToday,
    int ExitsToday);

// ── Por portaria ──────────────────────────────────────────────────────────────
public sealed record GateReport(
    string GateId,
    string GateName,
    int Attempts,
    int Approved,
    int Rejected,
    double ApprovalRate,
    int UniqueTickets);

// ── Por setor ─────────────────────────────────────────────────────────────────
public sealed record SectorReport(
    string SectorId,
    string SectorName,
    int TotalTickets,
    int ValidatedTickets,
    double CoverageRate,
    int PeopleInside,
    int? Capacity);

// ── Por hora ──────────────────────────────────────────────────────────────────
public sealed record HourlyReport(
    string Hour,
    int Approved,
    int Rejected,
    int Total);

// ── Motivos de rejeição ───────────────────────────────────────────────────────
public sealed record RejectionReason(
    string Code,
    string Reason,
    int Count,
    double Percentage);

// ── Últimas tentativas ────────────────────────────────────────────────────────
public sealed record RecentAttempt(
    string AttemptId,
    string CredentialCode,
    string? TicketExternalId,
    string Decision,
    string? GateName,
    string? SectorName,
    string Direction,
    DateTimeOffset RequestedAt,
    string? Reason = null);

// ── Relatório completo ────────────────────────────────────────────────────────
public sealed record ValidationReport(
    ValidationSummary Summary,
    IReadOnlyList<GateReport> ByGate,
    IReadOnlyList<SectorReport> BySector,
    IReadOnlyList<HourlyReport> ByHour,
    IReadOnlyList<RejectionReason> RejectionReasons,
    IReadOnlyList<RecentAttempt> RecentAttempts,
    string TimeZone,
    DateTimeOffset GeneratedAt);

// ── Interface ─────────────────────────────────────────────────────────────────
public interface IValidationReportService
{
    Task<ValidationReport> GetValidationReportAsync(
        Guid eventId,
        ValidationReportFilter filter,
        CancellationToken cancellationToken = default);
}
