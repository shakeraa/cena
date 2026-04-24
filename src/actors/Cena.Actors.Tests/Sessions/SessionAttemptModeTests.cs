// =============================================================================
// Cena Platform — SessionAttemptMode tests (PRR-260)
//
// Pure tests against the wire parser + the read-side override policy.
// Locks the four invariants from the PRR-260 scope:
//   1. Wire strings are stable (visible / hidden_reveal); unknown → reject.
//   2. Non-MC questions render Visible regardless of stored mode.
//   3. Author's forceOptionsVisible wins over stored mode.
//   4. Diagnostic block forces Visible (PRR-263 calibration contract).
// Plus the precedence matrix for overlapping overrides.
// =============================================================================

using Cena.Actors.Sessions;
using Xunit;

namespace Cena.Actors.Tests.Sessions;

public class SessionAttemptModeTests
{
    // ---- Wire parser -------------------------------------------------------

    [Theory]
    [InlineData("visible", SessionAttemptMode.Visible)]
    [InlineData("VISIBLE", SessionAttemptMode.Visible)]
    [InlineData(" Visible ", SessionAttemptMode.Visible)]
    [InlineData("hidden_reveal", SessionAttemptMode.HiddenReveal)]
    [InlineData("HIDDEN_REVEAL", SessionAttemptMode.HiddenReveal)]
    public void TryParse_accepts_canonical_wire_values_case_insensitive(
        string input, SessionAttemptMode expected)
    {
        Assert.True(SessionAttemptModeWire.TryParse(input, out var mode));
        Assert.Equal(expected, mode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("always_visible")]
    [InlineData("HiddenFirst")]          // similar but not the canonical form
    [InlineData("reveal")]
    public void TryParse_rejects_unknown_or_empty_input(string? input)
    {
        Assert.False(SessionAttemptModeWire.TryParse(input, out _));
    }

    [Fact]
    public void ToWire_roundtrips_Visible()
    {
        Assert.Equal("visible", SessionAttemptModeWire.ToWire(SessionAttemptMode.Visible));
    }

    [Fact]
    public void ToWire_roundtrips_HiddenReveal()
    {
        Assert.Equal("hidden_reveal", SessionAttemptModeWire.ToWire(SessionAttemptMode.HiddenReveal));
    }

    // ---- Read-side override policy -----------------------------------------

    [Fact]
    public void Policy_default_visible_when_stored_is_visible()
    {
        var ctx = new SessionAttemptModeContext(
            StoredMode: SessionAttemptMode.Visible,
            IsQuestionMultipleChoice: true,
            AuthorForceVisible: false,
            IsDiagnosticBlock: false);
        Assert.Equal(SessionAttemptMode.Visible, SessionAttemptModePolicy.ResolveEffective(ctx));
    }

    [Fact]
    public void Policy_returns_stored_hidden_reveal_when_no_overrides_apply()
    {
        var ctx = new SessionAttemptModeContext(
            StoredMode: SessionAttemptMode.HiddenReveal,
            IsQuestionMultipleChoice: true,
            AuthorForceVisible: false,
            IsDiagnosticBlock: false);
        Assert.Equal(SessionAttemptMode.HiddenReveal, SessionAttemptModePolicy.ResolveEffective(ctx));
    }

    [Fact]
    public void Policy_non_MC_question_forces_Visible_regardless_of_stored_mode()
    {
        // Step-solver math / chem / essay items: hide affordance is a no-op.
        var ctx = new SessionAttemptModeContext(
            StoredMode: SessionAttemptMode.HiddenReveal,
            IsQuestionMultipleChoice: false,
            AuthorForceVisible: false,
            IsDiagnosticBlock: false);
        Assert.Equal(SessionAttemptMode.Visible, SessionAttemptModePolicy.ResolveEffective(ctx));
    }

    [Fact]
    public void Policy_author_force_visible_overrides_stored_hidden_reveal()
    {
        // "Which graph is correct?" items — the options ARE the question.
        var ctx = new SessionAttemptModeContext(
            StoredMode: SessionAttemptMode.HiddenReveal,
            IsQuestionMultipleChoice: true,
            AuthorForceVisible: true,
            IsDiagnosticBlock: false);
        Assert.Equal(SessionAttemptMode.Visible, SessionAttemptModePolicy.ResolveEffective(ctx));
    }

    [Fact]
    public void Policy_diagnostic_block_forces_Visible_for_calibration_contract()
    {
        // PRR-263: diagnostic blocks ignore hide-then-reveal so per-target
        // calibration stays comparable across students.
        var ctx = new SessionAttemptModeContext(
            StoredMode: SessionAttemptMode.HiddenReveal,
            IsQuestionMultipleChoice: true,
            AuthorForceVisible: false,
            IsDiagnosticBlock: true);
        Assert.Equal(SessionAttemptMode.Visible, SessionAttemptModePolicy.ResolveEffective(ctx));
    }

    [Fact]
    public void Policy_overrides_precedence_non_MC_beats_everything()
    {
        // Non-MC + hidden_reveal stored + author force + diagnostic:
        // still Visible (non-MC is first in the precedence list).
        var ctx = new SessionAttemptModeContext(
            StoredMode: SessionAttemptMode.HiddenReveal,
            IsQuestionMultipleChoice: false,
            AuthorForceVisible: true,
            IsDiagnosticBlock: true);
        Assert.Equal(SessionAttemptMode.Visible, SessionAttemptModePolicy.ResolveEffective(ctx));
    }

    [Fact]
    public void Policy_all_overrides_stacked_all_resolve_Visible()
    {
        // Sanity: every override lane independently forces Visible; no
        // combination can "unlock" HiddenReveal once any override is on.
        foreach (var isMc in new[] { true, false })
        foreach (var force in new[] { true, false })
        foreach (var diag in new[] { true, false })
        {
            var ctx = new SessionAttemptModeContext(
                StoredMode: SessionAttemptMode.HiddenReveal,
                IsQuestionMultipleChoice: isMc,
                AuthorForceVisible: force,
                IsDiagnosticBlock: diag);
            var effective = SessionAttemptModePolicy.ResolveEffective(ctx);
            var shouldBeHidden = isMc && !force && !diag;
            Assert.Equal(
                shouldBeHidden ? SessionAttemptMode.HiddenReveal : SessionAttemptMode.Visible,
                effective);
        }
    }

    [Fact]
    public void Policy_rejects_null_context()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SessionAttemptModePolicy.ResolveEffective(null!));
    }

    // ---- Default projection value invariant --------------------------------

    [Fact]
    public void Projection_default_AttemptMode_parses_to_Visible_enum()
    {
        // Forward-compat guard: an older session projection that predates
        // the AttemptMode field will default-initialize to the empty
        // string on deserialization. The endpoint's Canonicalise helper
        // reads that as Visible — but the PROJECTION default constant
        // itself must already be "visible" so a fresh session is
        // immediately toggleable without a lazy default-set on first read.
        var defaultValue = Cena.Actors.Sessions.SessionAttemptModeWire.Visible;
        Assert.True(SessionAttemptModeWire.TryParse(defaultValue, out var mode));
        Assert.Equal(SessionAttemptMode.Visible, mode);
    }

    // ---- PRR-263 diagnostic carve-out (property tests) ---------------------

    [Theory]
    [InlineData("visible")]
    [InlineData("hidden_reveal")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown_mode_from_future_client")]
    public void Builder_diagnostic_session_always_resolves_Visible_regardless_of_stored_mode(
        string? storedAttemptMode)
    {
        // PRR-263 property: any diagnostic session × any stored attemptMode
        // value must render Visible. Persona-cogsci blocker — mixing
        // retrieval-practice (hide-then-reveal) with retrieval-strength
        // assessment (PRR-228 diagnostic) contaminates the posterior.
        var effective = SessionAttemptModeContextBuilder.ResolveFrom(
            sessionMode: "diagnostic",
            storedAttemptModeWire: storedAttemptMode,
            isMultipleChoice: true,
            authorForceVisible: false);
        Assert.Equal(SessionAttemptMode.Visible, effective);
    }

    [Theory]
    [InlineData("Diagnostic")]
    [InlineData("DIAGNOSTIC")]
    [InlineData(" diagnostic ")]
    public void Builder_diagnostic_mode_string_is_case_and_whitespace_tolerant(
        string diagnosticModeLabel)
    {
        // The session-mode string comes from a config / upstream feed; a
        // future producer might emit with capitalisation drift. The
        // carve-out must still trigger so the property test doesn't miss
        // a diagnostic session because of a Mode casing change.
        var effective = SessionAttemptModeContextBuilder.ResolveFrom(
            sessionMode: diagnosticModeLabel,
            storedAttemptModeWire: "hidden_reveal",
            isMultipleChoice: true,
            authorForceVisible: false);
        Assert.Equal(SessionAttemptMode.Visible, effective);
    }

    [Theory]
    [InlineData("practice")]
    [InlineData("review")]
    [InlineData("challenge")]
    [InlineData("exam")]
    [InlineData("")]
    [InlineData(null)]
    public void Builder_non_diagnostic_session_honours_stored_hidden_reveal_mode(
        string? nonDiagnosticMode)
    {
        // Non-diagnostic sessions (practice / review / challenge / exam /
        // unset) must NOT force Visible — they return the stored mode so
        // a student who opted into hide-then-reveal gets it during
        // practice. This is the inverse guard: the carve-out is scoped
        // to diagnostic specifically, not any "non-practice" session.
        var effective = SessionAttemptModeContextBuilder.ResolveFrom(
            sessionMode: nonDiagnosticMode,
            storedAttemptModeWire: "hidden_reveal",
            isMultipleChoice: true,
            authorForceVisible: false);
        Assert.Equal(SessionAttemptMode.HiddenReveal, effective);
    }

    [Fact]
    public void Builder_cross_session_stored_mode_does_not_leak_into_diagnostic()
    {
        // Regression guard: a student who opts into hidden_reveal in a
        // practice session (queue.AttemptMode = "hidden_reveal") then
        // starts a diagnostic session. The diagnostic queue is a different
        // projection — but even if the same queue doc were reused (bug),
        // the carve-out must still fire. Simulate by passing the stored
        // hidden_reveal + the diagnostic mode in one Build call; the
        // effective mode is still Visible.
        var effective = SessionAttemptModeContextBuilder.ResolveFrom(
            sessionMode: "diagnostic",
            storedAttemptModeWire: "hidden_reveal",
            isMultipleChoice: true,
            authorForceVisible: false);
        Assert.Equal(SessionAttemptMode.Visible, effective);
    }

    [Fact]
    public void Builder_unknown_stored_mode_parses_as_safe_default_Visible()
    {
        // A storage bug that writes a bogus value to queue.AttemptMode
        // (e.g. from a mid-deployment schema mismatch) must not crash
        // rendering. Default to Visible — the safe "traditional render"
        // fallback is always correct to show.
        var effective = SessionAttemptModeContextBuilder.ResolveFrom(
            sessionMode: "practice",
            storedAttemptModeWire: "garbage_from_future",
            isMultipleChoice: true,
            authorForceVisible: false);
        Assert.Equal(SessionAttemptMode.Visible, effective);
    }

    [Fact]
    public void Builder_DiagnosticModeString_constant_matches_canonical_value()
    {
        // Canonical session-mode string used across AdaptiveQuestionPool +
        // this carve-out must stay in sync. If AdaptiveQuestionPool
        // starts emitting a different string, this test fails loudly.
        Assert.Equal("diagnostic", SessionAttemptModeContextBuilder.DiagnosticModeString);
    }
}
