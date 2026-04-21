// =============================================================================
// Cena Platform — StudentPlanCommandHandler.ParentVisibility (prr-230)
//
// Partial containing the SetParentVisibility command handler. Kept in its own
// file so the core handler + QuestionPapers partial + this one all stay
// comfortably under the 500-LOC cap (architecture ratchet).
//
// Authority model:
//   This handler does NOT inspect the student's age band — authority is
//   checked at the endpoint layer (student-me visibility endpoint) against
//   the authoritative AgeBandPolicy lookup. The handler validates only
//   target-existence + non-archived + non-no-op semantics; emits a
//   ParentVisibilityChanged_V1 event with full audit metadata.
//
//   Safety-flag carve-out: attempting to set Hidden on a SafetyFlag-tagged
//   target is REJECTED — safety-flagged targets remain Visible regardless
//   of student preference, mirroring ADR-0041 duty-of-care.
// =============================================================================

using Cena.Actors.StudentPlan.Events;

namespace Cena.Actors.StudentPlan;

public sealed partial class StudentPlanCommandHandler
{
    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(
        SetParentVisibilityCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        var aggregate = await _store.LoadAsync(cmd.StudentAnonId, ct).ConfigureAwait(false);
        var state = aggregate.State;

        var target = state.Targets.FirstOrDefault(t => t.Id == cmd.TargetId);
        if (target is null)
        {
            return new CommandResult(Success: false, Error: CommandError.TargetNotFound);
        }
        if (!target.IsActive)
        {
            return new CommandResult(Success: false, Error: CommandError.TargetArchived);
        }

        // Safety-flag carve-out: cannot hide a safety-flagged target.
        // The student may still RE-SET to Visible (no-op on a safety target,
        // but we allow the event for audit clarity).
        if (target.IsSafetyFlagged && cmd.Visibility == ParentVisibility.Hidden)
        {
            return new CommandResult(
                Success: false,
                Error: CommandError.ParentVisibilitySafetyFlagLocked);
        }

        // No-op guard: if visibility is already the requested value, succeed
        // without emitting a noisy event.
        if (target.ParentVisibility == cmd.Visibility)
        {
            return new CommandResult(Success: true, TargetId: cmd.TargetId);
        }

        var now = _clock();
        await _store.AppendAsync(
            cmd.StudentAnonId,
            new ParentVisibilityChanged_V1(
                StudentAnonId: cmd.StudentAnonId,
                TargetId: cmd.TargetId,
                Visibility: cmd.Visibility,
                Initiator: cmd.Initiator,
                InitiatorActorId: cmd.InitiatorActorId,
                Reason: string.IsNullOrWhiteSpace(cmd.Reason) ? "unspecified" : cmd.Reason,
                ChangedAt: now),
            ct).ConfigureAwait(false);

        return new CommandResult(Success: true, TargetId: cmd.TargetId);
    }
}
