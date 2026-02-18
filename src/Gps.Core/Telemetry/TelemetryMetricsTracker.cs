namespace Gps.Core.Telemetry;

public sealed class TelemetryMetricsTracker
{
    private const double EarthRadiusMeters = 6_371_000.0;
    private const double SpeedJumpThresholdMps = 12.0;
    private static readonly TimeSpan SpeedJumpWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan NoFixWarningThreshold = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NoFixCriticalThreshold = TimeSpan.FromSeconds(30);

    private readonly DateTimeOffset _sessionStartUtc;
    private readonly TelemetryAlertRules? _alertRules;

    private Fix? _previousFix;
    private DateTimeOffset? _firstFixTimestampUtc;
    private DateTimeOffset? _lastFixTimestampUtc;

    private double _distanceTotalMeters;
    private int _fixCount;

    private double _speedSum;
    private int _speedSamples;

    private bool _noFixWarningRaised;
    private bool _noFixCriticalRaised;
    private bool? _wasSpeedLimitExceeded;
    private bool? _wasInsideGeofence;

    public TelemetryMetricsTracker(DateTimeOffset? sessionStartUtc = null, TelemetryAlertRules? alertRules = null)
    {
        _sessionStartUtc = (sessionStartUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        _alertRules = alertRules;
    }

    public TelemetrySnapshot RecordFix(Fix fix, ICollection<TelemetryAlert>? alerts = null)
    {
        ArgumentNullException.ThrowIfNull(fix);

        var utcTimestamp = fix.Timestamp.ToUniversalTime();

        if (_previousFix is not null)
        {
            _distanceTotalMeters += HaversineDistanceMeters(
                _previousFix.LatitudeDeg,
                _previousFix.LongitudeDeg,
                fix.LatitudeDeg,
                fix.LongitudeDeg);

            var speedJumpAlert = TryCreateSpeedJumpAlert(_previousFix, fix, utcTimestamp);
            if (speedJumpAlert is not null)
            {
                alerts?.Add(speedJumpAlert);
            }
        }

        var speedLimitAlert = TryCreateSpeedLimitTransitionAlert(fix, utcTimestamp);
        if (speedLimitAlert is not null)
        {
            alerts?.Add(speedLimitAlert);
        }

        var geofenceAlert = TryCreateGeofenceTransitionAlert(fix, utcTimestamp);
        if (geofenceAlert is not null)
        {
            alerts?.Add(geofenceAlert);
        }

        _previousFix = fix;
        _firstFixTimestampUtc ??= utcTimestamp;
        _lastFixTimestampUtc = utcTimestamp;
        _fixCount++;

        if (fix.SpeedMps.HasValue)
        {
            _speedSum += fix.SpeedMps.Value;
            _speedSamples++;
        }

        _noFixWarningRaised = false;
        _noFixCriticalRaised = false;

        return CreateSnapshot();
    }

    public TelemetrySnapshot GetSnapshot()
    {
        return CreateSnapshot();
    }

    public TelemetryDiagnostic CreateDiagnostic(DateTimeOffset nowUtc)
    {
        var now = nowUtc.ToUniversalTime();
        var noFixSeconds = GetNoFixDuration(now).TotalSeconds;

        return new TelemetryDiagnostic(
            CalculateFixRateHz(),
            noFixSeconds,
            noFixSeconds);
    }

    public IReadOnlyList<TelemetryAlert> EvaluateNoFixAlerts(DateTimeOffset nowUtc)
    {
        var now = nowUtc.ToUniversalTime();
        var noFixDuration = GetNoFixDuration(now);

        var alerts = new List<TelemetryAlert>(2);

        if (!_noFixWarningRaised && noFixDuration >= NoFixWarningThreshold)
        {
            _noFixWarningRaised = true;
            alerts.Add(new TelemetryAlert(
                "NO_FIX_10S",
                "warning",
                "No valid fix for 10 seconds.",
                noFixDuration.TotalSeconds,
                now));
        }

        if (!_noFixCriticalRaised && noFixDuration >= NoFixCriticalThreshold)
        {
            _noFixCriticalRaised = true;
            alerts.Add(new TelemetryAlert(
                "NO_FIX_30S",
                "critical",
                "No valid fix for 30 seconds.",
                noFixDuration.TotalSeconds,
                now));
        }

        return alerts;
    }

    private TelemetrySnapshot CreateSnapshot()
    {
        var averageSpeed = _speedSamples > 0 ? _speedSum / _speedSamples : 0.0;

        return new TelemetrySnapshot(
            _distanceTotalMeters,
            averageSpeed,
            _fixCount,
            _lastFixTimestampUtc);
    }

    private double CalculateFixRateHz()
    {
        if (_fixCount < 2 || !_firstFixTimestampUtc.HasValue || !_lastFixTimestampUtc.HasValue)
        {
            return 0.0;
        }

        var totalSeconds = (_lastFixTimestampUtc.Value - _firstFixTimestampUtc.Value).TotalSeconds;
        if (totalSeconds <= 0)
        {
            return 0.0;
        }

        return (_fixCount - 1) / totalSeconds;
    }

    private TimeSpan GetNoFixDuration(DateTimeOffset nowUtc)
    {
        var reference = _lastFixTimestampUtc ?? _sessionStartUtc;
        var duration = nowUtc - reference;
        return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
    }

    private static TelemetryAlert? TryCreateSpeedJumpAlert(Fix previousFix, Fix currentFix, DateTimeOffset currentTimestampUtc)
    {
        if (!previousFix.SpeedMps.HasValue || !currentFix.SpeedMps.HasValue)
        {
            return null;
        }

        var deltaTime = currentFix.Timestamp.ToUniversalTime() - previousFix.Timestamp.ToUniversalTime();
        if (deltaTime <= TimeSpan.Zero || deltaTime > SpeedJumpWindow)
        {
            return null;
        }

        var speedDelta = Math.Abs(currentFix.SpeedMps.Value - previousFix.SpeedMps.Value);
        if (speedDelta <= SpeedJumpThresholdMps)
        {
            return null;
        }

        return new TelemetryAlert(
            "SPEED_JUMP",
            "warning",
            "Speed changed abruptly between consecutive fixes.",
            speedDelta,
            currentTimestampUtc);
    }

    private TelemetryAlert? TryCreateSpeedLimitTransitionAlert(Fix fix, DateTimeOffset currentTimestampUtc)
    {
        if (_alertRules is null || !_alertRules.SpeedLimitMps.HasValue)
        {
            return null;
        }

        if (!fix.SpeedMps.HasValue)
        {
            return null;
        }

        var speedLimit = _alertRules.SpeedLimitMps.Value;
        var isExceeded = fix.SpeedMps.Value > speedLimit;

        if (!_wasSpeedLimitExceeded.HasValue)
        {
            _wasSpeedLimitExceeded = isExceeded;
            return null;
        }

        if (_wasSpeedLimitExceeded.Value == isExceeded)
        {
            return null;
        }

        _wasSpeedLimitExceeded = isExceeded;

        if (isExceeded)
        {
            return new TelemetryAlert(
                "SPEED_LIMIT_EXCEEDED",
                "warning",
                "Speed exceeded configured speed limit.",
                fix.SpeedMps.Value,
                currentTimestampUtc);
        }

        return new TelemetryAlert(
            "SPEED_LIMIT_RECOVERED",
            "info",
            "Speed returned below configured speed limit.",
            fix.SpeedMps.Value,
            currentTimestampUtc);
    }

    private TelemetryAlert? TryCreateGeofenceTransitionAlert(Fix fix, DateTimeOffset currentTimestampUtc)
    {
        if (_alertRules is null ||
            !_alertRules.GeofenceCenterLat.HasValue ||
            !_alertRules.GeofenceCenterLon.HasValue ||
            !_alertRules.GeofenceRadiusM.HasValue)
        {
            return null;
        }

        var distanceFromCenter = HaversineDistanceMeters(
            _alertRules.GeofenceCenterLat.Value,
            _alertRules.GeofenceCenterLon.Value,
            fix.LatitudeDeg,
            fix.LongitudeDeg);

        var isInsideGeofence = distanceFromCenter <= _alertRules.GeofenceRadiusM.Value;

        if (!_wasInsideGeofence.HasValue)
        {
            _wasInsideGeofence = isInsideGeofence;
            return null;
        }

        if (_wasInsideGeofence.Value == isInsideGeofence)
        {
            return null;
        }

        _wasInsideGeofence = isInsideGeofence;

        if (isInsideGeofence)
        {
            return new TelemetryAlert(
                "GEOFENCE_ENTER",
                "info",
                "Fix returned inside configured geofence.",
                distanceFromCenter,
                currentTimestampUtc);
        }

        return new TelemetryAlert(
            "GEOFENCE_EXIT",
            "warning",
            "Fix moved outside configured geofence.",
            distanceFromCenter,
            currentTimestampUtc);
    }

    private static double HaversineDistanceMeters(double latitude1Deg, double longitude1Deg, double latitude2Deg, double longitude2Deg)
    {
        var latitude1Rad = DegreesToRadians(latitude1Deg);
        var latitude2Rad = DegreesToRadians(latitude2Deg);
        var deltaLatitude = DegreesToRadians(latitude2Deg - latitude1Deg);
        var deltaLongitude = DegreesToRadians(longitude2Deg - longitude1Deg);

        var sinLat = Math.Sin(deltaLatitude / 2.0);
        var sinLon = Math.Sin(deltaLongitude / 2.0);

        var a = (sinLat * sinLat) +
                (Math.Cos(latitude1Rad) * Math.Cos(latitude2Rad) * sinLon * sinLon);

        var c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
        return EarthRadiusMeters * c;
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * (Math.PI / 180.0);
    }
}
