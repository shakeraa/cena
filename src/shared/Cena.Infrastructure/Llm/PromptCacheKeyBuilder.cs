// =============================================================================
// Cena Platform — PromptCacheKeyBuilder (prr-047 + prr-233)
//
// Single source of truth for prompt-cache key shapes. Matches the patterns
// called out in contracts/llm/routing-config.yaml §6.
//
// Namespacing rules:
//   - Every key has the `cena:` root prefix so it cannot collide with
//     other caches in shared Redis (rate-limit buckets, cost budgets, etc).
//   - Every key carries a cache_type segment (sys | explain | ctx) so metric
//     labels and dashboards can split by cache strategy.
//   - When a tenantId is supplied (the common case for student-scoped keys),
//     it sits BEFORE the domain segments so a stray Redis SCAN per tenant
//     is cheap and a cross-tenant bleed is impossible.
//   - prr-233: target-scoped keys include an `xt:<exam_target_code>` segment
//     IMMEDIATELY AFTER the tenant segment. Two purposes:
//       (1) Prevents a cache entry authored for BAGRUT_MATH_5U from being
//           served to a student whose active target is PET or SAT_MATH —
//           the prompt context differs per target, so a cross-target hit
//           would return mis-contextualised content.
//       (2) Makes per-target cache hit rate directly observable via the
//           `exam_target_code` metric label; the label and the key segment
//           share the same value so the dashboard's per-target heatmap lines
//           up with the underlying Redis namespace.
//
// Examples:
//   sys (global):               cena:sys:explain-math-v3
//   explain (tenant-only):      cena:t:school-42:explain:q-123:ProceduralError
//   explain (tenant + target):  cena:t:school-42:xt:BAGRUT_MATH_5U:explain:q-123:ProceduralError
//   ctx (tenant + target):      cena:t:school-42:xt:BAGRUT_MATH_5U:ctx:stu-anon-9f3a:a7b3c1d
// =============================================================================

namespace Cena.Infrastructure.Llm;

public static class PromptCacheKeyBuilder
{
    private const string Root = "cena";
    private const string TenantSegment = "t";
    private const string ExamTargetSegment = "xt";

    /// <summary>
    /// Key for a repeatable per-question misconception explanation. Matches
    /// the SAI-003 L2 pattern already used by ExplanationCacheService — the
    /// question bank invalidator drops `explain:{questionId}:*` when a
    /// question is edited, and that still works because the questionId is
    /// the first variable segment after the target (when present).
    /// </summary>
    /// <param name="questionId">Item id. Must not contain ':'.</param>
    /// <param name="errorType">Classified error type (e.g. "ProceduralError").</param>
    /// <param name="tenantId">Optional tenant scope (ADR-0001).</param>
    /// <param name="examTargetCode">prr-233: optional catalog exam-target code
    /// (e.g. "BAGRUT_MATH_5U"). When non-null the key includes an
    /// <c>xt:</c> segment before the domain segments so cache entries do not
    /// bleed across targets within the same tenant. See file header.</param>
    public static string ForExplanation(
        string questionId,
        string errorType,
        string? tenantId = null,
        string? examTargetCode = null)
    {
        RequireNonEmpty(questionId, nameof(questionId));
        RequireNonEmpty(errorType, nameof(errorType));
        RequireValidOptional(tenantId, nameof(tenantId));
        RequireValidOptional(examTargetCode, nameof(examTargetCode));
        var prefix = TenantPrefix(tenantId);
        var targetPrefix = TargetPrefix(examTargetCode);
        return $"{prefix}{targetPrefix}explain:{questionId}:{errorType}";
    }

    /// <summary>
    /// Key for a cached system-prompt block (Anthropic 1-hour write tier). System
    /// prompts are global to the service that owns them (e.g. "explain-math-v3",
    /// "socratic-tutor-v1") so no tenantId is expected here. If you find yourself
    /// wanting to tenant-scope a system prompt, you have a per-tenant prompt
    /// template problem, not a cache problem.
    /// </summary>
    public static string ForSystemPrompt(string systemPromptId)
    {
        RequireNonEmpty(systemPromptId, nameof(systemPromptId));
        return $"{Root}:sys:{systemPromptId}";
    }

