// =============================================================================
// Cena Platform -- Roster Import Processor (prr-021)
//
// Extracted from AdminUserService so that hardening logic does not bloat
// the user-service file past its 500-LOC grandfather baseline.
//
// Responsibilities:
//   * Run CsvRosterSanitizer with tenant-specific limits
//   * Invoke the per-row invite path, catching cross-tenant rejects
//   * Emit the audit event (structured log + AuditEventDocument)
//
// Callable state: none — all state flows through parameters so the
// processor is free of hidden coupling and easy to unit test.
// =============================================================================

using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Security;
using Cena.Infrastructure.Tenancy;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.RosterImport;

internal static class RosterImportProcessor
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// prr-021: Hardened roster-import pipeline. See
    /// docs/security/csv-import-threat-model.md for the STRIDE derivation.
    /// </summary>
    public static async Task<BulkInviteResult> RunAsync(
        Stream csvStream,
        ClaimsPrincipal caller,
        RosterImportOptions options,
        IDocumentStore store,
        ILogger logger,
        Func<InviteUserRequest, ClaimsPrincipal, Task> invite)
    {
        // FIND-sec-008 + prr-021: non-SUPER_ADMIN admins import only into
        // their own school; SUPER_ADMIN gets null (unrestricted).
        var schoolId = TenantScope.GetSchoolFilter(caller);
        var targetSchool = schoolId ?? string.Empty;

        var sanitizerConfig = options.ForTenant(schoolId);
        var parseResult = CsvRosterSanitizer.Parse(csvStream, sanitizerConfig);

        var created = 0;
        var failed = new List<BulkInviteFailure>();

        if (parseResult.FileRejected)
        {
            failed.Add(new BulkInviteFailure(
                Email: "<file>",
                Error: $"{parseResult.FileRejectionKind}: {parseResult.FileRejectionDetail}"));
        }
        else
        {
            foreach (var row in parseResult.Rows)
            {
                try
                {
                    // prr-021: force targetSchool from caller; CSV carries no
                    // school hint. Cross-tenant writes blocked by invite.
                    await invite(
                        new InviteUserRequest(row.Email, row.Role, targetSchool),
                        caller);
                    created++;
                }
                catch (UnauthorizedAccessException)
                {
                    failed.Add(new BulkInviteFailure(row.Email, "invite_failed"));
                }
                catch (Exception ex)
                {
                    failed.Add(new BulkInviteFailure(row.Email, ex.Message));
                }
            }
        }

        await WriteAuditAsync(
            caller: caller,
            tenantId: schoolId ?? "(super_admin)",
            parseResult: parseResult,
            created: created,
            failedCount: failed.Count,
            store: store,
            logger: logger);

        return new BulkInviteResult(created, failed);
    }

    private static async Task WriteAuditAsync(
        ClaimsPrincipal caller,
        string tenantId,
        CsvRosterParseResult parseResult,
        int created,
        int failedCount,
        IDocumentStore store,
        ILogger logger)
    {
        var adminUserId = caller.FindFirstValue("user_id")
            ?? caller.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? caller.FindFirstValue("sub")
            ?? "anonymous";
        var adminName = caller.FindFirstValue(ClaimTypes.Name)
            ?? caller.FindFirstValue("name")
            ?? caller.FindFirstValue("email")
            ?? string.Empty;
        var adminRole = caller.FindFirstValue(ClaimTypes.Role)
            ?? caller.FindFirstValue("role")
            ?? "unknown";
        var traceId = Activity.Current?.TraceId.ToString() ?? "no-trace";

        var rejectionsByKind = parseResult.RejectionsByKind
            .ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value);

        var totalRejected = parseResult.TotalRejections + failedCount
            - (parseResult.FileRejected ? 1 : 0);

        logger.LogInformation(
            "[AUDIT] roster_import tenant={TenantId} admin={AdminId} bytes={Bytes} rows_accepted={Created} rows_rejected={Rejected} kinds={Kinds} file_rejected={FileRejected} trace={TraceId}",
            tenantId,
            adminUserId,
            parseResult.BytesRead,
            created,
            totalRejected,
            string.Join(",", rejectionsByKind.Select(kv => $"{kv.Key}:{kv.Value}")),
            parseResult.FileRejected,
            traceId);

        try
        {
            var metadata = JsonSerializer.Serialize(new
            {
                byte_count = parseResult.BytesRead,
                row_count = parseResult.RowCount,
                rows_created = created,
                rows_failed = failedCount,
                rejections_by_kind = rejectionsByKind,
                file_rejected = parseResult.FileRejected,
                file_rejection_kind = parseResult.FileRejectionKind?.ToString(),
                file_rejection_detail = parseResult.FileRejectionDetail,
                trace_id = traceId,
            }, JsonOpts);

            await using var session = store.LightweightSession();
            session.Store(new AuditEventDocument
            {
                Id = $"audit:admin-action:roster-import:{Guid.NewGuid():N}",
                Timestamp = DateTimeOffset.UtcNow,
                EventType = "admin_action",
                UserId = adminUserId,
                UserName = adminName,
                UserRole = adminRole,
                TenantId = tenantId,
                Action = "users.bulk_invite",
                TargetType = "roster",
                TargetId = string.Empty,
                Description = parseResult.FileRejected
                    ? $"Roster import rejected at file level: {parseResult.FileRejectionKind}"
                    : $"Roster import: {created} invited, {failedCount} failed, {parseResult.TotalRejections} scrub-rejections",
                IpAddress = string.Empty,
                UserAgent = string.Empty,
                Success = created > 0 || (!parseResult.FileRejected && failedCount == 0),
                ErrorMessage = parseResult.FileRejectionDetail,
                MetadataJson = metadata,
            });
            await session.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AUDIT] Failed to persist roster-import audit row for tenant {TenantId}", tenantId);
        }
    }
}
