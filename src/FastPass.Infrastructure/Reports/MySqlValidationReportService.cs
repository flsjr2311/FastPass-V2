using FastPass.Application.Reports;
using FastPass.Infrastructure.Database;
using MySqlConnector;

namespace FastPass.Infrastructure.Reports;

/// <summary>
/// Serviço de relatórios analíticos de validação.
/// Todas as queries são somente-leitura e otimizadas para exibição em dashboard.
/// </summary>
public sealed class MySqlValidationReportService : IValidationReportService
{
    private readonly FastPassDbConnectionFactory _factory;

    public MySqlValidationReportService(FastPassDbConnectionFactory factory) => _factory = factory;

    public async Task<ValidationReport> GetValidationReportAsync(
        Guid eventId,
        ValidationReportFilter filter,
        CancellationToken ct = default)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("EventId é obrigatório.");

        var tz = string.IsNullOrWhiteSpace(filter.TimeZone) ? "UTC" : filter.TimeZone;
        var now = DateTimeOffset.UtcNow;

        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);

        var (summary, recentAttempts) = await GetSummaryAsync(conn, eventId, filter, now, ct);
        var byGate = await GetByGateAsync(conn, eventId, filter, ct);
        var bySector = await GetBySectorAsync(conn, eventId, filter, ct);
        var byHour = await GetByHourAsync(conn, eventId, filter, tz, ct);
        var rejections = await GetRejectionReasonsAsync(conn, eventId, filter, ct);

        return new ValidationReport(summary, byGate, bySector, byHour, rejections, recentAttempts, tz, now);
    }

    // ── Resumo ────────────────────────────────────────────────────────────────

    private static async Task<(ValidationSummary Summary, IReadOnlyList<RecentAttempt> Recent)> GetSummaryAsync(
        MySqlConnection conn, Guid eventId, ValidationReportFilter filter, DateTimeOffset now,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildBaseQuery("""
            SELECT
                COUNT(*) total,
                COALESCE(SUM(CASE WHEN a.decision = 'Approved' THEN 1 ELSE 0 END), 0) approved,
                COALESCE(SUM(CASE WHEN a.decision = 'Rejected' THEN 1 ELSE 0 END), 0) rejected,
                COUNT(DISTINCT CASE WHEN a.decision = 'Approved' THEN a.ticket_id END) unique_tickets
            FROM fp_access_attempts a
            """, filter, eventId);
        AddFilterParams(cmd, filter, eventId);

        int total = 0, approved = 0, rejected = 0, uniqueTickets = 0;
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (await r.ReadAsync(ct))
            {
                total         = r.IsDBNull(0) ? 0 : Convert.ToInt32(r.GetValue(0));
                approved      = r.IsDBNull(1) ? 0 : Convert.ToInt32(r.GetValue(1));
                rejected      = r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetValue(2));
                uniqueTickets = r.IsDBNull(3) ? 0 : Convert.ToInt32(r.GetValue(3));
            }
        }

        // Total de tickets do evento
        await using var tcmd = conn.CreateCommand();
        tcmd.CommandText = "SELECT COUNT(*) FROM fp_tickets WHERE event_id=@eid AND status='active';";
        tcmd.Parameters.AddWithValue("@eid", eventId.ToString());
        var totalTickets = Convert.ToInt32(await tcmd.ExecuteScalarAsync(ct));

        // Pessoas dentro agora
        await using var pcmd = conn.CreateCommand();
        pcmd.CommandText = "SELECT COALESCE(SUM(people_inside),0) FROM fp_tickets WHERE event_id=@eid AND status='active';";
        pcmd.Parameters.AddWithValue("@eid", eventId.ToString());
        var peopleInside = Convert.ToInt32(await pcmd.ExecuteScalarAsync(ct));

        // Entradas e saídas de hoje
        var todayUtc = now.UtcDateTime.Date;
        await using var todaycmd = conn.CreateCommand();
        todaycmd.CommandText = """
            SELECT
                SUM(CASE WHEN direction='Entry' AND decision='Approved' THEN 1 ELSE 0 END) entries,
                SUM(CASE WHEN direction='Exit'  AND decision='Approved' THEN 1 ELSE 0 END) exits
            FROM fp_access_attempts
            WHERE event_id=@eid AND requested_at >= @today;
            """;
        todaycmd.Parameters.AddWithValue("@eid", eventId.ToString());
        todaycmd.Parameters.AddWithValue("@today", todayUtc);
        int entriesToday = 0, exitsToday = 0;
        await using (var r2 = await todaycmd.ExecuteReaderAsync(ct))
        {
            if (await r2.ReadAsync(ct))
            {
                entriesToday = r2.IsDBNull(0) ? 0 : Convert.ToInt32(r2.GetValue(0));
                exitsToday   = r2.IsDBNull(1) ? 0 : Convert.ToInt32(r2.GetValue(1));
            }
        }

        double approvalRate = total > 0 ? Math.Round((double)approved / total * 100, 1) : 0;
        double coverageRate = totalTickets > 0 ? Math.Round((double)uniqueTickets / totalTickets * 100, 1) : 0;

        var summary = new ValidationSummary(total, approved, rejected, approvalRate,
            uniqueTickets, totalTickets, coverageRate, peopleInside, entriesToday, exitsToday);

        // Últimas 20 tentativas aprovadas
        await using var rcmd = conn.CreateCommand();
        rcmd.CommandText = BuildBaseQuery("""
            SELECT a.id, a.credential_code, t.external_id, a.decision,
                   g.name, s.name, a.direction, a.requested_at, a.reason
            FROM fp_access_attempts a
            LEFT JOIN fp_tickets t ON t.id = a.ticket_id
            LEFT JOIN fp_gates g ON g.id = a.gate_id
            LEFT JOIN fp_sectors s ON s.id = a.sector_id
            """, filter, eventId, " ORDER BY a.requested_at DESC LIMIT 20");
        AddFilterParams(rcmd, filter, eventId);
        var recent = new List<RecentAttempt>();
        await using var rr = await rcmd.ExecuteReaderAsync(ct);
        while (await rr.ReadAsync(ct))
        {
            recent.Add(new RecentAttempt(
                rr.GetValue(0).ToString()!,
                MaskCode(rr.GetString(1)),
                rr.IsDBNull(2) ? null : rr.GetString(2),
                rr.GetString(3),
                rr.IsDBNull(4) ? null : rr.GetString(4),
                rr.IsDBNull(5) ? null : rr.GetString(5),
                rr.GetString(6),
                new DateTimeOffset(DateTime.SpecifyKind(rr.GetDateTime(7), DateTimeKind.Utc)),
                rr.IsDBNull(8) ? null : rr.GetString(8)));
        }

        return (summary, recent);
    }

    // ── Por portaria ──────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<GateReport>> GetByGateAsync(
        MySqlConnection conn, Guid eventId, ValidationReportFilter filter, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        // Parte de fp_event_gates (portarias vinculadas ao evento), não de fp_access_attempts —
        // assim uma portaria recém-criada/sem tentativas ainda aparece no relatório com zeros,
        // no mesmo padrão já usado em GetBySectorAsync.
        var joinConditions = new System.Text.StringBuilder("a.event_id=@eid AND a.gate_id=eg.gate_id");
        if (filter.From.HasValue) joinConditions.Append(" AND a.requested_at >= @from");
        if (filter.To.HasValue) joinConditions.Append(" AND a.requested_at <= @to");
        if (filter.SectorId.HasValue) joinConditions.Append(" AND a.sector_id = @sector_id");
        if (!string.IsNullOrWhiteSpace(filter.Direction)) joinConditions.Append(" AND a.direction = @direction");

        cmd.CommandText = $"""
            SELECT eg.gate_id, g.name,
                   COUNT(a.id) total,
                   COALESCE(SUM(CASE WHEN a.decision='Approved' THEN 1 ELSE 0 END), 0) approved,
                   COALESCE(SUM(CASE WHEN a.decision='Rejected' THEN 1 ELSE 0 END), 0) rejected,
                   COUNT(DISTINCT a.ticket_id) unique_tickets
            FROM fp_event_gates eg
            INNER JOIN fp_gates g ON g.id = eg.gate_id
            LEFT JOIN fp_access_attempts a ON {joinConditions}
            WHERE eg.event_id=@eid AND eg.active=1 AND g.active=1
            GROUP BY eg.gate_id, g.name
            ORDER BY total DESC, g.name;
            """;
        cmd.Parameters.AddWithValue("@eid", eventId.ToString());
        if (filter.From.HasValue) cmd.Parameters.AddWithValue("@from", filter.From.Value.UtcDateTime);
        if (filter.To.HasValue) cmd.Parameters.AddWithValue("@to", filter.To.Value.UtcDateTime);
        if (filter.SectorId.HasValue) cmd.Parameters.AddWithValue("@sector_id", filter.SectorId.Value.ToString());
        if (!string.IsNullOrWhiteSpace(filter.Direction)) cmd.Parameters.AddWithValue("@direction", filter.Direction);

        var result = new List<GateReport>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var tot = r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetValue(2));
            var app = r.IsDBNull(3) ? 0 : Convert.ToInt32(r.GetValue(3));
            result.Add(new GateReport(
                r.GetValue(0).ToString()!,
                r.IsDBNull(1) ? "Desconhecida" : r.GetString(1),
                tot, app,
                r.IsDBNull(4) ? 0 : Convert.ToInt32(r.GetValue(4)),
                tot > 0 ? Math.Round((double)app / tot * 100, 1) : 0,
                r.IsDBNull(5) ? 0 : Convert.ToInt32(r.GetValue(5))));
        }
        return result;
    }

    // ── Por setor ─────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<SectorReport>> GetBySectorAsync(
        MySqlConnection conn, Guid eventId, ValidationReportFilter filter, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.name, s.capacity,
                   (SELECT COUNT(*) FROM fp_tickets t WHERE t.event_id=s.event_id AND t.sector_id=s.id AND t.status='active') total_tickets,
                   (SELECT COUNT(DISTINCT a2.ticket_id)
                    FROM fp_access_attempts a2
                    WHERE a2.event_id=s.event_id AND a2.sector_id=s.id AND a2.decision='Approved') validated,
                   (SELECT COALESCE(SUM(t2.people_inside),0)
                    FROM fp_tickets t2
                    WHERE t2.event_id=s.event_id AND t2.sector_id=s.id AND t2.status='active') people_inside
            FROM fp_sectors s
            WHERE s.event_id=@eid AND s.active=1
            ORDER BY s.name;
            """;
        cmd.Parameters.AddWithValue("@eid", eventId.ToString());
        var result = new List<SectorReport>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var total     = r.IsDBNull(3) ? 0 : Convert.ToInt32(r.GetValue(3));
            var validated = r.IsDBNull(4) ? 0 : Convert.ToInt32(r.GetValue(4));
            result.Add(new SectorReport(
                r.GetValue(0).ToString()!,
                r.GetString(1),
                total, validated,
                total > 0 ? Math.Round((double)validated / total * 100, 1) : 0,
                r.IsDBNull(5) ? 0 : Convert.ToInt32(r.GetValue(5)),
                r.IsDBNull(2) ? null : Convert.ToInt32(r.GetValue(2))));
        }
        return result;
    }

    // ── Por hora ──────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<HourlyReport>> GetByHourAsync(
        MySqlConnection conn, Guid eventId, ValidationReportFilter filter,
        string tz, CancellationToken ct)
    {
        // MySQL não suporta timezone por nome na função CONVERT_TZ de forma garantida;
        // usamos UTC e convertemos no cliente
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildBaseQuery("""
            SELECT DATE_FORMAT(a.requested_at, '%Y-%m-%d %H:00') hour_utc,
                   COALESCE(SUM(CASE WHEN a.decision='Approved' THEN 1 ELSE 0 END), 0) approved,
                   COALESCE(SUM(CASE WHEN a.decision='Rejected' THEN 1 ELSE 0 END), 0) rejected,
                   COUNT(*) total
            FROM fp_access_attempts a
            """, filter, eventId, " GROUP BY hour_utc ORDER BY hour_utc");
        AddFilterParams(cmd, filter, eventId);
        var result = new List<HourlyReport>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new HourlyReport(
                r.IsDBNull(0) ? "—" : r.GetString(0),
                r.IsDBNull(1) ? 0 : Convert.ToInt32(r.GetValue(1)),
                r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetValue(2)),
                r.IsDBNull(3) ? 0 : Convert.ToInt32(r.GetValue(3))));
        }
        return result;
    }

    // ── Motivos de rejeição ───────────────────────────────────────────────────

    private static async Task<IReadOnlyList<RejectionReason>> GetRejectionReasonsAsync(
        MySqlConnection conn, Guid eventId, ValidationReportFilter filter, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildBaseQuery("""
            SELECT COALESCE(a.reason_code, 'UNKNOWN') reason_code,
                   COALESCE(a.reason, 'Motivo desconhecido') reason_text,
                   COUNT(*) cnt
            FROM fp_access_attempts a
            """, filter, eventId, " AND a.decision='Rejected' GROUP BY a.reason_code, a.reason ORDER BY cnt DESC LIMIT 20");
        AddFilterParams(cmd, filter, eventId);
        var rows = new List<(string Code, string Reason, int Count)>();
        int totalRejected = 0;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var cnt = Convert.ToInt32(r.GetValue(2));
            rows.Add((r.GetString(0), r.GetString(1), cnt));
            totalRejected += cnt;
        }
        return rows.Select(x => new RejectionReason(
            x.Code, x.Reason, x.Count,
            totalRejected > 0 ? Math.Round((double)x.Count / totalRejected * 100, 1) : 0))
            .ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildBaseQuery(string select, ValidationReportFilter filter,
        Guid eventId, string suffix = "")
    {
        var where = new System.Text.StringBuilder("WHERE a.event_id=@eid");
        if (filter.From.HasValue) where.Append(" AND a.requested_at >= @from");
        if (filter.To.HasValue) where.Append(" AND a.requested_at <= @to");
        if (filter.GateId.HasValue) where.Append(" AND a.gate_id = @gate_id");
        if (filter.SectorId.HasValue) where.Append(" AND a.sector_id = @sector_id");
        if (!string.IsNullOrWhiteSpace(filter.Direction)) where.Append(" AND a.direction = @direction");
        return $"{select} {where}{suffix};";
    }

    private static void AddFilterParams(MySqlCommand cmd,
        ValidationReportFilter filter, Guid eventId)
    {
        cmd.Parameters.AddWithValue("@eid", eventId.ToString());
        if (filter.From.HasValue) cmd.Parameters.AddWithValue("@from", filter.From.Value.UtcDateTime);
        if (filter.To.HasValue) cmd.Parameters.AddWithValue("@to", filter.To.Value.UtcDateTime);
        if (filter.GateId.HasValue) cmd.Parameters.AddWithValue("@gate_id", filter.GateId.Value.ToString());
        if (filter.SectorId.HasValue) cmd.Parameters.AddWithValue("@sector_id", filter.SectorId.Value.ToString());
        if (!string.IsNullOrWhiteSpace(filter.Direction)) cmd.Parameters.AddWithValue("@direction", filter.Direction);
    }

    private static string MaskCode(string code)
    {
        if (code.Length <= 4) return "****";
        return code[..4] + new string('*', Math.Max(0, code.Length - 4));
    }
}
