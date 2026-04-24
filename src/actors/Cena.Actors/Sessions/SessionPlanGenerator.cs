// =============================================================================
// Cena Platform — SessionPlanGenerator (prr-149)
//
// The live production caller for AdaptiveScheduler.PrioritizeTopics. Owns
// the input-assembly concern: it reads the student's plan config, reads
// the student's per-topic ability estimates, builds a SchedulerInputs
// record, calls the scheduler, wraps the result in a SessionPlanSnapshot,
// and returns it.
//
// DESIGN CONSTRAINTS (enforced by SchedulerNoLlmCallTest):
//   - Pure heuristic path. This service MUST NOT depend on any LLM
//     client directly or transitively. The scheduler is not on the LLM
//     critical path per ADR-0026.
//   - Session-scoped output. The SessionPlanSnapshot returned here is
//     never persisted to a student-keyed document. Callers should emit
//     SessionPlanComputed_V1 to the `session-{id}` stream and/or store
//     a session-keyed Marten doc — not touch StudentProfileSnapshot.
//   - Input-source observability. The result carries an `InputsSource`
//     tag ("student-plan-config" vs "default-fallback") so ops can
//     monitor the prr-148 rollout without scraping logs.
//
// The actual "translate ConceptMasteryState → AbilityEstimate" mapping
// is intentionally narrow: the scheduler's weakness component wants a
// θ value, and mastery probability (0..1) maps to θ via the inverse-
// logit the IRT calibration uses (2 * (p - 0.5)). This is a coarse
// approximation good enough for the Phase-1 scheduler caller; a proper
// per-topic IRT θ will arrive when RDY-080 calibration ships.
// =============================================================================

using System.Collections.Immutable;
using Cena.Actors.Mastery;
using Cena.Actors.Sessions.Events;
using Cena.Actors.StudentPlan;
using Cena.Actors.Teacher.ScheduleOverride;

namespace Cena.Actors.Sessions;

/// <summary>
/// Per-topic ability signal the scheduler needs at session start.
/// Small abstraction so the API layer can supply estimates from
/// whatever source it owns (StudentProfileSnapshot.ConceptMastery
/// today; a dedicated AbilityEstimate projection post-RDY-080) without
/// this service knowing anything about Marten.
/// </summary>
public interface ISessionAbilityEstimateProvider
{
    /// <summary>
    /// Resolve the student's current per-topic ability estimates. Keys
    /// are topic slugs matching <see cref="BagrutTopicWeights"/>; the
    /// scheduler silently skips any topic without a weight entry, so
    /// unknown-topic keys in the returned map cost nothing. Missing
    /// topics are treated as "no evidence" and the scheduler will not
    /// schedule them. Implementations MUST return a non-null map; an
    /// empty map is a valid "cold-start" signal.
    /// </summary>
    Task<IReadOnlyDictionary<string, AbilityEstimate>> GetAsync(
        string studentAnonId, CancellationToken ct = default);
}

/// <summary>
/// Safe default when no concrete provider is registered (tests or
/// pre-wiring Phase 1 deployments). Returns an empty map which
/// exercises the scheduler's "no weighted topics to schedule" path
/// cleanly.
/// </summary>
public sealed class EmptySessionAbilityEstimateProvider : ISessionAbilityEstimateProvider
{
    public static readonly EmptySessionAbilityEstimateProvider Instance = new();

    private EmptySessionAbilityEstimateProvider() { }

