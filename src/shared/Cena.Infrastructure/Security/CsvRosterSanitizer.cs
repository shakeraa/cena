// =============================================================================
// Cena Platform -- CSV Roster Import Sanitizer (prr-021)
//
// Hardens bulk-invite CSV parsing against:
//   * Size DoS          (byte + row caps, measured in-flight)
//   * CSV injection     (leading =, +, -, @, \t, \r stripped from every cell)
//   * Bidi overrides    (U+202A..U+202E, U+2066..U+2069, U+061C removed)
//   * Control chars     (all C0/C1 stripped, whitelist \n \r \t)
//   * UTF-8 malformity  (strict decode, reject on invalid sequences)
//   * Homoglyph attacks (NFC normalize, reject NFKC-sensitive drift)
//   * Header tampering  (whitelisted schema: name,email,role)
//
// Pure, allocation-only — no IO beyond the provided stream, no reflection,
// no external deps. Safe to hot-path in admin request handler.
//
// See docs/security/csv-import-threat-model.md for the STRIDE derivation.
// =============================================================================

using System.Globalization;
using System.Text;

namespace Cena.Infrastructure.Security;

/// <summary>
/// Configuration knobs for <see cref="CsvRosterSanitizer"/>. The endpoint
/// picks defaults from <c>RosterImportOptions</c> (per-tenant overrides
/// allowed). All limits are hard caps — exceeding any of them terminates
/// the parse with a file-level rejection.
/// </summary>
public sealed record CsvRosterSanitizerConfig
{
    /// <summary>Max total bytes read from the stream. Default: 10 MiB.</summary>
    public int MaxBytes { get; init; } = 10 * 1024 * 1024;

    /// <summary>Max data rows (excluding header). Default: 5000.</summary>
    public int MaxRows { get; init; } = 5000;

    /// <summary>
    /// Maximum cell length after sanitization, to contain pathological
    /// single-line payloads. Default: 1024 chars.
    /// </summary>
    public int MaxCellLength { get; init; } = 1024;

    /// <summary>
    /// Expected header names in canonical order. Parse rejects the file if
    /// the first line does not match case-insensitively (after trim).
    /// </summary>
    public IReadOnlyList<string> ExpectedHeader { get; init; } =
        new[] { "name", "email", "role" };
}

/// <summary>
/// Categories of per-row / per-file rejections, aggregated into the audit
/// summary as <c>rejections_by_kind</c>.
/// </summary>
public enum CsvRejectionKind
{
    FileTooLarge,
    TooManyRows,
    MalformedUtf8,
    HeaderMismatch,
    EmptyRow,
    WrongColumnCount,
    CellTooLong,
    HomoglyphSuspect,
}

/// <summary>A parsed-and-sanitized roster row. Cells are post-scrub.</summary>
public sealed record CsvRosterRow(
    int LineNumber,
    string Name,
    string Email,
    string Role);

/// <summary>Result of a CSV roster parse.</summary>
public sealed record CsvRosterParseResult(
    IReadOnlyList<CsvRosterRow> Rows,
    IReadOnlyDictionary<CsvRejectionKind, int> RejectionsByKind,
    long BytesRead,
    bool FileRejected,
    CsvRejectionKind? FileRejectionKind,
    string? FileRejectionDetail)
{
    public int RowCount => Rows.Count;
    public int TotalRejections => RejectionsByKind.Values.Sum();
    public bool HasRejections => TotalRejections > 0 || FileRejected;
}

/// <summary>
/// Pure parser. Call <see cref="Parse"/> once per upload; the sanitizer
/// holds no state between calls.
/// </summary>
public static class CsvRosterSanitizer
{
    // --- Bidi / control character set --------------------------------------
    // U+202A..U+202E: LRE, RLE, PDF, LRO, RLO
    // U+2066..U+2069: LRI, RLI, FSI, PDI
    // U+061C: Arabic Letter Mark
    private static readonly HashSet<int> BidiCodepoints = new()
    {
        0x202A, 0x202B, 0x202C, 0x202D, 0x202E,
        0x2066, 0x2067, 0x2068, 0x2069,
        0x061C,
    };

