// =============================================================================
// Cena Platform — Admin consent-audit export endpoint (prr-130)
//
// GET /api/admin/institutes/{iid}/students/{sid}/consent-audit?format=csv|json
//
// Returns the full consent history for one student: every grant, revoke,
// veto, restore, purpose-added, and parent-review event in append order,
// with timestamp, role, actor id (decrypted through EncryptedFieldAccessor
// per ADR-0038), purpose, source, policy_version_accepted, and trace id.
//
// AuthZ:
//   - ADMIN or SUPER_ADMIN role (CenaAuthPolicies.AdminOnly).
//   - TenantScope: the {iid} route parameter must match the caller's
//     institute_id claim unless the caller is SUPER_ADMIN.
//
// PII exposure:
//   - The admin is already authorised to see the audit record for a
//     student within their institute. The exporter surfaces:
//       • anonymised actor ids (parent_anon_id / student_anon_id as
//         emitted by the aggregate; no display names, no emails).
//       • role enum values.
//       • purpose enum values.
//       • timestamps.
//       • source tag ("api", "unsubscribe-link", "admin-override",
//         "system-retention", "institute-policy").
//       • policy_version_accepted string.
//       • trace_id (if the event was correlated at write time).
//   - Encrypted subject/actor fields are decrypted in-process; plaintext
//     is only materialised for this response and NOT logged.
//
// Architecture ratchet:
//   ConsentAuditExportDoesNotOmitEventsTest reflects over
//   Cena.Actors.Consent.Events and asserts every concrete sealed record
//   appears in the RenderRow switch below. New events require a matching
//   arm or CI fails.
// =============================================================================

using System.Globalization;
using System.Security.Claims;
using System.Text;
using Cena.Actors.Consent;
using Cena.Actors.Consent.Events;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Compliance;
using Cena.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Features.ParentConsole;

// ---- Wire DTOs --------------------------------------------------------------

/// <summary>
/// Single row of the consent audit export. Kept flat for CSV friendliness;
/// JSON output uses the same shape so a downstream script can import
/// either format with identical column semantics.
/// </summary>
/// <param name="EventType">Short name of the event (e.g. <c>ConsentGranted_V2</c>).</param>
/// <param name="Timestamp">Wall-clock UTC timestamp of the event (ISO-8601).</param>
/// <param name="Purpose">Processing purpose (enum name) or empty if N/A.</param>
/// <param name="ActorRole">Actor role that produced the event, if known.</param>
/// <param name="ActorAnonId">Decrypted actor id (anon), empty if event is actor-less.</param>
/// <param name="PolicyVersionAccepted">Policy version accepted at grant time; empty for non-grant events.</param>
/// <param name="Source">Source channel tag (api / unsubscribe-link / admin-override / system-retention / institute-policy).</param>
/// <param name="Reason">Structural reason code, empty if N/A.</param>
/// <param name="Scope">Structural scope label (institute id, device, …), empty if N/A.</param>
/// <param name="InstituteId">Institute id the event was scoped to, if recorded on the event.</param>
/// <param name="TraceId">Trace id from the original write, empty if none.</param>
/// <param name="ExpiresAt">Grant expiry timestamp, empty if event is not a grant or grant is indefinite.</param>
public sealed record ConsentAuditRowDto(
    string EventType,
    string Timestamp,
    string Purpose,
    string ActorRole,
    string ActorAnonId,
    string PolicyVersionAccepted,
    string Source,
    string Reason,
    string Scope,
    string InstituteId,
    string TraceId,
    string ExpiresAt);

/// <summary>Envelope for JSON output. CSV output bypasses this wrapper.</summary>
/// <param name="StudentAnonId">Anonymised student identifier.</param>
/// <param name="InstituteId">Institute the export was scoped to.</param>
/// <param name="ExportedAtUtc">Wall-clock UTC timestamp when the export was produced.</param>
/// <param name="ExportedByRole">Role of the admin caller producing the export.</param>
/// <param name="Rows">Chronologically-ordered rows.</param>
public sealed record ConsentAuditExportDto(
    string StudentAnonId,
    string InstituteId,
    string ExportedAtUtc,
    string ExportedByRole,
    IReadOnlyList<ConsentAuditRowDto> Rows);

// ---- Endpoint ---------------------------------------------------------------

/// <summary>
/// Admin endpoint registering <c>GET /api/admin/institutes/{iid}/students/{sid}/consent-audit</c>.
/// </summary>
public static class ConsentAuditExportEndpoint
{
    /// <summary>Canonical route for the audit export.</summary>
    public const string Route =
        "/api/admin/institutes/{instituteId}/students/{studentAnonId}/consent-audit";

    /// <summary>Supported format values.</summary>
    public static readonly IReadOnlyList<string> ValidFormats = new[] { "json", "csv" };

    /// <summary>Marker for <see cref="ILogger{T}"/> stability.</summary>
    internal sealed class ConsentAuditExportMarker { }

