// =============================================================================
// RES-008: NATS outbox dead-letter and retry tracking tests
// =============================================================================

using Cena.Actors.Infrastructure;

namespace Cena.Actors.Tests.Infrastructure;

public sealed class NatsOutboxDeadLetterTests
{
    [Fact]
    public void DeadLetter_HasUniqueId()
    {
        var dl1 = new NatsOutboxDeadLetter();
        var dl2 = new NatsOutboxDeadLetter();
        Assert.NotEqual(dl1.Id, dl2.Id);
    }

    [Fact]
    public void DeadLetter_CapturesSequenceAndType()
    {
        var dl = new NatsOutboxDeadLetter
        {
            EventSequence = 42,
            StreamId = "student-abc",
            EventType = "ConceptAttempted_V1",
            RetryCount = 10,
            DeadLetteredAt = DateTimeOffset.UtcNow
        };

        Assert.Equal(42, dl.EventSequence);
        Assert.Equal("student-abc", dl.StreamId);
        Assert.Equal("ConceptAttempted_V1", dl.EventType);
        Assert.Equal(10, dl.RetryCount);
    }

    [Fact]
    public void MaxRetries_IsAccessibleViaReflection()
    {
        // Verify the MaxRetries constant is 10 as specified in RES-008
        var field = typeof(NatsOutboxPublisher).GetField(
            "MaxRetries",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(field);
        var value = (int)field!.GetValue(null)!;
        Assert.Equal(10, value);
    }

    [Fact]
    public void Checkpoint_DefaultValues()
    {
        var cp = new NatsOutboxCheckpoint();
        Assert.Equal(NatsOutboxCheckpoint.CheckpointId, cp.Id);
        Assert.Equal(0, cp.LastPublishedSequence);
        Assert.Equal(0, cp.TotalPublished);
    }
}
