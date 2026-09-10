using FastPass.Api.Auth;
using FastPass.Application.Access;
using FastPass.Application.Audit;
using FastPass.Application.Auth;
using FastPass.Infrastructure.Audit;
using FastPass.Application.Catalog;
using FastPass.Application.Import;
using FastPass.Domain.Enums;
using FastPass.Infrastructure.Access;
using FastPass.Infrastructure.Auth;
using FastPass.Infrastructure.Catalog;
using FastPass.Infrastructure.Database;
using FastPass.Infrastructure.Import;
using FastPass.Application.Reports;
using FastPass.Infrastructure.Reports;
using FastPass.Worker.Mqtt;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("FastPass");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:FastPass não foi configurada.");

// ── Serviços ──────────────────────────────────────────────────────────────────
builder.Services.AddSingleton(new FastPassDbConnectionFactory(connectionString));
builder.Services.AddSingleton<DatabaseProbe>();
builder.Services.AddSingleton<DatabaseMigrator>();
builder.Services.AddScoped<IAuthService, MySqlAuthService>();
builder.Services.AddSingleton<ICatalogService, MySqlCatalogService>();
builder.Services.AddSingleton<IAccessPolicyService, MySqlAccessPolicyService>();
builder.Services.AddSingleton<IAccessMessageService, MySqlAccessMessageService>();
builder.Services.AddSingleton<IAccessAttemptQueryService, MySqlAccessAttemptQueryService>();
builder.Services.AddSingleton<IAccessValidationService, MySqlAccessValidationService>();
builder.Services.AddSingleton<ITurnstileMonitoringService, MySqlTurnstileMonitoringService>();
builder.Services.AddScoped<ITicketImportService, MySqlTicketImportService>();
builder.Services.AddSingleton<IValidationReportService, MySqlValidationReportService>();
builder.Services.AddSingleton<IAuditTrailService, MySqlAuditTrailService>();

// ── MQTT + Worker de catracas ─────────────────────────────────────────────────
builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection("Mqtt"));
builder.Services.AddHostedService<MqttTurnstileService>();

