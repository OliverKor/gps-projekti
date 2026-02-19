using Gps.Core;

namespace Gps.Core.Tests;

public sealed class MqttQueuePolicyTests
{
    [Fact]
    public async Task TryEnqueue_DropsOldest_WhenCapacityExceeded()
    {
        var queue = new MqttMessageQueue<int>(capacity: 2);

        Assert.True(queue.TryEnqueue(1));
        Assert.True(queue.TryEnqueue(2));
        Assert.True(queue.TryEnqueue(3));

        Assert.Equal(1, queue.DroppedCount);
        Assert.Equal(2, queue.Count);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var first = await queue.ReadAsync(cancellation.Token);
        var second = await queue.ReadAsync(cancellation.Token);

        Assert.Equal(2, first.Item);
        Assert.Equal(3, second.Item);
    }

    [Fact]
    public void TryEnqueue_ReturnsFalse_WhenIntakeCompleted()
    {
        var queue = new MqttMessageQueue<string>(capacity: 2);

        queue.CompleteIntake();

        var accepted = queue.TryEnqueue("payload");

        Assert.False(accepted);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void DroppedCount_Increments_ForEachDroppedMessage()
    {
        var queue = new MqttMessageQueue<int>(capacity: 1);

        Assert.True(queue.TryEnqueue(10));
        Assert.True(queue.TryEnqueue(20));
        Assert.True(queue.TryEnqueue(30));

        Assert.Equal(2, queue.DroppedCount);
        Assert.Equal(1, queue.Count);
    }
}
