// =============================================================================
// Cena Platform — StudentPlanCommandHandler (prr-243, ADR-0050 §1)
//
// Partial — ships the four שאלון post-hoc commands separately from the
// wave-1 core (Add / Update / Archive / Complete / Override). Kept under
// its own roof so the core file stays under the 500-LOC cap.
//
// Commands handled here:
//   - AddQuestionPaperCommand
//   - RemoveQuestionPaperCommand
//   - SetPerPaperSittingOverrideCommand
//   - ClearPerPaperSittingOverrideCommand
//
// Also exports the two normalise helpers
// (NormaliseQuestionPaperCodes + NormalisePerPaperSittingOverride) used
// by the on-create path in the core AddExamTargetCommand handler.
//
// Invariants enforced here (ADR-0050 §1, PRR-243 DoD):
//   - Bagrut family ⇒ ≥1 paper (enforced on Add + Remove rejections).
//   - Standardized family ⇒ 0 papers (rejects AddQuestionPaper up-front).
//   - PerPaperSittingOverride keys ⊆ QuestionPaperCodes.
//   - PerPaperSittingOverride values ≠ primary Sitting (minimal map).
//   - Removing the last שאלון on a Bagrut target is rejected — caller
//     must archive the target instead.
// =============================================================================

using Cena.Actors.StudentPlan.Events;

namespace Cena.Actors.StudentPlan;

public sealed partial class StudentPlanCommandHandler
{
    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(AddQuestionPaperCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentException.ThrowIfNullOrWhiteSpace(cmd.PaperCode);

        var aggregate = await _store.LoadAsync(cmd.StudentAnonId, ct).ConfigureAwait(false);
        var target = aggregate.State.Targets.FirstOrDefault(t => t.Id == cmd.TargetId);
        if (target is null)
        {
            return new CommandResult(Success: false, Error: CommandError.TargetNotFound);
        }
        if (!target.IsActive)
        {
            return new CommandResult(Success: false, Error: CommandError.TargetArchived);
        }

        // Standardized family can never carry papers — reject up-front.
        if (target.Family == ExamCodeFamily.Standardized)
        {
            return new CommandResult(Success: false, Error: CommandError.QuestionPaperCodesForbidden);
        }

        if (target.QuestionPaperCodes.Contains(cmd.PaperCode))
        {
            return new CommandResult(Success: false, Error: CommandError.QuestionPaperCodeAlreadyPresent);
        }

        if (!_paperValidator.IsPaperCodeValid(target.ExamCode, target.Track, cmd.PaperCode))
        {
            return new CommandResult(Success: false, Error: CommandError.QuestionPaperCodeUnknown);
        }

        if (cmd.SittingOverride is { } ov && ov == target.Sitting)
        {
            return new CommandResult(Success: false, Error: CommandError.PerPaperSittingOverrideMatchesPrimary);
        }

        var now = _clock();
        await _store.AppendAsync(
            cmd.StudentAnonId,
            new QuestionPaperAdded_V1(cmd.StudentAnonId, cmd.TargetId, cmd.PaperCode, cmd.SittingOverride, now),
            ct).ConfigureAwait(false);

        return new CommandResult(Success: true, TargetId: cmd.TargetId);
    }

    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(RemoveQuestionPaperCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentException.ThrowIfNullOrWhiteSpace(cmd.PaperCode);

        var aggregate = await _store.LoadAsync(cmd.StudentAnonId, ct).ConfigureAwait(false);
        var target = aggregate.State.Targets.FirstOrDefault(t => t.Id == cmd.TargetId);
        if (target is null)
        {
            return new CommandResult(Success: false, Error: CommandError.TargetNotFound);
        }
        if (!target.IsActive)
        {
            return new CommandResult(Success: false, Error: CommandError.TargetArchived);
        }
        if (!target.QuestionPaperCodes.Contains(cmd.PaperCode))
        {
            return new CommandResult(Success: false, Error: CommandError.QuestionPaperCodeNotPresent);
        }

        // PRR-243 DoD: Bagrut-family removal must leave ≥1 paper.
        if (target.Family == ExamCodeFamily.Bagrut && target.QuestionPaperCodes.Count <= 1)
        {
            return new CommandResult(Success: false, Error: CommandError.QuestionPaperRemovalLeavesEmpty);
        }

        var now = _clock();
        await _store.AppendAsync(
            cmd.StudentAnonId,
            new QuestionPaperRemoved_V1(cmd.StudentAnonId, cmd.TargetId, cmd.PaperCode, now),
            ct).ConfigureAwait(false);

        return new CommandResult(Success: true, TargetId: cmd.TargetId);
    }

    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(SetPerPaperSittingOverrideCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentException.ThrowIfNullOrWhiteSpace(cmd.PaperCode);

        var aggregate = await _store.LoadAsync(cmd.StudentAnonId, ct).ConfigureAwait(false);
        var target = aggregate.State.Targets.FirstOrDefault(t => t.Id == cmd.TargetId);
        if (target is null)
        {
            return new CommandResult(Success: false, Error: CommandError.TargetNotFound);
        }
        if (!target.IsActive)
        {
            return new CommandResult(Success: false, Error: CommandError.TargetArchived);
        }
        if (!target.QuestionPaperCodes.Contains(cmd.PaperCode))
        {
            return new CommandResult(Success: false, Error: CommandError.PerPaperSittingOverrideKeyUnknown);
        }
        if (cmd.Sitting == target.Sitting)
        {
            return new CommandResult(Success: false, Error: CommandError.PerPaperSittingOverrideMatchesPrimary);
        }

        var now = _clock();
        await _store.AppendAsync(
            cmd.StudentAnonId,
            new PerPaperSittingOverrideSet_V1(cmd.StudentAnonId, cmd.TargetId, cmd.PaperCode, cmd.Sitting, now),
            ct).ConfigureAwait(false);

        return new CommandResult(Success: true, TargetId: cmd.TargetId);
    }

    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(ClearPerPaperSittingOverrideCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentException.ThrowIfNullOrWhiteSpace(cmd.PaperCode);

        var aggregate = await _store.LoadAsync(cmd.StudentAnonId, ct).ConfigureAwait(false);
        var target = aggregate.State.Targets.FirstOrDefault(t => t.Id == cmd.TargetId);
        if (target is null)
        {
            return new CommandResult(Success: false, Error: CommandError.TargetNotFound);
        }
        if (!target.IsActive)
        {
            return new CommandResult(Success: false, Error: CommandError.TargetArchived);
        }

        // Clearing is idempotent — if there's nothing to clear, we still
        // return success without emitting an event. Keeps callers simple
        // (PATCH {...override: null} is well-defined).
        if (target.PerPaperSittingOverride is null
            || !target.PerPaperSittingOverride.ContainsKey(cmd.PaperCode))
        {
            return new CommandResult(Success: true, TargetId: cmd.TargetId);
        }

        var now = _clock();
        await _store.AppendAsync(
            cmd.StudentAnonId,
            new PerPaperSittingOverrideCleared_V1(cmd.StudentAnonId, cmd.TargetId, cmd.PaperCode, now),
            ct).ConfigureAwait(false);

        return new CommandResult(Success: true, TargetId: cmd.TargetId);
    }

