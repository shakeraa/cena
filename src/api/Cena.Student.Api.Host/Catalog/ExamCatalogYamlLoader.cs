// =============================================================================
// Cena Platform — YAML → CatalogSnapshot loader (prr-220)
//
// Reads contracts/exam-catalog/*.yml + catalog-meta.yml into an
// immutable CatalogSnapshot. Uses YamlDotNet (already in Infrastructure).
//
// Monotonicity: the loader parses the catalog_version from meta, but
// it does NOT enforce monotonicity itself — that is the service's job,
// because the loader has no view of the "previous" state. The service
// compares before publishing.
//
// File size budget: every entry is O(lines) in its own file. A future
// catalog that balloons past 500 LOC will be split into sub-directories.
// =============================================================================

using System.Globalization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cena.Student.Api.Host.Catalog;

public sealed class ExamCatalogLoadException : Exception
{
    public ExamCatalogLoadException(string message, Exception? inner = null)
        : base(message, inner) { }
}

/// <summary>
/// Pure-disk YAML deserializer. No side effects, no caching, no DI.
/// The service class below is what's registered; this loader is a
/// free-standing helper the service delegates to.
/// </summary>
internal static class ExamCatalogYamlLoader
{
    private static readonly IDeserializer _yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static CatalogSnapshot Load(string catalogDir, DateTimeOffset now)
    {
        if (!Directory.Exists(catalogDir))
            throw new ExamCatalogLoadException(
                $"Exam catalog directory not found: {catalogDir}");

        var metaPath = Path.Combine(catalogDir, "catalog-meta.yml");
        if (!File.Exists(metaPath))
            throw new ExamCatalogLoadException(
                $"catalog-meta.yml missing in {catalogDir}");

        var meta = Deserialize<MetaYaml>(metaPath);
        if (string.IsNullOrWhiteSpace(meta.CatalogVersion))
            throw new ExamCatalogLoadException(
                "catalog-meta.yml: catalog_version is required");

        var targetsByCode = new Dictionary<string, CatalogTarget>(StringComparer.Ordinal);
        foreach (var yml in Directory.EnumerateFiles(catalogDir, "*.yml")
                     .Where(p => !string.Equals(
                         Path.GetFileName(p), "catalog-meta.yml", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            CatalogTarget target;
            try
            {
                target = MapTarget(Deserialize<TargetYaml>(yml));
            }
            catch (Exception ex)
            {
                throw new ExamCatalogLoadException(
                    $"Failed to parse catalog file {Path.GetFileName(yml)}: {ex.Message}", ex);
            }

            if (!targetsByCode.TryAdd(target.ExamCode, target))
                throw new ExamCatalogLoadException(
                    $"Duplicate exam_code in catalog: {target.ExamCode} "
                    + $"(conflict in {Path.GetFileName(yml)})");
        }

        // Families map normalization — preserve ordering from YAML, drop
        // any codes the map references but that did not deserialize.
        var families = (meta.Families ?? new Dictionary<string, List<string>>())
            .ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<string>)kvp.Value
                    .Where(code => targetsByCode.ContainsKey(code))
                    .ToArray(),
                StringComparer.Ordinal);

        var familyOrder = (meta.FamilyOrder ?? new List<string>())
            .Where(families.ContainsKey)
            .ToArray();

        return new CatalogSnapshot(
            CatalogVersion: meta.CatalogVersion.Trim(),
            LoadedAt: now,
            FamilyOrder: familyOrder,
            Families: families,
            TargetsByCode: targetsByCode);
    }

    private static T Deserialize<T>(string path)
    {
        using var reader = new StreamReader(path);
        var result = _yaml.Deserialize<T>(reader)
            ?? throw new ExamCatalogLoadException($"Empty YAML: {path}");
        return result;
    }

