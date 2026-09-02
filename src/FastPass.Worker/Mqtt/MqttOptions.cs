namespace FastPass.Worker.Mqtt;

/// <summary>
/// Configuração da conexão MQTT com as catracas (seção "Mqtt" do appsettings).
/// </summary>
public sealed class MqttOptions
{
    public const string SectionName = "Mqtt";

    /// <summary>Liga/desliga o serviço MQTT sem remover a config.</summary>
    public bool Enabled { get; set; } = true;

    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 1883;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string ClientId { get; set; } = "fastpass-worker";

    /// <summary>Identificador da instalação configurado nas placas (Facility ID).</summary>
    public string FacilityId { get; set; } = "FastPass";

    /// <summary>
    /// Prefixo dos tópicos. Por padrão igual ao FacilityId, formando
    /// "&lt;TopicPrefix&gt;/&lt;DeviceId&gt;/from/..." e ".../to/...".
    /// </summary>
    public string TopicPrefix { get; set; } = "FastPass";

    public int ReconnectDelaySeconds { get; set; } = 5;
}
