// =============================================================================
// Cena Platform — CAS Conformance Baseline Parser (CAS-CONFORMANCE-RUNNER)
//
// Parses ops/reports/cas-conformance-baseline.md and produces a strongly-
// typed list of BaselineCase records. The baseline doc is the written
// contract for the CAS router — any regression against these 27+ cases
// fails the nightly CI gate (target: ≥ 99% pass rate).
//
// Row format in the markdown (dashed-list items under "## Algebra — …"):
//   - alg-eq-001 | Equivalence | `2*x + 3*x` | `5*x` | `x` | Ok
//
// Cells are pipe-separated. Backticks around expressions are stripped.
// The expected_status column maps to BaselineExpected:
//   Ok          → case must verify  (Verified == true,  Status == Ok)
//   Failed      → case must NOT verify (Verified == false, Status == Ok)
//   Unverifiable → engine returns Unverifiable; Failure is also acceptable
//                  for malformed inputs the parser can't handle.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Cas;

public enum BaselineExpected
{
    Ok,
    Failed,
    Unverifiable,
}

public sealed record BaselineCase(
    string Id,
    CasOperation Operation,
    string ExpressionA,
    string? ExpressionB,
    string? Variable,
    BaselineExpected Expected,
    string Category);

public static class CasConformanceBaselineParser
{
    // Matches a row:  - id | Operation | `A` | `B` | var | status
    // Also tolerates an `#` comment tail after the status cell.
    private static readonly Regex RowRx = new(
        @"^\s*[-*]\s*" +
        @"(?<id>[A-Za-z0-9\-_]+)\s*\|\s*" +
        @"(?<op>[A-Za-z]+)\s*\|\s*" +
        @"(?<a>.+?)\s*\|\s*" +
        @"(?<b>.+?)\s*\|\s*" +
        @"(?<var>.+?)\s*\|\s*" +
        @"(?<status>Ok|Failed|Unverifiable)\b" +
        @"(?:\s*\#.*)?\s*$",
        RegexOptions.Compiled);

    private static readonly Regex HeadingRx = new(
        @"^\s*##\s+(?<category>.+?)\s*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses the markdown at the given path and returns the structured
    /// case list. Throws <see cref="FileNotFoundException"/> if the file
    /// is missing. Rows that don't match the expected format are skipped
    /// (they're section prose, not cases).
    /// </summary>
    public static IReadOnlyList<BaselineCase> ParseFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"CAS conformance baseline not found at {path}", path);

        return Parse(File.ReadAllLines(path));
    }

    public static IReadOnlyList<BaselineCase> Parse(IEnumerable<string> lines)
    {
        var cases = new List<BaselineCase>();
        var category = "uncategorised";

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();

            var heading = HeadingRx.Match(line);
            if (heading.Success)
            {
                category = heading.Groups["category"].Value;
                continue;
            }

            var row = RowRx.Match(line);
            if (!row.Success) continue;

            var id = row.Groups["id"].Value;
            if (!TryParseOperation(row.Groups["op"].Value, out var op))
                continue; // unknown op — skip, the runner stays strict with its built-in corpus

            var a = UnquoteExpression(row.Groups["a"].Value);
            var bCell = UnquoteExpression(row.Groups["b"].Value);
            var b = string.IsNullOrWhiteSpace(bCell) || bCell == "—" ? null : bCell;

            var varCell = UnquoteExpression(row.Groups["var"].Value);
            var variable = string.IsNullOrWhiteSpace(varCell) || varCell == "—" ? null : varCell;

            if (!Enum.TryParse<BaselineExpected>(row.Groups["status"].Value, ignoreCase: false, out var expected))
                continue;

            cases.Add(new BaselineCase(
                Id:          id,
                Operation:   op,
                ExpressionA: a,
                ExpressionB: b,
                Variable:    variable,
                Expected:    expected,
                Category:    category));
        }

        return cases;
    }

    private static bool TryParseOperation(string token, out CasOperation op)
    {
        switch (token.Trim())
        {
            case "Equivalence":         op = CasOperation.Equivalence;        return true;
            case "StepValidity":        op = CasOperation.StepValidity;       return true;
            case "NumericalTolerance":  op = CasOperation.NumericalTolerance; return true;
            case "NormalForm":          op = CasOperation.NormalForm;         return true;
            case "Solve":               op = CasOperation.Solve;              return true;
            case "SolveLinear":         op = CasOperation.Solve;              return true;   // baseline uses shorthand
            case "Derivative":          op = CasOperation.Equivalence;        return true;   // baseline encodes derivative as "check A' == B"
            case "Integral":            op = CasOperation.Equivalence;        return true;
            default:                    op = default;                          return false;
        }
    }

    private static string UnquoteExpression(string cell)
    {
        var s = cell.Trim();
        if (s.StartsWith('`') && s.EndsWith('`') && s.Length >= 2) s = s[1..^1];
        return s;
    }
}
