using System.Text.Json;
using FastPass.Application.Access;

namespace FastPass.Worker.Mqtt;

/// <summary>Resultado do parse de uma mensagem publicada pela placa.</summary>
public enum TurnstileInboundKind
{
    /// <summary>Telemetria (status/keepalive/info) — só atualiza heartbeat.</summary>
    Telemetry,
    /// <summary>Leitura de credencial (QR/cartão) — precisa validar e responder.</summary>
    CredentialRead,
    /// <summary>Não reconhecido — logar para engenharia reversa.</summary>
    Unknown,
}

/// <summary>Dados extraídos de uma leitura de credencial.</summary>
public sealed record TurnstileRead(string CredentialCode, string? RawReader);

/// <summary>
/// Metadados de telemetria da placa. Todos opcionais — o keepalive traz pouco,
/// o "info" traz firmware/serial/IP/mídia. Status vem do verbo/campo status.
/// </summary>
public sealed record TurnstileTelemetry(
    string? Status,
    string? Firmware,
    string? BoardId,
    string? SerialId,
    string? IpLocal,
    string? Media);

/// <summary>
/// Ponto ÚNICO de tradução do protocolo da catraca ⇆ FastPass.
///
/// Aqui ficam as duas funções que dependem do firmware da placa:
///   • Decode: interpretar o payload publicado pela placa (o que é telemetria,
///     o que é leitura de credencial e como extrair o código lido).
///   • Encode: montar o payload de comando (liberar/negar + sentido + display).
///
/// O que já é CONHECIDO (por captura de tráfego da Neon, firmware 1.0.35):
///   - Payloads são JSON com um campo "cmd".
///   - Telemetria observada: cmd ∈ { "status", "keepalive", "info" }.
///
/// O que ainda é A CONFIRMAR com a documentação da Neon 1.2:
///   - Qual "cmd"/verbo chega quando a placa lê um QR/cartão e em qual campo vem o código.
///   - Qual o formato do comando de liberação/negação que a placa espera no tópico "to".
/// Enquanto não confirmado, o Decode reconhece a telemetria conhecida e tenta
/// heurísticas para a leitura; o Encode produz um JSON provisório e legível.
/// </summary>
public sealed class TurnstileMessageCodec
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // Verbos de telemetria já observados na captura.
    private static readonly HashSet<string> TelemetryCmds =
        new(StringComparer.OrdinalIgnoreCase) { "status", "keepalive", "info" };

    /// <summary>
    /// Interpreta uma mensagem "from". Retorna o tipo e, conforme o caso, os dados
    /// da leitura (read) ou os metadados de telemetria (telemetry).
    /// </summary>
    public TurnstileInboundKind Decode(
        string verb, string payload, out TurnstileRead? read, out TurnstileTelemetry? telemetry)
    {
        read = null;
        telemetry = null;

        // Tenta interpretar o corpo JSON (a telemetria da placa é sempre JSON).
        JsonElement root;
        var isJsonObject = false;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            root = doc.RootElement.Clone();
            isJsonObject = root.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            root = default;
        }

        // Telemetria pelo verbo do tópico (…/from/keepalive|status|info).
        var telemetryByVerb = TelemetryCmds.Contains(verb);

        if (isJsonObject)
        {
            var cmd = root.TryGetProperty("cmd", out var cmdEl) && cmdEl.ValueKind == JsonValueKind.String
                ? cmdEl.GetString()
                : null;

            if (telemetryByVerb || (!string.IsNullOrEmpty(cmd) && TelemetryCmds.Contains(cmd)))
            {
                telemetry = ExtractTelemetry(root);
                return TurnstileInboundKind.Telemetry;
            }

            // TODO(Neon 1.2): confirmar o "cmd" de leitura e o nome do campo do código.
            // Heurística: procura o código em campos comuns.
            var code = FirstString(root, "code", "qrcode", "qr", "card", "cardnumber", "credential", "data", "value");
            if (!string.IsNullOrWhiteSpace(code))
            {
                read = new TurnstileRead(code!.Trim(), cmd);
                return TurnstileInboundKind.CredentialRead;
            }

            return TurnstileInboundKind.Unknown;
        }

        // Corpo não-JSON.
        if (telemetryByVerb)
        {
            telemetry = new TurnstileTelemetry(null, null, null, null, null, null);
            return TurnstileInboundKind.Telemetry;
        }

        // Pode ser o código cru terminado em <CR> vindo da UART.
        var raw = payload.Trim().Trim('\r', '\n');
        if (!string.IsNullOrEmpty(raw))
        {
            read = new TurnstileRead(raw, verb);
            return TurnstileInboundKind.CredentialRead;
        }

        return TurnstileInboundKind.Unknown;
    }

    private static TurnstileTelemetry ExtractTelemetry(JsonElement root) => new(
        Status: FirstString(root, "status"),
        Firmware: FirstString(root, "version", "firmware"),
        BoardId: FirstString(root, "boardid", "board_id"),
        SerialId: FirstString(root, "serialid", "serial_id", "serial"),
        IpLocal: FirstString(root, "iplocal", "ip_local", "ip"),
        Media: FirstString(root, "media"));

    /// <summary>
    /// Monta o comando de resposta para a placa a partir do resultado da validação.
    /// Retorna (verbo, payload) — o verbo compõe o tópico "…/to/&lt;verbo&gt;".
    ///
    /// TODO(Neon 1.2): ajustar verbo e formato conforme a documentação. O formato
    /// abaixo é provisório, legível e reflete a DECISÃO já tomada pelo FastPass
    /// (liberar/negar, sentido, pictograma e mensagem de display).
    /// </summary>
    public (string Verb, string Payload) EncodeCommand(AccessValidationResult result)
    {
        var release = string.Equals(result.ArmAction, "Unlock", StringComparison.OrdinalIgnoreCase);
        
        // Formata a mensagem para o display LCD: máximo 32 caracteres (2 linhas × 16 chars).
        // Se a mensagem tiver quebra de linha (\n), respeita. Caso contrário, quebra no meio.
        var displayMessage = FormatDisplayMessage(result.Message);
        
        var command = new
        {
            cmd = "access",
            authorized = result.Approved,
            release,                              // aciona o relé LOCK quando true
            direction = result.Direction,          // Entry | Exit — sentido a liberar
            pictogram = result.Pictogram,          // GreenArrowEntry | GreenArrowExit | RedCross
            reasonCode = result.ReasonCode,
            message = displayMessage,              // texto formatado para o display (até 32 chars)
            attemptId = result.AttemptId,
        };
        return ("access", JsonSerializer.Serialize(command, JsonOpts));
    }

    /// <summary>
    /// Formata a mensagem para o display LCD da catraca (2 linhas × 16 caracteres).
    /// Se houver quebra de linha (\n), usa como está. Caso contrário, quebra no máximo 32 chars.
    /// </summary>
    private static string FormatDisplayMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        var msg = message.Trim();
        
        // Trunca no máximo 32 caracteres (2 linhas × 16)
        if (msg.Length > 32)
            msg = msg[..32];

        // Se tiver quebra de linha, usa como está
        if (msg.Contains('\n'))
            return msg;

        // Se não tiver quebra de linha e couber em uma linha, deixa em uma
        if (msg.Length <= 16)
            return msg;

        // Quebra no meio para 2 linhas de 16 caracteres
        // Tenta quebrar em um espaço para não cortar palavra
        var midpoint = 16;
        var lastSpace = msg[..16].LastIndexOf(' ');
        if (lastSpace > 0)
            midpoint = lastSpace;

        var line1 = msg[..midpoint].Trim();
        var line2 = msg[midpoint..].Trim();

        return $"{line1}\n{line2}";
    }

    private static string? FirstString(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var el))
            {
                if (el.ValueKind == JsonValueKind.String) return el.GetString();
                if (el.ValueKind == JsonValueKind.Number) return el.GetRawText();
            }
        }
        return null;
    }
}
