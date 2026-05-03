// =============================================================================
// Cena Platform — Academic Integrity Routing architecture ratchet (prr-144).
//
// Asserts that:
//   1. The seam interface IAcademicIntegrityReviewService exists at its
//      canonical path. If a future PR moves it, the architecture test must
//      move with it in the same PR.
//   2. No production code path (outside a short allowlist) names the banned
//      cheating-alert vocabulary. Allowlist covers the interface file
//      itself (where the terms appear in doc comments describing what the
//      seam replaces), demo-user seed data in UserSeedData.cs (admin demo
//      data, not student-facing), content-moderation DTOs (admin-side
//      content ingestion), and the admin ingestion detail panel
//      (authored-content plagiarism score, admin-facing).
//
// Architectural rationale (senior-architect protocol):
//
//   WHY is this a separate architecture test, not an addition to an
//   existing ratchet? Because it answers a distinct question: "is every
//   integrity signal in the codebase routed through the canonical seam?"
//   No other architecture test owns that question; bundling it with
//   e.g. the shipgate-scanner-v2 ratchet would couple two independent
//   concerns. A focused failing test tells the reviewer exactly which
//   drift they are looking at.
//
//   WHY a list-based allowlist rather than an attribute or a naming
//   convention? Because the small finite set of non-student-facing
//   surfaces that legitimately name the banned terms is stable (the four
//   paths listed below) and maintaining a hand-curated list is cheaper
//   than teaching the test about attributes that don't yet exist.
//   Attributes can be added later if the list grows.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class AcademicIntegrityRoutingTest
{
    /// <summary>
    /// Canonical path of the seam interface. If this file moves, update
    /// both this constant and the scanner whitelist
    /// (scripts/shipgate/cheating-alert-framing-whitelist.yml) in the same
    /// PR.
    /// </summary>
    private const string SeamRelativePath =
        "src/actors/Cena.Actors/AcademicIntegrity/IAcademicIntegrityReviewService.cs";

    /// <summary>
    /// Allowlist of production files that legitimately reference the
    /// banned cheating-alert vocabulary. Keeping this list small and
    /// hand-curated is the point — a PR that adds a new integrity-signal
    /// call site must either route through
    /// <c>IAcademicIntegrityReviewService</c> or add its path here with a
    /// one-line reason in the PR description. Test folders are excluded
    /// implicitly (they can assert banned copy does NOT appear).
    /// </summary>
    private static readonly string[] AllowedVocabularyCallSites =
    {
        // The seam interface itself references the terms in doc comments
        // describing what it replaces. This is the allowlist's main entry.
        SeamRelativePath,

        // Demo-user seed data: 'Academic integrity violation' in a
        // SuspensionReason field on a DEMO user (admin-facing sample data
        // for the admin-console UI). Not a student-facing alert.
        "src/shared/Cena.Infrastructure/Seed/UserSeedData.cs",

        // Content-moderation DTOs: admin-side ingested content is scored
        // for plagiarism against authored material (not student work).
        // Admin surface, not student-facing.
        "src/api/Cena.Api.Contracts/Admin/Moderation/ModerationDtos.cs",
        "src/api/Cena.Api.Contracts/Admin/Ingestion/IngestionDtos.cs",
        "src/api/Cena.Admin.Api/IngestionPipelineService.cs",
        "src/api/Cena.Admin.Api/ContentModerationService.cs",

        // Admin ingestion detail panel: renders the content-moderation
        // plagiarism score on an admin-facing surface. Admin reviewer
        // assesses AUTHORED content originality; this is not a student
        // alert.
        "src/admin/full-version/src/views/apps/ingestion/ItemDetailPanel.vue",
    };

    /// <summary>
    /// Regex that matches the banned cheating-alert vocabulary in source
    /// text. Kept tight: we only match the student-facing framings the
    /// scanner rule pack catches (bare 'cheating', 'plagiarism alert',
    /// 'honor code violation', 'academic dishonesty'). Internal-only
    /// words like 'integrity' are NOT matched because they appear
    /// innocuously in many contexts ('data integrity', 'referential
    /// integrity').
    /// </summary>
    private static readonly Regex BannedVocabularyPattern = new(
        @"\b(cheating|plagiari[sz]ed?|honou?r\s+code\s+violation|academic\s+dishonest)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    [Fact]
    public void SeamInterfaceExistsAtCanonicalPath()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, SeamRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(
            File.Exists(path),
            $"Expected IAcademicIntegrityReviewService seam interface at {path}. " +
            $"This file is the architectural seam all integrity signals route through (prr-144).");
    }

    [Fact]
    public void SeamInterfaceDeclaresCanonicalName()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, SeamRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var src = File.ReadAllText(path);
        Assert.Matches(
            new Regex(@"interface\s+IAcademicIntegrityReviewService"),
            src);
    }

    [Fact]
    public void NoProductionCodeOutsideAllowlistReferencesBannedVocabulary()
    {
        var root = FindRepoRoot();
        var offenders = new List<string>();

        // Scan the main source trees.
        foreach (var scanRoot in new[]
        {
            Path.Combine(root, "src", "actors", "Cena.Actors"),
            Path.Combine(root, "src", "api"),
            Path.Combine(root, "src", "shared"),
        })
        {
            if (!Directory.Exists(scanRoot)) continue;

            var files = Directory.EnumerateFiles(scanRoot, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Generated{Path.DirectorySeparatorChar}"))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}"))
                .Where(f => !f.Contains($".Tests{Path.DirectorySeparatorChar}"));

            foreach (var file in files)
            {
                var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (AllowedVocabularyCallSites.Contains(rel, StringComparer.Ordinal)) continue;

                var content = File.ReadAllText(file);
                if (BannedVocabularyPattern.IsMatch(content))
                    offenders.Add(rel);
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"Banned cheating-alert vocabulary detected in files outside the allowlist. " +
            $"Route integrity signals through IAcademicIntegrityReviewService instead, " +
            $"or add the path to AllowedVocabularyCallSites with a one-line rationale. " +
            $"Offending files:{Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", offenders));
    }
}