    public Task<IReadOnlyDictionary<string, AbilityEstimate>> GetAsync(
        string studentAnonId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, AbilityEstimate>>(
            ImmutableDictionary<string, AbilityEstimate>.Empty);
}

/// <summary>
/// Provider for the TopicPrerequisiteGraph. Exposed as an interface so
/// the phase-1C admin loader (which hydrates from SyllabusDocument) can
/// slot in without changing callers. The default implementation
/// returns <see cref="TopicPrerequisiteGraph.Empty"/>.
/// </summary>
public interface ITopicPrerequisiteGraphProvider
{
    TopicPrerequisiteGraph Get();
}

/// <summary>Default provider — returns the empty graph.</summary>
public sealed class EmptyTopicPrerequisiteGraphProvider : ITopicPrerequisiteGraphProvider
{
    public static readonly EmptyTopicPrerequisiteGraphProvider Instance = new();
    private EmptyTopicPrerequisiteGraphProvider() { }
    public TopicPrerequisiteGraph Get() => TopicPrerequisiteGraph.Empty;
}

/// <summary>
/// Builds a SessionPlanSnapshot by calling AdaptiveScheduler with
/// inputs composed from the student's plan config + ability estimates.
/// Pure heuristic — no LLM call on this code path. See
/// SchedulerNoLlmCallTest for the static enforcement.
/// </summary>
public interface ISessionPlanGenerator
{
    /// <summary>
    /// Generate a plan snapshot for the given session start.
    /// </summary>
    /// <param name="studentAnonId">Session owner, anon id form.</param>
    /// <param name="sessionId">Newly-created session id.</param>
    /// <param name="nowUtc">Current wall-clock (injected for tests).</param>
    /// <param name="ct">Caller cancellation.</param>
    Task<SessionPlanGenerationResult> GenerateAsync(
        string studentAnonId,
        string sessionId,
        DateTimeOffset nowUtc,
        CancellationToken ct = default);
}

/// <summary>
/// Session-scoped provider that returns per-target candidate PlanEntry
/// lists for <see cref="InterleavingPolicy.Plan"/>. Prr-237 wires this as
/// an optional seam on <see cref="SessionPlanGenerator"/>: a host without
/// it registered keeps the wave-2 single-target behaviour. The concrete
/// implementation (to be shipped in a follow-up wave) calls
/// <see cref="AdaptiveScheduler.PrioritizeTopics"/> per-target against
/// each target's own topic subset + mastery-deficit scalar.
///
/// Pure heuristic path — no LLM call (ADR-0026, SchedulerNoLlmCallTest).
/// </summary>
public interface ISessionInterleavingInputsProvider
{
    /// <summary>
    /// Resolve per-target interleaving inputs for the session. The returned
    /// list order is treated as the deterministic tie-breaker by
    /// <see cref="InterleavingPolicy.Plan"/>. An empty list short-circuits
    /// interleaving to <see cref="InterleavingDisabledReason.SingleOrZeroTargets"/>.
    /// </summary>
    Task<IReadOnlyList<InterleavingTargetInput>> GetAsync(
        string studentAnonId,
        IReadOnlyList<ExamTarget> activeTargets,
        SchedulerInputs baseInputs,
        CancellationToken ct = default);
}

/// <summary>
/// Snapshot plus the provenance tag used for observability and for the
/// SessionPlanComputed_V1 event's <c>InputsSource</c> field.
/// </summary>
/// <param name="Snapshot">The generated plan snapshot.</param>
/// <param name="InputsSource">"student-plan-config" when prr-148's
/// service supplied real values; "default-fallback" when the scheduler
/// ran against defaults.</param>
/// <param name="ActiveExamTargetCode">prr-233: resolved catalog exam-target
/// code for this session (e.g. "BAGRUT_MATH_5U"), or null when no active
/// target was resolved (legacy prr-148 path or cold-start). Callers use
/// this to open an <see cref="IPromptCacheKeyContext"/> scope around the
/// session's downstream LLM fan-out so cache hits/misses and per-call cost
/// are labelled by target.</param>
/// <param name="Interleaving">prr-237 / EPIC-PRR-F: outcome of
/// <see cref="InterleavingPolicy.Plan"/> when the session has >1 active
/// targets, is NOT exam-week-locked, and an
/// <see cref="ISessionInterleavingInputsProvider"/> is wired. Disabled
/// otherwise — <see cref="InterleavingResult.DisabledReason"/> names the
/// short-circuit for audit. The <see cref="Snapshot"/> always carries the
/// wave-2 single-target plan for back-compat; callers that want the
/// interleaved entries read them off this field. See PRR-237 task body +
/// ADR-0049 citation integrity (d = 0.34, Brunmair 2019 meta, not Rohrer
/// cherry-pick).</param>
public sealed record SessionPlanGenerationResult(
    SessionPlanSnapshot Snapshot,
    string InputsSource,
    string? ActiveExamTargetCode = null,
    InterleavingResult Interleaving = default);

/// <summary>
/// Default implementation. Composes the three providers + the scheduler
/// and returns a fully-materialised snapshot. No external I/O beyond
/// the provider calls — safe to construct per-request.
/// </summary>
public sealed class SessionPlanGenerator : ISessionPlanGenerator
{
    private const string SourceStudentPlanConfig = "student-plan-config";
    private const string SourceDefaultFallback = "default-fallback";

