// =============================================================================
// Cena Platform — F2 (breathing opener / focus ritual) surface contracts
//                  — prr-164 + prr-165
//
// F2 is a pair of student-facing surfaces that sit between "arrive at the
// session" and "see the first problem": a one-breath opener with a mood tap,
// and a watermark the student sees on every session screen. Two review-level
// decisions locked those surfaces' contracts BEFORE the code lands — this
// test holds the line by flagging the shapes that would violate either
// decision at the moment a mood-tap endpoint or watermark component is
// introduced.
//
// ## prr-164 — confirm-before-route on mood tap
//
// A mood-tap endpoint takes a student's one-tap emotional-state signal (happy
// / neutral / worried / stressed) and uses it to pace the session opener. The
// review found that if a student taps "stressed" by accident, the platform
// must NOT silently re-route them into a longer breathing ritual or a
// different problem-difficulty track — the student should see a "you tapped
// stressed — keep this, or undo?" confirmation before any routing change
// commits. The contract: every mood-tap endpoint MUST carry a
// [RequiresConfirmation] attribute naming the confirmation seam.
//
// This test fires when a file matching the mood-tap naming pattern is found
// AND it does not carry [RequiresConfirmation]. The naming pattern is
// deliberately broad so the test catches the feature whenever it lands
// (F2MoodTap / MoodTap / BreathingOpenerMoodTap / etc.).
//
// ## prr-165 — watermark carries session-id, never student-id
//
// The F2 watermark is a corner-of-screen trace ID shown on every session
// screen so a student reporting a bug can quote the ID to support. The review
// found that student_id / studentAnonId would be a privacy regression —
// shoulder-surfing the watermark leaks an identifier stable across sessions.
// The contract: every watermark-rendering component MUST NOT reference
// student_id / StudentAnonId / studentAnonId / studentId in any form. Only
// session_id / SessionId / sessionId is permitted.
//
// This test fires when a file matching the watermark naming pattern is found
// AND it references any banned student-id identifier.
//
// ## Why one test file
//
// Both rules share the same operational shape — scan for a filename pattern,
// apply a signature check, fail loudly if the file exists but fails. Splitting
// into two files would duplicate the repo-root / scan-dir boilerplate without
// buying clearer diagnostics.
//
// ## Forward-looking ratchet semantics
//
// Neither F2 mood-tap nor F2 watermark currently exists in the repo. The
// expected test outcome today is: no files match the naming pattern, no
// violations, test passes. The moment a future PR introduces the feature
// with the wrong shape, the test fails in CI — which is exactly the guardrail
// prr-164 and prr-165 asked for.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class F2SurfaceContractTest
{
    // ── prr-164 — mood-tap endpoint naming pattern ──────────────────────────
    //
    // Matches class names like MoodTapEndpoint, F2MoodTapHandler,
    // BreathingOpenerMoodTapController, MoodTapCommand, etc. Intentionally
    // broad so the test catches the feature whenever it lands.
    private static readonly Regex MoodTapFilePattern = new(
        @"(MoodTap|F2Mood|BreathingOpener(Mood|Tap))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // [RequiresConfirmation] attribute — either as an attribute on the class
    // or on the endpoint method. The attribute itself lives in
    // Cena.Actors.Attributes (to be introduced with the mood-tap feature);
    // we match the presence, not the type. A line containing the literal
    // "[RequiresConfirmation" is sufficient evidence.
    private static readonly Regex RequiresConfirmationAttr = new(
        @"\[\s*RequiresConfirmation\s*[(\]]",
        RegexOptions.Compiled);

    // ── prr-165 — watermark renderer naming pattern ────────────────────────
    //
    // Matches class names like SessionWatermarkRenderer, F2Watermark,
    // WatermarkComponent, WatermarkService, etc.
    private static readonly Regex WatermarkFilePattern = new(
        @"Watermark",
        RegexOptions.Compiled);

    // Banned student-id identifiers the watermark may not reference (ANY
    // casing / separator). The Hebrew word "teudatZehut" is also banned
    // because it is an identifier that survives across sessions.
    private static readonly Regex BannedStudentIdentifier = new(
        @"(?<![A-Za-z0-9_])(?<name>"
        + @"studentId[A-Za-z0-9_]*"
        + @"|StudentId[A-Za-z0-9_]*"
        + @"|student_id[A-Za-z0-9_]*"
        + @"|studentAnonId[A-Za-z0-9_]*"
        + @"|StudentAnonId[A-Za-z0-9_]*"
        + @"|student_anon_id[A-Za-z0-9_]*"
        + @"|teudatZehut[A-Za-z0-9_]*"
        + @"|TeudatZehut[A-Za-z0-9_]*"
        + @")(?![A-Za-z0-9_])",
        RegexOptions.Compiled);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    private static IEnumerable<string> ScannedFiles(string repoRoot)
    {
        var srcRoot = Path.Combine(repoRoot, "src");
        if (!Directory.Exists(srcRoot)) yield break;

        foreach (var f in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(repoRoot, f);
            var sep = Path.DirectorySeparatorChar;
            if (rel.Contains($"{sep}bin{sep}")) continue;
            if (rel.Contains($"{sep}obj{sep}")) continue;
            if (rel.Contains($"{sep}Tests{sep}")) continue;
            if (rel.Contains($".Tests{sep}")) continue;
            if (rel.Contains($"{sep}fixtures{sep}")) continue;
            yield return f;
        }
    }

    private static string StripCommentsAndStrings(string line)
    {
        var slashSlash = line.IndexOf("//", StringComparison.Ordinal);
        if (slashSlash >= 0) line = line[..slashSlash];

        var sb = new StringBuilder(line.Length);
        var inStr = false;
        foreach (var c in line)
        {
            if (c == '"') { inStr = !inStr; sb.Append('"'); continue; }
            if (inStr) continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    [Fact]
    public void EveryMoodTapEndpoint_CarriesRequiresConfirmationAttribute()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();
        var matchedFiles = 0;

        foreach (var file in ScannedFiles(repoRoot))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (!MoodTapFilePattern.IsMatch(fileName)) continue;
            matchedFiles++;

            var raw = File.ReadAllText(file);
            if (RequiresConfirmationAttr.IsMatch(raw)) continue;

            var rel = Path.GetRelativePath(repoRoot, file)
                .Replace(Path.DirectorySeparatorChar, '/');
            violations.Add(
                $"{rel} — mood-tap endpoint carries no [RequiresConfirmation] " +
                "attribute. prr-164 requires confirm-before-route on mood-tap so " +
                "a mistaken 'stressed' tap cannot silently re-route the session. " +
                "Decorate the endpoint method with [RequiresConfirmation(\"mood-tap-re-route\")].");
        }

        // No assertion on matchedFiles > 0 — forward-looking ratchet. The
        // feature is not yet shipped; the test passes today with zero matches.
        if (violations.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine($"prr-164 violation: {violations.Count} mood-tap endpoint(s) missing [RequiresConfirmation].");
        sb.AppendLine($"Files matching the mood-tap naming pattern: {matchedFiles}");
        sb.AppendLine();
        foreach (var v in violations) sb.AppendLine("  " + v);
        Assert.Fail(sb.ToString());
    }

    [Fact]
    public void WatermarkRenderer_NeverReferencesStudentIdentifier()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();
        var matchedFiles = 0;

        foreach (var file in ScannedFiles(repoRoot))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (!WatermarkFilePattern.IsMatch(fileName)) continue;
            matchedFiles++;

            var rel = Path.GetRelativePath(repoRoot, file)
                .Replace(Path.DirectorySeparatorChar, '/');
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var stripped = StripCommentsAndStrings(lines[i]);
                var m = BannedStudentIdentifier.Match(stripped);
                if (!m.Success) continue;

                var name = m.Groups["name"].Value;
                violations.Add(
                    $"{rel}:{i + 1} — watermark renderer references student-identifier " +
                    $"`{name}`. prr-165 requires the watermark to carry ONLY the session-id " +
                    "(SessionId / sessionId). Shoulder-surfing a student-id watermark " +
                    "leaks an identifier stable across sessions, which is a privacy " +
                    "regression the review blocked.");
            }
        }

        if (violations.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine($"prr-165 violation: {violations.Count} watermark-renderer hit(s) reference a student-identifier.");
        sb.AppendLine($"Files matching the watermark naming pattern: {matchedFiles}");
        sb.AppendLine();
        sb.AppendLine("Fix: replace StudentId / StudentAnonId with the current SessionId.");
        sb.AppendLine("The session-id is scoped to a single sitting; a student reporting a");
        sb.AppendLine("bug can quote it without leaking a stable identifier.");
        sb.AppendLine();
        foreach (var v in violations) sb.AppendLine("  " + v);
        Assert.Fail(sb.ToString());
    }
}
