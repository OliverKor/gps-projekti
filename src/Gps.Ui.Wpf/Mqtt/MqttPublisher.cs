using System.Text.Json;
using Gps.Core;
using MQTTnet;
using MQTTnet.Protocol;

namespace Gps.Ui.Wpf.Mqtt;

internal sealed class MqttPublisher : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly MqttSettings _settings;
    private readonly MqttMessageQueue<OutboundMessage> _queue;
    private readonly MqttMessagePump<OutboundMessage> _pump;
    private readonly IMqttClient _client;
    private readonly object _stateSync = new();

    private long _publishFailures;
    private bool _started;
    private bool _stopped;
    private bool _isConnected;
    private string? _lastError;
    private DateTimeOffset? _lastErrorUtc;

    public MqttPublisher(MqttSettings settings)
    {
        _settings = settings;
        _queue = new MqttMessageQueue<OutboundMessage>(_settings.QueueCapacity);
        _pump = new MqttMessagePump<OutboundMessage>(_queue, PublishMessageAsync);
        _client = new MqttClientFactory().CreateMqttClient();
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _pump.Start();
    }

    public bool TryEnqueueJson<TPayload>(string topic, TPayload payload, bool retain = false)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return TryEnqueue(topic, json, retain);
    }

    public bool TryEnqueue(string topic, string payload, bool retain = false)
    {
        if (_stopped)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new ArgumentException("Topic is required.", nameof(topic));
        }

        var message = new OutboundMessage(topic, payload, retain);
        return _queue.TryEnqueue(message);
    }

    public MqttPublisherCounters GetCounters()
    {
        return new MqttPublisherCounters(
            _queue.Count,
            _queue.DroppedCount,
            Interlocked.Read(ref _publishFailures));
    }

    public MqttHealthSnapshot GetHealthSnapshot()
    {
        var counters = GetCounters();

        lock (_stateSync)
        {
            return new MqttHealthSnapshot(
                _isConnected,
                counters.QueueDepth,
                counters.DroppedCount,
                counters.PublishFailures,
                _lastError,
                _lastErrorUtc);
        }
    }

    public async Task<bool> StopAsync(TimeSpan drainTimeout)
    {
        if (_stopped)
        {
            return true;
        }

        _stopped = true;

        var drained = await _pump.StopAsync(drainTimeout).ConfigureAwait(false);

        if (_client.IsConnected)
        {
            try
            {
                await _client.DisconnectAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignore disconnect errors on shutdown.
            }
            finally
            {
                SetConnectionState(false);
            }
        }

        SetConnectionState(false);

        await _pump.DisposeAsync().ConfigureAwait(false);
        return drained;
    }

    private async Task PublishMessageAsync(OutboundMessage outbound, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

            var appMessage = new MqttApplicationMessageBuilder()
                .WithTopic(outbound.Topic)
                .WithPayload(outbound.Payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(outbound.Retain)
                .Build();

            try
            {
                await _client.PublishAsync(appMessage, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _publishFailures);
                RecordError("publish", ex.Message);
                await ForceDisconnectAsync().ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        while (!_client.IsConnected)
        {
            var options = BuildClientOptions();

            try
            {
                await _client.ConnectAsync(options, cancellationToken).ConfigureAwait(false);
                SetConnectionState(true);
                ClearLastError();
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _publishFailures);
                SetConnectionState(false);
                RecordError("connect", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private MqttClientOptions BuildClientOptions()
    {
        var statusTopic = BuildTopic("status");
        var willPayload = BuildStatusPayload("offline", "unexpected_disconnect");

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(_settings.Host, _settings.Port)
            .WithClientId($"gps-projekti-{_settings.DeviceId}-{Environment.ProcessId}")
            .WithCleanSession()
            .WithWillTopic(statusTopic)
            .WithWillPayload(willPayload)
            .WithWillRetain(true)
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);

        if (!string.IsNullOrWhiteSpace(_settings.Username))
        {
            builder.WithCredentials(_settings.Username, _settings.Password);
        }

        return builder.Build();
    }

    private async Task ForceDisconnectAsync()
    {
        if (!_client.IsConnected)
        {
            return;
        }

        try
        {
            await _client.DisconnectAsync().ConfigureAwait(false);
        }
        catch
        {
            // Ignore failures; reconnect loop handles recovery.
        }
        finally
        {
            SetConnectionState(false);
        }
    }

    private string BuildStatusPayload(string state, string reason)
    {
        var payload = new StatusPayload(
            1,
            DateTimeOffset.UtcNow,
            _settings.DeviceId,
            state,
            reason);

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private string BuildTopic(string suffix)
    {
        return $"{_settings.BaseTopic}/{_settings.DeviceId}/{suffix}";
    }

    public async ValueTask DisposeAsync()
    {
        _ = await StopAsync(TimeSpan.FromSeconds(_settings.DrainTimeoutSeconds)).ConfigureAwait(false);
    }

    private void SetConnectionState(bool isConnected)
    {
        lock (_stateSync)
        {
            _isConnected = isConnected;
        }
    }

    private void ClearLastError()
    {
        lock (_stateSync)
        {
            _lastError = null;
            _lastErrorUtc = null;
        }
    }

    private void RecordError(string stage, string? detail)
    {
        var message = string.IsNullOrWhiteSpace(detail) ? $"MQTT {stage} failure." : $"MQTT {stage} failure: {detail}";

        lock (_stateSync)
        {
            _lastError = message;
            _lastErrorUtc = DateTimeOffset.UtcNow;
        }
    }

    private readonly record struct OutboundMessage(string Topic, string Payload, bool Retain);
    private readonly record struct StatusPayload(int V, DateTimeOffset TsUtc, string DeviceId, string State, string Reason);
}

internal readonly record struct MqttPublisherCounters(int QueueDepth, long DroppedCount, long PublishFailures);