    // ── Normalisation helpers (used by the Add path in the core file) ─

    /// <summary>
    /// PRR-243: normalise + validate a list of Ministry שאלון codes for
    /// an ExamTarget being added. Produces a de-duplicated list preserving
    /// input order, and returns a <see cref="CommandError"/> when the
    /// list violates ADR-0050 §1 invariants.
    /// </summary>
    internal (IReadOnlyList<string> Papers, CommandError? Error) NormaliseQuestionPaperCodes(
        IReadOnlyList<string> input,
        ExamCode examCode,
        TrackCode? track)
    {
        // Reject empty / whitespace entries + de-duplicate in a single pass.
        var deduped = new List<string>(input.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in input)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var code = raw.Trim();
            if (!seen.Add(code))
            {
                return (Array.Empty<string>(), CommandError.QuestionPaperCodeDuplicate);
            }
            deduped.Add(code);
        }

        var family = ExamCodeFamilyClassifier.Classify(examCode);
        switch (family)
        {
            case ExamCodeFamily.Bagrut when deduped.Count == 0:
                return (Array.Empty<string>(), CommandError.QuestionPaperCodesRequired);
            case ExamCodeFamily.Standardized when deduped.Count > 0:
                return (Array.Empty<string>(), CommandError.QuestionPaperCodesForbidden);
        }

        // Catalog membership check — only meaningful when we have codes.
        foreach (var code in deduped)
        {
            if (!_paperValidator.IsPaperCodeValid(examCode, track, code))
            {
                return (Array.Empty<string>(), CommandError.QuestionPaperCodeUnknown);
            }
        }

        return (deduped, null);
    }

    /// <summary>
    /// PRR-243: normalise + validate a per-paper sitting override map.
    /// Enforces: keys ⊆ paperCodes, values ≠ primary Sitting, empty map
    /// is normalised to null.
    /// </summary>
    internal static (IReadOnlyDictionary<string, SittingCode>? Map, CommandError? Error) NormalisePerPaperSittingOverride(
        IReadOnlyDictionary<string, SittingCode>? input,
        IReadOnlyList<string> paperCodes,
        SittingCode primarySitting)
    {
        if (input is null || input.Count == 0)
        {
            return (null, null);
        }

        var paperSet = new HashSet<string>(paperCodes, StringComparer.Ordinal);
        var normalised = new Dictionary<string, SittingCode>(StringComparer.Ordinal);
        foreach (var (code, sitting) in input)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return (null, CommandError.PerPaperSittingOverrideKeyUnknown);
            }
            if (!paperSet.Contains(code))
            {
                return (null, CommandError.PerPaperSittingOverrideKeyUnknown);
            }
            if (sitting == primarySitting)
            {
                return (null, CommandError.PerPaperSittingOverrideMatchesPrimary);
            }
            normalised[code] = sitting;
        }

        return (normalised, null);
    }
}
