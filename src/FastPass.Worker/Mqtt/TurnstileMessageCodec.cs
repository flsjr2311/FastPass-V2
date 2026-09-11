using System.Text.Json;
using FastPass.Application.Access;

namespace FastPass.Worker.Mqtt;

/// <summary>Resultado do parse de uma mensagem publicada pela placa.</summary>
/// <summary>Resposta de configuração ACCMODE.</summary>
public record TurnstileAccmodeResponse(string? Accmode);

public enum TurnstileInboundKind
{
    /// <summary>Telemetria (status/keepalive/info) — só atualiza heartbeat.</summary>
    Telemetry,
    /// <summary>Leitura de credencial (QR/cartão) — precisa validar e responder.</summary>
    CredentialRead,
    /// <summary>Resposta de configuração ACCMODE (getconfig_ack) — deve verificar e eventualmente enviar setconfig.</summary>
    AccmodeResponse,
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

    // Mapa de mensagens para template IDs (baseado em migração 034)
    private static readonly Dictionary<string, int> MessageToTemplateId = new(StringComparer.OrdinalIgnoreCase)
    {
        { "PASSE SEU\nINGRESSO", 5 },      // Active mode
        { "LIBERADA\nENTRE", 2 },          // Free mode
        { "BLOQUEADA\nCADEADO", 4 },       // Blocked mode
    };

    /// <summary>
    /// Interpreta uma mensagem "from". Retorna o tipo e, conforme o caso, os dados
    /// da leitura (read) ou os metadados de telemetria (telemetry).
    /// </summary>
    public TurnstileInboundKind Decode(
        string verb, string payload, out TurnstileRead? read, out TurnstileTelemetry? telemetry, out TurnstileAccmodeResponse? accmodeResponse)
    {
        read = null;
        telemetry = null;
        accmodeResponse = null;

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

            // Handler para getconfig_ack (resposta de ACCMODE)
            if (cmd == "getconfig_ack")
            {
                var file = FirstString(root, "file");
                if (file == "/config_accmode.json")
                {
                    // Extrair o valor de accmode do objeto data
                    var accmode = (string?)null;
                    if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
                    {
                        accmode = FirstString(dataEl, "accmode");
                    }
                    accmodeResponse = new TurnstileAccmodeResponse(accmode);
                    return TurnstileInboundKind.AccmodeResponse;
                }
            }

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
        // Formato Neon 1.3: cmd="acc_req_auth" 
        // Tópico: ${facilityid}/${alias}/to/access
        var lines = SplitMessageForDisplay(result.Message);
        
        // Status mapping para Neon 1.3:
        var sts = result.Decision switch
        {
            "Approved" => 1,      // Autorizar
            "Rejected" => 0,      // Negar
            "Pending" => 1        // Pending/Active mode
        };
        
        // Timestamp no formato YYMMDDHHMMSS
        var now = DateTime.UtcNow;
        var timestamp = now.ToString("yyMMddHHmmss");
        
        // Sentido da liberação: "1" entrada, "2" saída, "0" ambos
        var channelaux = result.Direction switch
        {
            "Entry" => "1",
            "Exit" => "2",
            _ => "0"
        };

        // Tentar mapear a mensagem para um template ID conhecido
        var templateId = MessageToTemplateId.TryGetValue(result.Message, out var tid) ? tid : (int?)null;
        
        // Enviar com template_id se conseguir mapear
        object command;
        if (templateId.HasValue)
        {
            command = new
            {
                cmd = "acc_req_auth",
                timestamp = timestamp,
                msgid = result.AttemptId.GetHashCode() & 0x0000FFFF,
                sts = sts,
                channel = 1,
                channelaux = channelaux,
                template_id = templateId.Value
            };
        }
        else
        {
            // Fallback: enviar texto
            command = new
            {
                cmd = "acc_req_auth",
                timestamp = timestamp,
                msgid = result.AttemptId.GetHashCode() & 0x0000FFFF,
                sts = sts,
                channel = 1,
                channelaux = channelaux,
                disp1 = lines.line1,
                disp2 = lines.line2
            };
        }
        
        return ("access", JsonSerializer.Serialize(command, JsonOpts));
    }

