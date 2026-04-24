// =============================================================================
// Cena Platform — MasteryUpdated V1→V2 upcaster tests (prr-222)
//
// Covers:
//   - V1 → V2 fills the historical default ExamTargetCode (bagrut-math-5yu).
//   - Source is stamped as V1UpcastDefault5Yu for audit.
//   - V2 → V2 is identity (for replay-loop use).
//   - ToV2Dynamic routes by runtime type; rejects unknown types.
//   - All other V1 fields round-trip unchanged.
// =============================================================================

using Cena.Actors.ExamTargets;
using Cena.Actors.Mastery;
using Cena.Actors.Mastery.Events;
using Xunit;

namespace Cena.Actors.Tests.Mastery;

public sealed class MasteryUpcasterTests
{
#pragma warning disable CS0618, CS0619 // V1 obsolete-as-error — replay-only usage
    [Fact]
    public void ToV2_fills_historical_default_exam_target_code()
    {
        var t = DateTimeOffset.Parse("2026-04-20T09:00:00Z");
        var v1 = new MasteryUpdated_V1(
            StudentAnonId: "stu-1",
            SkillCode: SkillCode.Parse("math.algebra.quadratic"),
            MasteryProbability: 0.42f,
            UpdatedAt: t);

        var v2 = MasteryUpcaster.ToV2(v1);

        Assert.Equal("stu-1", v2.StudentAnonId);
        Assert.Equal(
            ExamTargetCode.V1UpcastDefault,
            v2.ExamTargetCode.Value);
        Assert.Equal("math.algebra.quadratic", v2.SkillCode.Value);
        Assert.Equal(0.42f, v2.MasteryProbability);
        Assert.Equal(t, v2.UpdatedAt);
        Assert.Equal(MasteryEventSource.V1UpcastDefault5Yu, v2.Source);
    }

    [Fact]
    public void ToV2_of_v2_is_identity()
    {
        var v2 = new MasteryUpdated_V2(
            StudentAnonId: "stu-1",
            ExamTargetCode: ExamTargetCode.Parse("pet-quant"),
            SkillCode: SkillCode.Parse("math.a.b"),
            MasteryProbability: 0.5f,
            UpdatedAt: DateTimeOffset.UtcNow,
            Source: MasteryEventSource.Native);

        Assert.Same(v2, MasteryUpcaster.ToV2(v2));
    }

    [Fact]
    public void ToV2Dynamic_routes_both_types()
    {
        var v1 = new MasteryUpdated_V1(
            "stu", SkillCode.Parse("math.a.b"), 0.3f, DateTimeOffset.UtcNow);
        var v2 = new MasteryUpdated_V2(
            "stu", ExamTargetCode.Parse("sat-math"), SkillCode.Parse("math.a.b"),
            0.3f, DateTimeOffset.UtcNow, MasteryEventSource.Native);

        var upcastV1 = MasteryUpcaster.ToV2Dynamic(v1);
        var upcastV2 = MasteryUpcaster.ToV2Dynamic(v2);

        Assert.Equal(MasteryEventSource.V1UpcastDefault5Yu, upcastV1.Source);
        Assert.Equal(MasteryEventSource.Native, upcastV2.Source);
    }

    [Fact]
    public void ToV2Dynamic_rejects_unknown_types()
    {
        Assert.Throws<NotSupportedException>(() =>
            MasteryUpcaster.ToV2Dynamic(new { unknown = true }));
    }

    [Fact]
    public void Upcaster_is_pure_deterministic_stable()
    {
        var t = DateTimeOffset.Parse("2026-04-20T09:00:00Z");
        var v1 = new MasteryUpdated_V1(
            "stu", SkillCode.Parse("math.a.b"), 0.4f, t);

        var a = MasteryUpcaster.ToV2(v1);
        var b = MasteryUpcaster.ToV2(v1);

        Assert.Equal(a, b); // record equality proves determinism
    }
#pragma warning restore CS0618, CS0619
}
