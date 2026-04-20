// =============================================================================
// Cena Platform — "No unregistered misconception PII store" arch ratchet
// (prr-015, ADR-0003)
//
// Invariant: every .cs file under src/ that references `misconception` in
// any persistence-adjacent code path must be accountable to the central
// IMisconceptionPiiStoreRegistry. We enforce that accountability by demanding
// that the file either:
//
//   (a) Carry a class-level [RegisteredMisconceptionStore("<name>", "<reason>")]
//       attribute naming the store this file writes into, OR
//
//   (b) Appear in the in-source allowlist below, paired with an issue/ADR
//       reference explaining why the file touches misconception data without
//       being a persistence seam (e.g. pure detection logic, catalog entries,
//       or event definitions).
//
// Detection heuristic:
//
//   File is flagged iff — within a single non-comment line of non-comment
//   code — BOTH a `misconception` token AND a persistence verb appear, OR the
//   file contains both a `misconception` token AND a persistence verb
//   anywhere in non-comment code. Persistence verbs:
//
//       Insert | Upsert | Save | SaveChanges | Marten | Redis | Cache |
//       AddEventType | Store( | Append( | Persist | Document
//
// Why the heuristic is lenient (file-wide, not line-local)?
//
//   Marten registration sites spread the event-type `Misconception*` names
//   across one line and the `AddEventType` verb across another line in the
//   same method. A line-local match would miss those — exactly the seam we
//   want to catch. The file-wide rule overmatches, but the allowlist is
//   auditable and small, and the cost of a false-negative (a new
//   unregistered store leaks misconception PII) is much higher than the
//   cost of a false-positive (author adds an allowlist entry with a
//   one-line ADR pointer).
//
// Escape hatches (allowlist):
//
//   LegacyAllowlist is the single source of truth. Every entry is
//   (relative path, short reason). Do NOT grow this list for new code —
//   add [RegisteredMisconceptionStore] to the new file instead.
//
// This test is the dual of NoAtRiskPersistenceTest / MlExclusionEnforcementTests.
// Same text-scan convention (comment/string-stripped, repo-root anchored).
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class NoUnregisteredMisconceptionStoreTest
{
    private static readonly Regex MisconceptionToken = new(
        @"(?<![A-Za-z0-9_])[Mm]isconception[A-Za-z0-9_]*",
        RegexOptions.Compiled);

    // Persistence verbs. Keep anchored on word boundary / `(` so identifier
    // suffixes don't false-negative ("Storage" is NOT "Store(").
    private static readonly Regex PersistenceVerb = new(
        @"\b(Insert|Upsert|SaveChangesAsync|SaveChanges|AddEventType|Marten|Redis|Append|Persist)\b" +
        @"|\bStore\s*\(" +                  // Session.Store(...)
        @"|\bCache\s*\(" +                  // Cache(...)
        @"|\bUseDocumentSession\b",
        RegexOptions.Compiled);

    private static readonly Regex RegisteredAttribute = new(
        @"\[RegisteredMisconceptionStore\s*\(",
        RegexOptions.Compiled);

    // Lines we consider prose rather than code.
    private static readonly Regex CommentLine = new(
        @"^\s*(//|\*|/\*|\*/|///)",
        RegexOptions.Compiled);

    private static string StripCommentsAndStrings(string line)
    {
        // Strip `//` trailing comment.
        var slashSlash = line.IndexOf("//", StringComparison.Ordinal);
        if (slashSlash >= 0) line = line[..slashSlash];

        // Collapse double-quoted literals so tokens inside strings don't count.
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
        throw new InvalidOperationException(
            "Repo root not found — looked for CLAUDE.md or src/actors/Cena.Actors/.");
    }

    // ── In-source allowlist ────────────────────────────────────────────────
    //
    // Entries are (relative POSIX path, short reason). Adding to this list
    // is a code-level review event — it means "this file mentions
    // misconception + a persistence verb but does NOT own a persistence
    // seam of its own". Every entry must carry a pointer to the
    // clarifying rationale.
    //
    // Default posture: add [RegisteredMisconceptionStore] to your file.
    // Only fall back to the allowlist when the file genuinely does not
    // own a persistence seam.
    private static readonly (string Path, string Reason)[] LegacyAllowlist =
    {
        // MartenConfiguration.cs registers the Marten event types but the
        // IMisconceptionPiiStoreRegistry row is declared via
        // MisconceptionStoreRegistrations.cs (which carries
        // [RegisteredMisconceptionStore]). The Marten file is the schema
        // wiring; the registry wiring is next door.
        ("src/actors/Cena.Actors/Configuration/MartenConfiguration.cs",
         "prr-015: registers event types with Marten; runtime registry entry " +
         "is declared in Configuration/MisconceptionStoreRegistrations.cs."),

        // Host composition root — registers IMisconceptionDetectionService
        // and wires AddCanonicalMartenMisconceptionStore(). Not a
        // persistence seam itself; it delegates to the registered store.
        ("src/actors/Cena.Actors.Host/Program.cs",
         "prr-015: composition root; wires AddCanonicalMartenMisconceptionStore() " +
         "which registers the canonical Marten store with the PII registry."),

        // Student API composition root — likewise delegates to the
        // canonical Marten store registered via AddCanonicalMartenMisconceptionStore.
        ("src/api/Cena.Student.Api.Host/Program.cs",
         "prr-015: composition root; delegates misconception persistence " +
         "to the canonical Marten store registered by Actors.Host."),

        // Student API answer endpoint — appends MisconceptionDetected_V1
        // events to the session stream. The canonical Marten store
        // (registered via MisconceptionStoreRegistrations.cs) owns this
        // stream; this file is a write call-site, not an independent seam.
        ("src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs",
         "prr-015: write call-site for MisconceptionDetected_V1; writes land " +
         "in the canonical Marten stream store registered by " +
         "Configuration/MisconceptionStoreRegistrations.cs."),

    };

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
            if (rel.Contains($"{sep}worktrees{sep}")) continue;
            yield return f;
        }
    }

    [Fact]
    public void EveryMisconceptionPersistenceFile_IsRegistered_OrAllowlisted()
    {
        var repoRoot = FindRepoRoot();
        var allowlist = new HashSet<string>(
            LegacyAllowlist.Select(e => e.Path.Replace('/', Path.DirectorySeparatorChar)),
            StringComparer.OrdinalIgnoreCase);

        var violations = new List<string>();
        var scanned = 0;

        foreach (var file in ScannedFiles(repoRoot))
        {
            scanned++;
            var rel = Path.GetRelativePath(repoRoot, file);

            // Stream lines, strip comments, check for the twin tokens.
            var hasMisconception = false;
            var hasPersistence = false;

            foreach (var rawLine in File.ReadLines(file))
            {
                if (CommentLine.IsMatch(rawLine)) continue;
                var line = StripCommentsAndStrings(rawLine);
                if (line.Length == 0) continue;

                if (!hasMisconception && MisconceptionToken.IsMatch(line))
                    hasMisconception = true;
                if (!hasPersistence && PersistenceVerb.IsMatch(line))
                    hasPersistence = true;
                if (hasMisconception && hasPersistence) break;
            }

            if (!(hasMisconception && hasPersistence)) continue;

            // Allowlisted?
            if (allowlist.Contains(rel)) continue;

            // Attribute present?
            var text = File.ReadAllText(file);
            if (RegisteredAttribute.IsMatch(text)) continue;

            violations.Add(
                $"{rel} — file references misconception AND a persistence verb but is " +
                "neither (a) annotated [RegisteredMisconceptionStore(\"<name>\", \"<reason>\")] " +
                "at the class level, nor (b) on the allowlist in " +
                "NoUnregisteredMisconceptionStoreTest.LegacyAllowlist. Register the store " +
                "with IMisconceptionPiiStoreRegistry and annotate this class, OR — if the " +
                "file truly does not own a persistence seam — add an allowlist entry " +
                "paired with the ADR/issue that explains why.");
        }

        Assert.True(scanned > 0,
            "NoUnregisteredMisconceptionStoreTest scanned zero files — scanner broken.");

        if (violations.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine($"prr-015 / ADR-0003: {violations.Count} unregistered misconception " +
                      "persistence seam(s) detected.");
        sb.AppendLine();
        sb.AppendLine("Every component that holds any misconception PII must register itself " +
                      "with IMisconceptionPiiStoreRegistry so the retention worker can enforce");
        sb.AppendLine("the 30-day / 90-day cap. See docs/adr/0003-misconception-session-scope.md.");
        sb.AppendLine();
        foreach (var v in violations) sb.AppendLine("  " + v);
        Assert.Fail(sb.ToString());
    }

    [Fact]
    public void Allowlist_EveryEntry_StillExistsAndStillNeedsAllowlisting()
    {
        // If an allowlisted file has been deleted or no longer matches the
        // detection heuristic, the entry is stale. Keep the list honest.
        var repoRoot = FindRepoRoot();
        var failures = new List<string>();

        foreach (var (posixPath, reason) in LegacyAllowlist)
        {
            var rel = posixPath.Replace('/', Path.DirectorySeparatorChar);
            var abs = Path.Combine(repoRoot, rel);
            if (!File.Exists(abs))
            {
                failures.Add($"  {rel}: listed in allowlist but the file no longer exists. " +
                             "Remove the entry.");
                continue;
            }

            var hasMisconception = false;
            var hasPersistence = false;
            foreach (var rawLine in File.ReadLines(abs))
            {
                if (CommentLine.IsMatch(rawLine)) continue;
                var line = StripCommentsAndStrings(rawLine);
                if (line.Length == 0) continue;
                if (MisconceptionToken.IsMatch(line)) hasMisconception = true;
                if (PersistenceVerb.IsMatch(line)) hasPersistence = true;
                if (hasMisconception && hasPersistence) break;
            }

            if (!(hasMisconception && hasPersistence))
            {
                failures.Add(
                    $"  {rel}: listed in allowlist (reason: \"{reason}\") but the file no " +
                    "longer contains both a misconception token AND a persistence verb. " +
                    "Remove the stale allowlist entry.");
            }
        }

        Assert.True(failures.Count == 0,
            "NoUnregisteredMisconceptionStoreTest.LegacyAllowlist has stale entries:\n" +
            string.Join('\n', failures));
    }
}
