// =============================================================================
// Cena Platform — No browse signal into BKT (PRR-261, ADR-0059 §15.6)
//
// ADR-0059 §15.6 invariant: BKT updates take only "active attempt" signals
// (i.e. an answer or skip from a learning session). They MUST NOT take
// "passive browse" signals — looking at a question, scrolling past it,
// hovering on it, etc. — because those signals are extremely noisy and
// do not represent skill engagement.
//
// PRR-261 lands the discount machinery for reference-anchored attempts;
// future code might be tempted to push reference-rendered events through
// the same pathway, conflating "saw a worked example" with "answered an
// item." This test catches that mistake at the source.
//
// What it asserts:
//
//   1. Every call to IBktStateTracker.UpdateAsync (the only BKT write
//      surface) lives in a file whose name does NOT match a "browse"
//      vocabulary (Browse, Render, View, Hover, Scroll, Reference).
//      Reference-anchored attempts SUPPLY the timing as a parameter to
//      a normal attempt update — that's allowed; the signal must arrive
//      via a real attempt event, not a render event.
//
//   2. There is no event type named *Browse*Attempted_V*, *Render*
//      Attempted_V*, or similar — i.e. no "browse-as-attempt" model
//      slipping in.
//
// Both checks are static-source — fast, deterministic, and exactly catch
// the failure mode that worries §15.6 (silent code-shape drift).
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class NoBrowseSignalInBktTest
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    private static string ActorsRoot() =>
        Path.Combine(FindRepoRoot(), "src", "actors", "Cena.Actors");

    /// <summary>
    /// Scope: any code path that calls into the BKT update surface. The
    /// caller's filename must not look like a passive-render surface.
    /// </summary>
    private static readonly Regex BktUpdateInvocation = new(
        @"\b_?bktTracker\?\.UpdateAsync\s*\(|\b_?bktStateTracker\?\.UpdateAsync\s*\(|" +
        @"IBktStateTracker[^.]*\.UpdateAsync\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// Filename tokens that signal "passive browse" surfaces. A file
    /// matching any of these AND calling BKT UpdateAsync is the failure
    /// mode §15.6 wants to prevent.
    /// </summary>
    private static readonly string[] BrowseTokens = new[]
    {
        "Browse", "Render", "View", "Hover", "Scroll",
        "Reference",   // a "ReferenceRenderHandler" calling UpdateAsync would be wrong
        "Preview",
    };

    [Fact]
    public void No_passive_browse_surface_calls_BktStateTracker_UpdateAsync()
    {
        var root = ActorsRoot();
        Assert.True(Directory.Exists(root), $"Actors root missing: {root}");

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal)) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal)) continue;

            var name = Path.GetFileNameWithoutExtension(file);

            // Only check files whose name carries a browse-like token.
            // (Random files shouldn't be matched on substrings like "View"
            // — gate on word-boundary AND on a BKT call inside.)
            var nameMatchesBrowse = BrowseTokens.Any(tok =>
                Regex.IsMatch(name, $@"\b{tok}\b", RegexOptions.IgnoreCase));
            if (!nameMatchesBrowse) continue;

            string content;
            try { content = File.ReadAllText(file); }
            catch { continue; }

            if (BktUpdateInvocation.IsMatch(content))
            {
                offenders.Add(Path.GetRelativePath(root, file));
            }
        }

        Assert.True(offenders.Count == 0,
            "ADR-0059 §15.6 invariant violated: passive-browse-named file(s) "
            + "call IBktStateTracker.UpdateAsync. BKT updates MUST come from "
            + "active-attempt code paths only. If a file legitimately needs "
            + "to update mastery, rename it to reflect the attempt-shape "
            + "(e.g. *AttemptHandler, not *RenderHandler):\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void No_event_type_models_browse_or_render_as_an_attempt()
    {
        // A class/record named *RenderAttempted_V* / *BrowseAttempted_V* /
        // *ViewAttempted_V* would conflate the two — exactly the §15.6
        // failure mode. Fail loud if anyone introduces one.
        var root = ActorsRoot();

        var offenders = new List<string>();
        var pattern = new Regex(
            @"(?:class|record)\s+(\w*(?:Browse|Render|View|Hover|Scroll|Preview)\w*Attempted(?:_V\d+)?)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal)) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal)) continue;

            string content;
            try { content = File.ReadAllText(file); }
            catch { continue; }

            foreach (Match m in pattern.Matches(content))
            {
                offenders.Add($"{Path.GetRelativePath(root, file)}: {m.Groups[1].Value}");
            }
        }

        Assert.True(offenders.Count == 0,
            "ADR-0059 §15.6 invariant violated: passive-browse signals modelled "
            + "as *Attempted events. Use a separate render/view event type and "
            + "DO NOT feed it into BKT:\n  "
            + string.Join("\n  ", offenders));
    }
}
