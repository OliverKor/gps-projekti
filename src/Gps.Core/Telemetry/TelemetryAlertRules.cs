namespace Gps.Core.Telemetry;

public sealed record TelemetryAlertRules(
    double? SpeedLimitMps = null,
    double? GeofenceCenterLat = null,
    double? GeofenceCenterLon = null,
    double? GeofenceRadiusM = null
);
