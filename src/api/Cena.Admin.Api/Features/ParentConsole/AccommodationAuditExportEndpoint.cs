// =============================================================================
// Cena Platform — Accommodation audit exports for parents (prr-106)
//
// GET /api/v1/parent/minors/{studentAnonId}/accommodation-audit?format=csv|json
//
// Returns the current accommodations profile + full history of every
// AccommodationProfileAssignedV1 event for the minor, with actor +
// timestamp + reason (assigner signature + ministry hatama code).
//
// AuthZ (prr-009):
//   - PARENT role, must pass ParentAuthorizationGuard.AssertCanAccessAsync
//     for the (parent, student, institute) triple.
//   - ADMIN / SUPER_ADMIN fall through for a support read path; Marten's
//     TenantScope filter keeps them pinned to their own institute unless
//     they are SUPER_ADMIN.
//
// Privacy (ADR-0003 + GDPR Art 9):
//   - Accommodation data is GDPR Art. 9 special-category (disability
//     status). The parent is legally entitled to this record for their
//     minor; the admin support path exists for DSAR servicing.
//   - NO misconception data appears on the export — accommodation events
//     carry only the enabled-dimensions set, the assigner, the ministry
//     hatama code, and a consent-document hash. Misconception data stays
//     session-scoped on the learning stream (ADR-0003 §Scope).
//   - We never render the consent PDF itself, only the hash so a parent
//     can prove identity of the PDF they signed.
// =============================================================================

using System.Globalization;
using System.Text;
using Cena.Actors.Accommodations;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.Security;
using Cena.Infrastructure.Tenancy;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Features.ParentConsole;

// ---- Wire DTOs --------------------------------------------------------------

/// <summary>
/// A single history row from the accommodation audit export. Flat shape
/// so the CSV writer and JSON serializer share one column set.
/// </summary>
/// <param name="AssignedAtUtc">ISO-8601 UTC timestamp of the event.</param>
/// <param name="Assigner">Who set the profile (Parent / Self / Teacher).</param>
/// <param name="AssignerAnonId">Anonymised actor id from the event.</param>
/// <param name="EnabledDimensions">Pipe-joined list of enabled dimensions.</param>
/// <param name="MinistryHatamaCode">Ministry hatama code (e.g. "HTM-1"), empty if none.</param>
/// <param name="ConsentDocumentHash">SHA hash of the consent PDF, empty if none.</param>
public sealed record AccommodationAuditRowDto(
    string AssignedAtUtc,
    string Assigner,
    string AssignerAnonId,
    string EnabledDimensions,
    string MinistryHatamaCode,
    string ConsentDocumentHash);

/// <summary>
/// Envelope for the JSON format. CSV bypasses this wrapper.
/// </summary>
/// <param name="StudentAnonId">Anonymised student id.</param>
/// <param name="ExportedAtUtc">Wall-clock timestamp of the export.</param>
/// <param name="ExportedByRole">Role of the caller producing the export.</param>
/// <param name="CurrentDimensions">The most-recent enabled dimensions (empty if none).</param>
/// <param name="CurrentMinistryHatamaCode">Most-recent ministry code, empty if none.</param>
/// <param name="History">Chronologically-ordered history rows (earliest first).</param>
public sealed record AccommodationAuditExportDto(
    string StudentAnonId,
    string ExportedAtUtc,
    string ExportedByRole,
    IReadOnlyList<string> CurrentDimensions,
    string CurrentMinistryHatamaCode,
    IReadOnlyList<AccommodationAuditRowDto> History);

// ---- Endpoint ---------------------------------------------------------------

public static class AccommodationAuditExportEndpoint
{
    /// <summary>Canonical route path.</summary>
    public const string Route =
        "/api/v1/parent/minors/{studentAnonId}/accommodation-audit";

    /// <summary>Supported format query values.</summary>
    public static readonly IReadOnlyList<string> ValidFormats = new[] { "json", "csv" };

    /// <summary>Marker for ILogger type-argument stability.</summary>
    internal sealed class AccommodationAuditExportMarker { }

