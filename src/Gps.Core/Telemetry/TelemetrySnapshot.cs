namespace Gps.Core.Telemetry;

public sealed record TelemetrySnapshot(
    double DistanceTotalM,
    double AverageSpeedMps,
    int FixCount,
    DateTimeOffset? LastFixTimestampUtc
);
