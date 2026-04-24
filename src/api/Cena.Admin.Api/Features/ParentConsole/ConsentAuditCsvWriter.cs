// =============================================================================
// Cena Platform — Consent audit CSV writer (prr-130)
//
// RFC 4180-conformant CSV serialisation of ConsentAuditRowDto. The schema
// mirrors the JSON output so a downstream script can import either format
// with the same column semantics.
//
// We do not use a third-party CSV library to avoid a new package on the
// Admin.Api surface; the escaping rules we need (comma, quote, newline,
// leading equals) fit in 20 lines of code and are easy to review.
// =============================================================================

using System.Text;

namespace Cena.Admin.Api.Features.ParentConsole;

/// <summary>
/// Writes <see cref="ConsentAuditRowDto"/> rows as RFC 4180 CSV.
/// </summary>
internal static class ConsentAuditCsvWriter
{
    /// <summary>Header row, in the same order as <see cref="ConsentAuditRowDto"/>.</summary>
    public static readonly string[] Header =
    {
        "event_type",
        "timestamp",
        "purpose",
        "actor_role",
        "actor_anon_id",
        "policy_version_accepted",
        "source",
        "reason",
        "scope",
        "institute_id",
        "trace_id",
        "expires_at",
    };

    /// <summary>
    /// Serialise the row sequence into a CSV payload. Returns an
    /// RFC-4180-compliant string with CRLF line endings.
    /// </summary>
    public static string Serialise(IReadOnlyList<ConsentAuditRowDto> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var sb = new StringBuilder();
        AppendRow(sb, Header);
        foreach (var row in rows)
        {
            AppendRow(sb, new[]
            {
                row.EventType,
                row.Timestamp,
                row.Purpose,
                row.ActorRole,
                row.ActorAnonId,
                row.PolicyVersionAccepted,
                row.Source,
                row.Reason,
                row.Scope,
                row.InstituteId,
                row.TraceId,
                row.ExpiresAt,
            });
        }
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, IReadOnlyList<string> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Escape(values[i] ?? string.Empty));
        }
        sb.Append("\r\n");
    }

    /// <summary>
    /// RFC-4180 escape. Fields containing comma, quote, CR, or LF are
    /// wrapped in double quotes with inner quotes doubled. Fields that
    /// begin with `=`, `+`, `-`, or `@` are prefixed with a single quote
    /// so Excel does not interpret them as formulas (CVE-2014-3524 class).
    /// </summary>
    internal static string Escape(string value)
    {
        var safe = value ?? string.Empty;
        var needsCsvEscape = safe.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        var firstChar = safe.Length > 0 ? safe[0] : '\0';
        var needsFormulaGuard = firstChar is '=' or '+' or '-' or '@';

        if (needsFormulaGuard)
        {
            safe = "'" + safe;
            needsCsvEscape = true; // forcing the quoting makes round-trip deterministic
        }

        if (!needsCsvEscape) return safe;
        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }
}
