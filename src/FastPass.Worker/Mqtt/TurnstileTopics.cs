namespace FastPass.Worker.Mqtt;

/// <summary>
/// Estrutura de tópicos das catracas (descoberta por captura de tráfego):
///   "&lt;Prefix&gt;/&lt;DeviceId&gt;/from/&lt;verbo&gt;"  → placa publica para o servidor
///   "&lt;Prefix&gt;/&lt;DeviceId&gt;/to/&lt;verbo&gt;"    → servidor publica para a placa
///
/// Ex.: "FastPass/Catraca 151/from/keepalive".
///
/// NOTA: os nomes de verbo (keepalive/status/info e o verbo de leitura/comando)
/// dependem do protocolo do firmware. O verbo de LEITURA e o de COMANDO ainda
/// serão confirmados com a documentação da Neon 1.2 — ver TurnstileMessageCodec.
/// </summary>
public sealed class TurnstileTopics
{
    private readonly string _prefix;

    public TurnstileTopics(string prefix)
    {
        _prefix = prefix.TrimEnd('/');
    }

    /// <summary>Filtro para assinar tudo que qualquer placa publica: "&lt;Prefix&gt;/+/from/#".</summary>
    public string SubscribeFromAll => $"{_prefix}/+/from/#";

    /// <summary>Monta um tópico de comando para uma placa específica.</summary>
    public string To(string deviceId, string verb) => 
        string.IsNullOrEmpty(verb) ? $"{_prefix}/{deviceId}/to" : $"{_prefix}/{deviceId}/to/{verb}";

    /// <summary>
    /// Extrai (deviceId, verb) de um tópico "from". Retorna false se o tópico
    /// não casar com o padrão esperado.
    /// </summary>
    public bool TryParseFrom(string topic, out string deviceId, out string verb)
    {
        deviceId = string.Empty;
        verb = string.Empty;
        if (string.IsNullOrEmpty(topic)) return false;

        // <prefix>/<deviceId>/from/<verb...>
        if (!topic.StartsWith(_prefix + "/", StringComparison.Ordinal)) return false;
        var rest = topic.Substring(_prefix.Length + 1);

        var fromIdx = rest.IndexOf("/from/", StringComparison.Ordinal);
        if (fromIdx < 0) return false;

        deviceId = rest.Substring(0, fromIdx);
        verb = rest.Substring(fromIdx + "/from/".Length);
        return deviceId.Length > 0 && verb.Length > 0;
    }
}