    /// <summary>
    /// Key for a per-session student-context block (Anthropic 5-minute write tier).
    /// Scoped by <paramref name="studentAnonId"/> — the hashed/anon identifier —
    /// and a content hash that changes whenever the underlying context (mastery
    /// snapshot, recent error types, etc.) changes. This is the same shape
    /// called out in routing-config.yaml §6.student_context.cache_key.
    /// </summary>
    /// <param name="studentAnonId">Hashed/anonymous student id.</param>
    /// <param name="contextHash">Stable hash of the underlying context block.</param>
    /// <param name="tenantId">Optional tenant scope (ADR-0001).</param>
    /// <param name="examTargetCode">prr-233: optional catalog exam-target code
    /// — identical semantics to the parameter on
    /// <see cref="ForExplanation"/>; see file header for why cross-target
    /// sharing is not safe.</param>
    public static string ForStudentContext(
        string studentAnonId,
        string contextHash,
        string? tenantId = null,
        string? examTargetCode = null)
    {
        RequireNonEmpty(studentAnonId, nameof(studentAnonId));
        RequireNonEmpty(contextHash, nameof(contextHash));
        RequireValidOptional(tenantId, nameof(tenantId));
        RequireValidOptional(examTargetCode, nameof(examTargetCode));
        var prefix = TenantPrefix(tenantId);
        var targetPrefix = TargetPrefix(examTargetCode);
        return $"{prefix}{targetPrefix}ctx:{studentAnonId}:{contextHash}";
    }

    /// <summary>
    /// prr-233 convenience overload: reads <paramref name="context"/> for the
    /// ambient institute + target, so call sites do not have to inline two
    /// property reads. Equivalent to calling the 4-arg <see cref="ForExplanation"/>
    /// with <c>context.InstituteId</c> and <c>context.ExamTargetCode</c>.
    /// </summary>
    public static string ForExplanation(
        string questionId,
        string errorType,
        IPromptCacheKeyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ForExplanation(questionId, errorType, context.InstituteId, context.ExamTargetCode);
    }

    /// <summary>
    /// prr-233 convenience overload — same semantics as the ambient-context
    /// overload of <see cref="ForExplanation"/>.
    /// </summary>
    public static string ForStudentContext(
        string studentAnonId,
        string contextHash,
        IPromptCacheKeyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ForStudentContext(studentAnonId, contextHash, context.InstituteId, context.ExamTargetCode);
    }

    /// <summary>
    /// Builds the tenant-prefix segment. Empty string when no tenantId is supplied.
    /// Intentionally includes a trailing colon so callers can do plain string
    /// concatenation without worrying about separators.
    /// </summary>
    private static string TenantPrefix(string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return $"{Root}:";
        }
        return $"{Root}:{TenantSegment}:{tenantId}:";
    }

    /// <summary>
    /// prr-233: target-prefix segment. Empty when no target code supplied
    /// (matches the legacy shape so existing keys continue to resolve).
    /// Trailing colon keeps concatenation trivial.
    /// </summary>
    private static string TargetPrefix(string? examTargetCode)
    {
        if (string.IsNullOrWhiteSpace(examTargetCode))
        {
            return string.Empty;
        }
        return $"{ExamTargetSegment}:{examTargetCode}:";
    }

    private static void RequireNonEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"PromptCacheKeyBuilder: {paramName} must not be empty — " +
                "empty segments collapse keys across unrelated entities and " +
                "defeat tenant isolation.",
                paramName);
        }
        if (value.Contains(':'))
        {
            throw new ArgumentException(
                $"PromptCacheKeyBuilder: {paramName} must not contain ':' — " +
                $"it is the key-segment separator. Supplied value: '{value}'.",
                paramName);
        }
    }

    /// <summary>
    /// Optional segment (e.g. tenantId): null/empty is fine (means "no segment"),
    /// but if supplied the same colon-safety rule as required segments applies.
    /// </summary>
    private static void RequireValidOptional(string? value, string paramName)
    {
        if (value is null) return;
        if (string.IsNullOrWhiteSpace(value)) return;
        if (value.Contains(':'))
        {
            throw new ArgumentException(
                $"PromptCacheKeyBuilder: {paramName} must not contain ':' — " +
                $"it is the key-segment separator. Supplied value: '{value}'.",
                paramName);
        }
    }
}
