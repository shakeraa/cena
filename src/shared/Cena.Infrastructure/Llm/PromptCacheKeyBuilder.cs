// =============================================================================
// Cena Platform — PromptCacheKeyBuilder (prr-047)
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
//
// Examples:
//   sys (global):       cena:sys:explain-math-v3
//   explain (tenant):   cena:t:school-42:explain:q-123:ProceduralError
//   ctx (tenant):       cena:t:school-42:ctx:stu-anon-9f3a:a7b3c1d
// =============================================================================

namespace Cena.Infrastructure.Llm;

public static class PromptCacheKeyBuilder
{
    private const string Root = "cena";
    private const string TenantSegment = "t";

    /// <summary>
    /// Key for a repeatable per-question misconception explanation. Matches
    /// the SAI-003 L2 pattern already used by ExplanationCacheService — the
    /// question bank invalidator drops `explain:{questionId}:*` when a
    /// question is edited, and that still works because the questionId is
    /// the first variable segment.
    /// </summary>
    public static string ForExplanation(string questionId, string errorType, string? tenantId = null)
    {
        RequireNonEmpty(questionId, nameof(questionId));
        RequireNonEmpty(errorType, nameof(errorType));
        RequireValidOptional(tenantId, nameof(tenantId));
        var prefix = TenantPrefix(tenantId);
        return $"{prefix}explain:{questionId}:{errorType}";
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
    public static string ForStudentContext(string studentAnonId, string contextHash, string? tenantId = null)
    {
        RequireNonEmpty(studentAnonId, nameof(studentAnonId));
        RequireNonEmpty(contextHash, nameof(contextHash));
        RequireValidOptional(tenantId, nameof(tenantId));
        var prefix = TenantPrefix(tenantId);
        return $"{prefix}ctx:{studentAnonId}:{contextHash}";
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
