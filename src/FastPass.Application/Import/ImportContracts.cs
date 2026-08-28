namespace FastPass.Application.Import;

// ── Modos de importação ───────────────────────────────────────────────────────
/// <summary>
/// ADICIONAR_ATUALIZAR — insere novos e atualiza (max_entries) os existentes.
/// SOMENTE_ADICIONAR   — pula codes que já existem no evento.
/// SOMENTE_ATUALIZAR   — atualiza apenas os que já existem; ignora novos.
/// </summary>
public enum ImportMode
{
    AdicionarAtualizar,
    SomenteAdicionar,
    SomenteAtualizar,
}

// ── Preview do CSV ────────────────────────────────────────────────────────────
/// <summary>Retorno do endpoint de pré-visualização: primeiras N linhas + detecção automática.</summary>
public sealed record CsvPreviewResult(
    string DetectedSeparator,
    IReadOnlyList<string[]> SampleRows,
    int TotalRows,
    string[] Headers);

// ── Comando de importação ─────────────────────────────────────────────────────
public sealed record StartImportCommand(
    Guid EventId,
    Guid? TicketTypeId,
    Guid? BatchId,
    string Separator,
    int ColCode,
    int? ColExternalId,
    int? ColMaxEntries,
    int DefaultMaxEntries,
    ImportMode Mode,
    /// <summary>Coluna opcional que define o status de cada ticket (active/cancelled/revoked).</summary>
    int? ColStatus = null,
    /// <summary>Status padrão quando não há coluna de status. Permite lista branca (active) ou lista negra (cancelled).</summary>
    string DefaultStatus = "active",
    /// <summary>Coluna opcional com o NOME do setor de cada linha (deve casar com um setor já cadastrado no evento).</summary>
    int? ColSector = null,
    /// <summary>Setor aplicado a todas as linhas quando não houver ColSector (ou como padrão para linhas sem valor na coluna).</summary>
    Guid? DefaultSectorId = null,
    /// <summary>Coluna opcional com o NOME do lote de cada linha (texto livre vindo da planilha da empresa).</summary>
    int? ColBatch = null,
    /// <summary>Lote aplicado a todas as linhas quando não houver ColBatch.</summary>
    string? DefaultBatchName = null,
    /// <summary>Conteúdo do arquivo já lido como texto (UTF-8 ou latin-1 normalizado).</summary>
    string CsvContent = "",
    string FileName = "");

/// <summary>Lançada quando o CSV referencia setores que não existem no evento — a importação é totalmente rejeitada.</summary>
public sealed class UnknownSectorsException : Exception
{
    public IReadOnlyList<string> UnknownSectorNames { get; }

    public UnknownSectorsException(IReadOnlyList<string> unknownSectorNames)
        : base($"O arquivo referencia {unknownSectorNames.Count} setor(es) não cadastrado(s) no evento: {string.Join(", ", unknownSectorNames)}. Cadastre esses setores antes de importar ou corrija o arquivo.")
    {
        UnknownSectorNames = unknownSectorNames;
    }
}

// ── Views ─────────────────────────────────────────────────────────────────────
public sealed record ImportView(
    Guid Id,
    Guid EventId,
    Guid? TicketTypeId,
    Guid? BatchId,
    string FileName,
    string Status,
    ImportMode Mode,
    int TotalRows,
    int Processed,
    int Inserted,
    int Updated,
    int Skipped,
    int Errors,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

public sealed record ImportRowView(
    int RowNumber,
    string Code,
    string? ExternalId,
    int MaxEntries,
    string Status,
    string? ErrorDetail,
    string? SectorName = null);

// ── Interface ─────────────────────────────────────────────────────────────────
public interface ITicketImportService
{
    /// <summary>Pré-visualiza o CSV (detecta separador, retorna amostra, não persiste).</summary>
    Task<CsvPreviewResult> PreviewAsync(
        string csvContent,
        CancellationToken cancellationToken = default);

    /// <summary>Cria o registro de importação e processa de forma síncrona.</summary>
    Task<ImportView> ImportAsync(
        StartImportCommand command,
        Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>Lista importações de um evento, mais recente primeiro.</summary>
    Task<IReadOnlyList<ImportView>> ListAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);

    /// <summary>Retorna detalhes + erros de uma importação.</summary>
    Task<(ImportView Import, IReadOnlyList<ImportRowView> Rows)> GetDetailAsync(
        Guid eventId,
        Guid importId,
        CancellationToken cancellationToken = default);
}
