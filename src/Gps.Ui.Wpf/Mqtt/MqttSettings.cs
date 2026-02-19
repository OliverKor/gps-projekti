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
    public double? SpeedLimitMps { get; init; }
    public double? GeofenceCenterLat { get; init; }
    public double? GeofenceCenterLon { get; init; }
    public double? GeofenceRadiusM { get; init; }

    public MqttSettings Sanitize()
    {
        var host = string.IsNullOrWhiteSpace(Host) ? "localhost" : Host.Trim();
        var baseTopic = string.IsNullOrWhiteSpace(BaseTopic) ? "gps/v1" : BaseTopic.Trim().Trim('/');
        var deviceId = string.IsNullOrWhiteSpace(DeviceId) ? "demo-truck-01" : DeviceId.Trim();
        var port = Math.Clamp(Port, 1, 65535);
        var diagInterval = Math.Clamp(DiagIntervalSeconds, 1, 3600);
        var queueCapacity = Math.Clamp(QueueCapacity, 10, 10000);
        var drainTimeout = Math.Clamp(DrainTimeoutSeconds, 1, 30);
        var speedLimit = SpeedLimitMps.HasValue && SpeedLimitMps.Value > 0 ? SpeedLimitMps : null;
        var geofenceLat = IsLatitude(GeofenceCenterLat) ? GeofenceCenterLat : null;
        var geofenceLon = IsLongitude(GeofenceCenterLon) ? GeofenceCenterLon : null;
        var geofenceRadius = GeofenceRadiusM.HasValue && GeofenceRadiusM.Value > 0 ? GeofenceRadiusM : null;

        if (!geofenceLat.HasValue || !geofenceLon.HasValue || !geofenceRadius.HasValue)
        {
            geofenceLat = null;
            geofenceLon = null;
            geofenceRadius = null;
        }

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
            Password = Password ?? string.Empty,
            SpeedLimitMps = speedLimit,
            GeofenceCenterLat = geofenceLat,
            GeofenceCenterLon = geofenceLon,
            GeofenceRadiusM = geofenceRadius
        };
    }

    private static bool IsLatitude(double? value)
    {
        return value.HasValue && value.Value >= -90.0 && value.Value <= 90.0;
    }

    private static bool IsLongitude(double? value)
    {
        return value.HasValue && value.Value >= -180.0 && value.Value <= 180.0;
    }

    public static MqttSettings DisabledDefaults => new()
    {
        Enabled = false
    };
}
