namespace Gps.Core;

public sealed record Fix(
    DateTimeOffset Timestamp,
    double LatitudeDeg,
    double LongitudeDeg,
    double? SpeedMps = null,
    int? NumSv = null,
    string? FixType = null,
    double? LatitudeMeters = null,
    double? LongitudeMeters = null
);
