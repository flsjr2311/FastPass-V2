using FastPass.Application.Audit;
using FastPass.Infrastructure.Database;
using MySqlConnector;

namespace FastPass.Infrastructure.Audit;

public sealed class MySqlAuditTrailService : IAuditTrailService
{
    private readonly FastPassDbConnectionFactory _factory;

    public MySqlAuditTrailService(FastPassDbConnectionFactory factory) => _factory = factory;

    public async Task RecordAsync(AuditTrailRecord record, CancellationToken ct = default)
    {
        try
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);
            await using var c = conn.CreateCommand();
            c.CommandText = """
                INSERT INTO fp_audit_trail
                    (id,user_id,user_name,action,method,path,target_id,status_code,summary,ip,user_agent,created_at)
                VALUES
                    (@id,@uid,@un,@action,@method,@path,@target,@status,@summary,@ip,@ua,@now);
                """;
            c.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            c.Parameters.AddWithValue("@uid", (object?)record.UserId?.ToString() ?? DBNull.Value);
            c.Parameters.AddWithValue("@un", (object?)Trunc(record.UserName, 128) ?? DBNull.Value);
            c.Parameters.AddWithValue("@action", Trunc(record.Action, 128) ?? "Ação");
            c.Parameters.AddWithValue("@method", Trunc(record.Method, 8) ?? "");
            c.Parameters.AddWithValue("@path", Trunc(record.Path, 512) ?? "");
            c.Parameters.AddWithValue("@target", (object?)Trunc(record.TargetId, 128) ?? DBNull.Value);
            c.Parameters.AddWithValue("@status", record.StatusCode);
            c.Parameters.AddWithValue("@summary", (object?)Trunc(record.Summary, 4000) ?? DBNull.Value);
            c.Parameters.AddWithValue("@ip", (object?)Trunc(record.Ip, 64) ?? DBNull.Value);
            c.Parameters.AddWithValue("@ua", (object?)Trunc(record.UserAgent, 512) ?? DBNull.Value);
            c.Parameters.AddWithValue("@now", DateTime.UtcNow);
            await c.ExecuteNonQueryAsync(ct);
        }
        catch { /* trilha é best-effort; nunca propaga */ }
    }

    public async Task<AuditTrailPage> ListAsync(
        int page, int pageSize, string? userName, string? action,
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 50;

        var where = new List<string>();
        var ps = new List<(string, object)>();
        if (!string.IsNullOrWhiteSpace(userName)) { where.Add("user_name LIKE @un"); ps.Add(("@un", $"%{userName.Trim()}%")); }
        if (!string.IsNullOrWhiteSpace(action)) { where.Add("action LIKE @action"); ps.Add(("@action", $"%{action.Trim()}%")); }
        if (from.HasValue) { where.Add("created_at >= @from"); ps.Add(("@from", from.Value.UtcDateTime)); }
        if (to.HasValue) { where.Add("created_at <= @to"); ps.Add(("@to", to.Value.UtcDateTime)); }
        var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);

        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM fp_audit_trail {whereSql};";
        foreach (var (n, v) in ps) countCmd.Parameters.AddWithValue(n, v);
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        var data = new List<AuditTrailEntry>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT id,user_id,user_name,action,method,path,target_id,status_code,summary,ip,user_agent,created_at
            FROM fp_audit_trail
            {whereSql}
            ORDER BY created_at DESC
            LIMIT @take OFFSET @skip;
            """;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.Parameters.AddWithValue("@take", pageSize);
        cmd.Parameters.AddWithValue("@skip", (page - 1) * pageSize);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            data.Add(new AuditTrailEntry(
                Guid.Parse(r.GetValue(0).ToString()!),
                r.IsDBNull(1) ? null : Guid.Parse(r.GetValue(1).ToString()!),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.GetString(3),
                r.GetString(4),
                r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.GetInt32(7),
                r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9),
                r.IsDBNull(10) ? null : r.GetString(10),
                new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(11), DateTimeKind.Utc))));
        }
        return new AuditTrailPage(page, pageSize, total, data);
    }

    private static string? Trunc(string? s, int max) =>
        s is null ? null : (s.Length > max ? s[..max] : s);
}
