using FastPass.Application.Auth;
using FastPass.Infrastructure.Auth;

namespace FastPass.Api.Auth;

/// <summary>
/// Middleware que lê o cookie de sessão, valida no banco e injeta
/// o SessionView no HttpContext.Items["Session"].
/// Rotas públicas (login, health, raiz) não passam por este middleware.
/// </summary>
public sealed class SessionMiddleware
{
    public const string SessionKey = "fp_session";
    public const string CookieName = "fp_token";

    private readonly RequestDelegate _next;

    public SessionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IServiceScopeFactory scopeFactory)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Rotas que não precisam de sessão
        if (IsPublicRoute(path))
        {
            await _next(context);
            return;
        }

        var token = context.Request.Cookies[CookieName];
        if (string.IsNullOrWhiteSpace(token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Autenticação necessária." });
            return;
        }

        var tokenHash = MySqlAuthService.HashToken(token);

        SessionView? session;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
            session = await authService.GetSessionAsync(tokenHash, context.RequestAborted);
        }

        if (session is null)
        {
            context.Response.Cookies.Delete(CookieName);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Sessão inválida ou expirada." });
            return;
        }

        context.Items[SessionKey] = session;
        await _next(context);
    }

    private static bool IsPublicRoute(string path) =>
        path == "/" ||
        path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Extensões para acessar a sessão e verificar permissões de forma concisa.
/// </summary>
public static class SessionExtensions
{
    public static SessionView? GetSession(this HttpContext context) =>
        context.Items[SessionMiddleware.SessionKey] as SessionView;

    public static SessionView RequireSession(this HttpContext context)
    {
        var session = context.GetSession()
            ?? throw new InvalidOperationException("Sessão não encontrada no contexto.");
        return session;
    }

    public static bool HasPermission(this HttpContext context, string permission)
    {
        var session = context.GetSession();
        return session is not null && session.Permissions.Contains(permission);
    }

    public static IResult? RequirePermission(this HttpContext context, string permission)
    {
        if (!context.HasPermission(permission))
        {
            return Results.Json(new { error = $"Permissão '{permission}' necessária." },
                statusCode: StatusCodes.Status403Forbidden);
        }
        return null;
    }

    public static void SetSessionCookie(this HttpContext context, string token, bool rememberMe = false)
    {
        context.Response.Cookies.Append(
            SessionMiddleware.CookieName,
            token,
            new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
                Expires = rememberMe ? DateTimeOffset.UtcNow.AddDays(30) : null,
                Path = "/"
            });
    }

    public static void ClearSessionCookie(this HttpContext context) =>
        context.Response.Cookies.Delete(SessionMiddleware.CookieName);
}
