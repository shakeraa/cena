// =============================================================================
// Cena Platform — StuckContextBuilder (RDY-063 Phase 1)
//
// Caller-facing convenience for assembling a StuckContext from the
// pieces the LearningSessionActor already has on hand:
//   - question state / LO ids
//   - latest advancement snapshot (RDY-061)
//   - recent attempts + session signals
//
// The builder is deliberately *synchronous* and does not touch the
// database — callers must load the advancement document themselves so
// we don't hide I/O behind a seemingly-pure function.
//
// This file also centralises the PII guard: ForbiddenPatterns lists
// substrings that MUST NOT appear in any field of the produced
// StuckContext; if they do, the builder throws. The arch test calls
// into the same guard to enforce the invariant repo-wide.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Diagnosis;

public interface IStuckContextBuilder
{
    StuckContext Build(StuckContextInputs inputs);
}

/// <summary>
/// Caller-side input bag. The builder turns these raw fields into an
/// anonymized, guard-checked StuckContext.
/// </summary>
public sealed record StuckContextInputs(
    string StudentId,
    string SessionId,
    string Locale,
    StuckContextQuestion Question,
    StuckContextAdvancement Advancement,
    IReadOnlyList<StuckContextAttempt> Attempts,
    StuckContextSessionSignals SessionSignals,
    DateTimeOffset AsOf);

public sealed class StuckContextBuilder : IStuckContextBuilder
{
    private readonly IStuckAnonymizer _anonymizer;

    public StuckContextBuilder(IStuckAnonymizer anonymizer)
    {
        _anonymizer = anonymizer ?? throw new ArgumentNullException(nameof(anonymizer));
    }

    public StuckContext Build(StuckContextInputs inputs)
    {
        if (inputs is null) throw new ArgumentNullException(nameof(inputs));

        // Compute the anon id BEFORE any field assembly so the rest of
        // the pipeline never sees the raw studentId.
        var anonId = _anonymizer.Anonymize(inputs.StudentId, inputs.SessionId);

        var ctx = new StuckContext(
            SessionId: inputs.SessionId,
            StudentAnonId: anonId,
            Question: inputs.Question,
            Advancement: inputs.Advancement,
            Attempts: inputs.Attempts,
            SessionSignals: inputs.SessionSignals,
            Locale: inputs.Locale,
            AsOf: inputs.AsOf);

        // Invariant guard: no PII substring may leak into any field.
        // We scan the serialised shape rather than field-by-field so
        // additions to StuckContext that forget the guard still trip.
        AssertNoPii(ctx, inputs.StudentId);

        return ctx;
    }

    // ── PII guard ──────────────────────────────────────────────────────

    // Literal substrings that must not appear in any field of a built
    // StuckContext. "@" catches emails; "studentId" / "email" / "fullName"
    // catch field-name confusion.
    internal static readonly IReadOnlyList<string> ForbiddenLiterals = new[]
    {
        "@",
        "studentId",
        "student_id",
        "email",
        "fullName",
        "full_name",
    };

    // PII pattern — an exact-match of the raw studentId in any field of
    // the StuckContext is a bug: the anonymizer should have replaced
    // every reference.
    public static void AssertNoPii(StuckContext ctx, string rawStudentId)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        if (string.IsNullOrEmpty(rawStudentId)) return;

        var serialised = System.Text.Json.JsonSerializer.Serialize(ctx);

        foreach (var literal in ForbiddenLiterals)
        {
            if (serialised.Contains(literal, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"StuckContext contains forbidden substring '{literal}' — " +
                    "the builder's PII guard rejected the input.");
        }

        if (serialised.Contains(rawStudentId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "StuckContext contains the raw studentId — anonymizer was bypassed.");
    }
}
