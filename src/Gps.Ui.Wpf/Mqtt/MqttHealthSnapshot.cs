namespace Gps.Ui.Wpf.Mqtt;

internal sealed record MqttHealthSnapshot(
    bool IsConnected,
    int QueueDepth,
    long DroppedCount,
    long PublishFailures,
    string? LastError,
    DateTimeOffset? LastErrorUtc
);
