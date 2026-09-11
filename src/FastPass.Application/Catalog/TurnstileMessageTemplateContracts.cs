namespace FastPass.Application.Catalog;

/// <summary>
/// View de um template de mensagem para catraca (Neon 1.3).
/// Display monochrome: 2 linhas × 16 caracteres cada.
/// Template ID 00-30, cada um pode ser customizado.
/// </summary>
public sealed record TurnstileMessageTemplateView(
    int TemplateId,
    string Line1,
    string Line2,
    bool Active,
    string UpdatedAt);

/// <summary>
/// Comando para atualizar um template de mensagem de catraca.
/// </summary>
public sealed record UpdateTurnstileMessageTemplateCommand(
    string Line1,
    string Line2,
    bool Active);

/// <summary>
/// Serviço para gerenciar templates de mensagens de catraca (MQTT/Neon 1.3).
/// Suporta IDs 00-30 com 2 linhas × 16 caracteres cada.
/// </summary>
public interface ITurnstileMessageTemplateService
{
    /// <summary>Listar todos os templates de catraca (00-30).</summary>
    Task<IReadOnlyList<TurnstileMessageTemplateView>> ListAsync();

    /// <summary>Obter um template específico por ID.</summary>
    Task<TurnstileMessageTemplateView?> GetAsync(int templateId);

    /// <summary>Atualizar um template de mensagem de catraca.</summary>
    Task<TurnstileMessageTemplateView> UpdateAsync(
        int templateId, 
        UpdateTurnstileMessageTemplateCommand command);

    /// <summary>
    /// Listar todos os templates ativos (para enviar no getconfig da catraca).
    /// Retorna em formato pronto para Neon 1.3: {templateId: {line1, line2}}.
    /// </summary>
    Task<Dictionary<int, (string Line1, string Line2)>> ListActiveTemplatesForDeviceAsync();
}
