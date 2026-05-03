// =============================================================================
// Cena Platform -- CsvRosterSanitizer tests (prr-021)
// Covers every STRIDE cell in docs/security/csv-import-threat-model.md.
// =============================================================================

using System.Text;
using Cena.Infrastructure.Security;

namespace Cena.Infrastructure.Tests.Security;

public class CsvRosterSanitizerTests
{
    private static readonly CsvRosterSanitizerConfig DefaultConfig = new()
    {
        MaxBytes = 10 * 1024 * 1024,
        MaxRows = 5000,
        MaxCellLength = 1024,
    };

    private static Stream Utf8Stream(string content, bool bom = false)
    {
        var ms = new MemoryStream();
        if (bom)
        {
            ms.Write(new byte[] { 0xEF, 0xBB, 0xBF }, 0, 3);
        }
        var bytes = Encoding.UTF8.GetBytes(content);
        ms.Write(bytes, 0, bytes.Length);
        ms.Position = 0;
        return ms;
    }

    // =========================================================================
    // Golden-path parsing
    // =========================================================================

    [Fact]
    public void Parse_ValidCsv_ReturnsAllRows()
    {
        var csv = "name,email,role\n"
                + "Alice,alice@example.com,STUDENT\n"
                + "Bob,bob@example.com,TEACHER\n";

        var result = CsvRosterSanitizer.Parse(Utf8Stream(csv), DefaultConfig);

        Assert.False(result.FileRejected);
        Assert.Equal(2, result.RowCount);
        Assert.Equal("Alice", result.Rows[0].Name);
        Assert.Equal("alice@example.com", result.Rows[0].Email);
        Assert.Equal("STUDENT", result.Rows[0].Role);
    }

    [Fact]
    public void Parse_BomPrefixed_Accepted()
    {
        var csv = "name,email,role\nAlice,alice@example.com,STUDENT\n";
        var result = CsvRosterSanitizer.Parse(Utf8Stream(csv, bom: true), DefaultConfig);

        Assert.False(result.FileRejected);
        Assert.Single(result.Rows);
    }

    [Fact]
    public void Parse_CrLfLineEndings_Accepted()
    {
        var csv = "name,email,role\r\nAlice,alice@example.com,STUDENT\r\n";
        var result = CsvRosterSanitizer.Parse(Utf8Stream(csv), DefaultConfig);

        Assert.False(result.FileRejected);
        Assert.Single(result.Rows);
        Assert.Equal("Alice", result.Rows[0].Name);
    }

    [Fact]
    public void Parse_QuotedCellsWithCommas_Preserved()
    {
        var csv = "name,email,role\n\"Smith, John\",j@x.com,STUDENT\n";
        var result = CsvRosterSanitizer.Parse(Utf8Stream(csv), DefaultConfig);

        Assert.False(result.FileRejected);
        Assert.Equal("Smith, John", result.Rows[0].Name);
    }

    // =========================================================================
    // Size caps — file too large, too many rows
    // =========================================================================

