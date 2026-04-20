// =============================================================================
// Cena Platform — Parent → child authorization guard (prr-009, EPIC-PRR-C)
//
// ADR-0041 canonical seam: every parent-facing HTTP endpoint routes
// authorization through ParentAuthorizationGuard.AssertCanAccessAsync.
// The endpoint passes the route-parameter studentAnonId + the institute
// resolved for that student; the guard cross-checks against the parent's
// `parent_of` JWT claims AND the authoritative IParentChildBindingService.
// Failure modes are collapsed to one exception shape so the IDOR surface
// is one file, not a sweep of if-ladders.
//
// Invariants:
//   1. The guard is the single controlled seam. An endpoint that reaches
//      student data without calling it is a bug; the
//      NoParentEndpointBypassesBindingTest architecture ratchet catches it.
//   2. Tenant check happens before any other substantive work. A parent
//      authenticated at institute X may NOT read a child at institute Y
//      even if a binding once existed elsewhere.
//   3. JWT `parent_of` claims are a CACHE. The authoritative store's
//      answer is the one that gates access. A parent whose link was
//      revoked after their session was issued sees the revocation take
//      effect on the next guard call.
//   4. Audit log every call. Deny outcomes emit at WARNING; allow outcomes
//      at INFO. No PII — anon IDs only.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using Cena.Infrastructure.Errors;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Security;

/// <summary>
/// Claim key for a parent's per-(student, institute) binding. Emitted as a
/// JSON object with `studentId` and `instituteId`. The claims transformer
/// is responsible for exploding the wire-level JSON array into individual
/// claims of this type.
/// </summary>
public static class ParentBindingClaims
{
    /// <summary>Claim type carrying one (student, institute) pair.</summary>
    public const string ParentOfClaim = "parent_of";

    /// <summary>Attach this when the parent must also pass a fresh-adult-consent probe.</summary>
    public const string ParentOfPayloadStudentIdKey = "studentId";

    /// <summary>JSON key for the institute id inside a parent_of claim value.</summary>
    public const string ParentOfPayloadInstituteIdKey = "instituteId";
}

/// <summary>
/// A single claim-carried (student, institute) hint. Parsed from a
/// <c>parent_of</c> claim's JSON payload; never trusted on its own —
/// the authoritative <see cref="IParentChildBindingStore"/> is.
/// </summary>
public sealed record ParentOfClaimEntry(string StudentId, string InstituteId)
{
    /// <summary>
    /// Try to parse a claim value into an entry. Claim values are JSON
    /// objects like <c>{"studentId":"abc","instituteId":"inst-a"}</c>.
    /// Malformed values return false; callers treat them as absent.
    /// </summary>
    public static bool TryParse(string claimValue, out ParentOfClaimEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(claimValue)) return false;

