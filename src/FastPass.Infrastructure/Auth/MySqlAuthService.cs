using System.Security.Cryptography;
using System.Text;
using FastPass.Application.Auth;
using FastPass.Infrastructure.Database;
using MySqlConnector;

namespace FastPass.Infrastructure.Auth;

/// <summary>
/// Implementação MySQL do serviço de autenticação.
///
/// Segurança:
///   - PBKDF2-SHA256, 100 000 iterações, salt 32 B, hash 32 B.
///   - Sessão por cookie: token aleatório 32 B — só o SHA-256 hex é persistido.
///   - Rate limiting: 5 falhas → bloqueio 15 min.
///   - Permissões privilegiadas: escopo.global e perfil.permissoes.gerenciar
///     só podem ser concedidas por quem já tem escopo.global.
///   - Perfis de sistema (system_role=1): imutáveis via API.
/// </summary>
public sealed class MySqlAuthService : IAuthService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SessionDuration = TimeSpan.FromHours(8);
    private const int Pbkdf2Iterations = 100_000;
    private const int SaltSize = 32;
    private const int HashSize = 32;

    // Perfis padrão: nome → (descrição, isSystem, permissões)
    private static readonly IReadOnlyList<(string Name, string Desc, bool System, string[] Perms)> DefaultRolesSeed =
    [
        (DefaultRoles.Administrador,
            "Acesso total ao sistema",
            true,
            [.. Permissions.All.Keys]),

        (DefaultRoles.GestorOperacional,
            "Gerencia eventos, portarias, setores e ingressos",
            false,
            [
                Permissions.ReportsView, Permissions.AccessesView,
                Permissions.ClientsManage,
                Permissions.EventsCreate, Permissions.EventsEdit, Permissions.EventsDelete,
                Permissions.GatesManage, Permissions.SectorsManage, Permissions.MatrixManage,
                Permissions.DevicesManage,
                Permissions.TicketsView, Permissions.TicketsStatus, Permissions.TicketsImport,
                Permissions.MessagesManage,
            ]),

        (DefaultRoles.OperadorPortaria,
            "Valida acesso nas portarias autorizadas e consulta ingressos/relatórios",
            false,
            [Permissions.AccessValidate, Permissions.TicketsView, Permissions.ReportsView, Permissions.AccessesView]),

        (DefaultRoles.Auditor,
            "Leitura de relatórios e auditoria",
            false,
            [Permissions.ReportsView, Permissions.AccessesView, Permissions.AuditView]),

        (DefaultRoles.Consulta,
            "Consulta de ingressos e relatórios",
            false,
            [Permissions.ReportsView, Permissions.TicketsView]),

        (DefaultRoles.ClienteOnline,
            "Acesso restrito ao relatório do próprio evento (escopo obrigatório)",
            false,
            [Permissions.ReportsView]),
    ];

    private readonly FastPassDbConnectionFactory _factory;

    public MySqlAuthService(FastPassDbConnectionFactory factory) => _factory = factory;

    // ═══════════════════════════════════════════════════════════════════════
    // Sessão
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<(string Token, SessionView Session)> LoginAsync(
        LoginCommand cmd, string? ip, string? ua, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.UserName) || string.IsNullOrWhiteSpace(cmd.Password))
            throw new AuthException("Usuário e senha são obrigatórios.");

        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var committed = false;
        try
        {
            var user = await ReadUserAuthAsync(conn, tx, cmd.UserName.Trim(), ct);
            var now = DateTime.UtcNow;

            if (user is null || !user.Active)
            {
                await tx.CommitAsync(ct); committed = true;
                throw new AuthException("Usuário ou senha inválidos.");
            }
            if (user.BlockedUntil.HasValue && user.BlockedUntil.Value > now)
            {
                await tx.CommitAsync(ct); committed = true;
                var mins = (int)Math.Ceiling((user.BlockedUntil.Value - now).TotalMinutes);
                throw new AuthException($"Conta bloqueada. Tente novamente em {mins} minuto(s).");
            }
            if (!VerifyPassword(cmd.Password, user.PasswordHash))
            {
                var failed = user.FailedAttempts + 1;
                DateTime? block = failed >= MaxFailedAttempts ? now.Add(BlockDuration) : null;
                await Exec(conn, tx, "UPDATE fp_users SET failed_attempts=@f,blocked_until=@b WHERE id=@id;",
                    ct, ("@f", failed), ("@b", (object?)block ?? DBNull.Value), ("@id", user.Id.ToString()));
                await tx.CommitAsync(ct); committed = true;
                throw new AuthException("Usuário ou senha inválidos.");
            }

            await Exec(conn, tx,
                "UPDATE fp_users SET failed_attempts=0,blocked_until=NULL,last_login_at=@now WHERE id=@id;",
                ct, ("@now", now), ("@id", user.Id.ToString()));

            var rawToken = GenerateToken();
            var tokenHash = HashToken(rawToken);
            await Exec(conn, tx, """
                INSERT INTO fp_sessions (id,user_id,token_hash,ip,user_agent,created_at,last_used_at,expires_at)
                VALUES (@id,@uid,@th,@ip,@ua,@now,@now,@exp);
                """, ct,
                ("@id", Guid.NewGuid().ToString()), ("@uid", user.Id.ToString()), ("@th", tokenHash),
                ("@ip", (object?)ip ?? DBNull.Value), ("@ua", (object?)ua ?? DBNull.Value),
                ("@now", now), ("@exp", now.Add(SessionDuration)));

            await tx.CommitAsync(ct); committed = true;
            var perms = await GetUserPermissionsAsync(conn, user.Id, ct);
            return (rawToken, new SessionView(user.Id, user.UserName, user.DisplayName, true, perms));
        }
        catch { if (!committed) await tx.RollbackAsync(ct); throw; }
    }

    public async Task<SessionView?> GetSessionAsync(string tokenHash, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        var now = DateTime.UtcNow;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT u.id,u.user_name,u.display_name,u.active,s.id
            FROM fp_sessions s INNER JOIN fp_users u ON u.id=s.user_id
            WHERE s.token_hash=@th AND s.revoked_at IS NULL AND s.expires_at>@now LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@th", tokenHash);
        cmd.Parameters.AddWithValue("@now", now);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var userId = ReadGuid(r, 0);
        var userName = r.GetString(1);
        var displayName = r.GetString(2);
        var active = Convert.ToBoolean(r.GetValue(3));
        var sessionId = r.GetValue(4).ToString()!;
        await r.CloseAsync();
        if (!active) return null;

        await using var upd = conn.CreateCommand();
        upd.CommandText = "UPDATE fp_sessions SET last_used_at=@now WHERE id=@id;";
        upd.Parameters.AddWithValue("@now", now);
        upd.Parameters.AddWithValue("@id", sessionId);
        await upd.ExecuteNonQueryAsync(ct);

        var perms = await GetUserPermissionsAsync(conn, userId, ct);
        return new SessionView(userId, userName, displayName, active, perms);
    }

    public async Task LogoutAsync(string tokenHash, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var c = conn.CreateCommand();
        c.CommandText = "UPDATE fp_sessions SET revoked_at=@now WHERE token_hash=@th AND revoked_at IS NULL;";
        c.Parameters.AddWithValue("@now", DateTime.UtcNow);
        c.Parameters.AddWithValue("@th", tokenHash);
        await c.ExecuteNonQueryAsync(ct);
    }

    public async Task LogoutAllAsync(Guid userId, Guid? exceptSessionId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var c = conn.CreateCommand();
        c.CommandText = exceptSessionId.HasValue
            ? "UPDATE fp_sessions SET revoked_at=@now WHERE user_id=@uid AND id<>@ex AND revoked_at IS NULL;"
            : "UPDATE fp_sessions SET revoked_at=@now WHERE user_id=@uid AND revoked_at IS NULL;";
        c.Parameters.AddWithValue("@now", DateTime.UtcNow);
        c.Parameters.AddWithValue("@uid", userId.ToString());
        if (exceptSessionId.HasValue) c.Parameters.AddWithValue("@ex", exceptSessionId.Value.ToString());
        await c.ExecuteNonQueryAsync(ct);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordCommand cmd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.NewPassword) || cmd.NewPassword.Length < 8)
            throw new ArgumentException("A nova senha deve ter pelo menos 8 caracteres.");
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var hash = await Scalar(conn, tx,
                "SELECT password_hash FROM fp_users WHERE id=@id AND active=1 LIMIT 1;",
                ct, ("@id", userId.ToString()));
            if (hash is null) throw new AuthException("Usuário não encontrado.");
            if (!VerifyPassword(cmd.CurrentPassword, hash.ToString()!))
                throw new AuthException("Senha atual incorreta.");
            await Exec(conn, tx, "UPDATE fp_users SET password_hash=@ph,updated_at=@now WHERE id=@id;",
                ct, ("@ph", HashPassword(cmd.NewPassword)), ("@now", DateTime.UtcNow), ("@id", userId.ToString()));
            await Exec(conn, tx,
                "UPDATE fp_sessions SET revoked_at=@now WHERE user_id=@id AND revoked_at IS NULL;",
                ct, ("@now", DateTime.UtcNow), ("@id", userId.ToString()));
            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Usuários
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<UserView> CreateUserAsync(CreateUserCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.UserName)) throw new ArgumentException("UserName é obrigatório.");
        if (string.IsNullOrWhiteSpace(command.DisplayName)) throw new ArgumentException("DisplayName é obrigatório.");
        if (string.IsNullOrWhiteSpace(command.Password) || command.Password.Length < 8)
            throw new ArgumentException("Password deve ter pelo menos 8 caracteres.");
        if (command.RoleIds == null || command.RoleIds.Count == 0)
            throw new ArgumentException("Ao menos um perfil deve ser atribuído.");
        if (command.EventScope == EventScopeMode.Especificos && (command.EventIds == null || command.EventIds.Count == 0))
            throw new ArgumentException("Ao menos um evento deve ser selecionado no modo Específicos.");
        if (command.PhysicalAccessEnabled && string.IsNullOrWhiteSpace(command.AccessBadgeCode))
            throw new ArgumentException("Código de acesso é obrigatório quando acesso físico está habilitado.");

        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            if (await Exists(conn, tx, "SELECT COUNT(*) FROM fp_users WHERE user_name=@un;",
                ct, ("@un", command.UserName.Trim())))
                throw new AuthConflictException("Já existe um usuário com esse nome.");

            // Valida unicidade do badge code entre usuários (crachá de acesso físico)
            if (command.PhysicalAccessEnabled && !string.IsNullOrWhiteSpace(command.AccessBadgeCode))
            {
                if (await Exists(conn, tx, "SELECT COUNT(*) FROM fp_users WHERE access_badge_code=@code;",
                    ct, ("@code", command.AccessBadgeCode.Trim())))
                    throw new AuthConflictException($"O código de acesso '{command.AccessBadgeCode}' já está em uso por outro usuário.");
            }

            await Exec(conn, tx, """
                INSERT INTO fp_users (id,user_name,display_name,password_hash,active,event_scope_mode,
                    physical_access_enabled,access_badge_code,created_at,updated_at)
                VALUES (@id,@un,@dn,@ph,1,@esm,@pa,@bc,@now,@now);
                """, ct,
                ("@id", id.ToString()), ("@un", command.UserName.Trim()), ("@dn", command.DisplayName.Trim()),
                ("@ph", HashPassword(command.Password)),
                ("@esm", command.EventScope.ToString().ToUpperInvariant()),
                ("@pa", command.PhysicalAccessEnabled ? 1 : 0),
                ("@bc", (object?)command.AccessBadgeCode?.Trim() ?? DBNull.Value),
                ("@now", now));

            await SaveUserLinksAsync(conn, tx, id, command.RoleIds, command.EventIds, command.GateIds,
                command.EventScope, ct);
            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
        return (await GetUserViewAsync(conn, id, ct))!;
    }

    public async Task<UserView> UpdateUserAsync(Guid userId, UpdateUserCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.DisplayName)) throw new ArgumentException("DisplayName é obrigatório.");
        if (command.RoleIds == null || command.RoleIds.Count == 0)
            throw new ArgumentException("Ao menos um perfil deve ser atribuído.");
        if (command.EventScope == EventScopeMode.Especificos && (command.EventIds == null || command.EventIds.Count == 0))
            throw new ArgumentException("Ao menos um evento deve ser selecionado no modo Específicos.");
        if (command.PhysicalAccessEnabled && string.IsNullOrWhiteSpace(command.AccessBadgeCode))
            throw new ArgumentException("Código de acesso é obrigatório quando acesso físico está habilitado.");

        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            if (!await Exists(conn, tx, "SELECT COUNT(*) FROM fp_users WHERE id=@id;",
                ct, ("@id", userId.ToString())))
                throw new ArgumentException("Usuário não encontrado.");

            // Busca o badge code atual do usuário
            await using var curCmd = conn.CreateCommand();
            curCmd.Transaction = tx;
            curCmd.CommandText = "SELECT access_badge_code FROM fp_users WHERE id=@id LIMIT 1;";
            curCmd.Parameters.AddWithValue("@id", userId.ToString());
            await using var curR = await curCmd.ExecuteReaderAsync(ct);
            string? existingBadge = null;
            if (await curR.ReadAsync(ct))
            {
                existingBadge = curR.IsDBNull(0) ? null : curR.GetValue(0).ToString();
            }
            await curR.CloseAsync();

            // Valida unicidade do badge code entre usuários (exceto o próprio)
            if (command.PhysicalAccessEnabled && !string.IsNullOrWhiteSpace(command.AccessBadgeCode)
                && command.AccessBadgeCode.Trim() != existingBadge)
            {
                if (await Exists(conn, tx, "SELECT COUNT(*) FROM fp_users WHERE access_badge_code=@code AND id<>@id;",
                    ct, ("@code", command.AccessBadgeCode.Trim()), ("@id", userId.ToString())))
                    throw new AuthConflictException($"O código de acesso '{command.AccessBadgeCode}' já está em uso.");
            }

            var now = DateTime.UtcNow;

            await Exec(conn, tx, """
                UPDATE fp_users
                SET display_name=@dn, active=@a, event_scope_mode=@esm,
                    physical_access_enabled=@pa, access_badge_code=@bc,
                    updated_at=@now
                WHERE id=@id;
                """, ct,
                ("@dn", command.DisplayName.Trim()), ("@a", command.Active ? 1 : 0),
                ("@esm", command.EventScope.ToString().ToUpperInvariant()),
                ("@pa", command.PhysicalAccessEnabled ? 1 : 0),
                ("@bc", (object?)command.AccessBadgeCode?.Trim() ?? DBNull.Value),
                ("@now", now), ("@id", userId.ToString()));

            if (!command.Active)
                await Exec(conn, tx,
                    "UPDATE fp_sessions SET revoked_at=@now WHERE user_id=@id AND revoked_at IS NULL;",
                    ct, ("@now", now), ("@id", userId.ToString()));

            await SaveUserLinksAsync(conn, tx, userId, command.RoleIds, command.EventIds, command.GateIds,
                command.EventScope, ct);
            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
        return (await GetUserViewAsync(conn, userId, ct))!;
    }

    public async Task<IReadOnlyList<UserView>> ListUsersAsync(bool activeOnly, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var c = conn.CreateCommand();
        c.CommandText = "SELECT id FROM fp_users WHERE (@ao=0 OR active=1) ORDER BY display_name,id;";
        c.Parameters.AddWithValue("@ao", activeOnly ? 1 : 0);
        var ids = new List<Guid>();
        await using var r = await c.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) ids.Add(ReadGuid(r, 0));
        await r.CloseAsync();
        var result = new List<UserView>();
        foreach (var id in ids) { var v = await GetUserViewAsync(conn, id, ct); if (v is not null) result.Add(v); }
        return result;
    }

    public async Task UnblockUserAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var c = conn.CreateCommand();
        c.CommandText = "UPDATE fp_users SET failed_attempts=0,blocked_until=NULL WHERE id=@id;";
        c.Parameters.AddWithValue("@id", userId.ToString());
        if (await c.ExecuteNonQueryAsync(ct) == 0) throw new ArgumentException("Usuário não encontrado.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Perfis
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<RoleView> CreateRoleAsync(CreateRoleCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name)) throw new ArgumentException("Name é obrigatório.");
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            if (await Exists(conn, tx, "SELECT COUNT(*) FROM fp_roles WHERE name=@n;",
                ct, ("@n", command.Name.Trim())))
                throw new AuthConflictException("Já existe um perfil com esse nome.");
            await Exec(conn, tx, """
                INSERT INTO fp_roles (id,name,description,active,system_role,created_at,updated_at)
                VALUES (@id,@n,@d,1,0,@now,@now);
                """, ct,
                ("@id", id.ToString()), ("@n", command.Name.Trim()),
                ("@d", (object?)command.Description?.Trim() ?? DBNull.Value), ("@now", now));
            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
        return new RoleView(id, command.Name.Trim(), command.Description?.Trim(), true, false, []);
    }

    public async Task<RoleView> UpdateRoleAsync(Guid roleId, UpdateRoleCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name)) throw new ArgumentException("Name é obrigatório.");
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var sys = await Scalar(conn, tx, "SELECT system_role FROM fp_roles WHERE id=@id;",
                ct, ("@id", roleId.ToString()));
            if (sys is null) throw new ArgumentException("Perfil não encontrado.");
            if (Convert.ToBoolean(sys))
                throw new AuthForbiddenException("Perfis de sistema não podem ser editados.");
            await Exec(conn, tx,
                "UPDATE fp_roles SET name=@n,description=@d,active=@a,updated_at=@now WHERE id=@id;",
                ct, ("@n", command.Name.Trim()), ("@d", (object?)command.Description?.Trim() ?? DBNull.Value),
                ("@a", command.Active ? 1 : 0), ("@now", DateTime.UtcNow), ("@id", roleId.ToString()));
            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
        return (await GetRoleViewAsync(conn, roleId, ct))!;
    }

    public async Task<RoleView> SetRolePermissionsAsync(
        Guid roleId, SetRolePermissionsCommand command,
        SessionView caller, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var sys = await Scalar(conn, tx, "SELECT system_role FROM fp_roles WHERE id=@id;",
                ct, ("@id", roleId.ToString()));
            if (sys is null) throw new ArgumentException("Perfil não encontrado.");
            if (Convert.ToBoolean(sys))
                throw new AuthForbiddenException("Permissões de perfis de sistema não podem ser alteradas.");

            var callerHasGlobal = caller.Permissions.Contains(Permissions.ScopeGlobal);
            var requested = command.PermissionCodes
                .Select(c => c.Trim().ToLowerInvariant()).Distinct().ToHashSet();

            if (!callerHasGlobal)
            {
                var forbidden = requested.Intersect(Permissions.Privileged).ToList();
                if (forbidden.Count > 0)
                    throw new AuthForbiddenException(
                        $"Permissão 'escopo.global' é necessária para conceder: {string.Join(", ", forbidden)}.");
            }

            var allCodes = Permissions.All.Keys.ToHashSet();
            var invalid = requested.Except(allCodes).ToList();
            if (invalid.Count > 0)
                throw new ArgumentException($"Permissões inválidas: {string.Join(", ", invalid)}.");

            await Exec(conn, tx, "DELETE FROM fp_role_permissions WHERE role_id=@id;",
                ct, ("@id", roleId.ToString()));
            foreach (var code in requested)
            {
                var pid = await Scalar(conn, tx, "SELECT id FROM fp_permissions WHERE code=@c LIMIT 1;",
                    ct, ("@c", code));
                if (pid is not null)
                    await Exec(conn, tx,
                        "INSERT IGNORE INTO fp_role_permissions (role_id,permission_id) VALUES (@rid,@pid);",
                        ct, ("@rid", roleId.ToString()), ("@pid", pid.ToString()!));
            }
            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
        return (await GetRoleViewAsync(conn, roleId, ct))!;
    }

    public async Task DeleteRoleAsync(Guid roleId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var sys = await Scalar(conn, tx, "SELECT system_role FROM fp_roles WHERE id=@id;",
                ct, ("@id", roleId.ToString()));
            if (sys is null) throw new ArgumentException("Perfil não encontrado.");
            if (Convert.ToBoolean(sys))
                throw new AuthForbiddenException("Perfis de sistema não podem ser excluídos.");
            if (await Exists(conn, tx, "SELECT COUNT(*) FROM fp_user_roles WHERE role_id=@id;",
                ct, ("@id", roleId.ToString())))
                throw new AuthConflictException("Não é possível excluir um perfil em uso por usuários.");

            await Exec(conn, tx, "DELETE FROM fp_role_permissions WHERE role_id=@id;",
                ct, ("@id", roleId.ToString()));
            await Exec(conn, tx, "DELETE FROM fp_roles WHERE id=@id;",
                ct, ("@id", roleId.ToString()));
            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    public async Task<IReadOnlyList<RoleView>> ListRolesAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var c = conn.CreateCommand();
        c.CommandText = "SELECT id FROM fp_roles ORDER BY system_role DESC,name,id;";
        var ids = new List<Guid>();
        await using var r = await c.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) ids.Add(ReadGuid(r, 0));
        await r.CloseAsync();
        var result = new List<RoleView>();
        foreach (var id in ids) { var v = await GetRoleViewAsync(conn, id, ct); if (v is not null) result.Add(v); }
        return result;
    }

    public async Task<IReadOnlyList<PermissionView>> ListPermissionsAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var c = conn.CreateCommand();
        c.CommandText = "SELECT code,description FROM fp_permissions ORDER BY code;";
        var result = new List<PermissionView>();
        await using var r = await c.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var code = r.GetString(0);
            result.Add(new PermissionView(code, r.GetString(1), Permissions.Privileged.Contains(code)));
        }
        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Seed
    // ═══════════════════════════════════════════════════════════════════════

    public async Task EnsureDefaultsAsync(
        string adminUser, string adminName, string adminPassword,
        CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var now = DateTime.UtcNow;

            // 1. Permissões
            foreach (var (code, desc) in Permissions.All)
                await Exec(conn, tx, """
                    INSERT INTO fp_permissions (id,code,description)
                    VALUES (@id,@c,@d)
                    ON DUPLICATE KEY UPDATE description=@d;
                    """, ct,
                    ("@id", Guid.NewGuid().ToString()), ("@c", code), ("@d", desc));

            // 2. Perfis padrão
            foreach (var (roleName, roleDesc, isSystem, perms) in DefaultRolesSeed)            {
                var rid = await Scalar(conn, tx, "SELECT id FROM fp_roles WHERE name=@n LIMIT 1;",
                    ct, ("@n", roleName));
                string ridStr;
                if (rid is null)
                {
                    ridStr = Guid.NewGuid().ToString();
                    await Exec(conn, tx, """
                        INSERT INTO fp_roles (id,name,description,active,system_role,created_at,updated_at)
                        VALUES (@id,@n,@d,1,@sr,@now,@now);
                        """, ct,
                        ("@id", ridStr), ("@n", roleName), ("@d", roleDesc),
                        ("@sr", isSystem ? 1 : 0), ("@now", now));
                }
                else
                {
                    ridStr = rid.ToString()!;
                    await Exec(conn, tx,
                        "UPDATE fp_roles SET system_role=@sr,description=@d,updated_at=@now WHERE id=@id;",
                        ct, ("@sr", isSystem ? 1 : 0), ("@d", roleDesc), ("@now", now), ("@id", ridStr));
                }

                // Perfis de sistema: repõe sempre; outros: só adiciona o que ainda não tem
                if (isSystem)
                    await Exec(conn, tx, "DELETE FROM fp_role_permissions WHERE role_id=@id;",
                        ct, ("@id", ridStr));

                foreach (var code in perms)
                {
                    var pid = await Scalar(conn, tx, "SELECT id FROM fp_permissions WHERE code=@c LIMIT 1;",
                        ct, ("@c", code));
                    if (pid is not null)
                        await Exec(conn, tx,
                            "INSERT IGNORE INTO fp_role_permissions (role_id,permission_id) VALUES (@rid,@pid);",
                            ct, ("@rid", ridStr), ("@pid", pid.ToString()!));
                }
            }

            // 3. Admin
            var adminId = await Scalar(conn, tx,
                "SELECT id FROM fp_users WHERE user_name=@un LIMIT 1;", ct, ("@un", adminUser));
            var adminRoleId = await Scalar(conn, tx,
                "SELECT id FROM fp_roles WHERE name=@n LIMIT 1;",
                ct, ("@n", DefaultRoles.Administrador));

            if (adminId is not null)
            {
                await Exec(conn, tx,
                    "UPDATE fp_users SET password_hash=@ph,active=1,updated_at=@now WHERE id=@id;",
                    ct, ("@ph", HashPassword(adminPassword)), ("@now", now), ("@id", adminId.ToString()!));
                if (adminRoleId is not null)
                    await Exec(conn, tx,
                        "INSERT IGNORE INTO fp_user_roles (user_id,role_id) VALUES (@uid,@rid);",
                        ct, ("@uid", adminId.ToString()!), ("@rid", adminRoleId.ToString()!));
            }
            else
            {
                var newAdminId = Guid.NewGuid().ToString();
                await Exec(conn, tx, """
                    INSERT INTO fp_users (id,user_name,display_name,password_hash,active,event_scope_mode,created_at,updated_at)
                    VALUES (@id,@un,@dn,@ph,1,'TODOS',@now,@now);
                    """, ct,
                    ("@id", newAdminId), ("@un", adminUser), ("@dn", adminName),
                    ("@ph", HashPassword(adminPassword)), ("@now", now));
                if (adminRoleId is not null)
                    await Exec(conn, tx,
                        "INSERT INTO fp_user_roles (user_id,role_id) VALUES (@uid,@rid);",
                        ct, ("@uid", newAdminId), ("@rid", adminRoleId.ToString()!));
            }

            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Escopo
    // ═══════════════════════════════════════════════════════════════════════

    public bool CanAccessEvent(SessionView session, string eventId, string eventStatus)
    {
        if (session.Permissions.Contains(Permissions.ScopeGlobal)) return true;
        // Delegado ao chamador que tem o UserView completo com AuthorizedEventIds
        return true;
    }

    public bool CanAccessGate(SessionView session, string gateId, IEnumerable<string> userGateIds)
    {
        if (session.Permissions.Contains(Permissions.ScopeGlobal)) return true;
        return userGateIds.Contains(gateId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers estáticos públicos
    // ═══════════════════════════════════════════════════════════════════════

    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers privados
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task SaveUserLinksAsync(
        MySqlConnection conn, MySqlTransaction tx, Guid userId,
        IReadOnlyList<Guid>? roleIds, IReadOnlyList<Guid>? eventIds, IReadOnlyList<Guid>? gateIds,
        EventScopeMode scope, CancellationToken ct)
    {
        await Exec(conn, tx, "DELETE FROM fp_user_roles WHERE user_id=@id;", ct, ("@id", userId.ToString()));
        foreach (var rid in (roleIds ?? []))
            if (await Exists(conn, tx, "SELECT COUNT(*) FROM fp_roles WHERE id=@rid AND active=1;",
                ct, ("@rid", rid.ToString())))
                await Exec(conn, tx, "INSERT IGNORE INTO fp_user_roles (user_id,role_id) VALUES (@uid,@rid);",
                    ct, ("@uid", userId.ToString()), ("@rid", rid.ToString()));

        await Exec(conn, tx, "DELETE FROM fp_user_events WHERE user_id=@id;", ct, ("@id", userId.ToString()));
        if (scope == EventScopeMode.Especificos)
            foreach (var eid in (eventIds ?? []))
                await Exec(conn, tx, "INSERT IGNORE INTO fp_user_events (user_id,event_id) VALUES (@uid,@eid);",
                    ct, ("@uid", userId.ToString()), ("@eid", eid.ToString()));

        await Exec(conn, tx, "DELETE FROM fp_user_gates WHERE user_id=@id;", ct, ("@id", userId.ToString()));
        foreach (var gid in (gateIds ?? []))
            await Exec(conn, tx, "INSERT IGNORE INTO fp_user_gates (user_id,gate_id) VALUES (@uid,@gid);",
                ct, ("@uid", userId.ToString()), ("@gid", gid.ToString()));
    }

    private static async Task<IReadOnlyList<string>> GetUserPermissionsAsync(
        MySqlConnection conn, Guid userId, CancellationToken ct)
    {
        await using var c = conn.CreateCommand();
        c.CommandText = """
            SELECT DISTINCT p.code
            FROM fp_permissions p
            INNER JOIN fp_role_permissions rp ON rp.permission_id=p.id
            INNER JOIN fp_user_roles ur ON ur.role_id=rp.role_id
            INNER JOIN fp_roles r ON r.id=ur.role_id
            WHERE ur.user_id=@uid AND r.active=1
            ORDER BY p.code;
            """;
        c.Parameters.AddWithValue("@uid", userId.ToString());
        var result = new List<string>();
        await using var r = await c.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result.Add(r.GetString(0));
        return result;
    }

    private static async Task<UserView?> GetUserViewAsync(MySqlConnection conn, Guid userId, CancellationToken ct)
    {
        await using var c = conn.CreateCommand();
        c.CommandText = """
            SELECT id,user_name,display_name,active,event_scope_mode,last_login_at,blocked_until,
                   physical_access_enabled,access_badge_code
            FROM fp_users WHERE id=@id LIMIT 1;
            """;
        c.Parameters.AddWithValue("@id", userId.ToString());
        await using var r = await c.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var id = ReadGuid(r, 0);
        var un = r.GetString(1); var dn = r.GetString(2); var active = Convert.ToBoolean(r.GetValue(3));
        var scopeStr = r.IsDBNull(4) ? "TODOS" : r.GetString(4);
        var scope = Enum.TryParse<EventScopeMode>(scopeStr, true, out var sm) ? sm : EventScopeMode.Todos;
        DateTimeOffset? lastLogin = r.IsDBNull(5) ? null : new(DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc));
        DateTimeOffset? blocked = r.IsDBNull(6) ? null : new(DateTime.SpecifyKind(r.GetDateTime(6), DateTimeKind.Utc));
        var physAccess = !r.IsDBNull(7) && Convert.ToBoolean(r.GetValue(7));
        var badgeCode = r.IsDBNull(8) ? null : r.GetValue(8).ToString();
        string? staffMemberId = null;
        await r.CloseAsync();

        var roles = await GetUserRolesAsync(conn, id, ct);

        await using var evtC = conn.CreateCommand();
        evtC.CommandText = "SELECT event_id FROM fp_user_events WHERE user_id=@uid;";
        evtC.Parameters.AddWithValue("@uid", id.ToString());
        var eventIds = new List<string>();
        await using var evtR = await evtC.ExecuteReaderAsync(ct);
        while (await evtR.ReadAsync(ct)) eventIds.Add(evtR.GetValue(0).ToString()!);
        await evtR.CloseAsync();

        await using var gateC = conn.CreateCommand();
        gateC.CommandText = "SELECT gate_id FROM fp_user_gates WHERE user_id=@uid;";
        gateC.Parameters.AddWithValue("@uid", id.ToString());
        var gateIds = new List<string>();
        await using var gateR = await gateC.ExecuteReaderAsync(ct);
        while (await gateR.ReadAsync(ct)) gateIds.Add(gateR.GetValue(0).ToString()!);

        return new UserView(id, un, dn, active, scope, lastLogin, blocked, roles, eventIds, gateIds,
            physAccess, badgeCode, staffMemberId);
    }

    private static async Task<IReadOnlyList<RoleSummary>> GetUserRolesAsync(
        MySqlConnection conn, Guid userId, CancellationToken ct)
    {
        await using var c = conn.CreateCommand();
        c.CommandText = """
            SELECT r.id,r.name,r.system_role FROM fp_roles r
            INNER JOIN fp_user_roles ur ON ur.role_id=r.id
            WHERE ur.user_id=@uid ORDER BY r.name;
            """;
        c.Parameters.AddWithValue("@uid", userId.ToString());
        var result = new List<RoleSummary>();
        await using var r = await c.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            result.Add(new RoleSummary(ReadGuid(r, 0), r.GetString(1), Convert.ToBoolean(r.GetValue(2))));
        return result;
    }

    private static async Task<RoleView?> GetRoleViewAsync(
        MySqlConnection conn, Guid roleId, CancellationToken ct)
    {
        await using var c = conn.CreateCommand();
        c.CommandText = "SELECT id,name,description,active,system_role FROM fp_roles WHERE id=@id LIMIT 1;";
        c.Parameters.AddWithValue("@id", roleId.ToString());
        await using var r = await c.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var id = ReadGuid(r, 0); var name = r.GetString(1);
        var desc = r.IsDBNull(2) ? null : r.GetString(2);
        var active = Convert.ToBoolean(r.GetValue(3)); var sys = Convert.ToBoolean(r.GetValue(4));
        await r.CloseAsync();
        var perms = await GetRolePermissionsAsync(conn, id, ct);
        return new RoleView(id, name, desc, active, sys, perms);
    }

    private static async Task<IReadOnlyList<string>> GetRolePermissionsAsync(
        MySqlConnection conn, Guid roleId, CancellationToken ct)
    {
        await using var c = conn.CreateCommand();
        c.CommandText = """
            SELECT p.code FROM fp_permissions p
            INNER JOIN fp_role_permissions rp ON rp.permission_id=p.id
            WHERE rp.role_id=@rid ORDER BY p.code;
            """;
        c.Parameters.AddWithValue("@rid", roleId.ToString());
        var result = new List<string>();
        await using var r = await c.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result.Add(r.GetString(0));
        return result;
    }

    private sealed record UserAuthData(
        Guid Id, string UserName, string DisplayName, string PasswordHash,
        bool Active, int FailedAttempts, DateTime? BlockedUntil);

    private static async Task<UserAuthData?> ReadUserAuthAsync(
        MySqlConnection conn, MySqlTransaction tx, string userName, CancellationToken ct)
    {
        await using var c = conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = """
            SELECT id,user_name,display_name,password_hash,active,failed_attempts,blocked_until
            FROM fp_users WHERE user_name=@un LIMIT 1;
            """;
        c.Parameters.AddWithValue("@un", userName);
        await using var r = await c.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new UserAuthData(
            ReadGuid(r, 0), r.GetString(1), r.GetString(2), r.GetString(3),
            Convert.ToBoolean(r.GetValue(4)), Convert.ToInt32(r.GetValue(5)),
            r.IsDBNull(6) ? null : r.GetDateTime(6));
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt,
            Pbkdf2Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"pbkdf2-sha256:{Pbkdf2Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string stored)
    {
        try
        {
            var parts = stored.Split(':');
            if (parts.Length != 4 || parts[0] != "pbkdf2-sha256") return false;
            var iterations = int.Parse(parts[1]);
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt,
                iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch { return false; }
    }

    private static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static async Task<bool> Exists(
        MySqlConnection conn, MySqlTransaction tx,
        string sql, CancellationToken ct,
        params (string Name, object Value)[] p)
    {
        await using var c = conn.CreateCommand();
        c.Transaction = tx; c.CommandText = sql;
        foreach (var x in p) c.Parameters.AddWithValue(x.Name, x.Value);
        return Convert.ToInt32(await c.ExecuteScalarAsync(ct)) > 0;
    }

    private static async Task<int> Exec(
        MySqlConnection conn, MySqlTransaction tx,
        string sql, CancellationToken ct,
        params (string Name, object Value)[] p)
    {
        await using var c = conn.CreateCommand();
        c.Transaction = tx; c.CommandText = sql;
        foreach (var x in p) c.Parameters.AddWithValue(x.Name, x.Value);
        return await c.ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> Scalar(
        MySqlConnection conn, MySqlTransaction tx,
        string sql, CancellationToken ct,
        params (string Name, object Value)[] p)
    {
        await using var c = conn.CreateCommand();
        c.Transaction = tx; c.CommandText = sql;
        foreach (var x in p) c.Parameters.AddWithValue(x.Name, x.Value);
        var result = await c.ExecuteScalarAsync(ct);
        return result is DBNull ? null : result;
    }

    private static Guid ReadGuid(MySqlDataReader r, int i) =>
        Guid.Parse(r.GetValue(i).ToString()!);
}
