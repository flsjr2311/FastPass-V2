namespace FastPass.Application.Access;

public sealed record AccessMessagePresentation(
    string Title,
    string Message,
    string BackgroundStart,
    string BackgroundEnd,
    string TitleColor,
    string MessageColor,
    string TitleSize,
    string MessageSize,
    bool TitleBold,
    bool MessageBold);

public sealed record AccessMessageView(
    Guid Id,
    Guid EventId,
    string Code,
    string Title,
    string Message,
    string BackgroundStart,
    string BackgroundEnd,
    string TitleColor,
    string MessageColor,
    string TitleSize,
    string MessageSize,
    bool TitleBold,
    bool MessageBold,
    bool Active,
    bool System,
    DateTimeOffset UpdatedAt,
    bool IsCustomized = false);

/// <summary>Template padrão global — vale para todos os eventos que não tiverem override.</summary>
public sealed record AccessMessageTemplateView(
    Guid Id,
    string Code,
    string Title,
    string Message,
    string BackgroundStart,
    string BackgroundEnd,
    string TitleColor,
    string MessageColor,
    string TitleSize,
    string MessageSize,
    bool TitleBold,
    bool MessageBold,
    bool Active,
    DateTimeOffset UpdatedAt);

public sealed record UpdateAccessMessageCommand(
    string Title,
    string Message,
    string BackgroundStart,
    string BackgroundEnd,
    string TitleColor,
    string MessageColor,
    string TitleSize = "28dp",
    string MessageSize = "18dp",
    bool TitleBold = true,
    bool MessageBold = true,
    bool Active = true);

public sealed record UpdateAccessMessageTemplateCommand(
    string Title,
    string Message,
    string BackgroundStart,
    string BackgroundEnd,
    string TitleColor,
    string MessageColor,
    string TitleSize = "28dp",
    string MessageSize = "18dp",
    bool TitleBold = true,
    bool MessageBold = true,
    bool Active = true);

public sealed record ResolvedAccessMessage(
    string Code,
    string Message,
    AccessMessagePresentation Presentation);

public interface IAccessMessageService
{
    // Mensagens por evento (herdam do template, exceto onde houver override)
    Task<IReadOnlyList<AccessMessageView>> ListAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);

    /// <summary>Cria/atualiza o override deste evento para o código informado.</summary>
    Task<AccessMessageView> UpdateAsync(
        Guid eventId,
        string code,
        UpdateAccessMessageCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Remove o override do evento, voltando a herdar do template padrão.</summary>
    Task RestoreDefaultAsync(
        Guid eventId,
        string code,
        CancellationToken cancellationToken = default);

    // Templates padrão globais (valem para todos os eventos sem override)
    Task<IReadOnlyList<AccessMessageTemplateView>> ListTemplatesAsync(
        CancellationToken cancellationToken = default);

    Task<AccessMessageTemplateView> UpdateTemplateAsync(
        string code,
        UpdateAccessMessageTemplateCommand command,
        CancellationToken cancellationToken = default);

    Task<ResolvedAccessMessage> ResolveAsync(
        Guid eventId,
        string code,
        string? fallback,
        bool approved,
        CancellationToken cancellationToken = default);
}
