// =============================================================================
// Cena Platform — IPromptCacheKeyContext (prr-233, extends prr-047 / prr-226)
//
// Ambient, AsyncLocal-backed context that carries the CURRENT per-session
// cache scope for the duration of an LLM call. Specifically:
//
//   - InstituteId      — tenant scope (ADR-0001). Optional; degrades to
//                        "unknown" on the metric label when absent (matches
//                        LlmCostMetric's convention so the two metrics agree
//                        on the null-tenant signifier).
//   - ExamTargetCode   — the ACTIVE multi-target scope (ADR-0050 §10 + prr-226).
//                        The Ministry ExamCode.Value string (e.g. "BAGRUT_MATH_5U",
//                        "PET", "SAT_MATH"). Operational label ONLY — this is a
//                        catalog code, not PII, so it is safe to emit on a
//                        Prometheus metric.
//
// WHY an ambient context instead of threading `examTargetCode` through every
// IPromptCache method signature:
//
//   1. The scheduler (AdaptiveScheduler / ActiveExamTargetPolicy) already
//      resolves the active target at session-start. Threading that value
//      through every intermediate LLM service would be a broad refactor (~30
//      call sites) purely to satisfy the observability seam.
//   2. The cache implementation IS the observability seam — it already owns
//      the metric emission. Pulling the ambient target from an AsyncLocal at
//      the seam means every caller gets per-target labels for free the moment
//      it enters a scope.
//   3. AsyncLocal flows through Task/await boundaries AND across BFF → domain
//      service hops inside the same request, so an NATS handler that opens a
//      target-scoped scope at the top of its activation will still have the
//      target visible on the hint-ladder → Socratic fan-out three calls
//      deeper.
//   4. Absent scope is a legitimate state for system prompts (sys:*) and
//      content-ingestion paths that have no active target — degrading to
//      "unknown" on the label is correct, not a bug.
//
// Enforcement:
//
//   The arch test PromptCacheKeyCarriesTargetContextTest scans for any file
//   that constructs a cache key in a target-scoped context — specifically any
//   file that both references PromptCacheKeyBuilder.ForExplanation /
//   ForStudentContext AND references ActiveExamTargetId (the scheduler's
//   multi-target scope). Such files MUST either:
//
//     (a) open a PushScope(...) around the key construction, OR
//     (b) carry the [PromptCacheKeyBypassesTargetContext("<reason>")] marker
//         with a written justification reviewed in PR.
//
// The marker exists because some paths (e.g. migration backfills, cross-target
// aggregated reports) legitimately construct a cache key without an active
// target; forcing a target on them would invalidate the key.
//
// See:
//   - ADR-0050 §10 (active target resolution)
//   - docs/adr/0026-llm-three-tier-routing.md §6 (prompt caching)
//   - ADR-0001 (tenant isolation — InstituteId convention)
// =============================================================================

namespace Cena.Infrastructure.Llm;

/// <summary>
/// Ambient, per-logical-call cache scope carrying tenant + active-target
/// context for cache metric labelling and key derivation.
/// </summary>
/// <remarks>
/// Implementations MUST use <see cref="AsyncLocal{T}"/> so the context flows
/// across <c>await</c> points without the caller having to re-thread it.
/// </remarks>
public interface IPromptCacheKeyContext
{
    /// <summary>
    /// Currently-active institute scope, or null when no scope is open.
    /// Stable tenant-scope for the duration of a request; changes across
    /// background-job activations.
    /// </summary>
    string? InstituteId { get; }

    /// <summary>
    /// Currently-active Ministry / catalog exam-target code, or null when
    /// no scope is open. Corresponds to <c>ExamCode.Value</c> per
    /// ADR-0050 §2. Operational label — safe to emit on metrics (not PII).
    /// </summary>
    string? ExamTargetCode { get; }

    /// <summary>
    /// Open a new scope. Nested calls stack — the innermost scope wins for
    /// reads, and <see cref="IDisposable.Dispose"/> restores the previous
    /// scope. Either argument may be null, which means "do not override
    /// the outer scope's value for this slot."
    /// </summary>
    /// <param name="instituteId">Tenant scope. Must not contain ':' (matches
    /// <see cref="PromptCacheKeyBuilder"/> segment-safety rule). Pass null to
    /// inherit the outer scope's institute.</param>
    /// <param name="examTargetCode">Ministry exam code (e.g. "BAGRUT_MATH_5U").
    /// Must not contain ':'. Pass null to inherit the outer scope's target.</param>
    IDisposable PushScope(string? instituteId, string? examTargetCode);
}

