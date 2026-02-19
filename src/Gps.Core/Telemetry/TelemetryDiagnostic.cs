namespace Gps.Core.Telemetry;

public sealed record TelemetryDiagnostic(
    double FixRateHz,
    double LastFixAgeSec,
    double NoFixSeconds
);