    [Fact]
    public void Parse_OversizeFile_Rejected()
    {
        var cfg = DefaultConfig with { MaxBytes = 100 };
        var padding = new string('x', 200);
        var csv = $"name,email,role\nAlice,alice@example.com,{padding}\n";

        var result = CsvRosterSanitizer.Parse(Utf8Stream(csv), cfg);

        Assert.True(result.FileRejected);
        Assert.Equal(CsvRejectionKind.FileTooLarge, result.FileRejectionKind);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Parse_TooManyRows_ExcessRejected()
    {
        var cfg = DefaultConfig with { MaxRows = 2 };
        var sb = new StringBuilder("name,email,role\n");
        for (var i = 0; i < 5; i++)
            sb.AppendLine($"User{i},u{i}@x.com,STUDENT");

        var result = CsvRosterSanitizer.Parse(Utf8Stream(sb.ToString()), cfg);

        Assert.False(result.FileRejected);
        Assert.Equal(2, result.RowCount);
        Assert.Equal(3, result.RejectionsByKind[CsvRejectionKind.TooManyRows]);
    }

    // =========================================================================
    // CSV-injection — =, +, -, @, \t, \r strip
    // =========================================================================

    [Theory]
    [InlineData("=cmd|' /c calc'!A1")]
    [InlineData("+HYPERLINK(\"http://evil\")")]
    [InlineData("-2+3+cmd")]
    [InlineData("@SUM(1+1)*cmd")]
    [InlineData("\tleadingtab")]
    public void Parse_InjectionPayloadInName_StrippedLeading(string hostile)
    {
        var csv = $"name,email,role\n\"{hostile}\",alice@x.com,STUDENT\n";
        var result = CsvRosterSanitizer.Parse(Utf8Stream(csv), DefaultConfig);

        Assert.False(result.FileRejected);
        Assert.Single(result.Rows);
        var name = result.Rows[0].Name;
        Assert.False(name.StartsWith("="));
        Assert.False(name.StartsWith("+"));
        Assert.False(name.StartsWith("-"));
        Assert.False(name.StartsWith("@"));
        Assert.False(name.StartsWith("\t"));
    }

    [Fact]
    public void Parse_MultipleLeadingTriggers_AllStripped()
    {
        var csv = "name,email,role\n\"=+-@test\",alice@x.com,STUDENT\n";
        var result = CsvRosterSanitizer.Parse(Utf8Stream(csv), DefaultConfig);

        Assert.False(result.FileRejected);
        Assert.Equal("test", result.Rows[0].Name);
    }

    [Fact]
    public void Parse_InteriorEqualsSign_Preserved()
    {
        // Only LEADING triggers are stripped; middle-of-cell = is legitimate.
        var csv = "name,email,role\n\"Foo=Bar\",alice@x.com,STUDENT\n";
        var result = CsvRosterSanitizer.Parse(Utf8Stream(csv), DefaultConfig);

        Assert.Equal("Foo=Bar", result.Rows[0].Name);
    }

    // =========================================================================
    // Bidi override strip
    // =========================================================================

    [Theory]
    [InlineData("\u202Eevil")]   // RLO
    [InlineData("\u202DChar")]    // LRO
    [InlineData("\u2066mask")]    // LRI
    [InlineData("\u061CArabicMark")] // ALM
    public void Parse_BidiOverride_Removed(string hostile)
    {
        var csv = $"name,email,role\n{hostile},alice@x.com,STUDENT\n";
        var result = CsvRosterSanitizer.Parse(Utf8Stream(csv), DefaultConfig);

        Assert.False(result.FileRejected);
        Assert.DoesNotContain('\u202A', result.Rows[0].Name);
        Assert.DoesNotContain('\u202B', result.Rows[0].Name);
        Assert.DoesNotContain('\u202C', result.Rows[0].Name);
        Assert.DoesNotContain('\u202D', result.Rows[0].Name);
        Assert.DoesNotContain('\u202E', result.Rows[0].Name);
        Assert.DoesNotContain('\u2066', result.Rows[0].Name);
        Assert.DoesNotContain('\u2067', result.Rows[0].Name);
        Assert.DoesNotContain('\u2068', result.Rows[0].Name);
        Assert.DoesNotContain('\u2069', result.Rows[0].Name);
        Assert.DoesNotContain('\u061C', result.Rows[0].Name);
    }

    // =========================================================================
    // UTF-8 handling
    // =========================================================================

    [Fact]
    public void Parse_NonUtf8BytesBomless_Rejected()
    {
        var ms = new MemoryStream();
        // "name,email,role\n" in ASCII
        ms.Write(Encoding.ASCII.GetBytes("name,email,role\n"));
        // Invalid UTF-8 continuation byte at start of a code point.
        ms.WriteByte(0xC3);
        ms.WriteByte(0x28); // not a valid continuation
        ms.Write(Encoding.ASCII.GetBytes(",alice@x.com,STUDENT\n"));
        ms.Position = 0;

        var result = CsvRosterSanitizer.Parse(ms, DefaultConfig);

        Assert.True(result.FileRejected);
        Assert.Equal(CsvRejectionKind.MalformedUtf8, result.FileRejectionKind);
    }

    [Fact]
    public void Parse_NfcNormalized_Preserved()
    {
        // "café" with a composed é (U+00E9) — already NFC, no drift.
        var csv = "name,email,role\ncaf\u00e9,alice@x.com,STUDENT\n";
        var result = CsvRosterSanitizer.Parse(Utf8Stream(csv), DefaultConfig);

        Assert.False(result.FileRejected);
        Assert.Equal("caf\u00e9", result.Rows[0].Name);
    }

    [Fact]
    public void Parse_NfcComposedFromDecomposed_Normalized()
    {
        // "e" + combining acute (U+0065 U+0301) → NFC should compose to U+00E9.
        var csv = "name,email,role\nCaf\u0065\u0301,alice@x.com,STUDENT\n";
        var result = CsvRosterSanitizer.Parse(Utf8Stream(csv), DefaultConfig);

        Assert.False(result.FileRejected);
        Assert.Equal("Caf\u00e9", result.Rows[0].Name);
    }

    [Fact]
    public void Parse_HomoglyphFullwidth_Rejected()
    {
        // FULLWIDTH LATIN CAPITAL A (U+FF21) — NFKC collapses to 'A',
        // NFC leaves as-is → drift detected → row rejected.
        var csv = "name,email,role\n\uFF21dmin,alice@x.com,STUDENT\n";
        var result = CsvRosterSanitizer.Parse(Utf8Stream(csv), DefaultConfig);

        Assert.False(result.FileRejected);
        Assert.Empty(result.Rows);
        Assert.Equal(1, result.RejectionsByKind[CsvRejectionKind.HomoglyphSuspect]);
    }

    // =========================================================================
    // Header whitelist
    // =========================================================================

    [Fact]
    public void Parse_UnknownHeader_RejectsFile()
    {
        var csv = "name,email,password\nAlice,alice@x.com,hunter2\n";
        var result = CsvRosterSanitizer.Parse(Utf8Stream(csv), DefaultConfig);

        Assert.True(result.FileRejected);
        Assert.Equal(CsvRejectionKind.HeaderMismatch, result.FileRejectionKind);
    }

    [Fact]
    public void Parse_CaseInsensitiveHeader_Accepted()
    {
        var csv = "Name,Email,ROLE\nAlice,alice@x.com,STUDENT\n";
        var result = CsvRosterSanitizer.Parse(Utf8Stream(csv), DefaultConfig);

        Assert.False(result.FileRejected);
        Assert.Single(result.Rows);
    }

    [Fact]
    public void Parse_MissingHeader_Rejected()
    {
        var csv = "Alice,alice@x.com,STUDENT\n";
        var result = CsvRosterSanitizer.Parse(Utf8Stream(csv), DefaultConfig);

        Assert.True(result.FileRejected);
        Assert.Equal(CsvRejectionKind.HeaderMismatch, result.FileRejectionKind);
    }

    // =========================================================================
    // Row-level rejections
    // =========================================================================

    [Fact]
    public void Parse_WrongColumnCount_RejectsRow()
    {
        var csv = "name,email,role\nAlice,alice@x.com\nBob,bob@x.com,STUDENT\n";
        var result = CsvRosterSanitizer.Parse(Utf8Stream(csv), DefaultConfig);

        Assert.False(result.FileRejected);
        Assert.Single(result.Rows);
        Assert.Equal("Bob", result.Rows[0].Name);
        Assert.Equal(1, result.RejectionsByKind[CsvRejectionKind.WrongColumnCount]);
    }

    [Fact]
    public void Parse_EmptyCell_RejectsRow()
    {
        var csv = "name,email,role\n,alice@x.com,STUDENT\n";
        var result = CsvRosterSanitizer.Parse(Utf8Stream(csv), DefaultConfig);

        Assert.Empty(result.Rows);
        Assert.Equal(1, result.RejectionsByKind[CsvRejectionKind.EmptyRow]);
    }

    [Fact]
    public void Parse_OverlongCell_RejectsRow()
    {
        var cfg = DefaultConfig with { MaxCellLength = 10 };
        var csv = $"name,email,role\n{new string('x', 50)},alice@x.com,STUDENT\n";
        var result = CsvRosterSanitizer.Parse(Utf8Stream(csv), cfg);

        Assert.Empty(result.Rows);
        Assert.Equal(1, result.RejectionsByKind[CsvRejectionKind.CellTooLong]);
    }

    // =========================================================================
    // Control character stripping (null bytes, etc)
    // =========================================================================

    [Fact]
    public void Parse_NullByteInName_Stripped()
    {
        var csv = "name,email,role\nAl\u0000ice,alice@x.com,STUDENT\n";
        var result = CsvRosterSanitizer.Parse(Utf8Stream(csv), DefaultConfig);

        Assert.False(result.FileRejected);
        Assert.Single(result.Rows);
        Assert.Equal("Alice", result.Rows[0].Name);
    }

    [Fact]
    public void Parse_C1Control_Stripped()
    {
        // U+0085 (NEL) is a C1 control we drop.
        var csv = "name,email,role\nAl\u0085ice,alice@x.com,STUDENT\n";
        var result = CsvRosterSanitizer.Parse(Utf8Stream(csv), DefaultConfig);

        Assert.Equal("Alice", result.Rows[0].Name);
    }

    // =========================================================================
    // Audit summary shape
    // =========================================================================

    [Fact]
    public void Parse_MixedRejections_BytesAndKindsReported()
    {
        var cfg = DefaultConfig with { MaxRows = 10, MaxCellLength = 1024 };
        var sb = new StringBuilder("name,email,role\n");
        sb.AppendLine("Alice,alice@x.com,STUDENT");          // OK
        sb.AppendLine("=cmd,bob@x.com,TEACHER");              // OK (stripped)
        sb.AppendLine(",empty@x.com,STUDENT");                // EmptyRow
        sb.AppendLine("Charlie,charlie@x.com");               // WrongColumnCount
        sb.AppendLine("\uFF21dmin,ad@x.com,ADMIN");           // HomoglyphSuspect

        var result = CsvRosterSanitizer.Parse(Utf8Stream(sb.ToString()), cfg);

        Assert.False(result.FileRejected);
        Assert.Equal(2, result.RowCount);
        Assert.True(result.BytesRead > 0);
        Assert.Contains(CsvRejectionKind.EmptyRow, result.RejectionsByKind.Keys);
        Assert.Contains(CsvRejectionKind.WrongColumnCount, result.RejectionsByKind.Keys);
        Assert.Contains(CsvRejectionKind.HomoglyphSuspect, result.RejectionsByKind.Keys);
    }
}