    /// <summary>Register the route.</summary>
    public static IEndpointRouteBuilder MapConsentAuditExportEndpoint(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, HandleAsync)
            .WithName("GetConsentAuditExport")
            .WithTags("Admin", "Consent", "Audit")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly);
        return app;
    }

    // ── Handler ──────────────────────────────────────────────────────────

    internal static async Task<IResult> HandleAsync(
        string instituteId,
        string studentAnonId,
        string? format,
        HttpContext http,
        IConsentAggregateStore consentStore,
        EncryptedFieldAccessor piiAccessor,
        ILogger<ConsentAuditExportMarker> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instituteId))
        {
            return Results.BadRequest(new { error = "missing-instituteId" });
        }
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            return Results.BadRequest(new { error = "missing-studentAnonId" });
        }

        var resolvedFormat = (format ?? "json").Trim().ToLowerInvariant();
        if (!ValidFormats.Contains(resolvedFormat, StringComparer.Ordinal))
        {
            return Results.BadRequest(new
            {
                error = "unknown-format",
                format = resolvedFormat,
                validFormats = ValidFormats,
            });
        }

        // Tenant scoping: SUPER_ADMIN may export any institute; plain ADMIN
        // is restricted to their own institute. Returning 403 (not 404)
        // for cross-tenant probes is intentional — distinguishing "unknown"
        // from "cross-tenant" would leak existence.
        if (!IsTenantAllowed(http.User, instituteId))
        {
            logger.LogWarning(
                "[prr-130] consent-audit export refused cross-tenant: "
                + "caller_institute_match=false requested={Requested}",
                instituteId);
            return Results.Forbid();
        }

        var events = await consentStore.ReadEventsAsync(studentAnonId, ct)
            .ConfigureAwait(false);

        // Unknown student → 404. Safe because we already established the
        // caller is authorised to see this institute.
        if (events.Count == 0)
        {
            return Results.NotFound(new
            {
                error = "student-not-found",
                studentAnonId,
                instituteId,
            });
        }

        // Decrypt PII fields and render rows. The exporter enforces tenant
        // scoping a second time by filtering out veto events that carry a
        // different InstituteId on the event itself (defence-in-depth
        // against a future cross-tenant write bug).
        var rows = new List<ConsentAuditRowDto>(events.Count);
        foreach (var raw in events)
        {
            var row = await ConsentAuditRowRenderer
                .RenderRowAsync(raw, studentAnonId, piiAccessor, ct)
                .ConfigureAwait(false);
            if (row is null) continue;
            // Drop event-scoped-but-cross-tenant rows.
            if (!string.IsNullOrEmpty(row.InstituteId)
                && !string.Equals(row.InstituteId, instituteId, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "[prr-130] consent-audit dropped cross-tenant event: "
                    + "row_institute={RowInstitute} requested={RequestedInstitute}",
                    row.InstituteId, instituteId);
                continue;
            }
            rows.Add(row);
        }

        var callerRole = http.User.FindFirstValue(ClaimTypes.Role)
            ?? http.User.FindFirstValue("role")
            ?? "ADMIN";

        logger.LogInformation(
            "[prr-130] consent-audit export: student={StudentAnonId} "
            + "institute={InstituteId} rows={RowCount} format={Format} role={Role}",
            studentAnonId, instituteId, rows.Count, resolvedFormat, callerRole);

        if (resolvedFormat == "csv")
        {
            var csv = ConsentAuditCsvWriter.Serialise(rows);
            var fileName = $"consent-audit-{studentAnonId}-{DateTime.UtcNow:yyyyMMddTHHmmssZ}.csv";
            return Results.File(
                Encoding.UTF8.GetBytes(csv),
                contentType: "text/csv; charset=utf-8",
                fileDownloadName: fileName);
        }

        return Results.Ok(new ConsentAuditExportDto(
            StudentAnonId: studentAnonId,
            InstituteId: instituteId,
            ExportedAtUtc: DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ExportedByRole: callerRole,
            Rows: rows));
    }

    /// <summary>
    /// Tenant check: SUPER_ADMIN sees everything; ADMIN must match the
    /// route institute against their institute_id claim. Does NOT use
    /// the TenantScope school-id helper because this endpoint operates
    /// on institute granularity (TENANCY-P1f).
    /// </summary>
    internal static bool IsTenantAllowed(ClaimsPrincipal user, string routeInstituteId)
    {
        var role = user.FindFirstValue(ClaimTypes.Role)
            ?? user.FindFirstValue("role");

        if (string.Equals(role, "SUPER_ADMIN", StringComparison.Ordinal))
        {
            return true;
        }

        var allowed = TenantScope.GetInstituteFilter(user, defaultInstituteId: null);
        if (allowed.Count == 0) return false;
        return allowed.Any(a => string.Equals(a, routeInstituteId, StringComparison.Ordinal));
    }
}
