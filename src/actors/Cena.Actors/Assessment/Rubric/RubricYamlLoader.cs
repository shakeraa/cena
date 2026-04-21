// =============================================================================
// Cena Platform — Rubric DSL YAML loader (prr-033, ADR-0052)
//
// Pure-disk deserializer. Mirrors ExamCatalogYamlLoader: YamlDotNet with
// underscore naming convention, IgnoreUnmatchedProperties, non-caching.
// The service class owns the lifetime; this loader is a free-standing
// helper the service delegates to on boot and on reload.
//
// Validation fails loudly:
//   - every track must carry a non-empty sign-off triple
//   - scoring_criteria weights must sum to 1.0 ± 0.001
//   - grade_bands must partition 0..100 with no overlap or gap
//   - duplicate exam_code across files is rejected
//
// A rubric that fails validation is NOT served as a degraded partial
// snapshot — the whole reload is refused. The previous snapshot stays
// live. Fail-closed is the correct posture for a regulator-facing
// artifact.
// =============================================================================

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cena.Actors.Assessment.Rubric;

internal static class RubricYamlLoader
{
    private const double WeightTolerance = 0.001;

    private static readonly IDeserializer _yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static RubricSnapshot Load(string rubricDir, DateTimeOffset now)
    {
        if (!Directory.Exists(rubricDir))
            throw new RubricLoadException($"Rubric directory not found: {rubricDir}");

        var byExamCode = new Dictionary<string, BagrutRubric>(StringComparer.Ordinal);
        var all = new List<BagrutRubric>();

        foreach (var path in Directory.EnumerateFiles(rubricDir, "*.yml")
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            BagrutRubric rubric;
            try
            {
                var raw = DeserializeFile(path);
                rubric = MapRubric(raw, Path.GetFileName(path));
            }
            catch (RubricLoadException) { throw; }
            catch (Exception ex)
            {
                throw new RubricLoadException(
                    $"Failed to parse rubric file {Path.GetFileName(path)}: {ex.Message}", ex);
            }

            if (!byExamCode.TryAdd(rubric.ExamCode, rubric))
            {
                var existing = byExamCode[rubric.ExamCode];
                throw new RubricLoadException(
                    $"Duplicate exam_code '{rubric.ExamCode}' at version " +
                    $"{rubric.RubricVersion} (conflicts with version {existing.RubricVersion}). " +
                    "Per-track only one pinned version is loadable at a time; " +
                    "deprecate the older file before adding a new one.");
            }
            all.Add(rubric);
        }

        return new RubricSnapshot(byExamCode, all, now);
    }

    private static RawRubric DeserializeFile(string path)
    {
        using var reader = new StreamReader(path);
        var raw = _yaml.Deserialize<RawRubric>(reader);
        if (raw is null)
            throw new RubricLoadException($"Empty YAML payload in {Path.GetFileName(path)}");
        return raw;
    }

    private static BagrutRubric MapRubric(RawRubric raw, string file)
    {
        if (string.IsNullOrWhiteSpace(raw.ExamCode))
            throw new RubricLoadException($"{file}: exam_code is required");
        if (string.IsNullOrWhiteSpace(raw.RubricVersion))
            throw new RubricLoadException($"{file}: rubric_version is required");

        // ── Sign-off triple is mandatory (ADR-0052 §3) ───────────────────
        if (string.IsNullOrWhiteSpace(raw.ApprovedByUserId))
            throw new RubricLoadException(
                $"{file}: approved_by_user_id is required — a rubric without sign-off " +
                "cannot be served. See ADR-0052 §3.");
        if (raw.ApprovedAtUtc is null)
            throw new RubricLoadException(
                $"{file}: approved_at_utc is required — a rubric without a sign-off " +
                "timestamp cannot be served. See ADR-0052 §3.");
        if (string.IsNullOrWhiteSpace(raw.MinistryCircularRef))
            throw new RubricLoadException(
                $"{file}: ministry_circular_ref is required — a rubric without a " +
                "citable Ministry source cannot be served. See ADR-0052 §3.");

        var signOff = new RubricSignOff(
            raw.ApprovedByUserId.Trim(),
            raw.ApprovedAtUtc.Value.ToUniversalTime(),
            raw.MinistryCircularRef.Trim());

        // ── Grade bands must partition 0..100 ────────────────────────────
        var bands = (raw.GradeBands ?? new List<RawGradeBand>())
            .Select(b => MapBand(b, file))
            .OrderBy(b => b.MinScore)
            .ToList();
        ValidateBandPartition(bands, file);

        // ── Scoring criteria weights must sum to 1.0 ─────────────────────
        var criteria = (raw.ScoringCriteria ?? new List<RawCriterion>())
            .Select(c => MapCriterion(c, file))
            .ToList();
        ValidateWeightSum(criteria, file);

        return new BagrutRubric(
            raw.ExamCode.Trim(),
            raw.RubricVersion.Trim(),
            signOff,
            bands,
            criteria);
    }

