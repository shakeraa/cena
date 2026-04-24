// =============================================================================
// RDY-074 Phase 1A — Socratic explain-it-back tests.
// =============================================================================

using Cena.Actors.Pedagogy;
using Xunit;

namespace Cena.Actors.Tests.Pedagogy;

public class JudgmentResultTests
{
    [Fact]
    public void Unavailable_result_carries_reason_in_backend()
    {
        var r = JudgmentResult.Unavailable("sidecar-offline");
        Assert.Equal(ExplainJudgment.Unavailable, r.Judgment);
        Assert.Equal(CopyBucket.SilentSkip, r.Bucket);
        Assert.Equal(ExplainRubricCategory.Unknown, r.Category);
        Assert.Equal("null:sidecar-offline", r.JudgeBackend);
    }
}

public class NullLlmJudgeTests
{
    [Fact]
    public async Task Always_returns_unavailable()
    {
        var judge = new NullLlmJudge();
        var r = await judge.JudgeAsync(new ExplainRequest(
            StudentAnonId: "stu-anon-1",
            ConceptSlug: "derivatives",
            ExpectedRulePlainLanguage: "bring the exponent down and subtract 1",
            StudentExplanationEphemeral: "you do the power rule",
            Locale: "en"));
        Assert.Equal(ExplainJudgment.Unavailable, r.Judgment);
        Assert.Equal(CopyBucket.SilentSkip, r.Bucket);
    }

    [Fact]
    public void Backend_name_is_null()
    {
        Assert.Equal("null", new NullLlmJudge().Backend);
    }
}

public class PromptFatigueGateTests
{
    [Fact]
    public void First_call_allows_prompt()
    {
        var gate = new PromptFatigueGate();
        Assert.True(gate.ShouldPrompt("stu-1"));
    }

    [Fact]
    public void Second_call_after_mark_blocks_prompt()
    {
        var gate = new PromptFatigueGate();
        Assert.True(gate.ShouldPrompt("stu-1"));
        gate.MarkPrompted("stu-1");
        Assert.False(gate.ShouldPrompt("stu-1"));
    }

    [Fact]
    public void Different_students_are_independent()
    {
        var gate = new PromptFatigueGate();
        gate.MarkPrompted("stu-1");
        Assert.False(gate.ShouldPrompt("stu-1"));
        Assert.True(gate.ShouldPrompt("stu-2"));
    }

    [Fact]
    public void Reset_clears_all()
    {
        var gate = new PromptFatigueGate();
        gate.MarkPrompted("stu-1");
        gate.MarkPrompted("stu-2");
        gate.Reset();
        Assert.True(gate.ShouldPrompt("stu-1"));
        Assert.True(gate.ShouldPrompt("stu-2"));
    }

    [Fact]
    public void Throws_on_null_or_empty_student_id()
    {
        var gate = new PromptFatigueGate();
        Assert.ThrowsAny<ArgumentException>(() => gate.ShouldPrompt(""));
        Assert.ThrowsAny<ArgumentException>(() => gate.MarkPrompted("   "));
    }
}
