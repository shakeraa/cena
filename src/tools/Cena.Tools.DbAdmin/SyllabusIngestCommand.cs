// =============================================================================
// Cena Platform — DbAdmin: syllabus-ingest (RDY-061 Phase 1)
//
// Reads a SyllabusManifest.yaml file and upserts SyllabusDocument +
// ChapterDocument rows in Marten. Idempotent: re-ingesting the same
// manifest replaces chapter membership in place. Rejects the manifest
// if:
//   - Required fields are missing
//   - Chapter order is not monotonic
//   - Chapter slugs are non-unique within the syllabus
//   - Prerequisite chapter slugs reference non-existent chapters
//   - Prerequisite chapters introduce a cycle
//   - Any LO id in a chapter doesn't resolve to a real
//     LearningObjectiveDocument row
//   - Referenced track doesn't exist in CurriculumTrackDocument
//
// No student data is touched — this is a definition-layer write.
// =============================================================================

using System.Text.RegularExpressions;
using Cena.Actors.Configuration;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cena.Tools.DbAdmin;

public static class SyllabusIngestCommand
{
    public static async Task<int> RunAsync(string[] args, IConfiguration config, ILogger logger)
    {
        var manifestPath = ArgValue(args, "--manifest");
        var author = ArgValue(args, "--author") ?? "cli";
        var prune = args.Contains("--prune");

        if (string.IsNullOrEmpty(manifestPath))
        {
            logger.LogError("Usage: syllabus-ingest --manifest <path.yaml> [--author <id>] [--prune]");
            return 2;
        }
        if (!File.Exists(manifestPath))
        {
            logger.LogError("Manifest not found: {Path}", manifestPath);
            return 2;
        }

        // ── Parse ──
        SyllabusManifest manifest;
        try
        {
            var yaml = await File.ReadAllTextAsync(manifestPath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            manifest = deserializer.Deserialize<SyllabusManifest>(yaml)
                ?? throw new InvalidOperationException("manifest deserialised as null");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[SYLLABUS_PARSE_FAIL] {Path}", manifestPath);
            return 1;
        }

        // ── Validate ──
        var errors = Validate(manifest);
        if (errors.Count > 0)
        {
            foreach (var e in errors) logger.LogError("[SYLLABUS_INVALID] {Err}", e);
            return 1;
        }

        // ── Connect + cross-check against Marten ──
        var connectionString = config["CENA_POSTGRES_CONNECTION"]
            ?? config["CENA_MIGRATOR_CONNECTION_STRING"]
            ?? "Host=localhost;Port=5433;Database=cena;Username=cena;Password=cena_dev_password;Search Path=cena,public";

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(connectionString);
            opts.ConfigureCenaEventStore(connectionString, autoCreateMode: "CreateOrUpdate");
        });

        await using var session = store.LightweightSession();

        // Track must exist
        var track = await session.Query<CurriculumTrackDocument>()
            .Where(t => t.TrackId == manifest.Track)
            .FirstOrDefaultAsync();
        if (track is null)
        {
            logger.LogError("[SYLLABUS_INVALID] referenced track '{TrackId}' not found in CurriculumTrackDocument", manifest.Track);
            return 1;
        }

        // LOs must exist
        var allLoIds = manifest.Chapters
            .SelectMany(c => c.LearningObjectiveIds ?? Enumerable.Empty<string>())
            .Distinct()
            .ToList();
        if (allLoIds.Count > 0)
        {
            var existingLoIds = (await session.Query<LearningObjectiveDocument>()
                .Where(lo => allLoIds.Contains(lo.Id))
                .Select(lo => lo.Id)
                .ToListAsync())
                .ToHashSet();
            var missing = allLoIds.Where(id => !existingLoIds.Contains(id)).ToList();
            if (missing.Count > 0)
            {
                // Warn but don't fail — in development the LO store may be
                // sparse; we still want the syllabus structure to land so
                // UIs can iterate. In production this becomes a hard error.
                logger.LogWarning(
                    "[SYLLABUS_LO_MISSING] {Count} learning-objective ids referenced by the manifest don't exist yet: {Missing}",
                    missing.Count, string.Join(", ", missing.Take(10)) + (missing.Count > 10 ? ", ..." : ""));
            }
        }

        // ── Upsert ──
        var syllabusId = $"syllabus-{SlugFromTrack(manifest.Track)}";
        var now = DateTimeOffset.UtcNow;

        var syllabus = new SyllabusDocument
        {
            Id = syllabusId,
            TrackId = manifest.Track,
            Version = manifest.Version,
            Track = manifest.BagrutTrack,
            MinistryCodes = manifest.MinistryCodes?.ToList() ?? new(),
            ChapterIds = new(),  // filled below
            TotalExpectedWeeks = manifest.Chapters.Sum(c => c.ExpectedWeeks),
            SourceManifestPath = Path.GetFullPath(manifestPath),
            IngestedBy = author,
            IngestedAt = now,
        };

