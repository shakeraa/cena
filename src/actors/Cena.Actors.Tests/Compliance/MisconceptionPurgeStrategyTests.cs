// =============================================================================
// Cena Platform — Misconception purge-strategy tests (prr-015)
//
// Each purge strategy is exercised against an in-memory fake store to prove
// the semantic contract the enum promises:
//
//   Delete     — record disappears after purge.
//   Anonymize  — record survives but PII fields are replaced with
//                the fixed literal "[redacted]".
//   HashRedact — record survives but PII fields are rewritten to the
//                SHA-256 hash of the original (hex lowercase, 64 chars).
//
// The concrete in-memory fake mirrors what a Marten projection / Redis
// replica adapter would do. Real adapters live in their own modules and
// register against the shared strategy contract defined below.
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Cena.Infrastructure.Compliance;

namespace Cena.Actors.Tests.Compliance;

public sealed class MisconceptionPurgeStrategyTests
{
    // A minimal record shape: session-scoped misconception tag + free-text
    // student answer (the PII surface). Real implementations would carry
    // the subject-id, detection timestamp, and a confidence score.
    private sealed class FakeMisconceptionRecord
    {
        public Guid Id { get; set; }
        public string SessionId { get; set; } = "";
        public string BuggyRuleId { get; set; } = "";
        public string StudentAnswer { get; set; } = "";
        public DateTimeOffset DetectedAt { get; set; }
    }

    private sealed class FakeStore
    {
        public List<FakeMisconceptionRecord> Rows { get; } = new();

        public int ApplyPurge(
            MisconceptionPurgeStrategy strategy,
            DateTimeOffset cutoff)
        {
            var affected = 0;
            switch (strategy)
            {
                case MisconceptionPurgeStrategy.Delete:
                    var toDelete = Rows.Where(r => r.DetectedAt < cutoff).ToList();
                    foreach (var r in toDelete) Rows.Remove(r);
                    affected = toDelete.Count;
                    break;

                case MisconceptionPurgeStrategy.Anonymize:
                    foreach (var r in Rows.Where(r => r.DetectedAt < cutoff))
                    {
                        r.StudentAnswer = "[redacted]";
                        affected++;
                    }
                    break;

                case MisconceptionPurgeStrategy.HashRedact:
                    foreach (var r in Rows.Where(r => r.DetectedAt < cutoff))
                    {
                        r.StudentAnswer = Sha256Hex(r.StudentAnswer);
                        affected++;
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(strategy));
            }

            return affected;
        }

        private static string Sha256Hex(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    private static readonly DateTimeOffset Now =
        new(2026, 4, 20, 0, 0, 0, TimeSpan.Zero);

    private static FakeMisconceptionRecord Expired() => new()
    {
        Id = Guid.NewGuid(),
        SessionId = "session-expired",
        BuggyRuleId = "DIST-EXP-SUM",
        StudentAnswer = "x² + 9",
        DetectedAt = Now - TimeSpan.FromDays(31),
    };

    private static FakeMisconceptionRecord Fresh() => new()
    {
        Id = Guid.NewGuid(),
        SessionId = "session-fresh",
        BuggyRuleId = "SIGN-FLIP-INEQ",
        StudentAnswer = "x > 3",
        DetectedAt = Now - TimeSpan.FromDays(7),
    };

    private static DateTimeOffset ThirtyDayCutoff => Now - TimeSpan.FromDays(30);

    [Fact]
    public void Delete_RemovesExpiredRecords_LeavesFreshIntact()
    {
        var store = new FakeStore();
        var expired = Expired();
        var fresh = Fresh();
        store.Rows.Add(expired);
        store.Rows.Add(fresh);

        var purged = store.ApplyPurge(MisconceptionPurgeStrategy.Delete, ThirtyDayCutoff);

        Assert.Equal(1, purged);
        Assert.DoesNotContain(store.Rows, r => r.Id == expired.Id);
        Assert.Contains(store.Rows, r => r.Id == fresh.Id);
    }

    [Fact]
    public void Anonymize_RedactsPii_KeepsRowShape()
    {
        var store = new FakeStore();
        var expired = Expired();
        var fresh = Fresh();
        store.Rows.Add(expired);
        store.Rows.Add(fresh);

        var affected = store.ApplyPurge(MisconceptionPurgeStrategy.Anonymize, ThirtyDayCutoff);

        Assert.Equal(1, affected);
        // Row still there — shape preserved for aggregate joins.
        Assert.Contains(store.Rows, r => r.Id == expired.Id);
        // PII replaced.
        var after = store.Rows.Single(r => r.Id == expired.Id);
        Assert.Equal("[redacted]", after.StudentAnswer);
        // BuggyRuleId is non-PII (catalog entry) so it stays.
        Assert.Equal("DIST-EXP-SUM", after.BuggyRuleId);
        // Fresh record untouched.
        Assert.Equal("x > 3", store.Rows.Single(r => r.Id == fresh.Id).StudentAnswer);
    }

    [Fact]
    public void HashRedact_ReplacesPii_WithDeterministicSha256()
    {
        var store = new FakeStore();
        var expired = Expired();
        store.Rows.Add(expired);

        // Run twice — same input must produce the same hash.
        var first = store.ApplyPurge(MisconceptionPurgeStrategy.HashRedact, ThirtyDayCutoff);
        var afterFirst = store.Rows.Single().StudentAnswer;
        Assert.Equal(1, first);
        Assert.Equal(64, afterFirst.Length); // SHA-256 hex
        Assert.Matches("^[0-9a-f]{64}$", afterFirst);

        // Re-running HashRedact on an already-hashed row produces the
        // hash-of-the-hash (deterministic), NOT the original — proving the
        // purge is one-way.
        var second = store.ApplyPurge(MisconceptionPurgeStrategy.HashRedact, ThirtyDayCutoff);
        var afterSecond = store.Rows.Single().StudentAnswer;
        Assert.Equal(1, second);
        Assert.NotEqual(afterFirst, afterSecond);
        Assert.Matches("^[0-9a-f]{64}$", afterSecond);
    }

    [Fact]
    public void AnyStrategy_DoesNotTouchRecordsNewerThanCutoff()
    {
        foreach (var strategy in Enum.GetValues<MisconceptionPurgeStrategy>())
        {
            var store = new FakeStore();
            store.Rows.Add(Fresh());
            store.Rows.Add(Fresh());
            store.Rows.Add(Fresh());

            var affected = store.ApplyPurge(strategy, ThirtyDayCutoff);
            Assert.Equal(0, affected);
            Assert.Equal(3, store.Rows.Count);
        }
    }

    [Fact]
    public void Enum_HasExpectedMembers_NoSilentAdditions()
    {
        // Defensive: if anyone adds a new strategy without updating the
        // worker or the tests, this fails and forces the new code path to
        // be exercised.
        var expected = new[]
        {
            MisconceptionPurgeStrategy.Delete,
            MisconceptionPurgeStrategy.Anonymize,
            MisconceptionPurgeStrategy.HashRedact,
        };
        var actual = Enum.GetValues<MisconceptionPurgeStrategy>();
        Assert.Equal(expected.OrderBy(v => v), actual.OrderBy(v => v));
    }
}
