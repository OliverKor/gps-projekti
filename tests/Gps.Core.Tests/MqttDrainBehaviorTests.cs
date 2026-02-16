using Gps.Core;

namespace Gps.Core.Tests;

public sealed class MqttDrainBehaviorTests
{
    [Fact]
    public async Task StopAsync_DrainsQueuedMessagesInOrder_WithinTimeout()
    {
        var queue = new MqttMessageQueue<int>(capacity: 10);
        var published = new List<int>();

        await using var pump = new MqttMessagePump<int>(queue, async (value, token) =>
        {
            await Task.Delay(10, token);
            published.Add(value);
        });

        pump.Start();
        Assert.True(queue.TryEnqueue(1));
        Assert.True(queue.TryEnqueue(2));
        Assert.True(queue.TryEnqueue(3));

        var drained = await pump.StopAsync(TimeSpan.FromSeconds(2));

        Assert.True(drained);
        Assert.Equal([1, 2, 3], published);
    }

    [Fact]
    public async Task StopAsync_ReturnsFalse_WhenDrainTimeoutExceeded()
    {
        var queue = new MqttMessageQueue<int>(capacity: 10);

        await using var pump = new MqttMessagePump<int>(queue, async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });

        pump.Start();
        Assert.True(queue.TryEnqueue(1));

        var drained = await pump.StopAsync(TimeSpan.FromMilliseconds(80));

        Assert.False(drained);
    }
}
