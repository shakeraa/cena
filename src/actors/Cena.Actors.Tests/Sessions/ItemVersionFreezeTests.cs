// =============================================================================
// RDY-075 Phase 1A — ItemVersionFreeze + OfflineSyncIngest tests.
// =============================================================================

using Cena.Actors.Sessions;
using Xunit;

namespace Cena.Actors.Tests.Sessions;

public class ItemVersionFreezeTests
{
    private static ItemVersionFreeze Freeze(string correct = "42") => new(
        ItemId: "item-1",
        ItemVersion: 3,
        QuestionText: "What is 6 x 7?",
        CorrectAnswerCanonical: correct,
        Difficulty: 0.3,
        Discrimination: 1.2,
        CasSnapshotHash: new string('a', 64),
        FrozenAtUtc: DateTimeOffset.UtcNow);

    [Theory]
    [InlineData("42", true)]
    [InlineData("  42 ", true)]
    [InlineData("42.", true)]
    [InlineData("43", false)]
    [InlineData(" ", false)]
    [InlineData("", false)]
    public void Is_answer_correct_normalises_whitespace_and_trailing_dot(string candidate, bool expected)
    {
        Assert.Equal(expected, Freeze().IsAnswerCorrect(candidate));
    }

    [Fact]
    public void Case_insensitive_by_default()
    {
        var f = Freeze("Answer");
        Assert.True(f.IsAnswerCorrect("answer"));
        Assert.True(f.IsAnswerCorrect("ANSWER"));
    }

    [Fact]
    public void Case_sensitive_when_requested()
    {
        var f = Freeze("Answer");
        Assert.True(f.IsAnswerCorrect("Answer", caseSensitive: true));
        Assert.False(f.IsAnswerCorrect("answer", caseSensitive: true));
    }
}

public class InMemoryOfflineSyncLedgerTests
{
    [Fact]
    public void Has_seen_returns_false_before_mark()
    {
        var l = new InMemoryOfflineSyncLedger();
        Assert.False(l.HasSeen("key-1"));
    }

    [Fact]
    public void Has_seen_returns_true_after_mark()
    {
        var l = new InMemoryOfflineSyncLedger();
        l.MarkSeen("key-1", DateTimeOffset.UtcNow);
        Assert.True(l.HasSeen("key-1"));
    }

    [Fact]
    public void Re_marking_same_key_does_not_change_count()
    {
        var l = new InMemoryOfflineSyncLedger();
        l.MarkSeen("key-1", DateTimeOffset.UtcNow);
        l.MarkSeen("key-1", DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(1, l.SeenCount);
    }

    [Fact]
    public void Throws_on_blank_key()
    {
        var l = new InMemoryOfflineSyncLedger();
        Assert.ThrowsAny<ArgumentException>(() => l.HasSeen(""));
        Assert.ThrowsAny<ArgumentException>(() => l.MarkSeen("   ", DateTimeOffset.UtcNow));
    }
}

public class OfflineSyncIngestTests
{
    private static OfflineAnswerEvent NewEvent(string key) => new(
        IdempotencyKey: key,
        StudentAnonId: "stu-anon-1",
        SessionId: "sess-1",
        Freeze: new ItemVersionFreeze(
            ItemId: "item-1",
            ItemVersion: 1,
            QuestionText: "Q",
            CorrectAnswerCanonical: "42",
            Difficulty: 0.3,
            Discrimination: 1.0,
            CasSnapshotHash: "hash",
            FrozenAtUtc: DateTimeOffset.UtcNow),
        SubmittedAnswer: "42",
        TimeSpent: TimeSpan.FromSeconds(30),
        AnsweredAtUtc: DateTimeOffset.UtcNow);

    [Fact]
    public void Accepts_first_time_event_when_item_exists()
    {
        var ledger = new InMemoryOfflineSyncLedger();
        var decision = OfflineSyncIngest.Decide(
            NewEvent("k1"), ledger, _ => true);
        Assert.Equal(OfflineIngestDecision.Accept, decision);
    }

    [Fact]
    public void Rejects_when_item_does_not_exist()
    {
        var ledger = new InMemoryOfflineSyncLedger();
        var decision = OfflineSyncIngest.Decide(
            NewEvent("k1"), ledger, _ => false);
        Assert.Equal(OfflineIngestDecision.Reject, decision);
    }

    [Fact]
    public void Duplicate_when_idempotency_key_already_seen()
    {
        var ledger = new InMemoryOfflineSyncLedger();
        ledger.MarkSeen("k1", DateTimeOffset.UtcNow);
        var decision = OfflineSyncIngest.Decide(
            NewEvent("k1"), ledger, _ => true);
        Assert.Equal(OfflineIngestDecision.Duplicate, decision);
    }

    [Fact]
    public void Duplicate_check_takes_precedence_over_item_existence()
    {
        // Even if the item no longer exists server-side, a duplicate
        // idempotency key is silently dropped rather than dead-lettered.
        var ledger = new InMemoryOfflineSyncLedger();
        ledger.MarkSeen("k1", DateTimeOffset.UtcNow);
        var decision = OfflineSyncIngest.Decide(
            NewEvent("k1"), ledger, _ => false);
        Assert.Equal(OfflineIngestDecision.Duplicate, decision);
    }
}
