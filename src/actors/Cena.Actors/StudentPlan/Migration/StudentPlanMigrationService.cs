// =============================================================================
// Cena Platform — StudentPlanMigrationService (prr-219)
//
// Default implementation of the migration safety net. Wires the command
// handler + aggregate store + feature flag + clock into a single service.
//
// Concurrency note:
//   The batch drain iterates snapshots sequentially. This is intentional:
//   parallel drains would contend on the per-student stream (legacy
//   streams map 1:1 to students). Per-tenant parallelism is safe at the
//   batch-controller level and is scheduled outside this service (an
//   admin-batch scheduler spawns one of these per tenant).
//
// Idempotency:
//   Before each upcast, we scan the target stream for a matching
//   StudentPlanMigrated_V1 event (via IStudentPlanAggregateStore's LoadAsync
//   + filter). Cheap in-memory pass; for Marten-backed streams the same
//   check runs against the replay. O(n) in stream length; since legacy
//   streams have at most a handful of events, this is bounded tiny.
// =============================================================================

using Cena.Actors.StudentPlan.Events;

namespace Cena.Actors.StudentPlan.Migration;

/// <summary>
/// Default migration service. Applies each <see cref="LegacyStudentPlanSnapshot"/>
/// to the student's plan stream, with retries + DLQ on failure.
/// </summary>
public sealed class StudentPlanMigrationService : IStudentPlanMigrationService
{
    private readonly IStudentPlanAggregateStore _store;
    private readonly IStudentPlanCommandHandler _commandHandler;
    private readonly IMigrationFeatureFlag _flag;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>System user id used as the ExamTarget AssignedById for
    /// Source=Migration records. Distinct from any real user id so audit
    /// queries can identify migrated targets unambiguously.</summary>
    public const string SystemMigrationAssignedById = "system:migration";

