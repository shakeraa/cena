// =============================================================================
// Cena Platform — "No unstripped image bytes" architecture ratchet (prr-001)
//
// Guards the EXIF-strip seam introduced by prr-001. The rule:
//
//   The ExifStripped flag on PhotoUploadResponse MUST come from the
//   StripResult produced by ExifStripper, never from file-type, never
//   from a hard-coded `true`, never from any other boolean.
//
// If someone later tries to bolt back on a "stripped = !isPdf" shortcut
// (the exact regression prr-001 was filed against), this scanner will
// catch it on CI long before the lying label reaches a student photo.
//
// Enforcement strategy
// --------------------
// Textual scan of PhotoUploadEndpoints.cs (and any future endpoint that
// constructs a PhotoUploadResponse). The right-hand side of
// `ExifStripped:` must be one of:
//
//   - stripResult.Success
//   - stripResult?.Success ?? false
//   - false                                (PDFs or equivalent — no image)
//
// Any other value fails the build with a pointer to prr-001.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class NoUnstrippedImageBytesTest
{
    // Matches the named-parameter form used by records / object initializers:
    //   ExifStripped: <expr>
    // <expr> continues until the next ',' or ')' at the same paren-depth
    // (the regex is conservative — if the expression spans multiple lines
    // we fall back to a simple to-end-of-line capture and scrutinize what
    // we got).
    private static readonly Regex ExifStrippedAssignment = new(
        @"ExifStripped\s*:\s*(?<rhs>[^,\)\n]+)",
        RegexOptions.Compiled);

    // The approved RHS shapes. Anything else is a regression.
    private static readonly string[] ApprovedRhsPatterns =
    {
        @"^\s*stripResult\.Success\s*$",
        @"^\s*stripResult\?\.Success\s*\?\?\s*false\s*$",
        @"^\s*false\s*$",
        // Defensive: allow references to a local bool that was itself
        // assigned from stripResult.Success — verified manually in the
        // test above via a secondary file scan. For now, keep the list
        // strict; if a future PR needs a new shape, the PR must add it
        // here so the review surfaces the intent.
    };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) return dir.FullName;
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "actors", "Cena.Actors")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root not found.");
    }

    private static IEnumerable<string> EndpointFiles(string repoRoot)
    {
        var apiRoot = Path.Combine(repoRoot, "src", "api");
        if (!Directory.Exists(apiRoot)) yield break;

        foreach (var file in Directory.EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(repoRoot, file);
            var sep = Path.DirectorySeparatorChar;
            if (rel.Contains($"{sep}bin{sep}")) continue;
            if (rel.Contains($"{sep}obj{sep}")) continue;
            if (rel.Contains($"{sep}Tests{sep}")) continue;
            if (rel.Contains($".Tests{sep}")) continue;
            yield return file;
        }
    }

    [Fact]
    public void AllPhotoUploadResponseConstructions_TieExifStripped_To_StripResult()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();
        var assignmentsSeen = 0;

        foreach (var file in EndpointFiles(repoRoot))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in ExifStrippedAssignment.Matches(text))
            {
                assignmentsSeen++;
                var rhs = m.Groups["rhs"].Value.Trim().TrimEnd(',').TrimEnd(')').Trim();

                var approved = false;
                foreach (var pattern in ApprovedRhsPatterns)
                {
                    if (Regex.IsMatch(rhs, pattern))
                    {
                        approved = true;
                        break;
                    }
                }

                if (!approved)
                {
                    var rel = Path.GetRelativePath(repoRoot, file).Replace(Path.DirectorySeparatorChar, '/');
                    violations.Add(
                        $"{rel} — ExifStripped assigned from `{rhs}`. " +
                        "Approved shapes: stripResult.Success | " +
                        "stripResult?.Success ?? false | false. See prr-001: " +
                        "the flag must reflect the ACTUAL strip outcome, never " +
                        "file-type or a hard-coded true.");
                }
            }
        }

        Assert.True(
            assignmentsSeen > 0,
            "NoUnstrippedImageBytesTest found zero `ExifStripped:` assignments under " +
            "src/api. If the PhotoUploadResponse shape moved, update this scanner " +
            "rather than silently disabling it.");

        if (violations.Count == 0) return;

        var msg = "prr-001 regression: PhotoUploadResponse.ExifStripped is being " +
                  "assigned from something other than the real strip outcome.\n" +
                  "The ORIGINAL bug was `ExifStripped = !isPdf` — a lie. This " +
                  "scanner exists to keep that lie from coming back.\n" +
                  "Violations:\n  - " + string.Join("\n  - ", violations);
        Assert.Fail(msg);
    }

    [Fact]
    public void ExifStripperService_Exists()
    {
        // Paranoia: if someone deletes ExifStripper.cs the whole upload
        // path silently falls back to "no strip" via a compilation error.
        // Catch the deletion here with a clearer message.
        var repoRoot = FindRepoRoot();
        var stripperPath = Path.Combine(
            repoRoot, "src", "shared", "Cena.Infrastructure",
            "Media", "ExifStripper.cs");
        Assert.True(
            File.Exists(stripperPath),
            $"prr-001 regression: ExifStripper.cs is missing at {stripperPath}. " +
            "The real EXIF-strip seam is gone; photo uploads will re-introduce " +
            "the lying-label bug.");
    }

    [Fact]
    public void ExifFixture_IsCommitted()
    {
        var repoRoot = FindRepoRoot();
        var fixturePath = Path.Combine(
            repoRoot, "tests", "fixtures", "exif", "exif-laden-sample.jpg");
        Assert.True(
            File.Exists(fixturePath),
            $"prr-001 regression: the EXIF-laden fixture is missing at {fixturePath}. " +
            "ExifStrippingIntegrationTests cannot prove the seam without it.");
    }
}