builder.Services.AddCors(options =>
{
    var origins = builder.Configuration["Cors:AllowedOrigins"]?.Split(',')
        ?? ["http://localhost:5173", "http://127.0.0.1:5173"];
    options.AddPolicy("WebApp", policy =>
        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

var app = builder.Build();
app.UseCors("WebApp");

// SessionMiddleware deve vir depois do CORS
app.UseMiddleware<SessionMiddleware>();
// AuditTrailMiddleware depois da sessão, para conhecer o usuário autenticado.
app.UseMiddleware<AuditTrailMiddleware>();

// ── Migrations + seed ─────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
    await app.Services.GetRequiredService<DatabaseMigrator>().ApplyAsync();

// Cria admin inicial se não houver nenhum usuário ativo
var adminUser = builder.Configuration["Auth:AdminUser"] ?? "admin";
var adminName = builder.Configuration["Auth:AdminName"] ?? "Administrador";
var adminPass = builder.Configuration["Auth:AdminPassword"];
if (!string.IsNullOrWhiteSpace(adminPass))
{
    using var seedScope = app.Services.CreateScope();
    var authSeed = seedScope.ServiceProvider.GetRequiredService<IAuthService>();
    try
    {
        await authSeed.EnsureDefaultsAsync(adminUser, adminName, adminPass);
        app.Logger.LogInformation("Seed de autenticação concluído para o usuário '{User}'.", adminUser);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Falha no seed de autenticação.");
        throw;
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Rotas públicas (sem sessão)
// ══════════════════════════════════════════════════════════════════════════════

app.MapGet("/", () => Results.Ok(new { service = "FastPass.Api", version = "2.0", status = "running" }));

app.MapGet("/health/database", async (DatabaseProbe probe, CancellationToken ct) =>
{
    var result = await probe.CheckAsync(ct);
    return result.Connected
        ? Results.Ok(result)
        : Results.Problem(detail: result.Error, statusCode: 503, title: "Banco indisponível");
});

// ══════════════════════════════════════════════════════════════════════════════
// Autenticação
// ══════════════════════════════════════════════════════════════════════════════

app.MapPost("/api/auth/login", async (
    LoginCommand command,
    IAuthService authService,
    HttpContext ctx,
    CancellationToken ct) =>
{
    try
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString();
        var ua = ctx.Request.Headers.UserAgent.ToString();
        var (token, session) = await authService.LoginAsync(command, ip, ua, ct);
        ctx.SetSessionCookie(token);
        return Results.Ok(session);
    }
    catch (AuthException)
    {
        // Mensagem genérica para não revelar se o usuário existe
        return Results.Unauthorized();
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/auth/me", (HttpContext ctx) =>
{
    var session = ctx.GetSession();
    return session is null ? Results.Unauthorized() : Results.Ok(session);
});

app.MapPost("/api/auth/logout", async (IAuthService authService, HttpContext ctx, CancellationToken ct) =>
{
    var token = ctx.Request.Cookies[SessionMiddleware.CookieName];
    if (!string.IsNullOrWhiteSpace(token))
        await authService.LogoutAsync(MySqlAuthService.HashToken(token), ct);
    ctx.ClearSessionCookie();
    return Results.Ok(new { message = "Sessão encerrada." });
});

app.MapPut("/api/auth/password", async (ChangePasswordCommand command, IAuthService authService, HttpContext ctx, CancellationToken ct) =>
{
    var session = ctx.RequireSession();
    try
    {
        await authService.ChangePasswordAsync(session.UserId, command, ct);
        ctx.ClearSessionCookie();
        return Results.Ok(new { message = "Senha alterada. Faça login novamente." });
    }
    catch (AuthException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ══════════════════════════════════════════════════════════════════════════════
// Usuários e Perfis  (requer users.manage)
// ══════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/users", async (bool? activeOnly, IAuthService auth, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("usuario.gerenciar") is { } e) return e;
    return Results.Ok(await auth.ListUsersAsync(activeOnly ?? true, ct));
});

app.MapPost("/api/users", async (CreateUserCommand command, IAuthService auth, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("usuario.gerenciar") is { } e) return e;
    try { return Results.Created("/api/users", await auth.CreateUserAsync(command, ct)); }
    catch (AuthConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPut("/api/users/{userId:guid}", async (Guid userId, UpdateUserCommand command, IAuthService auth, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("usuario.gerenciar") is { } e) return e;
    try { return Results.Ok(await auth.UpdateUserAsync(userId, command, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/users/{userId:guid}/unblock", async (Guid userId, IAuthService auth, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("usuario.gerenciar") is { } e) return e;
    try { await auth.UnblockUserAsync(userId, ct); return Results.Ok(new { message = "Usuário desbloqueado." }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// Reset de senha de outro usuário por administrador (não exige senha atual).
app.MapPost("/api/users/{userId:guid}/reset-password", async (
    Guid userId, ResetPasswordCommand command, IAuthService auth, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("usuario.gerenciar") is { } e) return e;
    try
    {
        await auth.ResetPasswordAsync(userId, command.NewPassword, ct);
        return Results.Ok(new { message = "Senha redefinida. O usuário deverá entrar com a nova senha." });
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapGet("/api/permissions", async (IAuthService auth, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("usuario.gerenciar") is { } e) return e;
    return Results.Ok(await auth.ListPermissionsAsync(ct));
});

app.MapGet("/api/roles", async (IAuthService auth, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("usuario.gerenciar") is { } e) return e;
    return Results.Ok(await auth.ListRolesAsync(ct));
});

app.MapPost("/api/roles", async (CreateRoleCommand command, IAuthService auth, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("usuario.gerenciar") is { } e) return e;
    try { return Results.Created("/api/roles", await auth.CreateRoleAsync(command, ct)); }
    catch (AuthConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPut("/api/roles/{roleId:guid}", async (Guid roleId, UpdateRoleCommand command, IAuthService auth, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("usuario.gerenciar") is { } e) return e;
    try { return Results.Ok(await auth.UpdateRoleAsync(roleId, command, ct)); }
    catch (AuthForbiddenException ex) { return Results.Json(new { error = ex.Message }, statusCode: 403); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPut("/api/roles/{roleId:guid}/permissions", async (Guid roleId, SetRolePermissionsCommand command, IAuthService auth, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("perfil.permissoes.gerenciar") is { } e) return e;
    var session = ctx.RequireSession();
    try { return Results.Ok(await auth.SetRolePermissionsAsync(roleId, command, session, ct)); }
    catch (AuthForbiddenException ex) { return Results.Json(new { error = ex.Message }, statusCode: 403); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapDelete("/api/roles/{roleId:guid}", async (Guid roleId, IAuthService auth, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("usuario.gerenciar") is { } e) return e;
    try { await auth.DeleteRoleAsync(roleId, ct); return Results.NoContent(); }
    catch (AuthForbiddenException ex) { return Results.Json(new { error = ex.Message }, statusCode: 403); }
    catch (AuthConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ══════════════════════════════════════════════════════════════════════════════
// Trilhas de auditoria  (requer auditoria.ler)
//   • login-log  → acesso ao SISTEMA (autenticação)
//   • audit-trail → ações que mudam estado (criação, edição, exclusão, importação…)
// ══════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/login-log", async (
    int? page, int? pageSize, string? userName, string? outcome,
    DateTimeOffset? from, DateTimeOffset? to,
    IAuthService auth, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("auditoria.ler") is { } e) return e;
    return Results.Ok(await auth.ListLoginLogAsync(page ?? 1, pageSize ?? 50, userName, outcome, from, to, ct));
});

app.MapGet("/api/audit-trail", async (
    int? page, int? pageSize, string? userName, string? action,
    DateTimeOffset? from, DateTimeOffset? to,
    IAuditTrailService audit, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("auditoria.ler") is { } e) return e;
    return Results.Ok(await audit.ListAsync(page ?? 1, pageSize ?? 50, userName, action, from, to, ct));
});

// ══════════════════════════════════════════════════════════════════════════════
// Venues  (requer events.view / events.manage)
// ══════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/venues", async (ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("evento.criar") is { } e) return e;
    return Results.Ok(await svc.ListVenuesAsync(ct));
});

app.MapPost("/api/venues", async (CreateVenueCommand command, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("evento.criar") is { } e) return e;
    try { return Results.Created("/api/venues", await svc.CreateVenueAsync(command, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ══════════════════════════════════════════════════════════════════════════════
// Eventos
// ══════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/events", async (Guid? venueId, string? status, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    // Leitura ampla: qualquer perfil operacional precisa enxergar a lista de eventos.
    if (ctx.RequireAnyPermission("evento.criar", "evento.editar", "acesso.validar", "relatorio.ler",
        "acessos.ler", "ticket.consultar", "portaria.gerenciar", "setor.gerenciar", "mensagem.gerenciar",
        "ticket.importar") is { } e) return e;
    try { return Results.Ok(await svc.ListEventsAsync(venueId, status, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/events", async (CreateEventCommand command, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("evento.criar") is { } e) return e;
    try { return Results.Created("/api/events", await svc.CreateEventAsync(command, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ══════════════════════════════════════════════════════════════════════════════
// Setores
// ══════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/events/{eventId:guid}/sectors", async (Guid eventId, bool? activeOnly, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequireAnyPermission("evento.criar", "evento.editar", "setor.gerenciar", "portaria.gerenciar",
        "relacao.gerenciar", "acesso.validar", "relatorio.ler", "ticket.consultar", "ticket.importar") is { } e) return e;
    try { return Results.Ok(await svc.ListSectorsAsync(eventId, activeOnly ?? true, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/events/{eventId:guid}/sectors", async (Guid eventId, CreateSectorCommand command, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("evento.criar") is { } e) return e;
    try { return Results.Created($"/api/events/{eventId}/sectors", await svc.CreateSectorAsync(eventId, command, ct)); }
    catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapDelete("/api/events/{eventId:guid}/sectors/{sectorId:guid}", async (Guid eventId, Guid sectorId, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("evento.criar") is { } e) return e;
    try
    {
        var removed = await svc.RemoveSectorAsync(eventId, sectorId, ct);
        return removed ? Results.NoContent() : Results.NotFound(new { error = "Setor não encontrado ou já removido." });
    }
    catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ══════════════════════════════════════════════════════════════════════════════
// Portarias
// ══════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/events/{eventId:guid}/gates", async (Guid eventId, bool? activeOnly, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequireAnyPermission("evento.criar", "evento.editar", "portaria.gerenciar", "setor.gerenciar",
        "relacao.gerenciar", "dispositivo.gerenciar", "acesso.validar", "relatorio.ler", "ticket.consultar") is { } e) return e;
    try { return Results.Ok(await svc.ListGatesAsync(eventId, activeOnly ?? true, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/events/{eventId:guid}/gates", async (Guid eventId, CreateGateCommand command, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("evento.criar") is { } e) return e;
    try { return Results.Created($"/api/events/{eventId}/gates", await svc.CreateGateAsync(eventId, command, ct)); }
    catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapDelete("/api/events/{eventId:guid}/gates/{gateId:guid}", async (Guid eventId, Guid gateId, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("evento.criar") is { } e) return e;
    try
    {
        var removed = await svc.RemoveGateAsync(eventId, gateId, ct);
        return removed ? Results.NoContent() : Results.NotFound(new { error = "Portaria não encontrada ou já removida." });
    }
    catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPut("/api/events/{eventId:guid}/gates/{gateId:guid}/operation-mode", async (Guid eventId, Guid gateId, SetGateOperationModeCommand command, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("evento.criar") is { } e) return e;
    try { return Results.Ok(await svc.SetGateOperationModeAsync(eventId, gateId, command, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ══════════════════════════════════════════════════════════════════════════════
// Dispositivos  (requer devices.manage)
// ══════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/events/{eventId:guid}/devices", async (Guid eventId, Guid? gateId, bool? activeOnly, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("dispositivo.gerenciar") is { } e) return e;
    try { return Results.Ok(await svc.ListDevicesAsync(eventId, gateId, activeOnly ?? true, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/events/{eventId:guid}/gates/{gateId:guid}/devices", async (Guid eventId, Guid gateId, CreateDeviceCommand command, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("dispositivo.gerenciar") is { } e) return e;
    try { return Results.Created($"/api/events/{eventId}/gates/{gateId}/devices", await svc.CreateDeviceAsync(eventId, gateId, command, ct)); }
    catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPut("/api/events/{eventId:guid}/gates/{gateId:guid}/devices/{deviceId:guid}", async (Guid eventId, Guid gateId, Guid deviceId, UpdateDeviceCommand command, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("dispositivo.gerenciar") is { } e) return e;
    try { return Results.Ok(await svc.UpdateDeviceAsync(eventId, gateId, deviceId, command, ct)); }
    catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPut("/api/events/{eventId:guid}/gates/{gateId:guid}/devices/{deviceId:guid}/operation-mode", async (Guid eventId, Guid gateId, Guid deviceId, SetDeviceOperationModeCommand command, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("dispositivo.gerenciar") is { } e) return e;
    try { return Results.Ok(await svc.SetDeviceOperationModeAsync(eventId, gateId, deviceId, command, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// Monitoramento GLOBAL de catracas MQTT: todas as placas já vistas (mesmo não cadastradas),
// status online/offline, firmware/IP e a portaria/evento a que estão atribuídas.
app.MapGet("/api/turnstiles", async (int? onlineWindowSeconds, ITurnstileMonitoringService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("dispositivo.gerenciar") is { } e) return e;
    return Results.Ok(await svc.ListAsync(onlineWindowSeconds ?? 90, ct));
});

// ══════════════════════════════════════════════════════════════════════════════
// Matriz Portaria × Setor
// ══════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/events/{eventId:guid}/gate-sectors", async (Guid eventId, bool? activeOnly, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequireAnyPermission("evento.criar", "evento.editar", "portaria.gerenciar", "setor.gerenciar",
        "relacao.gerenciar", "acesso.validar", "relatorio.ler", "ticket.consultar") is { } e) return e;
    try { return Results.Ok(await svc.ListGateSectorsAsync(eventId, activeOnly ?? true, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/events/{eventId:guid}/gate-sectors", async (Guid eventId, CreateGateSectorCommand command, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("evento.criar") is { } e) return e;
    try { return Results.Created($"/api/events/{eventId}/gate-sectors", await svc.CreateGateSectorAsync(eventId, command, ct)); }
    catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapDelete("/api/events/{eventId:guid}/gate-sectors/{gateSectorId:guid}", async (Guid eventId, Guid gateSectorId, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("evento.criar") is { } e) return e;
    try
    {
        var removed = await svc.RemoveGateSectorAsync(eventId, gateSectorId, ct);
        return removed ? Results.NoContent() : Results.NotFound(new { error = "Associação não encontrada ou já removida." });
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ══════════════════════════════════════════════════════════════════════════════
// Tickets  (requer tickets.manage)
// ══════════════════════════════════════════════════════════════════════════════

app.MapPost("/api/events/{eventId:guid}/ticket-types", async (Guid eventId, CreateTicketTypeCommand command, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("ticket.consultar") is { } e) return e;
    try { return Results.Created($"/api/events/{eventId}/ticket-types", await svc.CreateTicketTypeAsync(eventId, command, ct)); }
    catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/events/{eventId:guid}/ticket-batches", async (Guid eventId, CreateTicketBatchCommand command, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("ticket.consultar") is { } e) return e;
    try { return Results.Created($"/api/events/{eventId}/ticket-batches", await svc.CreateTicketBatchAsync(eventId, command, ct)); }
    catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/events/{eventId:guid}/tickets", async (Guid eventId, IssueTicketCommand command, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("ticket.consultar") is { } e) return e;
    try { return Results.Created($"/api/events/{eventId}/tickets", await svc.IssueTicketAsync(eventId, command, ct)); }
    catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapGet("/api/events/{eventId:guid}/tickets", async (
    Guid eventId, Guid? ticketTypeId, Guid? batchId, string? externalId,
    string? code, string? status, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("ticket.consultar") is { } e) return e;
    try { return Results.Ok(await svc.ListTicketsAsync(eventId, ticketTypeId, batchId, externalId, code, status, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ══════════════════════════════════════════════════════════════════════════════
// Políticas de acesso
// ══════════════════════════════════════════════════════════════════════════════

app.MapPost("/api/events/{eventId:guid}/access-policies", async (Guid eventId, CreateAccessPolicyCommand command, IAccessPolicyService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("evento.criar") is { } e) return e;
    try { return Results.Created($"/api/events/{eventId}/access-policies", await svc.CreateAsync(eventId, command, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapGet("/api/events/{eventId:guid}/access-policies", async (
    Guid eventId, Guid? ticketTypeId, Guid? batchId, Guid? gateId, Guid? sectorId,
    string? direction, bool? activeOnly, IAccessPolicyService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("evento.criar") is { } e) return e;
    try { return Results.Ok(await svc.ListAsync(eventId, ticketTypeId, batchId, gateId, sectorId, direction, activeOnly ?? true, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ══════════════════════════════════════════════════════════════════════════════
// Eventos — exclusão e desativação
// ══════════════════════════════════════════════════════════════════════════════

app.MapDelete("/api/events/{eventId:guid}", async (
    Guid eventId, HttpContext ctx, CancellationToken ct,
    FastPassDbConnectionFactory dbFactory) =>
{
    if (ctx.RequirePermission("evento.excluir") is { } e) return e;
    await using var conn = dbFactory.Create();
    await conn.OpenAsync(ct);
    // Bloqueia exclusão se houver tickets — recomenda usar Administração primeiro
    await using var check = conn.CreateCommand();
    check.CommandText = "SELECT COUNT(*) FROM fp_tickets WHERE event_id=@eid AND status NOT IN ('cancelled','revoked');";
    check.Parameters.AddWithValue("@eid", eventId.ToString());
    var ticketCount = Convert.ToInt32(await check.ExecuteScalarAsync(ct));
    if (ticketCount > 0)
        return Results.Conflict(new { error = $"Não é possível excluir um evento que possui {ticketCount} ingresso(s). Use Administração → Excluir dados do evento primeiro." });
    // Exclusão em cascata manual (respeitando a ordem das FKs)
    await using var cmd = conn.CreateCommand();
    cmd.Parameters.AddWithValue("@eid", eventId.ToString());
    cmd.CommandText = @"
        DELETE FROM fp_sync_outbox          WHERE event_id = @eid;
        DELETE FROM fp_audit_events         WHERE event_id = @eid;
        DELETE FROM fp_user_event_scopes    WHERE event_id = @eid;
        DELETE FROM fp_user_events          WHERE event_id = @eid;
        DELETE FROM fp_access_messages      WHERE event_id = @eid;
        DELETE FROM fp_ticket_import_rows   WHERE import_id IN (SELECT id FROM fp_ticket_imports WHERE event_id = @eid);
        DELETE FROM fp_ticket_imports       WHERE event_id = @eid;
        DELETE FROM fp_access_attempts      WHERE event_id = @eid;
        DELETE FROM fp_access_policies      WHERE event_id = @eid;
        DELETE FROM fp_gate_sectors         WHERE event_id = @eid;
        DELETE FROM fp_event_gates          WHERE event_id = @eid;
        DELETE FROM fp_sectors              WHERE event_id = @eid;
        DELETE FROM fp_tickets              WHERE event_id = @eid;
        DELETE FROM fp_ticket_batches       WHERE event_id = @eid;
        DELETE FROM fp_ticket_types         WHERE event_id = @eid;
        DELETE FROM fp_events               WHERE id       = @eid;";
    await cmd.ExecuteNonQueryAsync(ct);
    // Verifica se o evento existia de facto
    await using var checkGone = conn.CreateCommand();
    checkGone.CommandText = "SELECT COUNT(*) FROM fp_events WHERE id=@eid;";
    checkGone.Parameters.AddWithValue("@eid", eventId.ToString());
    var stillExists = Convert.ToInt32(await checkGone.ExecuteScalarAsync(ct));
    return stillExists > 0
        ? Results.NotFound(new { error = "Evento não encontrado." })
        : Results.Ok(new { message = "Evento excluído com sucesso." });
});

// Ticket individual: alterar status (active, cancelled, revoked) — requer ticket.status
app.MapPut("/api/events/{eventId:guid}/tickets/{ticketId:guid}/status", async (
    Guid eventId, Guid ticketId, TicketStatusCommand command,
    HttpContext ctx, CancellationToken ct,
    FastPassDbConnectionFactory dbFactory) =>
{
    if (ctx.RequirePermission("ticket.status") is { } e) return e;
    var newStatus = (command.Status ?? "").Trim().ToLowerInvariant();
    if (!new[] { "active", "cancelled", "revoked" }.Contains(newStatus))
        return Results.BadRequest(new { error = "Status deve ser 'active', 'cancelled' ou 'revoked'." });
    await using var conn = dbFactory.Create();
    await conn.OpenAsync(ct);
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "UPDATE fp_tickets SET status=@st,updated_at=@now WHERE id=@id AND event_id=@eid;";
    cmd.Parameters.AddWithValue("@st", newStatus);
    cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
    cmd.Parameters.AddWithValue("@id", ticketId.ToString());
    cmd.Parameters.AddWithValue("@eid", eventId.ToString());
    var rows = await cmd.ExecuteNonQueryAsync(ct);
    return rows == 0
        ? Results.NotFound(new { error = "Ticket não encontrado." })
        : Results.Ok(new { message = $"Status alterado para '{newStatus}'." });
});

// Ticket individual: cancelar/revogar (soft-delete via status)
app.MapDelete("/api/events/{eventId:guid}/tickets/{ticketId:guid}", async (
    Guid eventId, Guid ticketId, string? status,
    HttpContext ctx, CancellationToken ct,
    FastPassDbConnectionFactory dbFactory) =>
{
    if (ctx.RequirePermission("ticket.status") is { } e) return e;
    var newStatus = (status ?? "cancelled").ToLowerInvariant();
    if (!new[] { "cancelled", "revoked" }.Contains(newStatus))
        return Results.BadRequest(new { error = "Status deve ser 'cancelled' ou 'revoked'." });
    await using var conn = dbFactory.Create();
    await conn.OpenAsync(ct);
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "UPDATE fp_tickets SET status=@st,updated_at=@now WHERE id=@id AND event_id=@eid;";
    cmd.Parameters.AddWithValue("@st", newStatus);
    cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
    cmd.Parameters.AddWithValue("@id", ticketId.ToString());
    cmd.Parameters.AddWithValue("@eid", eventId.ToString());
    var rows = await cmd.ExecuteNonQueryAsync(ct);
    return rows == 0
        ? Results.NotFound(new { error = "Ticket não encontrado." })
        : Results.Ok(new { message = $"Ticket marcado como '{newStatus}'." });
});

// ══════════════════════════════════════════════════════════════════════════════
// Mensagens configuráveis  (requer messages.manage)
// ══════════════════════════════════════════════════════════════════════════════

// Templates padrão GLOBAIS — valem para todos os eventos que não tiverem override
app.MapGet("/api/message-templates", async (IAccessMessageService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("mensagem.gerenciar") is { } e) return e;
    return Results.Ok(await svc.ListTemplatesAsync(ct));
});

app.MapPut("/api/message-templates/{code}", async (string code, UpdateAccessMessageTemplateCommand command, IAccessMessageService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("mensagem.gerenciar") is { } e) return e;
    try { return Results.Ok(await svc.UpdateTemplateAsync(code, command, ct)); }
    catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// Mensagens por evento — herdam do template padrão, exceto onde houver override
app.MapGet("/api/events/{eventId:guid}/access-messages", async (Guid eventId, IAccessMessageService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("mensagem.gerenciar") is { } e) return e;
    try { return Results.Ok(await svc.ListAsync(eventId, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPut("/api/events/{eventId:guid}/access-messages/{code}", async (Guid eventId, string code, UpdateAccessMessageCommand command, IAccessMessageService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("mensagem.gerenciar") is { } e) return e;
    try { return Results.Ok(await svc.UpdateAsync(eventId, code, command, ct)); }
    catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapDelete("/api/events/{eventId:guid}/access-messages/{code}", async (Guid eventId, string code, IAccessMessageService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("mensagem.gerenciar") is { } e) return e;
    try { await svc.RestoreDefaultAsync(eventId, code, ct); return Results.Ok(new { message = "Mensagem restaurada ao padrão." }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ══════════════════════════════════════════════════════════════════════════════
// Tentativas de acesso  (requer validation.view)
// ══════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/events/{eventId:guid}/access-attempts", async (
    Guid eventId, DateTimeOffset? from, DateTimeOffset? to, Guid? gateId, Guid? sectorId,
    Guid? deviceId, Guid? ticketId, Guid? staffCredentialId, string? credentialType,
    string? direction, string? decision, string? status, int? page, int? pageSize,
    IAccessAttemptQueryService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("relatorio.ler") is { } e) return e;
    try
    {
        var result = await svc.ListAsync(eventId, new AccessAttemptAuditFilter(
            from, to, gateId, deviceId, ticketId, staffCredentialId,
            credentialType, direction, decision, status, page ?? 1, pageSize ?? 50, sectorId), ct);
        return Results.Ok(result);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapGet("/api/events/{eventId:guid}/access-attempts/summary", async (
    Guid eventId, DateTimeOffset? from, DateTimeOffset? to, Guid? gateId, Guid? sectorId,
    Guid? deviceId, Guid? ticketId, Guid? staffCredentialId, string? credentialType,
    string? direction, string? decision, string? status,
    IAccessAttemptQueryService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("relatorio.ler") is { } e) return e;
    try
    {
        var result = await svc.SummaryAsync(eventId, new AccessAttemptAuditFilter(
            from, to, gateId, deviceId, ticketId, staffCredentialId,
            credentialType, direction, decision, status, 1, 50, sectorId), ct);
        return Results.Ok(result);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ══════════════════════════════════════════════════════════════════════════════
// Validação de acesso — APP e Catraca
// (sem permissão de sessão: o APP/catraca autentica por IdempotencyKey + DeviceId)
// ══════════════════════════════════════════════════════════════════════════════

app.MapPost("/api/access/app/validate", async (ValidateAccessCommand command, IAccessValidationService svc, HttpContext ctx, CancellationToken ct) =>
{
    // O app do operador usa a sessão logada e exige permissão de validar acesso.
    if (ctx.RequirePermission("acesso.validar") is { } e) return e;
    try
    {
        var result = await svc.ValidateAsync(
            command with { Channel = "App", DeviceId = null }, ct);
        return Results.Ok(result);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/access/turnstile/validate", async (ValidateAccessCommand command, IAccessValidationService svc, CancellationToken ct) =>
{
    try
    {
        var result = await svc.ValidateAsync(command with { Channel = "Turnstile" }, ct);
        return Results.Ok(result);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/access/validate", async (ValidateAccessCommand command, IAccessValidationService svc, CancellationToken ct) =>
{
    try
    {
        var result = await svc.ValidateAsync(command, ct);
        return Results.Ok(result);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// Validação MANUAL pelo operador (backstage, exceções, falhas de catraca).
// Exige sessão autenticada + permissão acesso.validar. Grava channel='Manual'
// para diferenciar das validações automáticas de app/catraca.
app.MapPost("/api/access/manual/validate", async (
    ValidateAccessCommand command, IAccessValidationService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("acesso.validar") is { } e) return e;
    try
    {
        var result = await svc.ValidateAsync(
            command with { Channel = "Manual", DeviceId = null }, ct);
        return Results.Ok(result);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ══════════════════════════════════════════════════════════════════════════════
// Importação de ingressos  (requer ticket.importar)
// ══════════════════════════════════════════════════════════════════════════════

// Pré-visualização: envia o CSV, recebe amostra + separador detectado
app.MapPost("/api/events/{eventId:guid}/ticket-imports/preview", async (
    Guid eventId,
    HttpRequest req,
    ITicketImportService svc,
    HttpContext ctx,
    CancellationToken ct) =>
{
    if (ctx.RequirePermission("ticket.importar") is { } e) return e;
    try
    {
        string csvContent;
        if (req.HasFormContentType)
        {
            var form = await req.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null) return Results.BadRequest(new { error = "Arquivo CSV não encontrado no formulário." });
            using var sr = new StreamReader(file.OpenReadStream(), System.Text.Encoding.UTF8);
            csvContent = await sr.ReadToEndAsync(ct);
        }
        else
        {
            using var sr = new StreamReader(req.Body, System.Text.Encoding.UTF8);
            csvContent = await sr.ReadToEndAsync(ct);
        }
        var result = await svc.PreviewAsync(csvContent, ct);
        return Results.Ok(result);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// Importação efetiva
app.MapPost("/api/events/{eventId:guid}/ticket-imports", async (
    Guid eventId,
    HttpRequest req,
    ITicketImportService svc,
    HttpContext ctx,
    CancellationToken ct) =>
{
    if (ctx.RequirePermission("ticket.importar") is { } e) return e;
    var session = ctx.RequireSession();
    try
    {
        // Lê multipart/form-data com o CSV + configuração
        if (!req.HasFormContentType)
            return Results.BadRequest(new { error = "Content-Type deve ser multipart/form-data." });

        var form = await req.ReadFormAsync(ct);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null) return Results.BadRequest(new { error = "Arquivo CSV não encontrado." });

        using var sr = new StreamReader(file.OpenReadStream(), System.Text.Encoding.UTF8);
        var csvContent = await sr.ReadToEndAsync(ct);

        var command = new StartImportCommand(
            EventId: eventId,
            TicketTypeId: Guid.TryParse(form["ticketTypeId"], out var ttId) ? ttId : null,
            BatchId: Guid.TryParse(form["batchId"], out var bId) ? bId : null,
            Separator: form["separator"].FirstOrDefault() ?? ",",
            ColCode: int.TryParse(form["colCode"], out var cc) ? cc : 0,
            ColExternalId: int.TryParse(form["colExternalId"], out var cei) ? cei : null,
            ColMaxEntries: int.TryParse(form["colMaxEntries"], out var cme) ? cme : null,
            DefaultMaxEntries: int.TryParse(form["defaultMaxEntries"], out var dme) && dme >= 1 ? dme : 1,
            Mode: Enum.TryParse<ImportMode>(form["mode"].FirstOrDefault() ?? "", true, out var mode)
                ? mode : ImportMode.AdicionarAtualizar,
            ColStatus: int.TryParse(form["colStatus"], out var cs) ? cs : null,
            DefaultStatus: form["defaultStatus"].FirstOrDefault() is { Length: > 0 } ds ? ds : "active",
            ColSector: int.TryParse(form["colSector"], out var csec) ? csec : null,
            DefaultSectorId: Guid.TryParse(form["defaultSectorId"], out var dsid) ? dsid : null,
            ColBatch: int.TryParse(form["colBatch"], out var cbat) ? cbat : null,
            DefaultBatchName: form["defaultBatchName"].FirstOrDefault() is { Length: > 0 } dbn ? dbn : null,
            CsvContent: csvContent,
            FileName: file.FileName);

        var result = await svc.ImportAsync(command, session.UserId, ct);
        return Results.Created($"/api/events/{eventId}/ticket-imports/{result.Id}", result);
    }
    catch (UnknownSectorsException ex) { return Results.Conflict(new { error = ex.Message, unknownSectorNames = ex.UnknownSectorNames }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// Lista importações do evento
app.MapGet("/api/events/{eventId:guid}/ticket-imports", async (
    Guid eventId,
    ITicketImportService svc,
    HttpContext ctx,
    CancellationToken ct) =>
{
    if (ctx.RequirePermission("ticket.importar") is { } e) return e;
    try { return Results.Ok(await svc.ListAsync(eventId, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// Detalhe de uma importação com linhas de erro
app.MapGet("/api/events/{eventId:guid}/ticket-imports/{importId:guid}", async (
    Guid eventId,
    Guid importId,
    ITicketImportService svc,
    HttpContext ctx,
    CancellationToken ct) =>
{
    if (ctx.RequirePermission("ticket.importar") is { } e) return e;
    try
    {
        var (imp, rows) = await svc.GetDetailAsync(eventId, importId, ct);
        return Results.Ok(new { import = imp, rows });
    }
    catch (ArgumentException ex) { return Results.NotFound(new { error = ex.Message }); }
});

// ══════════════════════════════════════════════════════════════════════════════
// Clientes  (requer cliente.gerenciar / events.view)
// ══════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/clients", async (bool? activeOnly, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("cliente.gerenciar") is { } e) return e;
    return Results.Ok(await svc.ListClientsAsync(activeOnly ?? true, ct));
});

app.MapPost("/api/clients", async (CreateClientCommand command, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("cliente.gerenciar") is { } e) return e;
    try { return Results.Created("/api/clients", await svc.CreateClientAsync(command, ct)); }
    catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPut("/api/clients/{clientId:guid}", async (Guid clientId, UpdateClientCommand command, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("cliente.gerenciar") is { } e) return e;
    try { return Results.Ok(await svc.UpdateClientAsync(clientId, command, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapDelete("/api/clients/{clientId:guid}", async (
    Guid clientId, HttpContext ctx, CancellationToken ct,
    FastPassDbConnectionFactory dbFactory) =>
{
    if (ctx.RequirePermission("cliente.gerenciar") is { } e) return e;
    await using var conn = dbFactory.Create();
    await conn.OpenAsync(ct);

    // Bloqueia exclusão se houver eventos vinculados
    await using var check = conn.CreateCommand();
    check.CommandText = "SELECT COUNT(*) FROM fp_events WHERE client_id=@cid;";
    check.Parameters.AddWithValue("@cid", clientId.ToString());
    var eventCount = Convert.ToInt32(await check.ExecuteScalarAsync(ct));
    if (eventCount > 0)
        return Results.Conflict(new { error = $"Não é possível excluir um cliente vinculado a {eventCount} evento(s). Remova ou reatribua os eventos primeiro." });

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "DELETE FROM fp_clients WHERE id=@cid;";
    cmd.Parameters.AddWithValue("@cid", clientId.ToString());
    var rows = await cmd.ExecuteNonQueryAsync(ct);
    return rows == 0
        ? Results.NotFound(new { error = "Cliente não encontrado." })
        : Results.Ok(new { message = "Cliente excluído com sucesso." });
});

// ── Resumo de tickets por evento (requer tickets.manage)
app.MapGet("/api/events/{eventId:guid}/tickets/summary", async (
    Guid eventId, ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("ticket.consultar") is { } e) return e;
    try { return Results.Ok(await svc.GetTicketSummaryAsync(eventId, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ── Export ZIP do evento (tickets + acessos em CSV)  (requer relatorio.ler)
app.MapGet("/api/events/{eventId:guid}/export", async (
    Guid eventId,
    HttpContext ctx,
    CancellationToken ct,
    FastPassDbConnectionFactory dbFactory) =>
{
    if (ctx.RequirePermission("relatorio.ler") is { } e) return e;
    await using var conn = dbFactory.Create();
    await conn.OpenAsync(ct);

    // CSV de ingressos
    var ticketCsv = new System.Text.StringBuilder();
    ticketCsv.AppendLine("codigo,external_id,status,entradas_usadas,max_entradas,pessoas_dentro,criado_em");
    await using var tc = conn.CreateCommand();
    tc.CommandText = """
        SELECT code,external_id,status,entries_used,maximum_entries,people_inside,created_at
        FROM fp_tickets WHERE event_id=@eid ORDER BY code;
        """;
    tc.Parameters.AddWithValue("@eid", eventId.ToString());
    await using var tr = await tc.ExecuteReaderAsync(ct);
    while (await tr.ReadAsync(ct))
        ticketCsv.AppendLine(string.Join(",",
            ExportEscape(tr.GetValue(0).ToString()!), ExportEscape(tr.IsDBNull(1) ? "" : tr.GetString(1)),
            tr.GetString(2), tr.GetValue(3), tr.GetValue(4), tr.GetValue(5),
            ((DateTime)tr.GetValue(6)).ToString("yyyy-MM-dd HH:mm:ss")));
    await tr.CloseAsync();

    // CSV de acessos
    var accessCsv = new System.Text.StringBuilder();
    accessCsv.AppendLine("horario,codigo,credencial_type,portaria,setor,direcao,decisao,motivo");
    await using var ac = conn.CreateCommand();
    ac.CommandText = """
        SELECT a.requested_at,a.credential_code,a.credential_type,
               g.name,s.name,a.direction,a.decision,a.reason
        FROM fp_access_attempts a
        LEFT JOIN fp_gates g ON g.id=a.gate_id
        LEFT JOIN fp_sectors s ON s.id=a.sector_id
        WHERE a.event_id=@eid ORDER BY a.requested_at;
        """;
    ac.Parameters.AddWithValue("@eid", eventId.ToString());
    await using var ar = await ac.ExecuteReaderAsync(ct);
    while (await ar.ReadAsync(ct))
        accessCsv.AppendLine(string.Join(",",
            ((DateTime)ar.GetValue(0)).ToString("yyyy-MM-dd HH:mm:ss"),
            ExportEscape(ar.GetString(1)), ar.GetString(2),
            ExportEscape(ar.IsDBNull(3) ? "" : ar.GetString(3)),
            ExportEscape(ar.IsDBNull(4) ? "" : ar.GetString(4)),
            ar.GetString(5), ar.GetString(6),
            ExportEscape(ar.IsDBNull(7) ? "" : ar.GetString(7))));

    // Monta ZIP em memória
    using var zipStream = new System.IO.MemoryStream();
    using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create, true))
    {
        var ticketEntry = archive.CreateEntry($"ingressos_{eventId:N}.csv");
        await using var tw = ticketEntry.Open();
        await tw.WriteAsync(System.Text.Encoding.UTF8.GetBytes(ticketCsv.ToString()), ct);

        var accessEntry = archive.CreateEntry($"acessos_{eventId:N}.csv");
        await using var aw = accessEntry.Open();
        await aw.WriteAsync(System.Text.Encoding.UTF8.GetBytes(accessCsv.ToString()), ct);
    }

    var fileName = $"export_{eventId:N}_{DateTime.UtcNow:yyyyMMdd_HHmm}.zip";
    return Results.File(zipStream.ToArray(), "application/zip", fileName);
});

// ══════════════════════════════════════════════════════════════════════════════
// Relatórios analíticos  (requer reports.view)
// ══════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/events/{eventId:guid}/reports/validation", async (
    Guid eventId,
    DateTimeOffset? from, DateTimeOffset? to,
    Guid? gateId, Guid? sectorId, string? direction, string? timeZone,
    IValidationReportService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("relatorio.ler") is { } e) return e;
    try
    {
        var filter = new ValidationReportFilter(from, to, gateId, sectorId, direction, timeZone);
        return Results.Ok(await svc.GetValidationReportAsync(eventId, filter, ct));
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// Export CSV das tentativas
app.MapGet("/api/events/{eventId:guid}/reports/validation/export", async (
    Guid eventId,
    DateTimeOffset? from, DateTimeOffset? to,
    Guid? gateId, Guid? sectorId, string? direction,
    IAccessAttemptQueryService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("relatorio.ler") is { } e) return e;
    try
    {
        var filter = new AccessAttemptAuditFilter(from, to, gateId, null, null, null,
            null, direction, null, null, 1, 10000, sectorId);
        var page = await svc.ListAsync(eventId, filter, ct);
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("AttemptId,Horario,Credencial,ExternalId,Portaria,Setor,Direcao,Decisao,Motivo");
        foreach (var a in page.Data)
        {
            csv.AppendLine(string.Join(",",
                a.AttemptId, EscapeCsv(a.RequestedAt.ToString("yyyy-MM-dd HH:mm:ss")),
                EscapeCsv(a.CredentialCodeMasked), EscapeCsv(a.TicketExternalId ?? ""),
                EscapeCsv(a.GateName ?? ""), EscapeCsv(a.SectorName ?? ""),
                a.Direction, a.Decision, EscapeCsv(a.Reason ?? "")));
        }
        return Results.File(
            System.Text.Encoding.UTF8.GetBytes(csv.ToString()),
            "text/csv; charset=utf-8",
            $"relatorio_validacao_{eventId:N}_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ══════════════════════════════════════════════════════════════════════════════
// Fase 5 — Tickets em lote  (requer tickets.status)
// ══════════════════════════════════════════════════════════════════════════════

// Alterar status de tickets em lote
app.MapPut("/api/events/{eventId:guid}/tickets/bulk-status", async (
    Guid eventId,
    BulkTicketStatusCommand command,
    ICatalogService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("ticket.status") is { } e) return e;
    try { return Results.Ok(await svc.BulkUpdateTicketStatusAsync(eventId, command.TicketIds, command.Status, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// Histórico individual de um ingresso
app.MapGet("/api/events/{eventId:guid}/tickets/{ticketId:guid}/history", async (
    Guid eventId, Guid ticketId,
    IAccessAttemptQueryService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (ctx.RequirePermission("ticket.consultar") is { } e) return e;
    try
    {
        var filter = new AccessAttemptAuditFilter(null, null, null, null, ticketId,
            null, null, null, null, null, 1, 200, null);
        var result = await svc.ListAsync(eventId, filter, ct);
        return Results.Ok(result);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ══════════════════════════════════════════════════════════════════════════════
// Fase 5 — Reset de log  (requer acessos.resetar)
// ══════════════════════════════════════════════════════════════════════════════

app.MapDelete("/api/events/{eventId:guid}/access-attempts/reset", async (
    Guid eventId, HttpContext ctx, CancellationToken ct,
    FastPassDbConnectionFactory dbFactory) =>
{
    if (ctx.RequirePermission("acessos.resetar") is { } e) return e;
    try
    {
        await using var conn = dbFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        // Zera contadores dos tickets e remove log de tentativas
        await using var c1 = conn.CreateCommand();
        c1.Transaction = tx;
        c1.CommandText = "UPDATE fp_tickets SET entries_used=0,uses=0,people_inside=0,updated_at=@now WHERE event_id=@eid;";
        c1.Parameters.AddWithValue("@now", DateTime.UtcNow);
        c1.Parameters.AddWithValue("@eid", eventId.ToString());
        await c1.ExecuteNonQueryAsync(ct);

        await using var c2 = conn.CreateCommand();
        c2.Transaction = tx;
        c2.CommandText = "DELETE FROM fp_access_attempts WHERE event_id=@eid;";
        c2.Parameters.AddWithValue("@eid", eventId.ToString());
        var deleted = await c2.ExecuteNonQueryAsync(ct);

        await tx.CommitAsync(ct);
        return Results.Ok(new { message = $"Log resetado. {deleted} tentativa(s) removida(s). Contadores zerados." });
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ══════════════════════════════════════════════════════════════════════════════
// Fase 5 — Administração avançada  (requer ticket.dados.excluir)
// ══════════════════════════════════════════════════════════════════════════════

// Resumo antes de excluir (prévia)
app.MapGet("/api/events/{eventId:guid}/admin/data-summary", async (
    Guid eventId, HttpContext ctx, CancellationToken ct,
    FastPassDbConnectionFactory dbFactory) =>
{
    if (ctx.RequirePermission("ticket.dados.excluir") is { } e) return e;
    await using var conn = dbFactory.Create();
    await conn.OpenAsync(ct);
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        SELECT
            (SELECT COUNT(*) FROM fp_tickets WHERE event_id=@eid) tickets,
            (SELECT COUNT(*) FROM fp_access_attempts WHERE event_id=@eid) attempts,
            (SELECT COUNT(*) FROM fp_ticket_imports WHERE event_id=@eid) imports,
            (SELECT name FROM fp_events WHERE id=@eid LIMIT 1) event_name;
        """;
    cmd.Parameters.AddWithValue("@eid", eventId.ToString());
    await using var r = await cmd.ExecuteReaderAsync(ct);
    if (!await r.ReadAsync(ct)) return Results.NotFound(new { error = "Evento não encontrado." });
    return Results.Ok(new
    {
        eventName = r.IsDBNull(3) ? null : r.GetString(3),
        tickets = Convert.ToInt32(r.GetValue(0)),
        attempts = Convert.ToInt32(r.GetValue(1)),
        imports = Convert.ToInt32(r.GetValue(2))
    });
});

// Exclusão irreversível — exige confirmação no body
app.MapPost("/api/events/{eventId:guid}/admin/delete-data", async (
    Guid eventId, DeleteEventDataCommand command,
    HttpContext ctx, CancellationToken ct,
    FastPassDbConnectionFactory dbFactory) =>
{
    if (ctx.RequirePermission("ticket.dados.excluir") is { } e) return e;
    if (string.IsNullOrWhiteSpace(command.Confirmation) ||
        !command.Confirmation.Equals($"EXCLUIR {eventId}", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = $"Confirmação inválida. Digite: EXCLUIR {eventId}" });

    try
    {
        await using var conn = dbFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        async Task Exec(string sql)
        {
            await using var c = conn.CreateCommand();
            c.Transaction = tx; c.CommandText = sql;
            c.Parameters.AddWithValue("@eid", eventId.ToString());
            await c.ExecuteNonQueryAsync(ct);
        }

        await Exec("DELETE FROM fp_access_attempts WHERE event_id=@eid;");
        await Exec("DELETE FROM fp_ticket_import_rows WHERE import_id IN (SELECT id FROM fp_ticket_imports WHERE event_id=@eid);");
        await Exec("DELETE FROM fp_ticket_imports WHERE event_id=@eid;");
        await Exec("DELETE FROM fp_access_policies WHERE event_id=@eid;");
        await Exec("DELETE FROM fp_gate_sectors WHERE event_id=@eid;");
        await Exec("DELETE FROM fp_access_messages WHERE event_id=@eid;");
        await Exec("DELETE FROM fp_tickets WHERE event_id=@eid;");
        await Exec("DELETE FROM fp_ticket_batches WHERE event_id=@eid;");
        await Exec("DELETE FROM fp_ticket_types WHERE event_id=@eid;");

        await tx.CommitAsync(ct);
        return Results.Ok(new { message = "Dados do evento excluídos permanentemente." });
    }
    catch (Exception ex) { return Results.Problem(detail: ex.Message, statusCode: 500); }
});

app.Run();

// ── Helpers inline ────────────────────────────────────────────────────────────
static string EscapeCsv(string value) =>
    value.Contains(',') || value.Contains('"') || value.Contains('\n')
        ? $"\"{value.Replace("\"", "\"\"")}\""
        : value;

static string ExportEscape(string value) =>
    value.Contains(',') || value.Contains('"') || value.Contains('\n')
        ? $"\"{value.Replace("\"", "\"\"")}\""
        : value;

// Contratos usados nos novos endpoints
public sealed record BulkTicketStatusCommand(
    IReadOnlyList<Guid> TicketIds,
    string Status);

public sealed record TicketStatusCommand(string Status);

public sealed record ResetPasswordCommand(string NewPassword);

public sealed record DeleteEventDataCommand(string Confirmation);