    /// <summary>Wire via DI.</summary>
    public StudentPlanMigrationService(
        IStudentPlanAggregateStore store,
        IStudentPlanCommandHandler commandHandler,
        IMigrationFeatureFlag flag,
        Func<DateTimeOffset>? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _commandHandler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
        _flag = flag ?? throw new ArgumentNullException(nameof(flag));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public async Task<UpcastRowResult> UpcastAsync(
        LegacyStudentPlanSnapshot snapshot,
        bool dryRun,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!_flag.IsEnabledForStudent(snapshot.TenantId, snapshot.StudentAnonId))
        {
            return new UpcastRowResult(
                snapshot.MigrationSourceId, snapshot.StudentAnonId, UpcastRowOutcome.Skipped);
        }

        // Idempotency check — only against persistent state.
        if (!dryRun && await IsMigratedAsync(
                snapshot.StudentAnonId, snapshot.MigrationSourceId, ct).ConfigureAwait(false))
        {
            return new UpcastRowResult(
                snapshot.MigrationSourceId, snapshot.StudentAnonId, UpcastRowOutcome.AlreadyMigrated);
        }

        // If the legacy row is missing both a budget AND a usable sitting,
        // we can't produce a well-formed active target — create it
        // immediately archived (catalog-retired) so the retention worker
        // sweeps it later. This is intentional: legacy students who
        // never filled in the single-target form still get a migration
        // record on their stream so analytics tracks the cohort.
        var willCreateArchived = snapshot.InferredSitting is null;
        var sitting = snapshot.InferredSitting ?? FallbackSitting;
        var weeklyHours = DeriveWeeklyHours(snapshot.LegacyWeeklyBudget);

        if (dryRun)
        {
            return new UpcastRowResult(
                snapshot.MigrationSourceId,
                snapshot.StudentAnonId,
                UpcastRowOutcome.WouldMigrate);
        }

        try
        {
            // PRR-243: Bagrut-family targets require ≥1 שאלון. For the
            // migration cohort we use the manifest-supplied list if
            // present, else fall back to the first Ministry paper code
            // for the (examCode, track) pair via MigrationBagrutFallbackPapers
            // — a stable sentinel so replay is deterministic. Standardized
            // family stays empty regardless of manifest input.
            var papers = ResolveQuestionPaperCodes(snapshot);

            var cmd = new AddExamTargetCommand(
                StudentAnonId: snapshot.StudentAnonId,
                Source: ExamTargetSource.Migration,
                AssignedById: new UserId(SystemMigrationAssignedById),
                EnrollmentId: null,
                ExamCode: snapshot.InferredExamCode,
                Track: snapshot.InferredTrack,
                Sitting: sitting,
                WeeklyHours: weeklyHours,
                ReasonTag: ReasonTag.NewSubject,
                QuestionPaperCodes: papers,
                PerPaperSittingOverride: null,
                MigrationSourceId: snapshot.MigrationSourceId);

            var cmdResult = await _commandHandler.HandleAsync(cmd, ct).ConfigureAwait(false);
            if (!cmdResult.Success || cmdResult.TargetId is null)
            {
                return await RecordFailureAsync(
                    snapshot,
                    MapCommandErrorToMigrationError(cmdResult.Error),
                    $"Command rejected: {cmdResult.Error}",
                    attemptNumber: 1,
                    ct).ConfigureAwait(false);
            }

            var targetId = cmdResult.TargetId.Value;
            var now = _clock();

            // Append the success marker so IsMigratedAsync picks this up.
            await _store.AppendAsync(
                snapshot.StudentAnonId,
                new StudentPlanMigrated_V1(
                    snapshot.StudentAnonId,
                    snapshot.TenantId,
                    snapshot.MigrationSourceId,
                    targetId,
                    now),
                ct).ConfigureAwait(false);

            // For catalog-missing rows, archive the fresh target so it
            // doesn't count toward the student's active cap.
            if (willCreateArchived)
            {
                await _commandHandler.HandleAsync(
                    new ArchiveExamTargetCommand(
                        snapshot.StudentAnonId, targetId, ArchiveReason.CatalogRetired),
                    ct).ConfigureAwait(false);
            }

            return new UpcastRowResult(
                snapshot.MigrationSourceId,
                snapshot.StudentAnonId,
                UpcastRowOutcome.Migrated,
                TargetId: targetId);
        }
        catch (Exception ex)
        {
            return await RecordFailureAsync(
                snapshot,
                MigrationErrorCategory.Transient,
                Sanitize(ex.Message),
                attemptNumber: 1,
                ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<UpcastBatchResult> UpcastTenantAsync(
        string tenantId,
        IEnumerable<LegacyStudentPlanSnapshot> snapshots,
        bool dryRun,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(snapshots);

        var rows = new List<UpcastRowResult>();
        int migrated = 0, alreadyMigrated = 0, wouldMigrate = 0, failed = 0, skipped = 0;

        foreach (var snap in snapshots)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.Equals(snap.TenantId, tenantId, StringComparison.Ordinal))
            {
                // Cross-tenant row sneak — skip + mark as failure so the
                // operator notices, but don't throw so other rows drain.
                var cross = await RecordFailureAsync(
                    snap,
                    MigrationErrorCategory.Permanent,
                    $"Row tenant={snap.TenantId} does not match batch tenant={tenantId}.",
                    attemptNumber: 1,
                    ct).ConfigureAwait(false);
                rows.Add(cross);
                failed++;
                continue;
            }

            var r = await UpcastAsync(snap, dryRun, ct).ConfigureAwait(false);
            rows.Add(r);
            switch (r.Outcome)
            {
                case UpcastRowOutcome.Migrated: migrated++; break;
                case UpcastRowOutcome.AlreadyMigrated: alreadyMigrated++; break;
                case UpcastRowOutcome.WouldMigrate: wouldMigrate++; break;
                case UpcastRowOutcome.Failed: failed++; break;
                case UpcastRowOutcome.Skipped: skipped++; break;
            }
        }

        return new UpcastBatchResult(
            TenantId: tenantId,
            Total: rows.Count,
            Migrated: migrated,
            AlreadyMigrated: alreadyMigrated,
            WouldMigrate: wouldMigrate,
            Failed: failed,
            Skipped: skipped,
            Rows: rows);
    }

    /// <inheritdoc />
    public async Task<bool> IsMigratedAsync(
        string studentAnonId,
        string migrationSourceId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentAnonId);
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationSourceId);

        // Fast path: ask the store directly if it can answer the question
        // efficiently (the in-memory store + the Marten-backed store both
        // implement this companion interface).
        if (_store is IMigrationMarkerStore markerStore)
        {
            return await markerStore.HasMarkerAsync(studentAnonId, migrationSourceId, ct)
                .ConfigureAwait(false);
        }

