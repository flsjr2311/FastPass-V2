using System.Security.Cryptography;
using System.Text;
using FastPass.Application.Import;
using FastPass.Infrastructure.Database;
using MySqlConnector;

namespace FastPass.Infrastructure.Import;

/// <summary>
/// Importação de ingressos via CSV.
/// - Detecta separador automaticamente (vírgula, ponto-e-vírgula, tab, pipe).
/// - Remove BOM UTF-8 se presente.
/// - Modos: AdicionarAtualizar | SomenteAdicionar | SomenteAtualizar.
/// - Cria TicketType "Importação" e Batch por arquivo se não informados.
/// - Processa em lotes de 100 linhas dentro de transação.
/// </summary>
public sealed class MySqlTicketImportService : ITicketImportService
{
    private const int PreviewSampleSize = 5;
    private const int BatchSize = 100;

    private readonly FastPassDbConnectionFactory _factory;

    public MySqlTicketImportService(FastPassDbConnectionFactory factory) => _factory = factory;

    // ── Preview ───────────────────────────────────────────────────────────

    public Task<CsvPreviewResult> PreviewAsync(string csvContent, CancellationToken ct = default)
    {
        var content = StripBom(csvContent);
        var sep = DetectSeparator(content);
        var lines = SplitLines(content).ToList();

        string[] headers = [];
        var samples = new List<string[]>();
        var dataCount = 0;

        foreach (var line in lines)
        {
            var cells = SplitLine(line, sep);
            if (headers.Length == 0) { headers = cells; continue; }
            dataCount++;
            if (samples.Count < PreviewSampleSize) samples.Add(cells);
        }

        return Task.FromResult(new CsvPreviewResult(
            sep == '\t' ? "\\t" : sep.ToString(),
            samples, dataCount, headers));
    }

    // ── Importação ────────────────────────────────────────────────────────

    public async Task<ImportView> ImportAsync(
        StartImportCommand command, Guid? userId, CancellationToken ct = default)
    {
        if (command.EventId == Guid.Empty) throw new ArgumentException("EventId é obrigatório.");
        if (string.IsNullOrWhiteSpace(command.CsvContent)) throw new ArgumentException("Arquivo CSV vazio.");
        if (command.ColCode < 0) throw new ArgumentException("Índice de coluna de código inválido.");
        if (command.DefaultMaxEntries < 1) throw new ArgumentException("DefaultMaxEntries deve ser ≥ 1.");

        var content = StripBom(command.CsvContent);
        var sep = command.Separator == "\\t" ? '\t'
            : command.Separator is { Length: 1 } s ? s[0]
            : DetectSeparator(content);

        var lines = SplitLines(content).ToList();
        var dataLines = lines.Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var fileHash = ComputeHash(content);

        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);

        if (!await ExistsAsync(conn,
            "SELECT COUNT(*) FROM fp_events WHERE id=@id;", ct, ("@id", command.EventId.ToString())))
            throw new ArgumentException("Evento não encontrado.");

        // Setores cadastrados no evento (nome normalizado → id) — usado para resolver a
        // coluna de setor do CSV e para a validação bloqueante abaixo.
        var sectorsByName = await LoadSectorsByNameAsync(conn, command.EventId, ct);

        if (command.DefaultSectorId is { } defaultSectorId && defaultSectorId != Guid.Empty
            && !sectorsByName.Values.Contains(defaultSectorId))
            throw new ArgumentException("O setor padrão informado não está cadastrado neste evento.");

