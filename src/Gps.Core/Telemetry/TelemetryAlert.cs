namespace Gps.Core.Telemetry;

public sealed record TelemetryAlert(
    string Code,
    string Severity,
    string Message,
    double Value,
    DateTimeOffset TimestampUtc
);
