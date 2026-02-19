namespace Gps.Core;

internal sealed class MqttMessagePump<T> : IAsyncDisposable
{
    private readonly MqttMessageQueue<T> _queue;
    private readonly Func<T, CancellationToken, Task> _handler;
    private readonly CancellationTokenSource _cancellation = new();

    private Task? _worker;

    public MqttMessagePump(MqttMessageQueue<T> queue, Func<T, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(handler);

        _queue = queue;
        _handler = handler;
    }

    public void Start()
    {
        if (_worker is not null)
        {
            throw new InvalidOperationException("Message pump has already started.");
        }

        _worker = Task.Run(ProcessLoopAsync);
    }

    public async Task<bool> StopAsync(TimeSpan drainTimeout)
    {
        if (drainTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(drainTimeout));
        }

        _queue.CompleteIntake();

        if (_worker is null)
        {
            return true;
        }

        var completedTask = await Task.WhenAny(_worker, Task.Delay(drainTimeout)).ConfigureAwait(false);
        if (completedTask == _worker)
        {
            await _worker.ConfigureAwait(false);
            return true;
        }

        _cancellation.Cancel();

        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on forced shutdown.
        }

        return false;
    }

    private async Task ProcessLoopAsync()
    {
        while (true)
        {
            var result = await _queue.ReadAsync(_cancellation.Token).ConfigureAwait(false);
            if (result.IsCompleted)
            {
                return;
            }

            if (!result.HasItem)
            {
                continue;
            }

            await _handler(result.Item, _cancellation.Token).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();

        if (_worker is not null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation during disposal.
            }
        }

        _cancellation.Dispose();
    }
}