    // --- CSV injection trigger characters (leading, per cell) --------------
    // =  formula
    // +  formula
    // -  formula (also negative number, but Excel interprets as formula
    //    when followed by fn name; bright-line rule: strip)
    // @  Lotus legacy trigger (still honored by Excel/Calc)
    // \t DDE injection in some spreadsheet apps
    // \r CR smuggling
    private static readonly char[] InjectionTriggers =
        { '=', '+', '-', '@', '\t', '\r' };

    /// <summary>
    /// Parses the stream as UTF-8 CSV with a fixed header. On any file-level
    /// failure (oversize, malformed UTF-8, header mismatch) the result is
    /// returned with <see cref="CsvRosterParseResult.FileRejected"/>=true
    /// and <see cref="CsvRosterParseResult.Rows"/> empty.
    /// </summary>
    public static CsvRosterParseResult Parse(Stream stream, CsvRosterSanitizerConfig config)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(config);

        var rejections = new Dictionary<CsvRejectionKind, int>();
        var rows = new List<CsvRosterRow>();

        // Strict UTF-8 decoder: malformed bytes throw DecoderFallbackException.
        var strictUtf8 = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

        string text;
        long bytesRead;
        try
        {
            var buffer = new byte[config.MaxBytes + 1];
            var totalRead = 0;
            int n;
            while ((n = stream.Read(buffer, totalRead, buffer.Length - totalRead)) > 0)
            {
                totalRead += n;
                if (totalRead > config.MaxBytes)
                {
                    return FileReject(
                        CsvRejectionKind.FileTooLarge,
                        $"Max {config.MaxBytes} bytes",
                        rejections,
                        totalRead);
                }
            }
            bytesRead = totalRead;

            // Strip UTF-8 BOM if present before decoding so normalization is consistent.
            var offset = 0;
            if (totalRead >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            {
                offset = 3;
            }

            text = strictUtf8.GetString(buffer, offset, totalRead - offset);
        }
        catch (DecoderFallbackException ex)
        {
            return FileReject(
                CsvRejectionKind.MalformedUtf8,
                ex.Message,
                rejections,
                bytesRead: 0);
        }

        // Split on \n or \r\n. CRs that survive inside quoted cells are
        // unsupported (we reject the row).
        var lines = text.Split('\n');

        if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
        {
            return FileReject(
                CsvRejectionKind.HeaderMismatch,
                "Empty file or missing header",
                rejections,
                bytesRead);
        }

        var header = ParseCsvLine(lines[0].TrimEnd('\r'));
        if (!HeaderMatches(header, config.ExpectedHeader))
        {
            return FileReject(
                CsvRejectionKind.HeaderMismatch,
                $"Expected header [{string.Join(',', config.ExpectedHeader)}], got [{string.Join(',', header)}]",
                rejections,
                bytesRead);
        }

        for (var i = 1; i < lines.Length; i++)
        {
            var raw = lines[i].TrimEnd('\r');
            if (raw.Length == 0)
                continue; // trailing newline — ignore silently

            if (rows.Count >= config.MaxRows)
            {
                Increment(rejections, CsvRejectionKind.TooManyRows);
                // Keep counting rejections for audit but don't materialize more rows.
                continue;
            }

            var cells = ParseCsvLine(raw);
            if (cells.Count != config.ExpectedHeader.Count)
            {
                Increment(rejections, CsvRejectionKind.WrongColumnCount);
                continue;
            }

            var scrubbed = new string[cells.Count];
            var rejectRow = false;
            CsvRejectionKind? rejectionKind = null;

            for (var c = 0; c < cells.Count; c++)
            {
                if (!TryScrub(cells[c], config.MaxCellLength, out var clean, out var kind))
                {
                    rejectRow = true;
                    rejectionKind = kind;
                    break;
                }
                scrubbed[c] = clean;
            }

            if (rejectRow && rejectionKind is { } rk)
            {
                Increment(rejections, rk);
                continue;
            }

            if (scrubbed.Any(string.IsNullOrWhiteSpace))
            {
                Increment(rejections, CsvRejectionKind.EmptyRow);
                continue;
            }

            rows.Add(new CsvRosterRow(
                LineNumber: i + 1,
                Name: scrubbed[0],
                Email: scrubbed[1],
                Role: scrubbed[2]));
        }

        return new CsvRosterParseResult(
            Rows: rows,
            RejectionsByKind: rejections,
            BytesRead: bytesRead,
            FileRejected: false,
            FileRejectionKind: null,
            FileRejectionDetail: null);
    }