    public static IEndpointRouteBuilder MapAccommodationAuditExportEndpoint(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, HandleAsync)
            .WithName("GetMinorAccommodationAudit")
            .WithTags("Parent Console", "Accommodations", "Audit")
            .RequireAuthorization();
        return app;
    }

    internal static async Task<IResult> HandleAsync(
        string studentAnonId,
        string? format,
        HttpContext http,
        IDocumentStore documentStore,
        ILogger<AccommodationAuditExportMarker> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
            return Results.BadRequest(new { error = "missing-studentAnonId" });

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

        // AuthZ mirrors AccommodationsEndpoints.AuthorizeReadOrForbidAsync:
        // SUPER_ADMIN / ADMIN fall through; PARENT must pass the binding
        // guard; everyone else 403.
        var callerRole = "PARENT";
        if (http.User.IsInRole("SUPER_ADMIN"))
        {
            callerRole = "SUPER_ADMIN";
        }
        else if (http.User.IsInRole("ADMIN"))
        {
            callerRole = "ADMIN";
        }
        else if (http.User.IsInRole("PARENT"))
        {
            try
            {
                await RequireParentBindingAsync(http, studentAnonId, ct).ConfigureAwait(false);
            }
            catch (ForbiddenException ex) when (ex.ErrorCode == ErrorCodes.CENA_AUTH_IDOR_VIOLATION)
            {
                return Results.Forbid();
            }
        }
        else
        {
            return Results.Forbid();
        }

        // Load the full history — every AccommodationProfileAssignedV1 event
        // ever written to this student's stream.
        using var session = documentStore.QuerySession();
        var events = await session.Events
            .QueryRawEventDataOnly<AccommodationProfileAssignedV1>()
            .Where(e => e.StudentAnonId == studentAnonId)
            .OrderBy(e => e.AssignedAtUtc)
            .ToListAsync(ct);

        var rows = new List<AccommodationAuditRowDto>(events.Count);
        foreach (var ev in events)
        {
            rows.Add(new AccommodationAuditRowDto(
                AssignedAtUtc: FormatIso(ev.AssignedAtUtc),
                Assigner: ev.Assigner.ToString(),
                AssignerAnonId: ev.AssignerSignature ?? string.Empty,
                EnabledDimensions: string.Join("|",
                    ev.EnabledDimensions.Select(d => d.ToString())),
                MinistryHatamaCode: ev.MinistryHatamaCode ?? string.Empty,
                ConsentDocumentHash: ev.ConsentDocumentHash ?? string.Empty));
        }

        var current = events.Count == 0 ? null : events[^1];
        var currentDims = current is null
            ? Array.Empty<string>()
            : current.EnabledDimensions.Select(d => d.ToString()).ToArray();

        logger.LogInformation(
            "[prr-106] accommodation-audit export: student={StudentAnonId} "
            + "rows={RowCount} format={Format} role={Role}",
            studentAnonId, rows.Count, resolvedFormat, callerRole);

        if (resolvedFormat == "csv")
        {
            var csv = RenderCsv(studentAnonId, rows);
            var fileName =
                $"accommodation-audit-{studentAnonId}-{DateTime.UtcNow:yyyyMMddTHHmmssZ}.csv";
            return Results.File(
                Encoding.UTF8.GetBytes(csv),
                contentType: "text/csv; charset=utf-8",
                fileDownloadName: fileName);
        }

        return Results.Ok(new AccommodationAuditExportDto(
            StudentAnonId: studentAnonId,
            ExportedAtUtc: FormatIso(DateTimeOffset.UtcNow),
            ExportedByRole: callerRole,
            CurrentDimensions: currentDims,
            CurrentMinistryHatamaCode: current?.MinistryHatamaCode ?? string.Empty,
            History: rows));
    }

    // ── CSV helper ──────────────────────────────────────────────────────

    internal static string RenderCsv(
        string studentAnonId, IReadOnlyList<AccommodationAuditRowDto> rows)
    {
        var sb = new StringBuilder();
        // Header
        sb.Append("student_anon_id,assigned_at_utc,assigner,assigner_anon_id,")
          .AppendLine("enabled_dimensions,ministry_hatama_code,consent_document_hash");

        foreach (var row in rows)
        {
            sb.Append(CsvEscape(studentAnonId)).Append(',');
            sb.Append(CsvEscape(row.AssignedAtUtc)).Append(',');
            sb.Append(CsvEscape(row.Assigner)).Append(',');
            sb.Append(CsvEscape(row.AssignerAnonId)).Append(',');
            sb.Append(CsvEscape(row.EnabledDimensions)).Append(',');
            sb.Append(CsvEscape(row.MinistryHatamaCode)).Append(',');
            sb.AppendLine(CsvEscape(row.ConsentDocumentHash));
        }
        return sb.ToString();
    }

    /// <summary>
    /// RFC 4180 CSV field escape. Wraps in quotes and doubles internal
    /// quotes if the value contains a comma, quote, CR, or LF.
    /// </summary>
    internal static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var needsQuoting = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        if (!needsQuoting) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    // ── AuthZ helper — mirrors AccommodationsEndpoints ───────────────────

    internal static async Task<ParentChildBindingResolution> RequireParentBindingAsync(
        HttpContext http, string studentAnonId, CancellationToken ct)
    {
        var services = http.RequestServices;
        var bindingService = services.GetRequiredService<IParentChildBindingService>();
        var logger = services.GetRequiredService<ILogger<AccommodationAuditExportMarker>>();
        var claims = TenantScope.GetInstituteFilter(http.User, defaultInstituteId: null);
        var instituteId = claims.Count == 0 ? string.Empty : claims[0];
        if (string.IsNullOrWhiteSpace(instituteId))
        {
            throw new ForbiddenException(
                ErrorCodes.CENA_AUTH_IDOR_VIOLATION,
                "PARENT caller is missing a required institute_id claim.");
        }
        return await ParentAuthorizationGuard.AssertCanAccessAsync(
            http.User, studentAnonId, instituteId, bindingService, logger, ct)
            .ConfigureAwait(false);
    }

    private static string FormatIso(DateTimeOffset ts)
        => ts.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