        try
        {
            using var doc = JsonDocument.Parse(claimValue);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;

            if (!doc.RootElement.TryGetProperty(
                    ParentBindingClaims.ParentOfPayloadStudentIdKey, out var studentEl) ||
                studentEl.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            if (!doc.RootElement.TryGetProperty(
                    ParentBindingClaims.ParentOfPayloadInstituteIdKey, out var instEl) ||
                instEl.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var sid = studentEl.GetString();
            var iid = instEl.GetString();
            if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(iid))
                return false;

            entry = new ParentOfClaimEntry(sid, iid);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>
/// Authoritative source-of-truth lookup for parent bindings. Implemented
/// in <c>Cena.Actors.Parent</c>; declared here so the guard (which lives
/// in Infrastructure) does not need an upward reference to Actors.
/// </summary>
public interface IParentChildBindingService
{
    /// <summary>
    /// Returns true when the authoritative store reports an active
    /// binding for (parent, student) whose InstituteId equals the
    /// supplied institute. Any other outcome (no binding, revoked,
    /// cross-tenant) returns false — callers DO NOT get to distinguish
    /// these cases (refusing that distinction prevents existence-oracle
    /// leaks, matching the TeacherOverride ADR-0001 precedent).
    /// </summary>
    Task<bool> IsBindingActiveAsync(
        string parentActorId,
        string studentSubjectId,
        string instituteId,
        CancellationToken ct = default);
}

/// <summary>
/// Marks a parent-facing endpoint handler that intentionally does NOT
/// call <see cref="ParentAuthorizationGuard.AssertCanAccessAsync"/>.
/// Every use MUST carry a one-line justification that a security reviewer
/// has signed off on. The <c>NoParentEndpointBypassesBindingTest</c>
/// architecture ratchet reads these annotations from source and fails
/// CI when one is added without an updated allowlist.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class AllowsUnboundParentAttribute : Attribute
{
    public AllowsUnboundParentAttribute(string justification)
    {
        if (string.IsNullOrWhiteSpace(justification))
            throw new ArgumentException("justification required", nameof(justification));
        Justification = justification;
    }

    public string Justification { get; }
}

/// <summary>
/// ADR-0041 / prr-009: the single controlled seam through which every
/// parent-facing endpoint must route authorization. Throws
/// <see cref="ForbiddenException"/> with
/// <see cref="ErrorCodes.CENA_AUTH_IDOR_VIOLATION"/> on any denial path.
/// </summary>
public static class ParentAuthorizationGuard
{
    /// <summary>
    /// The tenant-crossing IDOR metric name. Elevated cardinality on this
    /// counter is an early-warning signal for stolen-token or buggy-client
    /// scenarios.
    /// </summary>
    public const string CrossTenantDeniedMetric = "cena_parent_idor_denied_total";

    /// <summary>
    /// Assert that the authenticated caller is authorized to act on
    /// behalf of <paramref name="studentSubjectId"/> inside
    /// <paramref name="instituteId"/>.
    ///
    /// Checks (in strict order — the first failing test throws):
    /// <list type="number">
    /// <item>Caller role is PARENT.</item>
    /// <item>Caller carries a parent actor id.</item>
    /// <item>Caller's JWT <c>parent_of</c> claim cache contains the
    ///       (student, institute) pair. The cache check is cheap and
    ///       rejects stale tokens without a store round-trip.</item>
    /// <item>Authoritative store reports an ACTIVE binding for exactly
    ///       this triple (revocations take effect on the next call).</item>
    /// </list>
    ///
    /// Tenant-crossing is refused before any substantive work: a parent
    /// who once held a binding at institute A and is now probing
    /// institute B is denied even if (student, A) is still active.
    /// </summary>
    public static async Task<ParentChildBindingResolution> AssertCanAccessAsync(
        ClaimsPrincipal caller,
        string studentSubjectId,
        string instituteId,
        IParentChildBindingService bindingService,
        ILogger logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(bindingService);
        ArgumentNullException.ThrowIfNull(logger);

        // Empty student/institute must surface as a 403, not a 400 — the
        // guard is called late enough that any empty value is either a
        // client bug OR an attacker probing; neither should reveal the
        // shape of the check via a distinct HTTP code.
        if (string.IsNullOrWhiteSpace(studentSubjectId) ||
            string.IsNullOrWhiteSpace(instituteId))
        {
            throw new ForbiddenException(
                ErrorCodes.CENA_AUTH_IDOR_VIOLATION,
                "Parent-binding check requires non-empty student + institute identifiers.");
        }

        var traceId = Activity.Current?.TraceId.ToString() ?? "no-trace";
        var parentActorId = ExtractParentActorId(caller);
        var role = caller.FindFirstValue(ClaimTypes.Role) ?? caller.FindFirstValue("role");

        if (!string.Equals(role, "PARENT", StringComparison.Ordinal))
        {
            LogOutcome(logger, "deny-not-parent", parentActorId, studentSubjectId, instituteId, traceId,
                bound: false, tenantMatch: false, level: LogLevel.Warning);
            throw new ForbiddenException(
                ErrorCodes.CENA_AUTH_IDOR_VIOLATION,
                $"Caller role '{role}' is not permitted to access parent-scoped data.");
        }

        if (string.IsNullOrWhiteSpace(parentActorId))
        {
            LogOutcome(logger, "deny-missing-parent-id", null, studentSubjectId, instituteId, traceId,
                bound: false, tenantMatch: false, level: LogLevel.Warning);
            throw new ForbiddenException(
                ErrorCodes.CENA_AUTH_IDOR_VIOLATION,
                "PARENT caller is missing the required actor identifier claim.");
        }

        var entries = ExtractParentOfEntries(caller);
        var claimMatch = entries.Any(e =>
            string.Equals(e.StudentId, studentSubjectId, StringComparison.Ordinal) &&
            string.Equals(e.InstituteId, instituteId, StringComparison.Ordinal));

        // Early-exit on claim mismatch. Distinguishing "no binding" from
        // "cross-tenant" on the deny message would be an existence oracle;
        // we emit the distinction ONLY to the structured log (operator
        // view), not to the HTTP response.
        var claimHasOtherInstituteForStudent = !claimMatch && entries.Any(e =>
            string.Equals(e.StudentId, studentSubjectId, StringComparison.Ordinal));

        if (!claimMatch)
        {
            var reason = claimHasOtherInstituteForStudent
                ? "deny-cross-tenant-claim"
                : "deny-unbound-claim";
            LogOutcome(logger, reason, parentActorId, studentSubjectId, instituteId, traceId,
                bound: false, tenantMatch: !claimHasOtherInstituteForStudent, level: LogLevel.Warning);
            throw new ForbiddenException(
                ErrorCodes.CENA_AUTH_IDOR_VIOLATION,
                $"PARENT caller is not bound to student '{studentSubjectId}' "
                + $"at institute '{instituteId}'.");
        }

        // Authoritative store — JWT cache may be stale post-revocation.
        var storeActive = await bindingService
            .IsBindingActiveAsync(parentActorId, studentSubjectId, instituteId, ct)
            .ConfigureAwait(false);

        if (!storeActive)
        {
            LogOutcome(logger, "deny-store-inactive", parentActorId, studentSubjectId, instituteId, traceId,
                bound: false, tenantMatch: true, level: LogLevel.Warning);
            throw new ForbiddenException(
                ErrorCodes.CENA_AUTH_IDOR_VIOLATION,
                $"PARENT caller's binding to student '{studentSubjectId}' "
                + $"at institute '{instituteId}' is not active.");
        }

        LogOutcome(logger, "allow", parentActorId, studentSubjectId, instituteId, traceId,
            bound: true, tenantMatch: true, level: LogLevel.Information);
        return new ParentChildBindingResolution(parentActorId, studentSubjectId, instituteId);
    }

    /// <summary>
    /// Extracts every <c>parent_of</c> claim entry present on the caller.
    /// Ignores malformed entries (they cannot authorize anything anyway).
    /// </summary>
    public static IReadOnlyList<ParentOfClaimEntry> ExtractParentOfEntries(ClaimsPrincipal caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var list = new List<ParentOfClaimEntry>();
        foreach (var claim in caller.FindAll(ParentBindingClaims.ParentOfClaim))
        {
            if (ParentOfClaimEntry.TryParse(claim.Value, out var entry) && entry is not null)
                list.Add(entry);
        }
        return list;
    }

    /// <summary>
    /// Pull the parent's actor id from conventional claim shapes. Accepts
    /// either the Cena-native <c>parentAnonId</c> or the standard
    /// <see cref="ClaimTypes.NameIdentifier"/> / <c>sub</c> subject id.
    /// </summary>
    public static string? ExtractParentActorId(ClaimsPrincipal caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return caller.FindFirstValue("parentAnonId")
            ?? caller.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? caller.FindFirstValue("sub");
    }

    [SuppressMessage("", "CA2254", Justification =
        "Outcome names are a closed set of constants; see the 'allow' / 'deny-*' strings below.")]
    private static void LogOutcome(
        ILogger logger,
        string outcome,
        string? parentId,
        string studentId,
        string instituteId,
        string traceId,
        bool bound,
        bool tenantMatch,
        LogLevel level)
    {
        logger.Log(
            level,
            "[prr-009] parent-binding-check parent={ParentId} child={ChildId} "
                + "institute={InstituteId} bound={Bound} tenant_match={TenantMatch} "
                + "outcome={Outcome} trace_id={TraceId}",
            parentId ?? "(missing)",
            studentId,
            instituteId,
            bound,
            tenantMatch,
            outcome,
            traceId);
    }
}

/// <summary>
/// Verified binding return value from <see cref="ParentAuthorizationGuard.AssertCanAccessAsync"/>.
/// Callers downstream of the guard consume the parent id from this
/// record rather than re-reading claims — this keeps "who authorised
/// what" traceable from exactly one source.
/// </summary>
public sealed record ParentChildBindingResolution(
    string ParentActorId,
    string StudentSubjectId,
    string InstituteId);
