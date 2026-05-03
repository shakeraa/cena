// =============================================================================
// Cena Platform — Canonicalizer tests (EPIC-PRR-J PRR-361, ADR-0002)
// =============================================================================

using Cena.Actors.Cas;
using Xunit;

namespace Cena.Actors.Tests.Cas;

public class CanonicalizerTests
{
    // ── NormalizeLatex (pure pass) ─────────────────────────────────────────

    [Fact]
    public void NormalizeLatex_is_idempotent()
    {
        var once = Canonicalizer.NormalizeLatex("  (x-2)(x+3)   ");
        var twice = Canonicalizer.NormalizeLatex(once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void NormalizeLatex_trims_whitespace()
    {
        Assert.Equal("x", Canonicalizer.NormalizeLatex("  x  "));
    }

    [Fact]
    public void NormalizeLatex_collapses_internal_whitespace_runs()
    {
        Assert.Equal("x + 2", Canonicalizer.NormalizeLatex("x   +    2"));
    }

    [Fact]
    public void NormalizeLatex_converts_unicode_minus_to_ascii()
    {
        // U+2212 MINUS SIGN (real minus), U+2013 EN DASH, U+2014 EM DASH.
        Assert.Equal("x - 1", Canonicalizer.NormalizeLatex("x − 1"));
        Assert.Equal("x - 1", Canonicalizer.NormalizeLatex("x – 1"));
        Assert.Equal("x - 1", Canonicalizer.NormalizeLatex("x — 1"));
    }

    [Fact]
    public void NormalizeLatex_converts_unicode_multiply_to_cdot()
    {
        Assert.Contains("\\cdot", Canonicalizer.NormalizeLatex("2 × x"));
        Assert.Contains("\\cdot", Canonicalizer.NormalizeLatex("2 · x"));
    }

    [Fact]
    public void NormalizeLatex_converts_asterisk_to_cdot()
    {
        var result = Canonicalizer.NormalizeLatex("2*x");
        Assert.Contains("\\cdot", result);
        Assert.DoesNotContain("*", result);
    }

    [Fact]
    public void NormalizeLatex_strips_leading_unary_plus()
    {
        Assert.Equal("3x", Canonicalizer.NormalizeLatex("+3x"));
        Assert.Equal("x", Canonicalizer.NormalizeLatex("+ x"));
    }

    [Fact]
    public void NormalizeLatex_preserves_case_latex_is_case_significant()
    {
        Assert.Equal("X + x", Canonicalizer.NormalizeLatex("X + x"));
        Assert.Equal("\\Pi \\pi", Canonicalizer.NormalizeLatex("\\Pi \\pi"));
    }

    [Fact]
    public void NormalizeLatex_handles_NBSP()
    {
        // U+00A0 NO-BREAK SPACE must be normalized to regular space then trimmed.
        Assert.Equal("x", Canonicalizer.NormalizeLatex(" x "));
    }

    [Fact]
    public void NormalizeLatex_empty_string_returns_empty()
    {
        Assert.Equal("", Canonicalizer.NormalizeLatex(""));
    }

    [Fact]
    public void NormalizeLatex_null_throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Canonicalizer.NormalizeLatex(null!));
    }

    [Fact]
    public void NormalizeLatex_reference_equal_when_already_canonical()
    {
        // Idempotency fast-path: an already-normalized string returns the
        // SAME reference (allocation-free).
        var input = "x + 2";
        var output = Canonicalizer.NormalizeLatex(input);
        Assert.Same(input, output);
    }

    // ── CanonicalizeAsync (SymPy delegation) ────────────────────────────────

    [Fact]
    public async Task CanonicalizeAsync_empty_input_returns_empty_without_CAS_call()
    {
        var router = new FakeCasRouter();
        var sut = new Canonicalizer(router);

        var result = await sut.CanonicalizeAsync(
            "", CasOperation.Equivalence, CancellationToken.None);

        Assert.Equal("", result.NormalizedLatex);
        Assert.Null(result.CanonicalExpanded);
        Assert.Equal(0, router.CallCount);   // CAS never called
    }

    [Fact]
    public async Task CanonicalizeAsync_calls_CAS_for_Equivalence_op()
    {
        var router = new FakeCasRouter(
            CasVerifyResult.Success(CasOperation.Canonicalize, "SymPy", 15,
                simplifiedA: "x**2 + x - 6"));
        var sut = new Canonicalizer(router);

        var result = await sut.CanonicalizeAsync(
            "(x-2)(x+3)", CasOperation.Equivalence, CancellationToken.None);

        Assert.Equal(1, router.CallCount);
        Assert.Equal("x**2 + x - 6", result.CanonicalExpanded);
    }

    [Fact]
    public async Task CanonicalizeAsync_cas_outage_returns_null_canonical_no_throw()
    {
        // ADR-0002: the oracle is the SOLE canonical form computer. When
        // it's down, we keep NormalizedLatex (the cheap pass) and return
        // null CanonicalExpanded — never throw, so the verifier chain can
        // still run a best-effort compare on the cheap-canonical form.
        var router = new FakeCasRouter(
            CasVerifyResult.Error(CasOperation.Canonicalize, "SymPy", 5000, "timeout"));
        var sut = new Canonicalizer(router);

        var result = await sut.CanonicalizeAsync(
            "x + 1", CasOperation.Equivalence, CancellationToken.None);

        Assert.Equal("x + 1", result.NormalizedLatex);
        Assert.Null(result.CanonicalExpanded);
    }

    [Fact]
    public async Task CanonicalizeAsync_null_input_throws()
    {
        var sut = new Canonicalizer(new FakeCasRouter());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.CanonicalizeAsync(null!, CasOperation.Equivalence, CancellationToken.None));
    }

    [Fact]
    public void Constructor_rejects_null_router()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new Canonicalizer(null!));
    }

    private sealed class FakeCasRouter : ICasRouterService
    {
        private readonly CasVerifyResult _fixed;
        public int CallCount { get; private set; }

        public FakeCasRouter()
            : this(CasVerifyResult.Success(CasOperation.Canonicalize, "SymPy", 10))
        {
        }

        public FakeCasRouter(CasVerifyResult fixedResult)
        {
            _fixed = fixedResult;
        }

        public Task<CasVerifyResult> VerifyAsync(
            CasVerifyRequest request, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(_fixed);
        }
    }
}
