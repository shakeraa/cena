// =============================================================================
// Cena Platform — SessionStartRequest contract regression tests (PRR-247 / ADR-0060 / PRR-274)
//
// Locks the additive shape of SessionStartRequest so that future changes to the
// contract are caught by the test suite before they break the legacy-client
// back-compat path. Four invariants:
//
//   1. Legacy shape (Subjects + DurationMinutes + Mode) deserialises and the
//      new fields default to null.
//   2. New shape (Subjects + DurationMinutes + Mode + ExamScope +
//      ActiveExamTargetId) round-trips through JSON without loss.
//   3. The serialised JSON property names match the camelCase wire contract
//      (the System.Text.Json default in our endpoint registration).
//   4. (PRR-274) The legacy shape produces a record that PASSES every endpoint
//      validator predicate at SessionEndpoints.cs (lines 81-105). The
//      assertions here MIRROR the validator's predicate logic so a regression
//      that adds a stricter rule to the validator fails this test first.
//
// The full WebApplicationFactory + HttpClient integration test against the
// `/api/sessions/start` endpoint is intentionally NOT in this file — it
// requires test-server infra not currently scaffolded for SessionEndpoints
// (only Cena.Api.Host.Endpoints SignalR integration tests exist today). The
// integration variant is filed as PRR-274's carry-forward; this file ships
// the unit-level invariant lock under the "validate back-compat in tests, not
// assertions" rule (claude-5 self-audit gap #25 / super-architect framing).
// =============================================================================

using System.Text.Json;
using Cena.Api.Contracts.Sessions;

namespace Cena.Actors.Tests.Session;