/// <summary>
/// Class-level opt-out marker for <c>PromptCacheKeyCarriesTargetContextTest</c>.
/// Apply to a class that legitimately constructs a cache key without routing
/// through <see cref="IPromptCacheKeyContext"/> and justify in
/// <see cref="Reason"/>.
///
/// Legitimate reasons (non-exhaustive):
///   - Migration backfill replaying historical events with no active-target
///     semantics (the event predates ADR-0050).
///   - Cross-target aggregate report where keying by a single target would
///     invalidate the shared cache entry.
///   - System prompt (sys:*) lookup where the prompt is target-independent
///     by contract.
///
/// Do NOT use this attribute to avoid a refactor. If the path has an active
/// target available (anywhere in the call chain), thread it into a
/// <see cref="IPromptCacheKeyContext.PushScope"/> call instead.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PromptCacheKeyBypassesTargetContextAttribute : Attribute
{
    /// <summary>
    /// Written PR-reviewable justification.
    /// </summary>
    public string Reason { get; }

    public PromptCacheKeyBypassesTargetContextAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "PromptCacheKeyBypassesTargetContext requires a written " +
                "justification so the bypass is reviewable in PR. Empty " +
                "reasons are banned.",
                nameof(reason));
        }
        Reason = reason;
    }
}

/// <summary>
/// Default <see cref="IPromptCacheKeyContext"/> implementation backed by
/// <see cref="AsyncLocal{T}"/>. Register as a singleton; instance state is
/// the AsyncLocal itself, which is process-local but logically call-local.
/// </summary>
public sealed class AsyncLocalPromptCacheKeyContext : IPromptCacheKeyContext
{
    private readonly AsyncLocal<Frame?> _top = new();

    public string? InstituteId => _top.Value?.InstituteId;

    public string? ExamTargetCode => _top.Value?.ExamTargetCode;

    public IDisposable PushScope(string? instituteId, string? examTargetCode)
    {
        RequireColonSafe(instituteId, nameof(instituteId));
        RequireColonSafe(examTargetCode, nameof(examTargetCode));

        // Inherit unspecified slots from the enclosing frame so nested scopes
        // can narrow one dimension (e.g. an inner service can push a new
        // target while keeping the outer institute).
        var outer = _top.Value;
        var effInstitute = instituteId ?? outer?.InstituteId;
        var effTarget = examTargetCode ?? outer?.ExamTargetCode;

        var frame = new Frame(effInstitute, effTarget, outer);
        _top.Value = frame;
        return new ScopeHandle(this, outer);
    }

    private static void RequireColonSafe(string? value, string paramName)
    {
        if (value is null) return;
        if (value.Contains(':'))
        {
            throw new ArgumentException(
                $"IPromptCacheKeyContext.PushScope: {paramName} must not contain " +
                $"':' — it is the cache-key segment separator. Supplied value: '{value}'.",
                paramName);
        }
    }

    private sealed record Frame(string? InstituteId, string? ExamTargetCode, Frame? Parent);

    private sealed class ScopeHandle : IDisposable
    {
        private readonly AsyncLocalPromptCacheKeyContext _ctx;
        private readonly Frame? _restore;
        private bool _disposed;

        public ScopeHandle(AsyncLocalPromptCacheKeyContext ctx, Frame? restore)
        {
            _ctx = ctx;
            _restore = restore;
        }

        public void Dispose()
        {
            // Dispose is idempotent; a caller double-disposing should not
            // clobber a newer scope above us. This mirrors the convention
            // used by Activity.Dispose + AsyncLocal frames in the .NET BCL.
            if (_disposed) return;
            _disposed = true;
            _ctx._top.Value = _restore;
        }
    }
}

/// <summary>
/// No-op implementation for call sites that legitimately have no target
/// context (content ingestion, migration workers). Returns null for both
/// properties; <see cref="PushScope"/> returns a disposable that does nothing.
/// The null context is valid input to <see cref="RedisPromptCache"/>, which
/// degrades to the "unknown" label when no scope is active.
/// </summary>
public sealed class NullPromptCacheKeyContext : IPromptCacheKeyContext
{
    public string? InstituteId => null;
    public string? ExamTargetCode => null;

    public IDisposable PushScope(string? instituteId, string? examTargetCode) => NoopScope.Instance;

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose() { }
    }
}