    private readonly IStudentPlanConfigService _planConfig;
    private readonly ISessionAbilityEstimateProvider _abilityProvider;
    private readonly ITopicPrerequisiteGraphProvider _graphProvider;
    // prr-150: optional bridge that applies teacher/mentor overrides on
    // top of the base SchedulerInputs. Null when the TeacherOverride
    // bounded context is not wired into this host — in that case the
    // generator behaves exactly as before.
    private readonly IOverrideAwareSchedulerInputsBridge? _overrideBridge;

    // prr-226 / ADR-0050 §10: the three multi-target seams. All optional
    // because the prr-148 path (single legacy target via IStudentPlanConfigService)
    // must continue to work for hosts that have not wired prr-218 yet.
    // When the reader is non-null, the generator resolves an active target
    // via ActiveExamTargetPolicy and stamps it on SchedulerInputs. When the
    // reader is null, ActiveExamTargetId stays null — no behavioural change
    // from prr-149.
    private readonly IStudentPlanReader? _planReader;
    private readonly ISittingCanonicalDateResolver _sittingResolver;
    private readonly IExamTargetOverrideReader _overrideReader;

    // prr-237 / EPIC-PRR-F: optional within-session cross-target interleaving.
    // When both are wired, the generator runs InterleavingPolicy after the
    // base scheduler and either surfaces the interleaved entries via
    // SessionPlanGenerationResult.Interleaving (enabled path) or records
    // the short-circuit reason on the audit event (disabled path). When
    // either is null, the generator stays on the wave-2 single-target
    // path — exam-week-lock sessions always go through this null branch
    // because ActiveExamTargetPolicy sets LockedForExamWeek = true and
    // the policy short-circuits itself.
    private readonly ISessionInterleavingInputsProvider? _interleavingInputs;
    private readonly ISessionInterleavingAuditSink _interleavingAudit;

    /// <summary>
    /// prr-149 constructor — legacy single-target path. Kept for backward
    /// compatibility; new hosts should use the prr-226 overload.
    /// </summary>
    public SessionPlanGenerator(
        IStudentPlanConfigService planConfig,
        ISessionAbilityEstimateProvider abilityProvider,
        ITopicPrerequisiteGraphProvider graphProvider,
        IOverrideAwareSchedulerInputsBridge? overrideBridge = null)
        : this(
            planConfig,
            abilityProvider,
            graphProvider,
            overrideBridge,
            planReader: null,
            sittingResolver: EmptySittingCanonicalDateResolver.Instance,
            overrideReader: NullExamTargetOverrideReader.Instance,
            interleavingInputs: null,
            interleavingAudit: null)
    {
    }

    /// <summary>
    /// prr-226 constructor — multi-target path. Production wiring registers
    /// a real <see cref="IStudentPlanReader"/>, a catalog-backed
    /// <see cref="ISittingCanonicalDateResolver"/>, and an event-store
    /// backed <see cref="IExamTargetOverrideReader"/>. Tests can inject
    /// the in-memory / null variants.
    /// </summary>
    public SessionPlanGenerator(
        IStudentPlanConfigService planConfig,
        ISessionAbilityEstimateProvider abilityProvider,
        ITopicPrerequisiteGraphProvider graphProvider,
        IOverrideAwareSchedulerInputsBridge? overrideBridge,
        IStudentPlanReader? planReader,
        ISittingCanonicalDateResolver sittingResolver,
        IExamTargetOverrideReader overrideReader)
        : this(
            planConfig,
            abilityProvider,
            graphProvider,
            overrideBridge,
            planReader,
            sittingResolver,
            overrideReader,
            interleavingInputs: null,
            interleavingAudit: null)
    {
    }