    /// <summary>
    /// Divide a mensagem em 2 linhas de 16 caracteres cada para o display LCD.
    /// Entrada: "LIBERADA\nENTRE" → Saída: ("LIBERADA", "ENTRE")
    /// </summary>
    private static (string line1, string line2) SplitMessageForDisplay(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return ("", "");

        var msg = message.Trim();
        
        // Se tem quebra de linha, usa
        if (msg.Contains('\n'))
        {
            var parts = msg.Split('\n', 2);
            var line1 = (parts[0] ?? "").Trim();
            var line2 = (parts.Length > 1 ? parts[1] : "").Trim();
            
            // Trunca em 16 chars cada
            line1 = line1.Length > 16 ? line1[..16] : line1;
            line2 = line2.Length > 16 ? line2[..16] : line2;
            
            return (line1, line2);
        }

        // Se não tem quebra, tenta quebrar no meio
        if (msg.Length <= 16)
            return (msg, "");

        // Trunca no máximo 32 chars total
        if (msg.Length > 32)
            msg = msg[..32];

        var midpoint = 16;
        var lastSpace = msg[..16].LastIndexOf(' ');
        if (lastSpace > 0)
            midpoint = lastSpace;

        var l1 = msg[..midpoint].Trim();
        var l2 = msg[midpoint..].Trim();

        return (l1, l2);
    }

    /// <summary>
    /// Monta o comando getconfig para enviar a lista de templates de mensagens para a catraca.
    /// Neon 1.3: cmd="setconfig", com array de templates ID 00-30.
    /// Cada template tem 2 linhas de 16 caracteres max.
    /// 
    /// Retorna (verbo, payload) — o verbo compõe o tópico "…/to/&lt;verbo&gt;".
    /// </summary>
    public (string Verb, string Payload) EncodeGetAccmodeCommand()
    {
        // Solicitar o arquivo de configuração ACCMODE via getconfig
        var now = DateTime.UtcNow;
        var timestamp = now.ToString("yyMMddHHmmss");
        
        var command = new
        {
            cmd = "getconfig",
            msgid = new Random().Next(0, 60000),
            file = "/config_accmode.json",
            timestamp = timestamp
        };
        
        return ("config", JsonSerializer.Serialize(command, JsonOpts));
    }

    public (string Verb, string Payload) EncodeSetAccmodeCommand(string accmode = "11")
    {
        // Enviar configuração ACCMODE via setconfig para forçar modo "11"
        // "11" = Catraca telecomando controle giro (mode de automação)
        var now = DateTime.UtcNow;
        var timestamp = now.ToString("yyMMddHHmmss");
        
        var command = new
        {
            cmd = "setconfig",
            msgid = new Random().Next(0, 60000),
            file = "/config_accmode.json",
            timestamp = timestamp,
            data = new
            {
                accmode = accmode
            }
        };
        
        return ("config", JsonSerializer.Serialize(command, JsonOpts));
    }

    public (string Verb, string Payload) EncodeGetConfigCommand(Dictionary<int, (string Line1, string Line2)> templates)
    {
        // Timestamp no formato YYMMDDHHMMSS
        var now = DateTime.UtcNow;
        var timestamp = now.ToString("yyMMddHHmmss");
        
        // Montar array com os templates (00-30)
        var templateArray = new List<object>();
        for (int i = 0; i <= 30; i++)
        {
            if (templates.TryGetValue(i, out var template))
            {
                // Garantir que não exceda 16 chars por linha
                var line1 = template.Line1.Length > 16 ? template.Line1[..16] : template.Line1;
                var line2 = template.Line2.Length > 16 ? template.Line2[..16] : template.Line2;
                
                templateArray.Add(new
                {
                    id = i,
                    line1 = line1,
                    line2 = line2
                });
            }
            else
            {
                // Se não houver template para este ID, enviar vazio
                templateArray.Add(new
                {
                    id = i,
                    line1 = "",
                    line2 = ""
                });
            }
        }
        
        var command = new
        {
            cmd = "setconfig",
            timestamp = timestamp,
            msgid = new Random().Next(0, 60000),
            templates = templateArray
        };
        
        return ("config", JsonSerializer.Serialize(command, JsonOpts));
    }

    /// <summary>
    /// Monta comando para configurar as mensagens de display da catraca
    /// via /config_msgs.json (Neon 1.3 seção 3.23.8)
    /// </summary>
    public (string Verb, string Payload) EncodeSetMsgsCommand()
    {
        var now = DateTime.UtcNow;
        var timestamp = now.ToString("yyMMddHHmmss");
        
        // Usar Dictionary para chaves numéricas string
        var msgData = new Dictionary<string, string>
        {
            { "00", "PASSE SEU" },        // Acesso solicitado
            { "01", "LIBERADO" },         // Acesso autorizado
            { "02", "INVALIDO" },         // Identificador inválido
            { "05", "NEGADO" },           // Acesso negado pelo servidor
            { "30", "INGRESSO" }          // Apresente identificação
        };
        
        var command = new
        {
            cmd = "setconfig",
            msgid = new Random().Next(0, 60000),
            file = "/config_msgs.json",
            timestamp = timestamp,
            data = msgData
        };
        
        return ("config", JsonSerializer.Serialize(command, JsonOpts));
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