    private static CatalogTarget MapTarget(TargetYaml y)
    {
        if (string.IsNullOrWhiteSpace(y.ExamCode))
            throw new ExamCatalogLoadException("target yaml: exam_code missing");
        if (string.IsNullOrWhiteSpace(y.Family))
            throw new ExamCatalogLoadException($"{y.ExamCode}: family missing");
        if (string.IsNullOrWhiteSpace(y.Region))
            throw new ExamCatalogLoadException($"{y.ExamCode}: region missing");
        if (string.IsNullOrWhiteSpace(y.Availability))
            throw new ExamCatalogLoadException($"{y.ExamCode}: availability missing");
        if (string.IsNullOrWhiteSpace(y.ItemBankStatus))
            throw new ExamCatalogLoadException($"{y.ExamCode}: item_bank_status missing");
        if (string.IsNullOrWhiteSpace(y.Regulator))
            throw new ExamCatalogLoadException($"{y.ExamCode}: regulator missing");

        var sittings = (y.Sittings ?? new List<SittingYaml>())
            .Select(s => new CatalogSitting(
                Code: Req(s.Code, "sitting.code"),
                AcademicYear: Req(s.AcademicYear, "sitting.academic_year"),
                Season: Req(s.Season, "sitting.season"),
                Moed: Req(s.Moed, "sitting.moed"),
                CanonicalDate: DateOnly.Parse(
                    Req(s.CanonicalDate, "sitting.canonical_date"),
                    CultureInfo.InvariantCulture)))
            .ToArray();

        var display = (y.Display ?? new Dictionary<string, DisplayYaml>())
            .ToDictionary(
                kvp => kvp.Key,
                kvp => new LocalizedEntry(
                    Name: Req(kvp.Value.Name, $"display.{kvp.Key}.name"),
                    ShortDescription: kvp.Value.ShortDescription),
                StringComparer.OrdinalIgnoreCase);

        var topics = (y.Topics ?? new List<TopicYaml>())
            .Select(t => new CatalogTopic(
                TopicId: Req(t.TopicId, "topic.topic_id"),
                Display: (t.Display ?? new Dictionary<string, string>())
                    .ToDictionary(
                        kv => kv.Key,
                        kv => new LocalizedEntry(kv.Value, null),
                        StringComparer.OrdinalIgnoreCase)))
            .ToArray();

        return new CatalogTarget(
            ExamCode: y.ExamCode,
            Family: y.Family,
            Region: y.Region,
            Track: string.IsNullOrWhiteSpace(y.Track) ? null : y.Track,
            Units: y.Units,
            Regulator: y.Regulator,
            MinistrySubjectCode: string.IsNullOrWhiteSpace(y.MinistrySubjectCode)
                ? null : y.MinistrySubjectCode,
            MinistryQuestionPaperCodes: (y.MinistryQuestionPaperCodes ?? new List<string>()).ToArray(),
            Availability: y.Availability,
            ItemBankStatus: y.ItemBankStatus,
            PassbackEligible: y.PassbackEligible,
            DefaultLeadDays: y.DefaultLeadDays > 0 ? y.DefaultLeadDays : 90,
            Sittings: sittings,
            Display: display,
            Topics: topics);
    }

    private static string Req(string? v, string field) =>
        string.IsNullOrWhiteSpace(v)
            ? throw new ExamCatalogLoadException($"required field missing: {field}")
            : v.Trim();

    // ---------------- raw YAML shape (mutable DTOs for the deserializer) -----

    private sealed class MetaYaml
    {
        public string? CatalogVersion { get; set; }
        public List<string>? FamilyOrder { get; set; }
        public Dictionary<string, List<string>>? Families { get; set; }
    }

    private sealed class TargetYaml
    {
        public string? ExamCode { get; set; }
        public string? Family { get; set; }
        public string? Track { get; set; }
        public int? Units { get; set; }
        public string? Region { get; set; }
        public string? Regulator { get; set; }
        public string? MinistrySubjectCode { get; set; }
        public List<string>? MinistryQuestionPaperCodes { get; set; }
        public string? Availability { get; set; }
        public string? ItemBankStatus { get; set; }
        public bool PassbackEligible { get; set; }
        public int DefaultLeadDays { get; set; }
        public List<SittingYaml>? Sittings { get; set; }
        public Dictionary<string, DisplayYaml>? Display { get; set; }
        public List<TopicYaml>? Topics { get; set; }
    }

    private sealed class SittingYaml
    {
        public string? Code { get; set; }
        public string? AcademicYear { get; set; }
        public string? Season { get; set; }
        public string? Moed { get; set; }
        public string? CanonicalDate { get; set; }
    }

    private sealed class DisplayYaml
    {
        public string? Name { get; set; }
        public string? ShortDescription { get; set; }
    }

    private sealed class TopicYaml
    {
        public string? TopicId { get; set; }
        public Dictionary<string, string>? Display { get; set; }
    }
}
