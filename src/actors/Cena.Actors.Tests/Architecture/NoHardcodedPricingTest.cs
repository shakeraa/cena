// =============================================================================
// Cena Platform — NoHardcodedPricingTest (prr-244)
//
// Architectural ratchet: no dollar literals ($19, $14.00, 19.0m, etc.) may
// appear in pricing-adjacent namespaces outside the sanctioned surface
// (contracts/pricing/default-pricing.yml + DefaultPricingYaml.cs + the
// resolver itself). Every pricing-bearing code path MUST route through
// IInstitutePricingResolver.
//
// Enforcement is textual. The scanner walks every *.cs under src/ and
// flags lines that look like a dollar amount ($NN.NN, NNm, NN.NNm, etc.)
// if the containing file lives in a pricing-adjacent namespace.
//
// Initial allowlist is empty. Adding an exemption requires a written
// rationale in the file's CLAUDE.md-friendly comment + a code owner sign-
// off on the test itself.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class NoHardcodedPricingTest
{
    // Namespace roots scanned. A file is considered pricing-adjacent if
    // its path contains any of these fragments (case-insensitive).
    private static readonly string[] PricingAdjacentPathFragments =
    {
        "Pricing",
        "Billing",
        "Stripe",
        "Subscription",
    };

    // Files explicitly allowed to carry dollar literals. The YAML loader
    // and the resolver must reference defaults somehow, and the arch test
    // itself obviously contains the string "pricing". Keep this list short
    // and WHY-commented — every exemption is a potential audit gap.
    private static readonly HashSet<string> ExemptFileNames = new(StringComparer.Ordinal)
    {
        // Loader for the YAML-sourced defaults. The bounds.PricingBounds
        // defaults live here as C# literals so a corrupt YAML file still
        // yields a safe lower bound, per PRR-244 §Tests.
        "DefaultPricingYaml.cs",
        // The arch test itself.
        "NoHardcodedPricingTest.cs",
    };

    // Pattern: a literal that looks like a dollar amount next to a
    // pricing-shaped identifier. We match `\$\d+(\.\d{1,4})?m?` anywhere
    // on the line, but only count it if the same line has a pricing-
    // sensitive token nearby (price / seat / tier / cost / usd).
    private static readonly Regex DollarLike = new(
        @"(?<![A-Za-z0-9_])(\$\d+(\.\d{1,4})?|\d+(\.\d{1,4})?m)(?![A-Za-z0-9_])",
        RegexOptions.Compiled);

    private static readonly Regex PricingContext = new(
        @"(?i)price|seat|tier|cost|usd|dollar|subscription",
        RegexOptions.Compiled);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Repo root (CLAUDE.md) not found from test base directory.");
    }

    [Fact]
    public void NoDollarLiterals_InPricingAdjacentNamespaces()
    {
        var repoRoot = FindRepoRoot();
        var srcDir = Path.Combine(repoRoot, "src");
        if (!Directory.Exists(srcDir)) return;

        var violations = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

            // Only scan pricing-adjacent paths.
            if (!PricingAdjacentPathFragments.Any(p =>
                    file.Contains(Path.DirectorySeparatorChar + p + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)
                    || file.Contains(Path.DirectorySeparatorChar + p,
                        StringComparison.OrdinalIgnoreCase)))
                continue;

            var fileName = Path.GetFileName(file);
            if (ExemptFileNames.Contains(fileName)) continue;

            // Skip test files — they often use $19 in assertions deliberately.
            if (file.Contains(".Tests", StringComparison.Ordinal)) continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
                if (trimmed.StartsWith("*", StringComparison.Ordinal)) continue;
                if (trimmed.StartsWith("///", StringComparison.Ordinal)) continue;

                if (!DollarLike.IsMatch(line)) continue;
                if (!PricingContext.IsMatch(line)) continue;

                violations.AppendLine(
                    $"  {Path.GetRelativePath(repoRoot, file)}:{i + 1}: `{line.Trim()}`");
            }
        }

        Assert.True(violations.Length == 0,
            "prr-244 architectural violation: hard-coded dollar literal in a pricing-adjacent "
            + "namespace. Route every pricing-bearing code path through "
            + "IInstitutePricingResolver.ResolveAsync — never a literal.\n"
            + violations);
    }

    [Fact]
    public void FixtureDetection_CraftedLiteral_IsCaught()
    {
        // Belt-and-braces: this test doubles as a live regex-check by
        // proving the pattern would flag a crafted offender. We do NOT
        // write a file to disk — we just run the regex over a string so
        // a broken regex is detected even if no real violation exists.
        var offender = "        public decimal StudentMonthlyPriceUsd = 19.00m;";
        Assert.True(DollarLike.IsMatch(offender),
            "DollarLike regex failed to catch a crafted offender line.");
        Assert.True(PricingContext.IsMatch(offender),
            "PricingContext regex failed to catch a crafted offender line.");
    }
}