    /// <summary>
    /// prr-237 constructor — adds the within-session cross-target
    /// interleaving seam + audit sink. When both are null the behaviour
    /// is identical to the prr-226 constructor.
    /// </summary>
    public SessionPlanGenerator(
        IStudentPlanConfigService planConfig,
        ISessionAbilityEstimateProvider abilityProvider,
        ITopicPrerequisiteGraphProvider graphProvider,
        IOverrideAwareSchedulerInputsBridge? overrideBridge,
        IStudentPlanReader? planReader,
        ISittingCanonicalDateResolver sittingResolver,
        IExamTargetOverrideReader overrideReader,
        ISessionInterleavingInputsProvider? interleavingInputs,
        ISessionInterleavingAuditSink? interleavingAudit)
    {
        _planConfig = planConfig ?? throw new ArgumentNullException(nameof(planConfig));
        _abilityProvider = abilityProvider ?? throw new ArgumentNullException(nameof(abilityProvider));
        _graphProvider = graphProvider ?? throw new ArgumentNullException(nameof(graphProvider));
        _overrideBridge = overrideBridge;
        _planReader = planReader;
        _sittingResolver = sittingResolver ?? throw new ArgumentNullException(nameof(sittingResolver));
        _overrideReader = overrideReader ?? throw new ArgumentNullException(nameof(overrideReader));
        _interleavingInputs = interleavingInputs;
        _interleavingAudit = interleavingAudit ?? NullSessionInterleavingAuditSink.Instance;
    }

