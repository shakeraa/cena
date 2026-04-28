// =============================================================================
// Cena Platform — SessionStartRequest contract regression tests (PRR-247 / ADR-0060)
//
// Locks the additive shape of SessionStartRequest so that future changes to the
// contract are caught by the test suite before they break the legacy-client
// back-compat path. Three invariants:
//
//   1. Legacy shape (Subjects + DurationMinutes + Mode) deserialises and the
//      new fields default to null.
//   2. New shape (Subjects + DurationMinutes + Mode + ExamScope +
//      ActiveExamTargetId) round-trips through JSON without loss.
//   3. The serialised JSON property names match the camelCase wire contract
//      (the System.Text.Json default in our endpoint registration).
//
// The endpoint-level validator (which actually rejects malformed combinations)
// is exercised in SessionEndpoint integration tests; this file is the unit-test
// floor for the record shape itself.
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
}
