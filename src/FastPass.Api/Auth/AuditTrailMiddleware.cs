using System.Text;
using System.Text.Json;
using FastPass.Application.Audit;
using FastPass.Application.Auth;

namespace FastPass.Api.Auth;

/// <summary>
/// Captura, de forma centralizada, toda requisição que MUDA ESTADO
/// (POST/PUT/DELETE/PATCH) e conclui com sucesso (2xx), gravando na trilha
/// de auditoria (fp_audit_trail). Roda depois do SessionMiddleware para
/// ter acesso ao usuário autenticado.
///
/// A gravação é best-effort e nunca interfere na resposta ao cliente.
/// </summary>
public sealed class AuditTrailMiddleware
{
    private static readonly HashSet<string> MutatingMethods =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "DELETE", "PATCH" };

    // Campos sensíveis cujo valor é mascarado no resumo do corpo.
    private static readonly HashSet<string> SensitiveKeys =
        new(StringComparer.OrdinalIgnoreCase)
        { "password", "newPassword", "currentPassword", "senha", "novaSenha", "token", "secret" };

    private readonly RequestDelegate _next;

    public AuditTrailMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IServiceScopeFactory scopeFactory)
    {
        var method = context.Request.Method;
        var path = context.Request.Path.Value ?? string.Empty;

        // Só interessa mutações em rotas de API (login é tratado à parte, na trilha de login).
        if (!MutatingMethods.Contains(method) || !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Captura o corpo para o resumo, preservando-o para o handler.
        string? bodySummary = null;
        if (context.Request.ContentLength is > 0 &&
            (context.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            context.Request.EnableBuffering();
            bodySummary = await ReadAndSummarizeBodyAsync(context);
            context.Request.Body.Position = 0;
        }

        await _next(context);

        // Só registra sucessos (2xx). Erros de validação/permissão não sujam a trilha.
        if (context.Response.StatusCode is < 200 or >= 300) return;

        var session = context.GetSession();
        var record = new AuditTrailRecord(
            UserId: session?.UserId,
            UserName: session?.UserName,
            Action: DescribeAction(method, path),
            Method: method.ToUpperInvariant(),
            Path: path,
            TargetId: ExtractTargetId(path),
            StatusCode: context.Response.StatusCode,
            Summary: bodySummary,
            Ip: context.Connection.RemoteIpAddress?.ToString(),
            UserAgent: context.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null);

        // Best-effort e fora do caminho crítico da resposta.
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditTrailService>();
            await audit.RecordAsync(record, context.RequestAborted);
        }
        catch { /* nunca propaga */ }
    }

    private static async Task<string?> ReadAndSummarizeBodyAsync(HttpContext context)
    {
        try
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var raw = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // Tenta mascarar campos sensíveis; se não for JSON de objeto, guarda um trecho cru.
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var parts = new List<string>();
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        var value = SensitiveKeys.Contains(prop.Name)
                            ? "***"
                            : SummarizeValue(prop.Value);
                        parts.Add($"{prop.Name}={value}");
                    }
                    var joined = string.Join(", ", parts);
                    return joined.Length > 1000 ? joined[..1000] : joined;
                }
            }
            catch { /* não era JSON de objeto; cai no trecho cru abaixo */ }

            return raw.Length > 500 ? raw[..500] : raw;
        }
        catch { return null; }
    }

    private static string SummarizeValue(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => Shorten(e.GetString() ?? ""),
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        JsonValueKind.Array => $"[{e.GetArrayLength()} item(s)]",
        JsonValueKind.Object => "{...}",
        _ => "?",
    };

    private static string Shorten(string s) => s.Length > 60 ? s[..60] + "…" : s;

    /// <summary>Extrai o primeiro GUID/segmento identificador da rota, se houver.</summary>
    private static string? ExtractTargetId(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Procura um segmento que pareça um id (guid) — normalmente o alvo da ação.
        foreach (var seg in segments)
            if (Guid.TryParse(seg, out _)) return seg;
        return null;
    }

    /// <summary>Descreve a ação de forma legível a partir do método e da rota.</summary>
    private static string DescribeAction(string method, string path)
    {
        var p = path.ToLowerInvariant();
        var verb = method.ToUpperInvariant();

        // Mapeamentos específicos (mais legíveis) — checados por trechos da rota.
        if (p.Contains("/reset-password")) return "Redefiniu senha de usuário";
        if (p.Contains("/unblock")) return "Desbloqueou usuário";
        if (p.Contains("/auth/password")) return "Alterou a própria senha";
        if (p.Contains("/auth/logout")) return "Encerrou a sessão (logout)";
        if (p.Contains("/access-attempts/reset")) return "Zerou o log de acessos do evento";
        if (p.Contains("/access/manual")) return "Validou acesso manualmente";
        if (p.Contains("/tickets/import") || p.EndsWith("/import")) return "Importou ingressos";
        if (p.Contains("/status")) return "Alterou status de ingresso";
        if (p.Contains("/permissions")) return "Alterou permissões de perfil";
        if (p.Contains("/delete-data") || p.Contains("/admin/")) return "Executou ação administrativa no evento";

        // Genérico por recurso + verbo.
        var resource = ResourceName(p);
        return verb switch
        {
            "POST" => $"Criou {resource}",
            "PUT" or "PATCH" => $"Editou {resource}",
            "DELETE" => $"Excluiu {resource}",
            _ => $"{verb} {resource}",
        };
    }

    private static string ResourceName(string path)
    {
        // /api/{recurso}/... → nome amigável do recurso
        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var idx = Array.FindIndex(segs, s => s.Equals("api", StringComparison.OrdinalIgnoreCase));
        var resource = (idx >= 0 && segs.Length > idx + 1) ? segs[idx + 1] : (segs.Length > 0 ? segs[^1] : "recurso");
        return resource switch
        {
            "users" => "usuário",
            "roles" => "perfil",
            "events" => "evento",
            "gates" => "portaria",
            "sectors" => "setor",
            "clients" => "cliente",
            "devices" => "dispositivo",
            "tickets" => "ingresso",
            "gate-sectors" => "relação portaria/setor",
            "access-messages" => "mensagem de acesso",
            "access-policies" => "regra de acesso",
            _ => resource,
        };
    }
}