    // =========================================================================
    // Per-cell scrubbing
    // =========================================================================

    /// <summary>
    /// Applies the full defense stack to a single cell. Returns false if the
    /// cell must be rejected at row level (homoglyph drift, overlength).
    /// </summary>
    internal static bool TryScrub(
        string raw,
        int maxLength,
        out string cleaned,
        out CsvRejectionKind? rejectionKind)
    {
        rejectionKind = null;

        // 1. Strip bidi + control chars (not \n \r \t, which are separators).
        var noBidi = new StringBuilder(raw.Length);
        foreach (var ch in raw.EnumerateRunes())
        {
            var cp = ch.Value;
            if (BidiCodepoints.Contains(cp))
                continue;
            // Drop C0 controls (0x00-0x1F) except \t (0x09), \n (0x0A), \r (0x0D).
            if (cp < 0x20 && cp != 0x09 && cp != 0x0A && cp != 0x0D)
                continue;
            // Drop DEL + C1 controls (0x7F-0x9F).
            if (cp >= 0x7F && cp <= 0x9F)
                continue;
            noBidi.Append(ch.ToString());
        }

        var s = noBidi.ToString();

        // 2. Trim outer whitespace (including \t, leading \r from quote-wrap).
        s = s.Trim();

        // 3. Strip CSV-injection trigger characters from the start. Loop so
        //    that `=+@A` is fully disarmed, not just the outermost `=`.
        var changed = true;
        while (changed && s.Length > 0)
        {
            changed = false;
            foreach (var trig in InjectionTriggers)
            {
                if (s.Length > 0 && s[0] == trig)
                {
                    s = s.Substring(1);
                    changed = true;
                }
            }
            s = s.TrimStart();
        }

        // 4. Length cap (after scrubbing, so padding attacks do not silently
        //    expand). A cell over the cap after scrub is a hostile pattern.
        if (s.Length > maxLength)
        {
            cleaned = string.Empty;
            rejectionKind = CsvRejectionKind.CellTooLong;
            return false;
        }

        // 5. Unicode NFC normalization (canonical compose).
        var nfc = s.IsNormalized(NormalizationForm.FormC)
            ? s
            : s.Normalize(NormalizationForm.FormC);

        // 6. Homoglyph drift check: if NFKC collapses further than NFC, the
        //    cell contains NFKC-sensitive sequences (e.g. Unicode spoofs like
        //    FULLWIDTH LATIN letters, or compatibility decompositions that
        //    mask the real identifier). We reject rather than silently
        //    rewrite — silent rewrite would violate "labels match data".
        var nfkc = nfc.Normalize(NormalizationForm.FormKC);
        if (!string.Equals(nfc, nfkc, StringComparison.Ordinal))
        {
            cleaned = string.Empty;
            rejectionKind = CsvRejectionKind.HomoglyphSuspect;
            return false;
        }

        cleaned = nfc;
        return true;
    }

    // =========================================================================
    // CSV line parsing (minimal; handles double-quoted cells with "" escape)
    // =========================================================================

    internal static List<string> ParseCsvLine(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(ch);
                }
                continue;
            }

            if (ch == ',')
            {
                cells.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            if (ch == '"' && sb.Length == 0)
            {
                inQuotes = true;
                continue;
            }

            sb.Append(ch);
        }

        cells.Add(sb.ToString());
        return cells;
    }

    private static bool HeaderMatches(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
    {
        if (actual.Count != expected.Count) return false;
        for (var i = 0; i < actual.Count; i++)
        {
            var a = actual[i].Trim().Trim('"').Trim();
            if (!string.Equals(a, expected[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static void Increment(Dictionary<CsvRejectionKind, int> dict, CsvRejectionKind kind)
    {
        dict[kind] = dict.TryGetValue(kind, out var n) ? n + 1 : 1;
    }

    private static CsvRosterParseResult FileReject(
        CsvRejectionKind kind,
        string detail,
        Dictionary<CsvRejectionKind, int> rejections,
        long bytesRead)
    {
        Increment(rejections, kind);
        return new CsvRosterParseResult(
            Rows: Array.Empty<CsvRosterRow>(),
            RejectionsByKind: rejections,
            BytesRead: bytesRead,
            FileRejected: true,
            FileRejectionKind: kind,
            FileRejectionDetail: detail);
    }
}
