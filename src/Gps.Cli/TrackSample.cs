namespace Gps.Cli;

internal readonly record struct TrackSample(
    DateTimeOffset Timestamp,
    double LatitudeDeg,
    double LongitudeDeg,
    double SpeedMps,
    int NumSv,
    string FixType
);