    /// <inheritdoc />
    public async Task<SessionPlanGenerationResult> GenerateAsync(
        string studentAnonId,
        string sessionId,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
            throw new ArgumentException(
                "Student anon id must be non-empty.", nameof(studentAnonId));
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException(
                "Session id must be non-empty.", nameof(sessionId));

        var config = await _planConfig.GetAsync(studentAnonId, ct).ConfigureAwait(false);
        var estimates = await _abilityProvider.GetAsync(studentAnonId, ct).ConfigureAwait(false)
            ?? ImmutableDictionary<string, AbilityEstimate>.Empty;
        var graph = _graphProvider.Get() ?? TopicPrerequisiteGraph.Empty;

        // Determine provenance. If the config matches the defaults (same
        // horizon, same budget, neutral profile) we flag as fallback so
        // ops can track prr-148 adoption. Exact equality is fine — the
        // fallback service always returns the canonical build.
        var fallback = StudentPlanConfigDefaults.Build(nowUtc);
        var isFallback =
            config.MotivationProfile == fallback.MotivationProfile
            && config.WeeklyBudget == fallback.WeeklyBudget
            && config.DeadlineUtc.HasValue
            && fallback.DeadlineUtc.HasValue
            && Math.Abs((config.DeadlineUtc.Value - fallback.DeadlineUtc.Value).TotalSeconds) < 2;

        // prr-226 / ADR-0050 §10: if the multi-target reader is wired, resolve
        // the single active exam target (and the silent 14-day lock flag)
        // and stamp both onto SchedulerInputs. When the reader is null we
        // stay on the legacy prr-148 path — ActiveExamTargetId remains
        // null, no lock, and downstream consumers see the identical shape
        // they got before prr-226.
        //
        // prr-233: we ALSO resolve the target's catalog ExamCode (if any) so
        // callers can open an IPromptCacheKeyContext scope around the
        // downstream LLM fan-out. The code is the Ministry שאלון family
        // identifier ("BAGRUT_MATH_5U", "PET", "SAT_MATH") — operational,
        // not PII — used as a Prometheus label only.
        ExamTargetId? activeTargetId = null;
        string? activeExamCode = null;
        var lockedForExamWeek = false;
        IReadOnlyList<ExamTarget> activeTargetsCaptured = Array.Empty<ExamTarget>();
        if (_planReader is not null)
        {
            var activeTargets = await _planReader
                .ListTargetsAsync(studentAnonId, includeArchived: false, ct)
                .ConfigureAwait(false);
            activeTargetsCaptured = activeTargets;

            var overrideTargetId = await _overrideReader
                .GetOverrideAsync(studentAnonId, sessionId, ct)
                .ConfigureAwait(false);

            var resolution = ActiveExamTargetPolicy.Resolve(
                activeTargets: activeTargets,
                nowUtc: nowUtc,
                sittingDateResolver: _sittingResolver,
                overrideTargetId: overrideTargetId,
                deficitFunc: null);

            activeTargetId = resolution.ActiveTargetId;
            lockedForExamWeek = resolution.LockedForExamWeek;

            // Resolve the ExamCode string for the active target, if one was
            // picked. The ExamCode on ExamTarget is the catalog key, not a
            // PII-tainted value — safe to surface on metric labels per
            // ADR-0050 §2 + prr-233 label-safety rules.
            if (activeTargetId is { } resolvedId)
            {
                var activeTarget = activeTargets.FirstOrDefault(t => t.Id == resolvedId);
                if (activeTarget is not null)
                {
                    activeExamCode = activeTarget.ExamCode.Value;
                }
            }
        }

        var inputs = new SchedulerInputs(
            StudentAnonId: studentAnonId,
            PerTopicEstimates: estimates,
            DeadlineUtc: config.DeadlineUtc,
            WeeklyTimeBudget: config.WeeklyBudget,
            MotivationProfile: config.MotivationProfile,
            NowUtc: nowUtc,
            PrerequisiteGraph: graph,
            ActiveExamTargetId: activeTargetId,
            LockedForExamWeek: lockedForExamWeek);

        // prr-150: apply teacher/mentor overrides on top of the base inputs
        // if the bridge is wired in. Precedence: override > student plan
        // input > scheduler default. When no override exists for this
        // student (or the bridge is not registered at all) the inputs pass
        // through unchanged. Using ScopeAll here — future callers that
        // know the session-type can pass a narrower scope for scoped
        // motivation overrides.
        if (_overrideBridge is not null)
        {
            var applied = await _overrideBridge
                .ApplyAsync(inputs, TeacherOverrideCommands.ScopeAll, ct)
                .ConfigureAwait(false);
            inputs = applied.EffectiveInputs;
        }

        // Pure heuristic call — no LLM path.
        var entries = AdaptiveScheduler.PrioritizeTopics(inputs);

        // prr-150: the snapshot reflects the EFFECTIVE inputs (post-override)
        // so downstream consumers (UI, events, audit) see the values that
        // actually drove prioritisation. When no override is active the
        // effective inputs equal the student's own config.
        var snapshot = new SessionPlanSnapshot(
            StudentAnonId: studentAnonId,
            SessionId: sessionId,
            GeneratedAtUtc: nowUtc,
            PriorityOrdered: entries,
            MotivationProfile: inputs.MotivationProfile,
            DeadlineUtc: inputs.DeadlineUtc,
            WeeklyBudgetMinutes: (int)inputs.WeeklyTimeBudget.TotalMinutes);

        // prr-237 / EPIC-PRR-F: within-session cross-target interleaving.
        // Runs AFTER the base scheduler so we have the effective inputs
        // (post-override) and can pass them to the inputs provider for
        // per-target subset resolution. Disabled inside exam-week lock
        // (preserves wave-2 single-target behaviour) and when <2 active
        // targets or no provider wired.
        //
        // WHY (research citation): interleaved practice produces
        // discrimination-learning gains of d ≈ 0.34 per the Brunmair
        // (2019) meta over 59 studies; Rohrer & Taylor (2007) is the
        // canonical single-study reference. We DO NOT cite the Rohrer
        // cherry-pick d = 1.05 — "Honest not complimentary" memory +
        // ADR-0049 citation integrity + PRR-237 DoD.
        var interleaving = await RunInterleavingAsync(
            studentAnonId,
            sessionId,
            nowUtc,
            activeTargetsCaptured,
            lockedForExamWeek,
            inputs,
            ct).ConfigureAwait(false);

        return new SessionPlanGenerationResult(
            Snapshot: snapshot,
            InputsSource: isFallback ? SourceDefaultFallback : SourceStudentPlanConfig,
            ActiveExamTargetCode: activeExamCode,
            Interleaving: interleaving);
    }