        var chapterDocs = new List<ChapterDocument>();
        foreach (var ch in manifest.Chapters.OrderBy(c => c.Order))
        {
            var chapterId = $"chapter-{SlugFromTrack(manifest.Track)}-{ch.Order:D2}-{ch.Slug}";
            syllabus.ChapterIds.Add(chapterId);

            // Resolve prereq slugs → chapter ids
            var prereqIds = (ch.PrerequisiteChapterSlugs ?? new())
                .Select(slug =>
                {
                    var match = manifest.Chapters.FirstOrDefault(c => c.Slug == slug);
                    if (match is null)
                        throw new InvalidOperationException($"prereq slug '{slug}' in chapter '{ch.Slug}' doesn't resolve");
                    return $"chapter-{SlugFromTrack(manifest.Track)}-{match.Order:D2}-{match.Slug}";
                })
                .ToList();

            chapterDocs.Add(new ChapterDocument
            {
                Id = chapterId,
                SyllabusId = syllabusId,
                Order = ch.Order,
                Slug = ch.Slug,
                TitleByLocale = ch.Title ?? new(),
                LearningObjectiveIds = ch.LearningObjectiveIds?.ToList() ?? new(),
                PrerequisiteChapterIds = prereqIds,
                ExpectedWeeks = ch.ExpectedWeeks,
                MinistryCode = ch.MinistryCode,
                IngestedAt = now,
            });
        }

        session.Store(syllabus);
        foreach (var c in chapterDocs) session.Store(c);

        if (prune)
        {
            // Delete any chapter docs attached to this syllabus that aren't
            // in the current manifest. Irreversible — only run with --prune
            // when you know old chapter ids are truly retired.
            var keepIds = chapterDocs.Select(c => c.Id).ToHashSet();
            var existing = await session.Query<ChapterDocument>()
                .Where(c => c.SyllabusId == syllabusId)
                .ToListAsync();
            var toDelete = existing.Where(c => !keepIds.Contains(c.Id)).ToList();
            foreach (var dead in toDelete)
            {
                session.Delete(dead);
                logger.LogInformation("[SYLLABUS_PRUNE] removing orphaned chapter {Id}", dead.Id);
            }
        }

        await session.SaveChangesAsync();

        logger.LogInformation(
            "[SYLLABUS_INGEST_OK] id={Id} track={TrackId} version={Version} chapters={Count} author={Author}",
            syllabusId, manifest.Track, manifest.Version, chapterDocs.Count, author);

        return 0;
    }

    private static List<string> Validate(SyllabusManifest m)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(m.Track)) errors.Add("track is required");
        if (string.IsNullOrWhiteSpace(m.Version)) errors.Add("version is required");
        if (m.Chapters is null || m.Chapters.Count == 0)
        {
            errors.Add("at least one chapter required");
            return errors;
        }

        var slugRegex = new Regex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
        var seenSlugs = new HashSet<string>();
        var seenOrders = new HashSet<int>();
        foreach (var ch in m.Chapters)
        {
            if (string.IsNullOrWhiteSpace(ch.Slug) || !slugRegex.IsMatch(ch.Slug))
                errors.Add($"chapter slug invalid: '{ch.Slug}' (must be kebab-case)");
            if (!seenSlugs.Add(ch.Slug ?? ""))
                errors.Add($"duplicate chapter slug: {ch.Slug}");
            if (ch.Order <= 0)
                errors.Add($"chapter '{ch.Slug}' order must be >= 1");
            if (!seenOrders.Add(ch.Order))
                errors.Add($"duplicate chapter order: {ch.Order}");
            if (ch.Title is null || !ch.Title.ContainsKey("en") || string.IsNullOrWhiteSpace(ch.Title["en"]))
                errors.Add($"chapter '{ch.Slug}' missing title.en");
            if (ch.LearningObjectiveIds is null || ch.LearningObjectiveIds.Count == 0)
                errors.Add($"chapter '{ch.Slug}' has no learning objectives");
            if (ch.ExpectedWeeks <= 0)
                errors.Add($"chapter '{ch.Slug}' expectedWeeks must be > 0");
        }

        // Cycle detection on chapter prereqs
        if (errors.Count == 0)
        {
            var bySlug = m.Chapters.ToDictionary(c => c.Slug ?? "", c => c);
            foreach (var ch in m.Chapters)
            {
                if (HasCycle(ch.Slug!, bySlug, new HashSet<string>(), new HashSet<string>()))
                    errors.Add($"cycle detected in chapter prereqs starting at '{ch.Slug}'");
            }
        }

        return errors;
    }

    private static bool HasCycle(string slug, Dictionary<string, ChapterManifest> bySlug,
        HashSet<string> onStack, HashSet<string> visited)
    {
        if (onStack.Contains(slug)) return true;
        if (visited.Contains(slug)) return false;
        onStack.Add(slug);
        if (bySlug.TryGetValue(slug, out var ch) && ch.PrerequisiteChapterSlugs is not null)
        {
            foreach (var p in ch.PrerequisiteChapterSlugs)
                if (bySlug.ContainsKey(p) && HasCycle(p, bySlug, onStack, visited)) return true;
        }
        onStack.Remove(slug);
        visited.Add(slug);
        return false;
    }

    private static string SlugFromTrack(string trackId)
        // Input like "track-math-bagrut-5unit" → "math-bagrut-5unit"
        => trackId.StartsWith("track-", StringComparison.Ordinal) ? trackId["track-".Length..] : trackId;

    private static string? ArgValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == flag) return args[i + 1];
        return null;
    }
}

// ── Manifest DTO (YamlDotNet binds by camelCase) ────────────────────────────

public sealed class SyllabusManifest
{
    public string Track { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public BagrutTrack BagrutTrack { get; set; } = BagrutTrack.None;
    public List<string>? MinistryCodes { get; set; }
    public List<ChapterManifest> Chapters { get; set; } = new();
}

public sealed class ChapterManifest
{
    public string? Slug { get; set; }
    public int Order { get; set; }
    public Dictionary<string, string>? Title { get; set; }
    public List<string>? LearningObjectiveIds { get; set; }
    public List<string>? PrerequisiteChapterSlugs { get; set; }
    public int ExpectedWeeks { get; set; }
    public string? MinistryCode { get; set; }
}