        // Validação bloqueante: se houver coluna de setor, TODOS os nomes distintos do
        // arquivo precisam existir como setor cadastrado no evento. Se faltar algum,
        // a importação inteira é rejeitada antes de tocar no banco — evita tickets órfãos
        // e importações parciais silenciosas.
        if (command.ColSector.HasValue)
        {
            var unknown = dataLines
                .Select(l => GetCell(SplitLine(l, sep), command.ColSector.Value)?.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(name => !sectorsByName.ContainsKey(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (unknown.Count > 0)
                throw new UnknownSectorsException(unknown);
        }

        // Garante TicketType
        Guid ticketTypeId;
        if (command.TicketTypeId is { } ttId && ttId != Guid.Empty)
        {
            ticketTypeId = ttId;
        }
        else
        {
            var existing = await ScalarAsync(conn,
                "SELECT id FROM fp_ticket_types WHERE event_id=@eid AND name='Importação' LIMIT 1;",
                ct, ("@eid", command.EventId.ToString()));
            if (existing is null)
            {
                ticketTypeId = Guid.NewGuid();
                var n = DateTime.UtcNow;
                await ExecAsync(conn, """
                    INSERT INTO fp_ticket_types
                        (id,event_id,name,allows_reentry,active,created_at,updated_at)
                    VALUES (@id,@eid,'Importação',0,1,@now,@now);
                    """, ct,
                    ("@id", ticketTypeId.ToString()), ("@eid", command.EventId.ToString()), ("@now", n));
            }
            else ticketTypeId = Guid.Parse(existing.ToString()!);
        }

        // Garante Batch
        Guid batchId;
        if (command.BatchId is { } bId && bId != Guid.Empty)
        {
            batchId = bId;
        }
        else
        {
            batchId = Guid.NewGuid();
            var batchName = $"Importação {Path.GetFileNameWithoutExtension(command.FileName)} {Guid.NewGuid().ToString("N")[..8]}";
            var n = DateTime.UtcNow;
            await ExecAsync(conn, """
                INSERT INTO fp_ticket_batches
                    (id,event_id,ticket_type_id,name,created_at,updated_at)
                VALUES (@id,@eid,@tid,@name,@now,@now);
                """, ct,
                ("@id", batchId.ToString()), ("@eid", command.EventId.ToString()),
                ("@tid", ticketTypeId.ToString()), ("@name", batchName), ("@now", n));
        }

        // Cria registro de importação
        var importId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;
        var modeStr = ModeToString(command.Mode);

        await ExecAsync(conn, """
            INSERT INTO fp_ticket_imports
                (id,event_id,ticket_type_id,batch_id,file_name,file_hash,col_separator,
                 col_code,col_external_id,col_max_entries,default_max_entries,
                 import_mode,import_status,total_rows,processed,inserted,updated,skipped,errors,
                 created_by,started_at,created_at)
            VALUES
                (@id,@eid,@tid,@bid,@fn,@fh,@sep,
                 @cc,@cei,@cme,@dme,
                 @mode,'processing',@total,0,0,0,0,0,
                 @uid,@now,@now);
            """, ct,
            ("@id", importId.ToString()), ("@eid", command.EventId.ToString()),
            ("@tid", ticketTypeId.ToString()), ("@bid", batchId.ToString()),
            ("@fn", command.FileName), ("@fh", fileHash),
            ("@sep", sep == '\t' ? "\\t" : sep.ToString()),            ("@cc", command.ColCode),
            ("@cei", (object?)command.ColExternalId ?? DBNull.Value),
            ("@cme", (object?)command.ColMaxEntries ?? DBNull.Value),
            ("@dme", command.DefaultMaxEntries), ("@mode", modeStr),
            ("@total", dataLines.Count),
            ("@uid", (object?)userId?.ToString() ?? DBNull.Value),
            ("@now", createdAt));

        int inserted = 0, updated = 0, skipped = 0, errors = 0;

        // Processa em lotes
        for (int bStart = 0; bStart < dataLines.Count; bStart += BatchSize)
        {
            var chunk = dataLines.Skip(bStart).Take(BatchSize).ToList();
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                foreach (var (line, idx) in chunk.Select((l, i) => (l, i)))
                {
                    var rowNum = bStart + idx + 1;
                    var cells = SplitLine(line, sep);

                    var code = GetCell(cells, command.ColCode)?.Trim();
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        await InsertRow(conn, tx, importId, rowNum, "(vazio)", null,
                            command.DefaultMaxEntries, "skipped", "Código vazio", null, null, null, ct);
                        skipped++;
                        continue;
                    }

                    var externalId = command.ColExternalId.HasValue
                        ? GetCell(cells, command.ColExternalId.Value)?.Trim() ?? code
                        : code;

                    var maxEnt = command.DefaultMaxEntries;
                    if (command.ColMaxEntries.HasValue)
                    {
                        var rawMax = GetCell(cells, command.ColMaxEntries.Value);
                        if (int.TryParse(rawMax, out var pm) && pm >= 1) maxEnt = pm;
                    }

                    // Resolve status: coluna do CSV tem precedência sobre o padrão
                    var ticketStatus = command.DefaultStatus;
                    if (command.ColStatus.HasValue)
                    {
                        var rawStatus = GetCell(cells, command.ColStatus.Value)?.Trim();
                        if (!string.IsNullOrWhiteSpace(rawStatus))
                            ticketStatus = NormalizeStatus(rawStatus);
                    }

                    // Resolve setor: coluna do CSV tem precedência; cai para o setor padrão do
                    // formulário quando a célula estiver vazia. Já foi validado que todo nome
                    // presente no arquivo existe em sectorsByName (bloqueio acima).
                    string? sectorNameRaw = null;
                    Guid? sectorId = command.DefaultSectorId is { } dsi && dsi != Guid.Empty ? dsi : null;
                    if (command.ColSector.HasValue)
                    {
                        sectorNameRaw = GetCell(cells, command.ColSector.Value)?.Trim();
                        if (!string.IsNullOrWhiteSpace(sectorNameRaw) && sectorsByName.TryGetValue(sectorNameRaw, out var resolvedSectorId))
                            sectorId = resolvedSectorId;
                    }

                    // Resolve lote (batch): coluna do CSV tem precedência; cai para o padrão do formulário.
                    string? batchName = string.IsNullOrWhiteSpace(command.DefaultBatchName) ? null : command.DefaultBatchName.Trim();
                    if (command.ColBatch.HasValue)
                    {
                        var rawBatch = GetCell(cells, command.ColBatch.Value)?.Trim();
                        if (!string.IsNullOrWhiteSpace(rawBatch)) batchName = rawBatch;
                    }

                    var existingId = await ScalarTx(conn, tx,
                        "SELECT id FROM fp_tickets WHERE event_id=@eid AND code=@code LIMIT 1;",
                        ct, ("@eid", command.EventId.ToString()), ("@code", code));

                    try
                    {
                        Guid? ticketId = null;
                        string rowStatus;

                        if (existingId is null)
                        {
                            if (command.Mode == ImportMode.SomenteAtualizar)
                            {
                                await InsertRow(conn, tx, importId, rowNum, code, externalId, maxEnt,
                                    "skipped", "Modo somente atualizar — código não existe", null, sectorId, sectorNameRaw, ct);
                                skipped++;
                                continue;
                            }
                            ticketId = Guid.NewGuid();
                            var n = DateTime.UtcNow;
                            await ExecTx(conn, tx, """
                                INSERT INTO fp_tickets
                                    (id,event_id,batch_id,ticket_type_id,sector_id,batch_name,external_id,code,
                                     maximum_uses,uses,maximum_entries,entries_used,
                                     people_inside,status,created_at,updated_at)
                                VALUES
                                    (@id,@eid,@bid,@tid,@sid,@bname,@extid,@code,
                                     @me,0,@me,0,0,@status,@now,@now);
                                """, ct,
                                ("@id", ticketId.ToString()), ("@eid", command.EventId.ToString()),
                                ("@bid", batchId.ToString()), ("@tid", ticketTypeId.ToString()),
                                ("@sid", (object?)sectorId?.ToString() ?? DBNull.Value),
                                ("@bname", (object?)batchName ?? DBNull.Value),
                                ("@extid", externalId), ("@code", code),
                                ("@me", maxEnt), ("@status", ticketStatus), ("@now", n));
                            inserted++;
                            rowStatus = "inserted";
                        }
                        else
                        {
                            ticketId = Guid.Parse(existingId.ToString()!);
                            if (command.Mode == ImportMode.SomenteAdicionar)
                            {
                                await InsertRow(conn, tx, importId, rowNum, code, externalId, maxEnt,
                                    "skipped", "Ticket já existe (modo somente adicionar)", ticketId, sectorId, sectorNameRaw, ct);
                                skipped++;
                                continue;
                            }
                            await ExecTx(conn, tx,
                                "UPDATE fp_tickets SET maximum_uses=@me,maximum_entries=@me,status=@status,sector_id=COALESCE(@sid,sector_id),batch_name=COALESCE(@bname,batch_name),updated_at=@now WHERE id=@id;",
                                ct, ("@me", maxEnt), ("@status", ticketStatus),
                                ("@sid", (object?)sectorId?.ToString() ?? DBNull.Value),
                                ("@bname", (object?)batchName ?? DBNull.Value),
                                ("@now", DateTime.UtcNow), ("@id", ticketId.ToString()));
                            updated++;
                            rowStatus = "updated";
                        }

                        await InsertRow(conn, tx, importId, rowNum, code, externalId, maxEnt,
                            rowStatus, null, ticketId, sectorId, sectorNameRaw, ct);
                    }
                    catch (Exception ex)
                    {
                        var detail = ex.Message.Length > 490 ? ex.Message[..490] : ex.Message;
                        await InsertRow(conn, tx, importId, rowNum, code, externalId, maxEnt,
                            "error", detail, null, sectorId, sectorNameRaw, ct);
                        errors++;
                    }
                }
                await tx.CommitAsync(ct);
            }
            catch { await tx.RollbackAsync(ct); throw; }
        }

        var finalStatus = errors > 0 && inserted + updated == 0 ? "failed" : "done";
        var finishedAt = DateTime.UtcNow;
        await ExecAsync(conn, """
            UPDATE fp_ticket_imports
            SET import_status=@st,processed=@p,inserted=@ins,updated=@upd,
                skipped=@sk,errors=@err,finished_at=@ft
            WHERE id=@id;
            """, ct,
            ("@st", finalStatus), ("@p", dataLines.Count),
            ("@ins", inserted), ("@upd", updated), ("@sk", skipped),
            ("@err", errors), ("@ft", finishedAt), ("@id", importId.ToString()));

        return new ImportView(importId, command.EventId, ticketTypeId, batchId,
            command.FileName, finalStatus, command.Mode,
            dataLines.Count, dataLines.Count, inserted, updated, skipped, errors,
            null,
            new DateTimeOffset(createdAt, TimeSpan.Zero),
            new DateTimeOffset(createdAt, TimeSpan.Zero),
            new DateTimeOffset(finishedAt, TimeSpan.Zero));
    }

