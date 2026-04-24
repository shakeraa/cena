// =============================================================================
// Cena Platform — TypedStepsDiagnosticEndpoint validation tests (PRR-384)
//
// Pure tests against the endpoint's static `ValidateRequest` branch. The
// wire contract is the public surface the Vue typed-steps mode builds
// against; rejection codes (`steps_required`, `too_many_steps`,
// `empty_step`, `step_too_long`, `index_out_of_order`) are stable so the
// UI can render the right localized error. These tests lock the
// validation matrix so a future refactor of the endpoint cannot silently
// weaken the input guards.
// =============================================================================

using Cena.Api.Contracts.Diagnostic;
using Cena.Student.Api.Host.Endpoints;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class TypedStepsDiagnosticEndpointTests
{
    [Fact]
    public void ValidateRequest_returns_null_for_valid_request()
    {
        var request = new TypedStepsDiagnosticRequest(
            Steps: new[]
            {
                new TypedStepInputDto(Index: 0, Latex: "2x + 3 = 7"),
                new TypedStepInputDto(Index: 1, Latex: "2x = 4"),
                new TypedStepInputDto(Index: 2, Latex: "x = 2"),
            },
            Locale: "en");

        var result = TypedStepsDiagnosticEndpoint.ValidateRequest(request);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateRequest_rejects_empty_steps_list()
    {
        var request = new TypedStepsDiagnosticRequest(
            Steps: Array.Empty<TypedStepInputDto>(),
            Locale: "en");

        var result = TypedStepsDiagnosticEndpoint.ValidateRequest(request);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateRequest_rejects_too_many_steps()
    {
        var steps = Enumerable.Range(0, TypedStepsDiagnosticEndpoint.MaxSteps + 1)
            .Select(i => new TypedStepInputDto(i, "x = 0"))
            .ToList();
        var request = new TypedStepsDiagnosticRequest(steps, "en");

        var result = TypedStepsDiagnosticEndpoint.ValidateRequest(request);
        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateRequest_accepts_exactly_MaxSteps()
    {
        // Boundary: MaxSteps is inclusive; MaxSteps+1 rejects (above test).
        var steps = Enumerable.Range(0, TypedStepsDiagnosticEndpoint.MaxSteps)
            .Select(i => new TypedStepInputDto(i, "x = 0"))
            .ToList();
        var request = new TypedStepsDiagnosticRequest(steps, "en");

        Assert.Null(TypedStepsDiagnosticEndpoint.ValidateRequest(request));
    }

    [Fact]
    public void ValidateRequest_rejects_whitespace_latex()
    {
        var request = new TypedStepsDiagnosticRequest(
            new[] { new TypedStepInputDto(0, "   ") },
            "en");

        Assert.NotNull(TypedStepsDiagnosticEndpoint.ValidateRequest(request));
    }

    [Fact]
    public void ValidateRequest_rejects_null_latex()
    {
        var request = new TypedStepsDiagnosticRequest(
            new[] { new TypedStepInputDto(0, null!) },
            "en");

        Assert.NotNull(TypedStepsDiagnosticEndpoint.ValidateRequest(request));
    }

    [Fact]
    public void ValidateRequest_rejects_step_latex_over_max_length()
    {
        var oversized = new string('x', TypedStepsDiagnosticEndpoint.MaxStepLatexLength + 1);
        var request = new TypedStepsDiagnosticRequest(
            new[] { new TypedStepInputDto(0, oversized) },
            "en");

        Assert.NotNull(TypedStepsDiagnosticEndpoint.ValidateRequest(request));
    }

    [Fact]
    public void ValidateRequest_accepts_step_at_exact_MaxLength()
    {
        var exact = new string('x', TypedStepsDiagnosticEndpoint.MaxStepLatexLength);
        var request = new TypedStepsDiagnosticRequest(
            new[] { new TypedStepInputDto(0, exact) },
            "en");

        Assert.Null(TypedStepsDiagnosticEndpoint.ValidateRequest(request));
    }

    [Fact]
    public void ValidateRequest_rejects_out_of_order_index()
    {
        // Client sent index 2 for position 0 — likely a client bug, and
        // silently re-ordering would hide it. Surface via 400.
        var request = new TypedStepsDiagnosticRequest(
            new[]
            {
                new TypedStepInputDto(Index: 2, Latex: "2x + 3 = 7"),
                new TypedStepInputDto(Index: 1, Latex: "2x = 4"),
            },
            "en");

        Assert.NotNull(TypedStepsDiagnosticEndpoint.ValidateRequest(request));
    }

    [Fact]
    public void ValidateRequest_rejects_duplicate_index()
    {
        var request = new TypedStepsDiagnosticRequest(
            new[]
            {
                new TypedStepInputDto(Index: 0, Latex: "a"),
                new TypedStepInputDto(Index: 0, Latex: "b"),
            },
            "en");

        Assert.NotNull(TypedStepsDiagnosticEndpoint.ValidateRequest(request));
    }

    [Fact]
    public void ValidateRequest_rejects_skipped_index()
    {
        // Index 0 then index 2 — missing 1.
        var request = new TypedStepsDiagnosticRequest(
            new[]
            {
                new TypedStepInputDto(Index: 0, Latex: "a"),
                new TypedStepInputDto(Index: 2, Latex: "b"),
            },
            "en");

        Assert.NotNull(TypedStepsDiagnosticEndpoint.ValidateRequest(request));
    }

    [Fact]
    public void MaxSteps_covers_a_reasonable_Bagrut_derivation()
    {
        // Defensive assertion: the cap must not land below a typical
        // Bagrut-style long-form derivation. If a future commit drops
        // MaxSteps to e.g. 5, this test screams. 20 is a generous
        // bench value (10 steps is a long derivation in practice).
        Assert.True(TypedStepsDiagnosticEndpoint.MaxSteps >= 20,
            $"MaxSteps={TypedStepsDiagnosticEndpoint.MaxSteps} is too small for Bagrut derivations.");
    }
}
