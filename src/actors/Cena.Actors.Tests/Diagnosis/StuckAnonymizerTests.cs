// =============================================================================
// Cena Platform — StuckAnonymizer tests (RDY-063)
//
// Contract: stable within (studentId, sessionId, salt); differs when any
// of the three change; never contains the raw studentId or sessionId.
// =============================================================================

using Cena.Actors.Diagnosis;

namespace Cena.Actors.Tests.Diagnosis;

public class StuckAnonymizerTests
{
    [Fact]
    public void SameInputs_YieldSameAnonId()
    {
        var a = new StuckAnonymizer("salt-v1");
        var b = new StuckAnonymizer("salt-v1");

        var idA = a.Anonymize("stu-abc", "sess-123");
        var idB = b.Anonymize("stu-abc", "sess-123");

        Assert.Equal(idA, idB);
    }

    [Fact]
    public void DifferentSession_YieldsDifferentAnonId()
    {
        var anon = new StuckAnonymizer("salt-v1");

        var id1 = anon.Anonymize("stu-abc", "sess-1");
        var id2 = anon.Anonymize("stu-abc", "sess-2");

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void DifferentStudent_YieldsDifferentAnonId()
    {
        var anon = new StuckAnonymizer("salt-v1");

        var id1 = anon.Anonymize("stu-a", "sess-1");
        var id2 = anon.Anonymize("stu-b", "sess-1");

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void DifferentSalt_YieldsDifferentAnonId()
    {
        var a = new StuckAnonymizer("salt-v1");
        var b = new StuckAnonymizer("salt-v2");

        var id1 = a.Anonymize("stu", "sess");
        var id2 = b.Anonymize("stu", "sess");

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void AnonId_DoesNotContainRawInputs()
    {
        var anon = new StuckAnonymizer("salt-v1");

        var id = anon.Anonymize("stu-very-identifiable-id", "sess-also-identifiable");

        Assert.DoesNotContain("stu-very-identifiable-id", id, StringComparison.Ordinal);
        Assert.DoesNotContain("sess-also-identifiable", id, StringComparison.Ordinal);
        Assert.Equal(16, id.Length);  // HMAC prefix length
    }

    [Fact]
    public void EmptySalt_ThrowsOnConstruction()
    {
        Assert.Throws<ArgumentException>(() => new StuckAnonymizer(""));
    }

    [Fact]
    public void EmptyStudentId_ThrowsOnAnonymize()
    {
        var anon = new StuckAnonymizer("salt");
        Assert.Throws<ArgumentException>(() => anon.Anonymize("", "sess"));
    }

    [Fact]
    public void ContextBuilder_AssertsNoPii_Passes_WithAnonimizedContext()
    {
        var anon = new StuckAnonymizer("salt-v1");
        var builder = new StuckContextBuilder(anon);

        var ctx = builder.Build(new StuckContextInputs(
            StudentId: "stu-123",
            SessionId: "sess-1",
            Locale: "en",
            Question: new StuckContextQuestion("q-1", "solve for x", "ch-1",
                new[] { "lo-1" }, "free_response", 0.5f),
            Advancement: new StuckContextAdvancement("ch-1", "InProgress", 0.8f, 2, 10),
            Attempts: new[]
            {
                new StuckContextAttempt(DateTimeOffset.UtcNow, "x=5", false, 10, 0.2f, null)
            },
            SessionSignals: new StuckContextSessionSignals(30, 0, 1, 0, 0.7, 1.0),
            AsOf: DateTimeOffset.UtcNow));

        // Serialized context must not contain raw studentId anywhere.
        var json = System.Text.Json.JsonSerializer.Serialize(ctx);
        Assert.DoesNotContain("stu-123", json, StringComparison.Ordinal);
        Assert.Equal(anon.Anonymize("stu-123", "sess-1"), ctx.StudentAnonId);
    }

    [Fact]
    public void ContextBuilder_AssertsNoPii_RejectsEmailInAttempt()
    {
        var anon = new StuckAnonymizer("salt-v1");
        var builder = new StuckContextBuilder(anon);

        // An attempt with an unscrubbed email literal must be rejected.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.Build(new StuckContextInputs(
                StudentId: "stu-x",
                SessionId: "sess-1",
                Locale: "en",
                Question: new StuckContextQuestion("q-1", null, null,
                    Array.Empty<string>(), null, null),
                Advancement: new StuckContextAdvancement(null, null, 0, 0, 0),
                Attempts: new[]
                {
                    new StuckContextAttempt(
                        DateTimeOffset.UtcNow,
                        "please help me at test@cena.local",   // contains '@'
                        false, 10, 0.1f, null)
                },
                SessionSignals: new StuckContextSessionSignals(30, 0, 0, 0, 0, 0),
                AsOf: DateTimeOffset.UtcNow)));

        Assert.Contains("@", ex.Message);
    }
}