    private static RubricGradeBand MapBand(RawGradeBand b, string file)
    {
        if (string.IsNullOrWhiteSpace(b.Band))
            throw new RubricLoadException($"{file}: grade_bands[*].band is required");
        if (b.MinScore < 0 || b.MaxScore > 100 || b.MinScore > b.MaxScore)
            throw new RubricLoadException(
                $"{file}: grade_bands[{b.Band}] invalid bounds ({b.MinScore}..{b.MaxScore})");
        return new RubricGradeBand(
            b.Band.Trim(),
            b.MinScore,
            b.MaxScore,
            MapLocalized(b.Descriptor, file, $"grade_bands[{b.Band}].descriptor"));
    }

    private static void ValidateBandPartition(IReadOnlyList<RubricGradeBand> bands, string file)
    {
        if (bands.Count == 0)
            throw new RubricLoadException($"{file}: grade_bands must be non-empty");
        if (bands[0].MinScore != 0)
            throw new RubricLoadException(
                $"{file}: grade_bands must start at 0 (got {bands[0].MinScore})");
        if (bands[^1].MaxScore != 100)
            throw new RubricLoadException(
                $"{file}: grade_bands must end at 100 (got {bands[^1].MaxScore})");
        for (int i = 1; i < bands.Count; i++)
        {
            if (bands[i].MinScore != bands[i - 1].MaxScore + 1)
                throw new RubricLoadException(
                    $"{file}: grade_bands have gap or overlap between " +
                    $"'{bands[i - 1].Band}' ({bands[i - 1].MaxScore}) and " +
                    $"'{bands[i].Band}' ({bands[i].MinScore}). Must partition 0..100.");
        }
    }

    private static RubricCriterion MapCriterion(RawCriterion c, string file)
    {
        if (string.IsNullOrWhiteSpace(c.CriterionId))
            throw new RubricLoadException($"{file}: scoring_criteria[*].criterion_id is required");
        if (c.Weight <= 0 || c.Weight > 1)
            throw new RubricLoadException(
                $"{file}: scoring_criteria[{c.CriterionId}].weight must be in (0,1]");
        var checkpoints = (c.Checkpoints ?? new List<RawCheckpoint>())
            .Select(cp => MapCheckpoint(cp, file, c.CriterionId))
            .ToList();
        if (checkpoints.Count == 0)
            throw new RubricLoadException(
                $"{file}: scoring_criteria[{c.CriterionId}].checkpoints must be non-empty");
        return new RubricCriterion(
            c.CriterionId.Trim(),
            c.Weight,
            MapLocalized(c.Display, file, $"scoring_criteria[{c.CriterionId}].display"),
            checkpoints);
    }

    private static void ValidateWeightSum(IReadOnlyList<RubricCriterion> criteria, string file)
    {
        if (criteria.Count == 0)
            throw new RubricLoadException($"{file}: scoring_criteria must be non-empty");
        var sum = criteria.Sum(c => c.Weight);
        if (Math.Abs(sum - 1.0) > WeightTolerance)
            throw new RubricLoadException(
                $"{file}: scoring_criteria weights sum to {sum:F4}, expected 1.0000 " +
                $"(tolerance ±{WeightTolerance})");
    }

    private static RubricCheckpoint MapCheckpoint(RawCheckpoint cp, string file, string crit)
    {
        if (string.IsNullOrWhiteSpace(cp.Id))
            throw new RubricLoadException(
                $"{file}: scoring_criteria[{crit}].checkpoints[*].id is required");
        if (cp.Points <= 0)
            throw new RubricLoadException(
                $"{file}: scoring_criteria[{crit}].checkpoints[{cp.Id}].points must be positive");
        return new RubricCheckpoint(
            cp.Id.Trim(),
            cp.Points,
            (cp.DescriptionEn ?? string.Empty).Trim());
    }

    private static RubricLocalizedText MapLocalized(RawLocalized? loc, string file, string path)
    {
        if (loc is null || string.IsNullOrWhiteSpace(loc.En))
            throw new RubricLoadException($"{file}: {path}.en is required");
        return new RubricLocalizedText(loc.En.Trim(), loc.He?.Trim(), loc.Ar?.Trim());
    }

    // ── YAML shape (private, underscore naming) ──────────────────────────

    private sealed class RawRubric
    {
        public string? ExamCode { get; set; }
        public string? RubricVersion { get; set; }
        public string? MinistryCircularRef { get; set; }
        public string? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAtUtc { get; set; }
        public List<RawGradeBand>? GradeBands { get; set; }
        public List<RawCriterion>? ScoringCriteria { get; set; }
    }

    private sealed class RawGradeBand
    {
        public string? Band { get; set; }
        public int MinScore { get; set; }
        public int MaxScore { get; set; }
        public RawLocalized? Descriptor { get; set; }
    }

    private sealed class RawCriterion
    {
        public string? CriterionId { get; set; }
        public double Weight { get; set; }
        public RawLocalized? Display { get; set; }
        public List<RawCheckpoint>? Checkpoints { get; set; }
    }

    private sealed class RawCheckpoint
    {
        public string? Id { get; set; }
        public int Points { get; set; }
        public string? DescriptionEn { get; set; }
    }

    private sealed class RawLocalized
    {
        public string? En { get; set; }
        public string? He { get; set; }
        public string? Ar { get; set; }
    }
}
