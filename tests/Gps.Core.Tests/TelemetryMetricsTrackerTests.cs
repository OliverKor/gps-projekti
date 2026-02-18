using Gps.Core.Telemetry;

namespace Gps.Core.Tests;

public sealed class TelemetryMetricsTrackerTests
{
    [Fact]
    public void RecordFix_AccumulatesDistanceAndFixCount()
    {
        var tracker = new TelemetryMetricsTracker(new DateTimeOffset(2026, 2, 16, 12, 0, 0, TimeSpan.Zero));
        var start = new Fix(new DateTimeOffset(2026, 2, 16, 12, 0, 0, TimeSpan.Zero), 62.7905840, 22.8185170, 1.0);
        var next = new Fix(new DateTimeOffset(2026, 2, 16, 12, 0, 1, TimeSpan.Zero), 62.7906840, 22.8186170, 2.0);

        _ = tracker.RecordFix(start);
        var snapshot = tracker.RecordFix(next);

        Assert.Equal(2, snapshot.FixCount);
        Assert.True(snapshot.DistanceTotalM > 0.0);
        Assert.Equal(1.5, snapshot.AverageSpeedMps, 3);
        Assert.Equal(next.Timestamp, snapshot.LastFixTimestampUtc);
    }

    [Fact]
    public void RecordFix_EmitsSpeedJumpAlert_WhenDeltaExceedsThresholdWithinWindow()
    {
        var tracker = new TelemetryMetricsTracker();
        var alerts = new List<TelemetryAlert>();

        _ = tracker.RecordFix(new Fix(
            new DateTimeOffset(2026, 2, 16, 12, 0, 0, TimeSpan.Zero),
            62.7905840,
            22.8185170,
            2.0));

        _ = tracker.RecordFix(new Fix(
            new DateTimeOffset(2026, 2, 16, 12, 0, 1, TimeSpan.Zero),
            62.7905841,
            22.8185171,
            15.5), alerts);

        Assert.Single(alerts);
        Assert.Equal("SPEED_JUMP", alerts[0].Code);
        Assert.Equal("warning", alerts[0].Severity);
        Assert.True(alerts[0].Value > 12.0);
    }

    [Fact]
    public void EvaluateNoFixAlerts_EmitsThresholdAlertsOnceAndResetsAfterFix()
    {
        var sessionStart = new DateTimeOffset(2026, 2, 16, 12, 0, 0, TimeSpan.Zero);
        var tracker = new TelemetryMetricsTracker(sessionStart);

        var warningAlerts = tracker.EvaluateNoFixAlerts(sessionStart.AddSeconds(10));
        Assert.Single(warningAlerts);
        Assert.Equal("NO_FIX_10S", warningAlerts[0].Code);

        var repeatedWarning = tracker.EvaluateNoFixAlerts(sessionStart.AddSeconds(12));
        Assert.Empty(repeatedWarning);

        var criticalAlerts = tracker.EvaluateNoFixAlerts(sessionStart.AddSeconds(30));
        Assert.Single(criticalAlerts);
        Assert.Equal("NO_FIX_30S", criticalAlerts[0].Code);

        _ = tracker.RecordFix(new Fix(
            sessionStart.AddSeconds(31),
            62.7905840,
            22.8185170,
            1.0));

        var warningAfterReset = tracker.EvaluateNoFixAlerts(sessionStart.AddSeconds(41));
        Assert.Single(warningAfterReset);
        Assert.Equal("NO_FIX_10S", warningAfterReset[0].Code);
    }

    [Fact]
    public void CreateDiagnostic_ReturnsFixRateAndNoFixSeconds()
    {
        var sessionStart = new DateTimeOffset(2026, 2, 16, 12, 0, 0, TimeSpan.Zero);
        var tracker = new TelemetryMetricsTracker(sessionStart);

        _ = tracker.RecordFix(new Fix(sessionStart, 62.7905840, 22.8185170, 1.0));
        _ = tracker.RecordFix(new Fix(sessionStart.AddSeconds(1), 62.7905841, 22.8185171, 1.0));
        _ = tracker.RecordFix(new Fix(sessionStart.AddSeconds(2), 62.7905842, 22.8185172, 1.0));

        var diagnostic = tracker.CreateDiagnostic(sessionStart.AddSeconds(4));

        Assert.Equal(1.0, diagnostic.FixRateHz, 2);
        Assert.Equal(2.0, diagnostic.LastFixAgeSec, 2);
        Assert.Equal(2.0, diagnostic.NoFixSeconds, 2);
    }