    // ── Listagem / detalhe ────────────────────────────────────────────────

    public async Task<IReadOnlyList<ImportView>> ListAsync(Guid eventId, CancellationToken ct = default)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("EventId é obrigatório.");
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id,event_id,ticket_type_id,batch_id,file_name,import_status,import_mode,
                   total_rows,processed,inserted,updated,skipped,errors,error_message,
                   created_at,started_at,finished_at
            FROM fp_ticket_imports WHERE event_id=@eid ORDER BY created_at DESC;
            """;
        cmd.Parameters.AddWithValue("@eid", eventId.ToString());
        var result = new List<ImportView>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result.Add(ReadImportView(r));
        return result;
    }

    public async Task<(ImportView Import, IReadOnlyList<ImportRowView> Rows)> GetDetailAsync(
        Guid eventId, Guid importId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);

        await using var ic = conn.CreateCommand();
        ic.CommandText = """
            SELECT id,event_id,ticket_type_id,batch_id,file_name,import_status,import_mode,
                   total_rows,processed,inserted,updated,skipped,errors,error_message,
                   created_at,started_at,finished_at
            FROM fp_ticket_imports WHERE id=@id AND event_id=@eid LIMIT 1;
            """;
        ic.Parameters.AddWithValue("@id", importId.ToString());
        ic.Parameters.AddWithValue("@eid", eventId.ToString());
        await using var ir = await ic.ExecuteReaderAsync(ct);
        if (!await ir.ReadAsync(ct)) throw new ArgumentException("Importação não encontrada.");
        var importView = ReadImportView(ir);
        await ir.CloseAsync();

        await using var rc = conn.CreateCommand();
        rc.CommandText = """
            SELECT import_row_number,ticket_code,external_id,max_entries,row_status,error_detail,sector_name_raw
            FROM fp_ticket_import_rows WHERE import_id=@id ORDER BY import_row_number;
            """;
        rc.Parameters.AddWithValue("@id", importId.ToString());
        var rows = new List<ImportRowView>();
        await using var rr = await rc.ExecuteReaderAsync(ct);
        while (await rr.ReadAsync(ct))
            rows.Add(new ImportRowView(
                Convert.ToInt32(rr.GetValue(0)), rr.GetString(1),
                rr.IsDBNull(2) ? null : rr.GetString(2),
                Convert.ToInt32(rr.GetValue(3)), rr.GetString(4),
                rr.IsDBNull(5) ? null : rr.GetString(5),
                rr.IsDBNull(6) ? null : rr.GetString(6)));
        return (importView, rows);
    }

    // ── Helpers CSV ───────────────────────────────────────────────────────

    private static char DetectSeparator(string content)
    {
        var first = content.Split('\n', 2)[0];
        return new[] { ',', ';', '\t', '|' }
            .OrderByDescending(c => first.Count(ch => ch == c)).First();
    }

    private static string StripBom(string s) => s.StartsWith('\uFEFF') ? s[1..] : s;

    private static IEnumerable<string> SplitLines(string content) =>
        content.Replace("\r\n", "\n").Replace("\r", "\n")
               .Split('\n', StringSplitOptions.RemoveEmptyEntries)
               .Where(l => !string.IsNullOrWhiteSpace(l));

    private static string[] SplitLine(string line, char sep)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQ = false;
        foreach (var ch in line)
        {
            if (ch == '"') { inQ = !inQ; continue; }
            if (ch == sep && !inQ) { result.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        result.Add(sb.ToString());
        return [.. result];
    }

    private static string? GetCell(string[] cells, int idx) =>
        idx >= 0 && idx < cells.Length ? cells[idx] : null;

    private static string ComputeHash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static string ModeToString(ImportMode mode) => mode switch
    {
        ImportMode.SomenteAdicionar  => "SOMENTE_ADICIONAR",
        ImportMode.SomenteAtualizar  => "SOMENTE_ATUALIZAR",
        _                            => "ADICIONAR_ATUALIZAR",
    };

    /// <summary>
    /// Normaliza qualquer variante de status para os valores aceitos pelo banco.
    /// Lista branca  → "active"    (ativo, active, valido, válido, 1, true)
    /// Lista negra   → "cancelled" (cancelado, cancelled, invalido, inválido, 0, false, bloqueado)
    /// Revogado      → "revoked"   (revogado, revoked)
    /// </summary>
    private static string NormalizeStatus(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        return s switch
        {
            "active" or "ativo" or "valido" or "válido" or "1" or "true" or "sim" or "s" or "y" or "yes"
                => "active",
            "revoked" or "revogado"
                => "revoked",
            // tudo o mais (cancelado, bloqueado, inválido, 0, false, n, no...) vira cancelled
            _   => "cancelled",
        };
    }

    private static ImportMode ParseMode(string s) => s switch
    {
        "SOMENTE_ADICIONAR" => ImportMode.SomenteAdicionar,
        "SOMENTE_ATUALIZAR" => ImportMode.SomenteAtualizar,
        _                   => ImportMode.AdicionarAtualizar,
    };

    private static ImportView ReadImportView(MySqlDataReader r)
    {
        var mode = r.IsDBNull(6) ? ImportMode.AdicionarAtualizar : ParseMode(r.GetString(6));
        return new ImportView(
            Guid.Parse(r.GetValue(0).ToString()!),
            Guid.Parse(r.GetValue(1).ToString()!),
            r.IsDBNull(2) ? null : Guid.Parse(r.GetValue(2).ToString()!),
            r.IsDBNull(3) ? null : Guid.Parse(r.GetValue(3).ToString()!),
            r.GetString(4), r.GetString(5), mode,
            Convert.ToInt32(r.GetValue(7)), Convert.ToInt32(r.GetValue(8)),
            Convert.ToInt32(r.GetValue(9)), Convert.ToInt32(r.GetValue(10)),
            Convert.ToInt32(r.GetValue(11)), Convert.ToInt32(r.GetValue(12)),
            r.IsDBNull(13) ? null : r.GetString(13),
            new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(14), DateTimeKind.Utc)),
            r.IsDBNull(15) ? null : new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(15), DateTimeKind.Utc)),
            r.IsDBNull(16) ? null : new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(16), DateTimeKind.Utc)));
    }

    // ── Helpers BD ────────────────────────────────────────────────────────

    private static async Task InsertRow(
        MySqlConnection conn, MySqlTransaction tx,
        Guid importId, int rowNum, string code, string? extId,
        int maxEnt, string status, string? errDetail, Guid? ticketId,
        Guid? sectorId, string? sectorNameRaw,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO fp_ticket_import_rows
                (id,import_id,import_row_number,ticket_code,external_id,max_entries,row_status,error_detail,ticket_id,sector_id,sector_name_raw)
            VALUES
                (@id,@iid,@rn,@code,@extid,@me,@st,@err,@tid,@sid,@snr);
            """;
        cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("@iid", importId.ToString());
        cmd.Parameters.AddWithValue("@rn", rowNum);
        cmd.Parameters.AddWithValue("@code", code);
        cmd.Parameters.AddWithValue("@extid", (object?)extId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@me", maxEnt);
        cmd.Parameters.AddWithValue("@st", status);
        cmd.Parameters.AddWithValue("@err", (object?)errDetail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tid", (object?)ticketId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sid", (object?)sectorId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@snr", (object?)sectorNameRaw ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<Dictionary<string, Guid>> LoadSectorsByNameAsync(
        MySqlConnection conn, Guid eventId, CancellationToken ct)
    {
        var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM fp_sectors WHERE event_id=@eid AND active=1;";
        cmd.Parameters.AddWithValue("@eid", eventId.ToString());
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            result[r.GetString(1)] = Guid.Parse(r.GetValue(0).ToString()!);
        return result;
    }

    private static async Task<bool> ExistsAsync(
        MySqlConnection conn, string sql, CancellationToken ct,
        params (string Name, object Value)[] p)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var x in p) cmd.Parameters.AddWithValue(x.Name, x.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    private static async Task<object?> ScalarAsync(
        MySqlConnection conn, string sql, CancellationToken ct,
        params (string Name, object Value)[] p)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var x in p) cmd.Parameters.AddWithValue(x.Name, x.Value);
        var r = await cmd.ExecuteScalarAsync(ct);
        return r is DBNull ? null : r;
    }

    private static async Task<object?> ScalarTx(
        MySqlConnection conn, MySqlTransaction tx, string sql, CancellationToken ct,
        params (string Name, object Value)[] p)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var x in p) cmd.Parameters.AddWithValue(x.Name, x.Value);
        var r = await cmd.ExecuteScalarAsync(ct);
        return r is DBNull ? null : r;
    }

    private static async Task ExecAsync(
        MySqlConnection conn, string sql, CancellationToken ct,
        params (string Name, object Value)[] p)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var x in p) cmd.Parameters.AddWithValue(x.Name, x.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task ExecTx(
        MySqlConnection conn, MySqlTransaction tx, string sql, CancellationToken ct,
        params (string Name, object Value)[] p)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var x in p) cmd.Parameters.AddWithValue(x.Name, x.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