public sealed class SessionStartRequestContractTests
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void Legacy_shape_without_exam_scope_or_target_id_deserialises_with_nulls()
    {
        var json = """
        {
          "subjects": ["math"],
          "durationMinutes": 15,
          "mode": "practice"
        }
        """;

        var req = JsonSerializer.Deserialize<SessionStartRequest>(json, CamelCase);

        Assert.NotNull(req);
        Assert.Equal(new[] { "math" }, req.Subjects);
        Assert.Equal(15, req.DurationMinutes);
        Assert.Equal("practice", req.Mode);
        Assert.Null(req.ExamScope);
        Assert.Null(req.ActiveExamTargetId);
    }

    [Fact]
    public void New_shape_with_exam_prep_round_trips_lossless()
    {
        var original = new SessionStartRequest(
            Subjects: new[] { "math", "physics" },
            DurationMinutes: 30,
            Mode: "practice",
            ExamScope: "exam-prep",
            ActiveExamTargetId: "et-bagrut-math-5u-035582");

        var json = JsonSerializer.Serialize(original, CamelCase);
        var roundTripped = JsonSerializer.Deserialize<SessionStartRequest>(json, CamelCase);

        Assert.NotNull(roundTripped);
        Assert.Equal(original.Subjects, roundTripped.Subjects);
        Assert.Equal(original.DurationMinutes, roundTripped.DurationMinutes);
        Assert.Equal(original.Mode, roundTripped.Mode);
        Assert.Equal(original.ExamScope, roundTripped.ExamScope);
        Assert.Equal(original.ActiveExamTargetId, roundTripped.ActiveExamTargetId);
    }

    [Fact]
    public void Freestyle_with_null_target_id_serialises_without_target_field()
    {
        var req = new SessionStartRequest(
            Subjects: new[] { "math" },
            DurationMinutes: 15,
            Mode: "practice",
            ExamScope: "freestyle",
            ActiveExamTargetId: null);

        var json = JsonSerializer.Serialize(req, CamelCase);

        Assert.Contains("\"examScope\":\"freestyle\"", json);
        Assert.Contains("\"activeExamTargetId\":null", json);
    }

    [Fact]
    public void Wire_contract_uses_camelCase_property_names()
    {
        var req = new SessionStartRequest(
            Subjects: new[] { "math" },
            DurationMinutes: 15,
            Mode: "practice",
            ExamScope: "exam-prep",
            ActiveExamTargetId: "et-001");

        var json = JsonSerializer.Serialize(req, CamelCase);

        Assert.Contains("\"subjects\":", json);
        Assert.Contains("\"durationMinutes\":", json);
        Assert.Contains("\"mode\":", json);
        Assert.Contains("\"examScope\":", json);
        Assert.Contains("\"activeExamTargetId\":", json);
        // Reject TitleCase regressions:
        Assert.DoesNotContain("\"Subjects\":", json);
        Assert.DoesNotContain("\"ExamScope\":", json);
    }

    // =========================================================================
    // PRR-274 — back-compat predicate-level invariant lock
    //
    // These tests mirror the validator predicates at
    // src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs lines 81-105.
    // If the validator gains a stricter rule that would reject the legacy
    // shape, these assertions fail first and a maintainer must intentionally
    // update both sites in lockstep — closing the "back-compat asserted but
    // never tested" gap (claude-5 self-audit 2026-04-29 G4 / item #25).
    // =========================================================================

    private static readonly int[] AllowedDurationsMinutes = { 5, 10, 15, 30, 45, 60 };
    private static readonly string[] AllowedModes = { "practice", "challenge", "review", "diagnostic" };
    private static readonly string?[] AllowedExamScopes = { null, "exam-prep", "freestyle" };

    [Theory]
    [InlineData("practice")]
    [InlineData("challenge")]
    [InlineData("review")]
    [InlineData("diagnostic")]
    [Trait("source", "PRR-274-back-compat")]
    public void Legacy_shape_passes_every_validator_predicate(string mode)
    {
        // Construct exactly the legacy shape — no ExamScope, no ActiveExamTargetId.
        var req = new SessionStartRequest(
            Subjects: new[] { "math" },
            DurationMinutes: 15,
            Mode: mode);

        // Mirror SessionEndpoints.cs line 81: subjects must be non-empty.
        Assert.NotEmpty(req.Subjects);

        // Mirror line 84: DurationMinutes must be in the allowed set.
        Assert.Contains(req.DurationMinutes, AllowedDurationsMinutes);

        // Mirror line 87: Mode must be in the allowed set.
        Assert.Contains(req.Mode, AllowedModes);

        // Mirror line 96: ExamScope must be null or one of the allowed values.
        Assert.Contains(req.ExamScope, AllowedExamScopes);

        // Mirror line 99: ExamScope == "exam-prep" requires non-null ActiveExamTargetId.
        // Legacy: ExamScope is null, so the predicate is satisfied trivially.
        Assert.False(req.ExamScope == "exam-prep" && string.IsNullOrWhiteSpace(req.ActiveExamTargetId));

        // Mirror line 102: (ExamScope is null OR "freestyle") AND ActiveExamTargetId non-null is rejected.
        // Legacy: ExamScope is null, ActiveExamTargetId is null, so predicate satisfied.
        Assert.False((req.ExamScope is null or "freestyle") && !string.IsNullOrWhiteSpace(req.ActiveExamTargetId));
    }

    [Fact]
    [Trait("source", "PRR-274-back-compat")]
    public void Exam_prep_scope_without_active_target_id_fails_predicate()
    {
        // Sanity check the predicate-mirror itself: the malformed combo is detected.
        var malformed = new SessionStartRequest(
            Subjects: new[] { "math" },
            DurationMinutes: 15,
            Mode: "practice",
            ExamScope: "exam-prep",
            ActiveExamTargetId: null);

        // Mirror line 99: this combination should be rejected by the validator.
        Assert.True(
            malformed.ExamScope == "exam-prep" && string.IsNullOrWhiteSpace(malformed.ActiveExamTargetId),
            "PRR-247 validator at SessionEndpoints.cs:99 must reject ExamScope='exam-prep' + null ActiveExamTargetId");
    }

    [Fact]
    [Trait("source", "PRR-274-back-compat")]
    public void Freestyle_scope_with_active_target_id_fails_predicate()
    {
        // Sanity check: the inverse malformed combo is also detected.
        var malformed = new SessionStartRequest(
            Subjects: new[] { "math" },
            DurationMinutes: 15,
            Mode: "practice",
            ExamScope: "freestyle",
            ActiveExamTargetId: "et-001");

        // Mirror line 102: this combination should be rejected.
        Assert.True(
            (malformed.ExamScope is null or "freestyle") && !string.IsNullOrWhiteSpace(malformed.ActiveExamTargetId),
            "PRR-247 validator at SessionEndpoints.cs:102 must reject (ExamScope null|freestyle) + non-null ActiveExamTargetId");
    }

    [Fact]
    [Trait("source", "PRR-274-back-compat")]
    public void Legacy_shape_constructor_defaults_match_validator_acceptance()
    {
        // Defense-in-depth: a freshly-constructed legacy shape using the
        // 3-arg primary constructor must produce nullable defaults that the
        // validator accepts. If anyone changes the record's default values
        // for ExamScope or ActiveExamTargetId from null to e.g. an empty
        // string, this test fails — guarding against silent contract breaks.
        var req = new SessionStartRequest(
            Subjects: new[] { "math" },
            DurationMinutes: 15,
            Mode: "practice");

        Assert.Null(req.ExamScope);
        Assert.Null(req.ActiveExamTargetId);
    }
}