    private async Task<InterleavingResult> RunInterleavingAsync(
        string studentAnonId,
        string sessionId,
        DateTimeOffset nowUtc,
        IReadOnlyList<ExamTarget> activeTargets,
        bool lockedForExamWeek,
        SchedulerInputs inputs,
        CancellationToken ct)
    {
        // Short-circuit BEFORE the provider call when the exam-week lock
        // is active — preserves wave-2 ActiveExamTargetPolicy behaviour
        // byte-for-byte. The architecture test
        // InterleavingDisabledInExamWeekLockTest guards this branch.
        if (lockedForExamWeek)
        {
            var locked = InterleavingPolicy.Plan(
                targets: Array.Empty<InterleavingTargetInput>(),
                lockedForExamWeek: true);
            await AuditAsync(studentAnonId, sessionId, nowUtc, locked, activeTargets, ct)
                .ConfigureAwait(false);
            return locked;
        }

        if (_interleavingInputs is null || activeTargets.Count < 2)
        {
            var noop = InterleavingPolicy.Plan(
                targets: Array.Empty<InterleavingTargetInput>(),
                lockedForExamWeek: false);
            // No audit emitted when no provider is wired — keeps the
            // wave-2 compatibility path silent. When ≥2 targets exist but
            // no provider is wired we still emit the audit so ops can see
            // "feature off" vs "feature on, but short-circuit" distinctly.
            if (_interleavingInputs is null)
            {
                return noop;
            }
            await AuditAsync(studentAnonId, sessionId, nowUtc, noop, activeTargets, ct)
                .ConfigureAwait(false);
            return noop;
        }

        var targetInputs = await _interleavingInputs
            .GetAsync(studentAnonId, activeTargets, inputs, ct)
            .ConfigureAwait(false)
            ?? Array.Empty<InterleavingTargetInput>();

        var result = InterleavingPolicy.Plan(
            targets: targetInputs,
            lockedForExamWeek: false);

        await AuditAsync(studentAnonId, sessionId, nowUtc, result, activeTargets, ct)
            .ConfigureAwait(false);
        return result;
    }

    private Task AuditAsync(
        string studentAnonId,
        string sessionId,
        DateTimeOffset nowUtc,
        InterleavingResult result,
        IReadOnlyList<ExamTarget> activeTargets,
        CancellationToken ct)
    {
        // Map allocation rows to the wire-stable event shape. ExamCode
        // lookup via the captured active-target list — O(allocations ×
        // active-targets) is fine for MVP (active-targets count ≤ 8
        // per ADR-0050 weekly-hours invariant).
        var rows = new List<SessionInterleavingAllocation_V1>(result.Allocations.Length);
        foreach (var alloc in result.Allocations)
        {
            var target = activeTargets.FirstOrDefault(t => t.Id == alloc.TargetId);
            rows.Add(new SessionInterleavingAllocation_V1(
                TargetId: alloc.TargetId.Value,
                ExamCode: target?.ExamCode.Value ?? string.Empty,
                Slots: alloc.Slots,
                BucketWeight: alloc.BucketWeight));
        }

        var total = 0;
        foreach (var r in rows) total += r.Slots;

        var @event = new SessionInterleavingPlanned_V1(
            StudentAnonId: studentAnonId,
            SessionId: sessionId,
            PlannedAtUtc: nowUtc,
            Enabled: !result.Disabled,
            DisabledReasonTag: SessionInterleavingPlanned_V1.TagFromReason(result.DisabledReason),
            Allocations: rows,
            TotalSlots: total);

        return _interleavingAudit.AppendAsync(@event, ct);
    }
}
