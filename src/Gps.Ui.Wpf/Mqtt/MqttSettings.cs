namespace Gps.Ui.Wpf.Mqtt;

public sealed record MqttSettings
{
    public bool Enabled { get; init; }
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 1883;
    public string BaseTopic { get; init; } = "gps/v1";
    public string DeviceId { get; init; } = "demo-truck-01";
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public int DiagIntervalSeconds { get; init; } = 5;
    public int QueueCapacity { get; init; } = 500;
    public int DrainTimeoutSeconds { get; init; } = 2;

    public MqttSettings Sanitize()
    {
        var host = string.IsNullOrWhiteSpace(Host) ? "localhost" : Host.Trim();
        var baseTopic = string.IsNullOrWhiteSpace(BaseTopic) ? "gps/v1" : BaseTopic.Trim().Trim('/');
        var deviceId = string.IsNullOrWhiteSpace(DeviceId) ? "demo-truck-01" : DeviceId.Trim();
        var port = Math.Clamp(Port, 1, 65535);
        var diagInterval = Math.Clamp(DiagIntervalSeconds, 1, 3600);
        var queueCapacity = Math.Clamp(QueueCapacity, 10, 10000);
        var drainTimeout = Math.Clamp(DrainTimeoutSeconds, 1, 30);

        return this with
        {
            Host = host,
            Port = port,
            BaseTopic = baseTopic,
            DeviceId = deviceId,
            DiagIntervalSeconds = diagInterval,
            QueueCapacity = queueCapacity,
            DrainTimeoutSeconds = drainTimeout,
            Username = Username?.Trim() ?? string.Empty,
            Password = Password ?? string.Empty
        };
    }

    public static MqttSettings DisabledDefaults => new()
    {
        Enabled = false
    };
}
