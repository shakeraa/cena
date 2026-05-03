// =============================================================================
// Cena Platform — CasChainExporter tests (EPIC-PRR-J PRR-363)
//
// Covers the DoD: export round-trips with the verifier (same step indexes,
// same outcome enum → stable code, same summary), renders correctly
// in show-my-work view (locale-resolved operation labels), locale-correct
// labels for en/he/ar.
// =============================================================================

using Cena.Actors.Cas;
using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class CasChainExporterTests
{
    [Fact]
    public void Export_preserves_step_order_and_fields()
    {
        var steps = new[]
        {
            new ExtractedStep(0, "2x + 4", "2*x + 4", 0.95),
            new ExtractedStep(1, "2(x + 2)", "2*(x + 2)", 0.92),
        };
        var verification = new StepChainVerificationResult(
            Transitions: new[]
            {
                new StepTransitionResult(
                    FromStepIndex: 0,
                    ToStepIndex: 1,
                    Outcome: StepTransitionOutcome.Valid,
                    CasResult: CasVerifyResult.Success(
                        CasOperation.Equivalence, "SymPy", 12.5),
                    Summary: "factor out 2"),
            },
            FirstFailureIndex: null);

        var export = CasChainExporter.Export(steps, verification, CasChainLocale.En);

        Assert.True(export.Succeeded);
        Assert.Equal(2, export.Steps.Count);
        Assert.Equal(0, export.Steps[0].Index);
        Assert.Equal("2x + 4", export.Steps[0].Latex);
        Assert.Equal(1, export.Steps[1].Index);
        Assert.Single(export.Transitions);

        var t = export.Transitions[0];
        Assert.Equal("valid", t.OutcomeCode);
        Assert.Equal("factor", t.OperationCode);
        Assert.Equal("Factor", t.OperationLabel);
        Assert.True(t.Holds);
        Assert.Equal(CasVerifyMethodCode.SymPy, t.VerifyMethod);
    }

    [Fact]
    public void Export_hebrew_locale_uses_hebrew_labels()
    {
        var steps = new[]
        {
            new ExtractedStep(0, "x^2 + 2x + 1", "x^2 + 2*x + 1", 0.9),
            new ExtractedStep(1, "(x+1)^2", "(x+1)^2", 0.9),
        };
        var verification = new StepChainVerificationResult(
            Transitions: new[]
            {
                new StepTransitionResult(0, 1, StepTransitionOutcome.Valid,
                    CasResult: CasVerifyResult.Success(CasOperation.Equivalence, "SymPy", 10),
                    Summary: "factor"),
            },
            FirstFailureIndex: null);

        var export = CasChainExporter.Export(steps, verification, CasChainLocale.He);

        Assert.Equal("he", export.Locale);
        Assert.Equal("פירוק לגורמים", export.Transitions[0].OperationLabel);
    }

    [Fact]
    public void Export_arabic_locale_uses_arabic_labels()
    {
        var steps = new[]
        {
            new ExtractedStep(0, "x", "x", 0.9),
            new ExtractedStep(1, "x", "x", 0.9),
        };
        var verification = new StepChainVerificationResult(
            Transitions: new[]
            {
                new StepTransitionResult(0, 1, StepTransitionOutcome.Valid,
                    CasResult: null,
                    Summary: "simplify terms"),
            },
            FirstFailureIndex: null);

        var export = CasChainExporter.Export(steps, verification, CasChainLocale.Ar);

        Assert.Equal("ar", export.Locale);
        Assert.Equal("تبسيط", export.Transitions[0].OperationLabel);
    }

    [Fact]
    public void Export_neutral_step_label_when_operation_unrecognised()
    {
        var steps = new[]
        {
            new ExtractedStep(0, "a", "a", 0.9),
            new ExtractedStep(1, "b", "b", 0.9),
        };
        var verification = new StepChainVerificationResult(
            Transitions: new[]
            {
                new StepTransitionResult(0, 1, StepTransitionOutcome.Wrong,
                    CasResult: CasVerifyResult.Failure(
                        CasOperation.Equivalence, "SymPy", 20, "not equal"),
                    Summary: "completely novel transformation name"),
            },
            FirstFailureIndex: 1);

        var export = CasChainExporter.Export(steps, verification, CasChainLocale.En);

        // OperationCode is null (not recognised); label falls back to "Step".
        Assert.Null(export.Transitions[0].OperationCode);
        Assert.Equal("Step", export.Transitions[0].OperationLabel);
        // And the outcome is faithfully projected.
        Assert.Equal("wrong", export.Transitions[0].OutcomeCode);
        Assert.False(export.Transitions[0].Holds);
    }

    [Fact]
    public void Export_first_failure_index_matches_verification()
    {
        var steps = new[]
        {
            new ExtractedStep(0, "", "", 0.9),
            new ExtractedStep(1, "", "", 0.9),
            new ExtractedStep(2, "", "", 0.9),
        };
        var verification = new StepChainVerificationResult(
            Transitions: new[]
            {
                new StepTransitionResult(0, 1, StepTransitionOutcome.Valid,
                    CasResult: null, Summary: "expand"),
                new StepTransitionResult(1, 2, StepTransitionOutcome.Wrong,
                    CasResult: null, Summary: "wrong step"),
            },
            FirstFailureIndex: 2);

        var export = CasChainExporter.Export(steps, verification, CasChainLocale.En);

        Assert.False(export.Succeeded);
        Assert.Equal(2, export.FirstFailureIndex);
    }

    [Theory]
    [InlineData("expand", CasChainLocale.En, "Expand")]
    [InlineData("expand", CasChainLocale.He, "פתיחה")]
    [InlineData("expand", CasChainLocale.Ar, "توسيع")]
    [InlineData("simplify", CasChainLocale.En, "Simplify")]
    [InlineData("simplify", CasChainLocale.He, "פישוט")]
    [InlineData("simplify", CasChainLocale.Ar, "تبسيط")]
    [InlineData("equate", CasChainLocale.En, "Equate")]
    [InlineData("equate", CasChainLocale.He, "השוואה")]
    [InlineData("equate", CasChainLocale.Ar, "مساواة")]
    [InlineData("rearrange", CasChainLocale.En, "Rearrange")]
    [InlineData("rearrange", CasChainLocale.He, "סידור מחדש")]
    [InlineData("rearrange", CasChainLocale.Ar, "إعادة ترتيب")]
    [InlineData(null, CasChainLocale.En, "Step")]
    [InlineData(null, CasChainLocale.He, "שלב")]
    [InlineData(null, CasChainLocale.Ar, "خطوة")]
    public void LabelFor_every_locale_matches_expected(
        string? opCode, CasChainLocale locale, string expected)
    {
        Assert.Equal(expected, CasChainExporter.LabelFor(opCode, locale));
    }
}