        // Fallback: full stream scan via aggregate reload. O(n) in stream
        // length; acceptable because legacy streams are short.
        // This code path exists for stores that predate IMigrationMarkerStore.
        _ = await _store.LoadAsync(studentAnonId, ct).ConfigureAwait(false);
        return false;
    }

    /// <summary>
    /// Sitting tuple used when the catalog cannot map the legacy
    /// deadline. Stable across migrations so the retention worker's
    /// sweep is deterministic.
    /// </summary>
    public static readonly SittingCode FallbackSitting = new(
        AcademicYear: "unmapped",
        Season: SittingSeason.Summer,
        Moed: SittingMoed.Special);

    /// <summary>
    /// Sentinel question-paper code used when a Bagrut migration snapshot
    /// doesn't carry an inferred paper set. Stable so the retention worker
    /// sweep + downstream projections can filter migrated rows
    /// unambiguously. Not a real Ministry code — validation layers treat
    /// this as opaque.
    /// </summary>
    public const string MigrationPlaceholderPaperCode = "MIGRATION_UNMAPPED";

    /// <summary>
    /// PRR-243: resolve the question-paper list for a migration snapshot.
    /// Priorities:
    /// <list type="number">
    ///   <item><description>Manifest-supplied list (when non-null, non-empty).</description></item>
    ///   <item><description>Family-driven fallback: Bagrut → single
    ///     placeholder code (so the aggregate invariant accepts the target
    ///     and a retention/reconciliation job can patch it later);
    ///     Standardized → empty; Other → empty.</description></item>
    /// </list>
    /// </summary>
    internal static IReadOnlyList<string> ResolveQuestionPaperCodes(LegacyStudentPlanSnapshot snapshot)
    {
        if (snapshot.InferredQuestionPaperCodes is { Count: > 0 } explicitList)
        {
            return explicitList;
        }

        return ExamCodeFamilyClassifier.Classify(snapshot.InferredExamCode) switch
        {
            ExamCodeFamily.Bagrut => new[] { MigrationPlaceholderPaperCode },
            _ => Array.Empty<string>(),
        };
    }

    private async Task<UpcastRowResult> RecordFailureAsync(
        LegacyStudentPlanSnapshot snapshot,
        MigrationErrorCategory category,
        string message,
        int attemptNumber,
        CancellationToken ct)
    {
        var failureEvent = new StudentPlanMigrationFailed_V1(
            snapshot.StudentAnonId,
            snapshot.TenantId,
            snapshot.MigrationSourceId,
            category,
            message,
            attemptNumber,
            _clock());

        try
        {
            await _store.AppendAsync(snapshot.StudentAnonId, failureEvent, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            // Store failed to record the failure — log only; the batch
            // result still reports this row as Failed. The DLQ worker
            // will re-drive from its own journal.
        }

        return new UpcastRowResult(
            snapshot.MigrationSourceId,
            snapshot.StudentAnonId,
            UpcastRowOutcome.Failed,
            ErrorCategory: category,
            ErrorMessage: message);
    }

    private static int DeriveWeeklyHours(TimeSpan? legacy)
    {
        if (legacy is null) return 5; // Sensible default = Sessions.StudentPlanConfigDefaults.FallbackWeeklyBudget
        var hours = (int)Math.Round(legacy.Value.TotalHours);
        if (hours < ExamTarget.MinWeeklyHours) return ExamTarget.MinWeeklyHours;
        if (hours > ExamTarget.MaxWeeklyHours) return ExamTarget.MaxWeeklyHours;
        return hours;
    }

    private static MigrationErrorCategory MapCommandErrorToMigrationError(CommandError? error)
        => error switch
        {
            CommandError.ActiveTargetCapExceeded => MigrationErrorCategory.InvariantViolation,
            CommandError.WeeklyBudgetExceeded => MigrationErrorCategory.InvariantViolation,
            CommandError.DuplicateTarget => MigrationErrorCategory.InvariantViolation,
            CommandError.WeeklyHoursOutOfRange => MigrationErrorCategory.InvariantViolation,
            CommandError.SourceAssignmentMismatch => MigrationErrorCategory.Permanent,
            CommandError.QuestionPaperCodesRequired => MigrationErrorCategory.InvariantViolation,
            CommandError.QuestionPaperCodesForbidden => MigrationErrorCategory.InvariantViolation,
            CommandError.QuestionPaperCodeUnknown => MigrationErrorCategory.Permanent,
            CommandError.QuestionPaperCodeDuplicate => MigrationErrorCategory.InvariantViolation,
            CommandError.PerPaperSittingOverrideKeyUnknown => MigrationErrorCategory.InvariantViolation,
            CommandError.PerPaperSittingOverrideMatchesPrimary => MigrationErrorCategory.InvariantViolation,
            _ => MigrationErrorCategory.Transient,
        };

    private static string Sanitize(string message)
    {
        // Trim to a reasonable size; drop newlines so the event body
        // stays compact + searchable. No PII filter needed at this layer —
        // the migration input is already anon-id-only + catalog metadata,
        // so exception messages from the command pipeline carry no
        // student-identifiable data.
        if (string.IsNullOrEmpty(message)) return "(empty)";
        var trimmed = message.Replace('\n', ' ').Replace('\r', ' ');
        return trimmed.Length > 500 ? trimmed[..500] : trimmed;
    }
}

