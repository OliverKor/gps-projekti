namespace Gps.Core;

internal sealed class MqttMessageQueue<T>
{
    private readonly object _sync = new();
    private readonly Queue<T> _items = [];
    private readonly SemaphoreSlim _availableItems = new(0);
    private readonly int _capacity;

    private long _droppedCount;
    private bool _accepting = true;

    public MqttMessageQueue(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _items.Count;
            }
        }
    }

    public long DroppedCount
    {
        get
        {
            lock (_sync)
            {
                return _droppedCount;
            }
        }
    }

    public bool IsAccepting
    {
        get
        {
            lock (_sync)
            {
                return _accepting;
            }
        }
    }

    public bool TryEnqueue(T item)
    {
        var releaseSignal = false;

        lock (_sync)
        {
            if (!_accepting)
            {
                return false;
            }

            if (_items.Count == _capacity)
            {
                _ = _items.Dequeue();
                _droppedCount++;
            }
            else
            {
                releaseSignal = true;
            }

            _items.Enqueue(item);
        }

        if (releaseSignal)
        {
            _availableItems.Release();
        }

        return true;
    }

    public void CompleteIntake()
    {
        var releaseSignal = false;

        lock (_sync)
        {
            if (!_accepting)
            {
                return;
            }

            _accepting = false;
            if (_items.Count == 0)
            {
                releaseSignal = true;
            }
        }

        if (releaseSignal)
        {
            _availableItems.Release();
        }
    }

    public async ValueTask<MqttMessageQueueReadResult<T>> ReadAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await _availableItems.WaitAsync(cancellationToken).ConfigureAwait(false);

            lock (_sync)
            {
                if (_items.Count > 0)
                {
                    var item = _items.Dequeue();
                    if (_items.Count == 0 && !_accepting)
                    {
                        _availableItems.Release();
                    }

                    return MqttMessageQueueReadResult<T>.FromItem(item);
                }

                if (!_accepting)
                {
                    return MqttMessageQueueReadResult<T>.Completed;
                }
            }
        }
    }
}

internal readonly record struct MqttMessageQueueReadResult<T>(bool HasItem, bool IsCompleted, T Item)
{
    public static MqttMessageQueueReadResult<T> FromItem(T item)
    {
        return new MqttMessageQueueReadResult<T>(true, false, item);
    }

    public static MqttMessageQueueReadResult<T> Completed => new(false, true, default!);
}