    [Fact]
    public void RecordFix_EmitsSpeedLimitAlerts_OnThresholdTransitionsOnly()
    {
        var rules = new TelemetryAlertRules(SpeedLimitMps: 5.0);
        var tracker = new TelemetryMetricsTracker(alertRules: rules);
        var alerts = new List<TelemetryAlert>();

        _ = tracker.RecordFix(new Fix(
            new DateTimeOffset(2026, 2, 16, 12, 0, 0, TimeSpan.Zero),
            62.7905840,
            22.8185170,
            4.0), alerts);

        _ = tracker.RecordFix(new Fix(
            new DateTimeOffset(2026, 2, 16, 12, 0, 1, TimeSpan.Zero),
            62.7905841,
            22.8185171,
            6.0), alerts);

        _ = tracker.RecordFix(new Fix(
            new DateTimeOffset(2026, 2, 16, 12, 0, 2, TimeSpan.Zero),
            62.7905842,
            22.8185172,
            7.0), alerts);

        _ = tracker.RecordFix(new Fix(
            new DateTimeOffset(2026, 2, 16, 12, 0, 3, TimeSpan.Zero),
            62.7905843,
            22.8185173,
            3.0), alerts);

        Assert.Collection(
            alerts.Where(alert => alert.Code is "SPEED_LIMIT_EXCEEDED" or "SPEED_LIMIT_RECOVERED"),
            alert =>
            {
                Assert.Equal("SPEED_LIMIT_EXCEEDED", alert.Code);
                Assert.Equal("warning", alert.Severity);
            },
            alert =>
            {
                Assert.Equal("SPEED_LIMIT_RECOVERED", alert.Code);
                Assert.Equal("info", alert.Severity);
            });
    }

    [Fact]
    public void RecordFix_EmitsGeofenceAlerts_OnBoundaryTransitionsOnly()
    {
        var rules = new TelemetryAlertRules(
            GeofenceCenterLat: 62.7905840,
            GeofenceCenterLon: 22.8185170,
            GeofenceRadiusM: 20.0);

        var tracker = new TelemetryMetricsTracker(alertRules: rules);
        var alerts = new List<TelemetryAlert>();

        _ = tracker.RecordFix(new Fix(
            new DateTimeOffset(2026, 2, 16, 12, 0, 0, TimeSpan.Zero),
            62.7905840,
            22.8185170), alerts);

        _ = tracker.RecordFix(new Fix(
            new DateTimeOffset(2026, 2, 16, 12, 0, 1, TimeSpan.Zero),
            62.7909000,
            22.8185170), alerts);

        _ = tracker.RecordFix(new Fix(
            new DateTimeOffset(2026, 2, 16, 12, 0, 2, TimeSpan.Zero),
            62.7909100,
            22.8185200), alerts);

        _ = tracker.RecordFix(new Fix(
            new DateTimeOffset(2026, 2, 16, 12, 0, 3, TimeSpan.Zero),
            62.7905900,
            22.8185170), alerts);

        Assert.Collection(
            alerts.Where(alert => alert.Code is "GEOFENCE_EXIT" or "GEOFENCE_ENTER"),
            alert =>
            {
                Assert.Equal("GEOFENCE_EXIT", alert.Code);
                Assert.Equal("warning", alert.Severity);
                Assert.True(alert.Value > 20.0);
            },
            alert =>
            {
                Assert.Equal("GEOFENCE_ENTER", alert.Code);
                Assert.Equal("info", alert.Severity);
                Assert.True(alert.Value <= 20.0);
            });
    }
}
