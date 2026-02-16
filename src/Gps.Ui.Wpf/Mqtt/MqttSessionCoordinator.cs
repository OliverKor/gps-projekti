using System.Diagnostics;
using Gps.Core;
using Gps.Core.Telemetry;
using System.Windows.Threading;

namespace Gps.Ui.Wpf.Mqtt;

internal sealed class MqttSessionCoordinator : IAsyncDisposable
{
    private readonly MqttSettings _settings;
    private readonly MqttPublisher _publisher;
    private readonly TelemetryMetricsTracker _tracker;
    private readonly DispatcherTimer _diagTimer;

    private readonly string _fixTopic;
    private readonly string _diagTopic;
    private readonly string _alertTopic;
    private readonly string _statusTopic;

    private readonly int _ownerThreadId;

    private bool _running;

    public MqttSessionCoordinator(MqttSettings settings)
    {
        _settings = settings.Sanitize();
        _publisher = new MqttPublisher(_settings);
        _tracker = new TelemetryMetricsTracker();

        var baseTopic = _settings.BaseTopic.Trim('/');
        _fixTopic = $"{baseTopic}/{_settings.DeviceId}/fix";
        _diagTopic = $"{baseTopic}/{_settings.DeviceId}/diag";
        _alertTopic = $"{baseTopic}/{_settings.DeviceId}/alert";
        _statusTopic = $"{baseTopic}/{_settings.DeviceId}/status";

        _diagTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(_settings.DiagIntervalSeconds)
        };

        _diagTimer.Tick += OnDiagTimerTick;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public void Start()
    {
        EnsureOwnerThread();

        if (_running)
        {
            return;
        }

        _publisher.Start();
        _running = true;
        _diagTimer.Start();

        PublishStatus("online", string.Empty);
    }

    public void OnFixReceived(Fix fix)
    {
        EnsureOwnerThread();

        if (!_running)
        {
            return;
        }

        var alerts = new List<TelemetryAlert>(2);
        var snapshot = _tracker.RecordFix(fix, alerts);

        var fixPayload = new FixPayload(
            1,
            fix.Timestamp.ToUniversalTime(),
            _settings.DeviceId,
            fix.LatitudeDeg,
            fix.LongitudeDeg,
            fix.SpeedMps,
            fix.NumSv,
            fix.FixType,
            fix.LatitudeMeters,
            fix.LongitudeMeters,
            snapshot.DistanceTotalM,
            snapshot.AverageSpeedMps,
            snapshot.FixCount);

        _publisher.TryEnqueueJson(_fixTopic, fixPayload);
        PublishAlerts(alerts);
    }

    private void OnDiagTimerTick(object? sender, EventArgs e)
    {
        EnsureOwnerThread();

        if (!_running)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var diagnostic = _tracker.CreateDiagnostic(now);
        var counters = _publisher.GetCounters();

        var diagPayload = new DiagPayload(
            1,
            now,
            _settings.DeviceId,
            diagnostic.FixRateHz,
            diagnostic.LastFixAgeSec,
            diagnostic.NoFixSeconds,
            counters.QueueDepth,
            counters.DroppedCount,
            counters.PublishFailures);

        _publisher.TryEnqueueJson(_diagTopic, diagPayload);
        PublishAlerts(_tracker.EvaluateNoFixAlerts(now));
    }

    public async Task StopAsync(string reason)
    {
        EnsureOwnerThread();

        if (!_running)
        {
            await _publisher.StopAsync(TimeSpan.FromSeconds(_settings.DrainTimeoutSeconds));
            return;
        }

        _running = false;
        _diagTimer.Stop();
        _diagTimer.Tick -= OnDiagTimerTick;

        PublishStatus("offline", reason);

        _ = await _publisher.StopAsync(TimeSpan.FromSeconds(_settings.DrainTimeoutSeconds));
    }

    private void PublishStatus(string state, string reason)
    {
        var payload = new StatusPayload(
            1,
            DateTimeOffset.UtcNow,
            _settings.DeviceId,
            state,
            reason);

        _publisher.TryEnqueueJson(_statusTopic, payload, retain: true);
    }

    private void PublishAlerts(IReadOnlyList<TelemetryAlert> alerts)
    {
        foreach (var alert in alerts)
        {
            var payload = new AlertPayload(
                1,
                alert.TimestampUtc,
                _settings.DeviceId,
                alert.Code,
                alert.Severity,
                alert.Message,
                alert.Value);

            _publisher.TryEnqueueJson(_alertTopic, payload);
        }
    }

    [Conditional("DEBUG")]
    private void EnsureOwnerThread()
    {
        Debug.Assert(
            Environment.CurrentManagedThreadId == _ownerThreadId,
            "MqttSessionCoordinator must be used from the same UI thread that created it.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync("disposed");
        await _publisher.DisposeAsync();
    }

    private readonly record struct FixPayload(
        int V,
        DateTimeOffset TsUtc,
        string DeviceId,
        double Lat,
        double Lon,
        double? SpeedMps,
        int? NumSv,
        string? FixType,
        double? LatM,
        double? LonM,
        double DistanceTotalM,
        double AverageSpeedMps,
        int FixCount);

    private readonly record struct DiagPayload(
        int V,
        DateTimeOffset TsUtc,
        string DeviceId,
        double FixRateHz,
        double LastFixAgeSec,
        double NoFixSeconds,
        int QueueDepth,
        long DroppedCount,
        long PublishFailures);

    private readonly record struct AlertPayload(
        int V,
        DateTimeOffset TsUtc,
        string DeviceId,
        string Code,
        string Severity,
        string Message,
        double Value);

    private readonly record struct StatusPayload(
        int V,
        DateTimeOffset TsUtc,
        string DeviceId,
        string State,
        string Reason);
}
