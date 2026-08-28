using System.Text.Json.Serialization;

namespace FastPass.Application.Auth;

// ── Permissões disponíveis no sistema ────────────────────────────────────────
// Mapeamento canônico de código → descrição legível.
// Permissões privilegiadas (só concedidas por quem tem escopo.global):
//   - escopo.global, perfil.permissoes.gerenciar

public static class Permissions
{
    // Validação operacional
    public const string AccessValidate   = "acesso.validar";
    // Relatórios e consultas
    public const string ReportsView      = "relatorio.ler";
    public const string AccessesView     = "acessos.ler";
    public const string AuditView        = "auditoria.ler";
    // Catálogo
    public const string ClientsManage    = "cliente.gerenciar";
    public const string EventsCreate     = "evento.criar";
    public const string EventsEdit       = "evento.editar";
    public const string EventsDelete     = "evento.excluir";
    public const string GatesManage      = "portaria.gerenciar";
    public const string SectorsManage    = "setor.gerenciar";
    public const string MatrixManage     = "relacao.gerenciar";
    public const string DevicesManage    = "dispositivo.gerenciar";
    // Ingressos
    public const string TicketsView      = "ticket.consultar";
    public const string TicketsStatus    = "ticket.status";
    public const string TicketsImport    = "ticket.importar";
    public const string TicketsDelete    = "ticket.dados.excluir";
    // Mensagens
    public const string MessagesManage   = "mensagem.gerenciar";
    // Colaboradores (staff)
    public const string StaffManage      = "staff.gerenciar";
    // Administração
    public const string UsersManage      = "usuario.gerenciar";
    public const string RolesManage      = "perfil.gerenciar";
    public const string RolesPermManage  = "perfil.permissoes.gerenciar";  // privilegiada
    public const string SessionsRevoke   = "sessao.revogar";
    public const string ScopeGlobal      = "escopo.global";                // privilegiada

    /// <summary>Permissões que só podem ser concedidas/revogadas por quem tem escopo.global.</summary>
    public static readonly IReadOnlySet<string> Privileged = new HashSet<string>
    {
        ScopeGlobal,
        RolesPermManage,
    };

    /// <summary>Catálogo completo código → descrição legível.</summary>
    public static readonly IReadOnlyDictionary<string, string> All =
        new Dictionary<string, string>
        {
            [AccessValidate]  = "Validar acesso em portaria",
            [ReportsView]     = "Consultar relatórios operacionais",
            [AccessesView]    = "Consultar log de acessos",
            [AuditView]       = "Consultar auditoria administrativa",
            [ClientsManage]   = "Gerenciar clientes e organizadores",
            [EventsCreate]    = "Criar eventos",
            [EventsEdit]      = "Editar eventos",
            [EventsDelete]    = "Excluir eventos",
            [GatesManage]     = "Gerenciar portarias",
            [SectorsManage]   = "Gerenciar setores",
            [MatrixManage]    = "Gerenciar matriz portaria × setor",
            [DevicesManage]   = "Gerenciar dispositivos e catracas",
            [TicketsView]     = "Consultar ingressos e histórico",
            [TicketsStatus]   = "Alterar status de ingressos",
            [TicketsImport]   = "Importar ingressos via CSV",
            [TicketsDelete]   = "Excluir dados de ingressos de evento",
            [MessagesManage]  = "Configurar mensagens de validação",
            [StaffManage]     = "Gerenciar colaboradores e crachás",
            [UsersManage]     = "Gerenciar usuários do sistema",
            [RolesManage]     = "Gerenciar perfis",
            [RolesPermManage] = "Atribuir permissões a perfis",
            [SessionsRevoke]  = "Revogar sessões de outros usuários",
            [ScopeGlobal]     = "Acesso global a todos os eventos e portarias",
        };
}

// ── Perfis padrão ─────────────────────────────────────────────────────────────
public static class DefaultRoles
{
    public const string Administrador      = "Administrador";
    public const string GestorOperacional  = "Gestor Operacional";
    public const string OperadorPortaria   = "Operador de Portaria";
    public const string Auditor            = "Auditor";
    public const string Consulta           = "Consulta";
    public const string ClienteOnline      = "Cliente Online";
}

// ── Modos de escopo de eventos ────────────────────────────────────────────────
/// <summary>Modo de escopo de eventos por usuário.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventScopeMode { Todos, Ativos, Especificos }

// ── Comandos ─────────────────────────────────────────────────────────────────

public sealed record LoginCommand(string UserName, string Password);

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword);

public sealed record CreateUserCommand(
    string UserName,
    string DisplayName,
    string Password,
    IReadOnlyList<Guid> RoleIds,
    EventScopeMode EventScope = EventScopeMode.Todos,
    IReadOnlyList<Guid>? EventIds = null,
    IReadOnlyList<Guid>? GateIds = null,
    bool PhysicalAccessEnabled = false,
    string? AccessBadgeCode = null);

public sealed record UpdateUserCommand(
    string DisplayName,
    bool Active,
    IReadOnlyList<Guid> RoleIds,
    EventScopeMode EventScope = EventScopeMode.Todos,
    IReadOnlyList<Guid>? EventIds = null,
    IReadOnlyList<Guid>? GateIds = null,
    bool PhysicalAccessEnabled = false,
    string? AccessBadgeCode = null);

public sealed record CreateRoleCommand(string Name, string? Description = null);

public sealed record UpdateRoleCommand(string Name, string? Description, bool Active);

public sealed record SetRolePermissionsCommand(IReadOnlyList<string> PermissionCodes);

// ── Views ─────────────────────────────────────────────────────────────────────

public sealed record SessionView(
    Guid UserId,
    string UserName,
    string DisplayName,
    bool Active,
    IReadOnlyList<string> Permissions);

public sealed record UserView(
    Guid Id,
    string UserName,
    string DisplayName,
    bool Active,
    EventScopeMode EventScope,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? BlockedUntil,
    IReadOnlyList<RoleSummary> Roles,
    IReadOnlyList<string> AuthorizedEventIds,
    IReadOnlyList<string> AuthorizedGateIds,
    bool PhysicalAccessEnabled = false,
    string? AccessBadgeCode = null,
    string? StaffMemberId = null);

public sealed record RoleSummary(Guid Id, string Name, bool SystemRole);

public sealed record RoleView(
    Guid Id,
    string Name,
    string? Description,
    bool Active,
    bool SystemRole,
    IReadOnlyList<string> Permissions);

public sealed record PermissionView(string Code, string Description, bool Privileged);

// ── Exceções ──────────────────────────────────────────────────────────────────

public sealed class AuthException : Exception
{
    public AuthException(string message) : base(message) { }
}

public sealed class AuthConflictException : Exception
{
    public AuthConflictException(string message) : base(message) { }
}

public sealed class AuthForbiddenException : Exception
{
    public AuthForbiddenException(string message) : base(message) { }
}

// ── Interface ─────────────────────────────────────────────────────────────────

public interface IAuthService
{
    // Sessão
    Task<(string Token, SessionView Session)> LoginAsync(
        LoginCommand command, string? ip, string? userAgent,
        CancellationToken cancellationToken = default);

    Task<SessionView?> GetSessionAsync(
        string tokenHash, CancellationToken cancellationToken = default);

    Task LogoutAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task LogoutAllAsync(Guid userId, Guid? exceptSessionId,
        CancellationToken cancellationToken = default);

    Task ChangePasswordAsync(Guid userId, ChangePasswordCommand command,
        CancellationToken cancellationToken = default);

    // Usuários
    Task<UserView> CreateUserAsync(CreateUserCommand command,
        CancellationToken cancellationToken = default);

    Task<UserView> UpdateUserAsync(Guid userId, UpdateUserCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserView>> ListUsersAsync(bool activeOnly,
        CancellationToken cancellationToken = default);

    Task UnblockUserAsync(Guid userId, CancellationToken cancellationToken = default);

    // Perfis
    Task<RoleView> CreateRoleAsync(CreateRoleCommand command,
        CancellationToken cancellationToken = default);

    Task<RoleView> UpdateRoleAsync(Guid roleId, UpdateRoleCommand command,
        CancellationToken cancellationToken = default);

    Task<RoleView> SetRolePermissionsAsync(Guid roleId, SetRolePermissionsCommand command,
        SessionView callerSession, CancellationToken cancellationToken = default);

    Task DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleView>> ListRolesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionView>> ListPermissionsAsync(
        CancellationToken cancellationToken = default);

    // Seed
    Task EnsureDefaultsAsync(string adminUser, string adminName, string adminPassword,
        CancellationToken cancellationToken = default);

    // Verificação de escopo (usada nos outros serviços)
    bool CanAccessEvent(SessionView session, string eventId, string eventStatus);
    bool CanAccessGate(SessionView session, string gateId, IEnumerable<string> userGateIds);
}
