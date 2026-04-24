// =============================================================================
// Cena Platform — Sub-Processor Registry (prr-035)
//
// Loads contracts/privacy/sub-processors.yml into an immutable registry
// served to:
//   - Admin UI (privacy-admin transparency page)
//   - Parent transparency surface (subset filtered by parent_visible=true)
//   - Architecture test (SubProcessorRegistryCompleteTest)
//
// Invariants enforced at load (service refuses to construct on violation):
//   - every entry carries a non-empty DPA link, SSO method, residency,
//     purpose, status
//   - every data_categories tag is in the declared taxonomy
//   - status ∈ {active, suspended, deprecated, pending}
// =============================================================================

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cena.Actors.Infrastructure.Privacy;

public sealed record SubProcessor(
    string Id,
    string Vendor,
    string Category,
    string Purpose,
    IReadOnlyList<string> DataCategories,
    string DataResidency,
    string SsoMethod,
    string DpaLink,
    DateTimeOffset DpaEffectiveDate,
    string Status,
    bool ParentVisible,
    IReadOnlyList<string> Hostnames,
    string? Notes);

public sealed record SubProcessorRegistrySnapshot(
    string RegistryVersion,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<string> DataCategoryTaxonomy,
    IReadOnlyList<SubProcessor> All)
{
    public IEnumerable<SubProcessor> ActiveAndPending =>
        All.Where(p => p.Status is "active" or "pending");

    public IEnumerable<SubProcessor> ParentVisible =>
        All.Where(p => p.ParentVisible);

    public SubProcessor? ForHost(string hostname) =>
        All.FirstOrDefault(p => p.Hostnames.Any(h =>
            string.Equals(h, hostname, StringComparison.OrdinalIgnoreCase)));
}

public interface ISubProcessorRegistry
{
    SubProcessorRegistrySnapshot Current { get; }
}

public sealed class SubProcessorRegistryLoadException : Exception
{
    public SubProcessorRegistryLoadException(string message, Exception? inner = null)
        : base(message, inner) { }
}

public sealed class SubProcessorRegistry : ISubProcessorRegistry
{
    private static readonly string[] LegalStatus =
        { "active", "suspended", "deprecated", "pending" };

    private static readonly IDeserializer _yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public SubProcessorRegistrySnapshot Current { get; }

    public SubProcessorRegistry(string registryPath)
    {
        if (string.IsNullOrWhiteSpace(registryPath))
            throw new ArgumentException("registryPath is required", nameof(registryPath));
        if (!File.Exists(registryPath))
            throw new SubProcessorRegistryLoadException(
                $"Sub-processor registry not found at {registryPath}");

        RawRegistry raw;
        try
        {
            using var reader = new StreamReader(registryPath);
            raw = _yaml.Deserialize<RawRegistry>(reader)
                ?? throw new SubProcessorRegistryLoadException(
                    $"Empty YAML in {registryPath}");
        }
        catch (SubProcessorRegistryLoadException) { throw; }
        catch (Exception ex)
        {
            throw new SubProcessorRegistryLoadException(
                $"Failed to parse {registryPath}: {ex.Message}", ex);
        }

        if (string.IsNullOrWhiteSpace(raw.RegistryVersion))
            throw new SubProcessorRegistryLoadException(
                $"{registryPath}: registry_version is required");

        var taxonomy = (raw.DataCategoryTaxonomy ?? new List<string>())
            .Select(t => t.Trim()).Where(t => t.Length > 0).ToArray();
        if (taxonomy.Length == 0)
            throw new SubProcessorRegistryLoadException(
                $"{registryPath}: data_category_taxonomy must be non-empty");

        var taxonomySet = new HashSet<string>(taxonomy, StringComparer.Ordinal);

        var all = new List<SubProcessor>();
        var idSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in raw.SubProcessors ?? new List<RawEntry>())
        {
            all.Add(ValidateAndMap(e, taxonomySet, idSet, registryPath));
        }
        if (all.Count == 0)
            throw new SubProcessorRegistryLoadException(
                $"{registryPath}: sub_processors must be non-empty");

        Current = new SubProcessorRegistrySnapshot(
            raw.RegistryVersion.Trim(),
            raw.UpdatedAtUtc ?? DateTimeOffset.UnixEpoch,
            taxonomy,
            all);
    }

    private static SubProcessor ValidateAndMap(
        RawEntry e,
        HashSet<string> taxonomy,
        HashSet<string> idSet,
        string file)
    {
        if (string.IsNullOrWhiteSpace(e.Id))
            throw new SubProcessorRegistryLoadException($"{file}: entry missing id");
        var id = e.Id.Trim();
        if (!idSet.Add(id))
            throw new SubProcessorRegistryLoadException(
                $"{file}: duplicate sub-processor id '{id}'");

        Require(e.Vendor, $"{file}[{id}]: vendor");
        Require(e.Category, $"{file}[{id}]: category");
        Require(e.Purpose, $"{file}[{id}]: purpose");
        Require(e.DataResidency, $"{file}[{id}]: data_residency");
        Require(e.SsoMethod, $"{file}[{id}]: sso_method");
        Require(e.DpaLink, $"{file}[{id}]: dpa_link");
        Require(e.Status, $"{file}[{id}]: status");
        if (e.DpaEffectiveDate is null)
            throw new SubProcessorRegistryLoadException(
                $"{file}[{id}]: dpa_effective_date is required");

        var status = e.Status!.Trim().ToLowerInvariant();
        if (!LegalStatus.Contains(status))
            throw new SubProcessorRegistryLoadException(
                $"{file}[{id}]: status '{status}' not in " +
                $"{{{string.Join(",", LegalStatus)}}}");

        var categories = (e.DataCategories ?? new List<string>())
            .Select(c => c.Trim()).Where(c => c.Length > 0).ToArray();
        if (categories.Length == 0)
            throw new SubProcessorRegistryLoadException(
                $"{file}[{id}]: data_categories must be non-empty");
        foreach (var c in categories)
        {
            if (!taxonomy.Contains(c))
                throw new SubProcessorRegistryLoadException(
                    $"{file}[{id}]: unknown data_category '{c}' — " +
                    "add to data_category_taxonomy or remove");
        }

        var hostnames = (e.Hostnames ?? new List<string>())
            .Select(h => h.Trim()).Where(h => h.Length > 0).ToArray();
        if (hostnames.Length == 0)
            throw new SubProcessorRegistryLoadException(
                $"{file}[{id}]: hostnames must be non-empty — needed by " +
                "SubProcessorRegistryCompleteTest to match outbound integrations");

        return new SubProcessor(
            id,
            e.Vendor!.Trim(),
            e.Category!.Trim().ToLowerInvariant(),
            e.Purpose!.Trim(),
            categories,
            e.DataResidency!.Trim(),
            e.SsoMethod!.Trim().ToLowerInvariant(),
            e.DpaLink!.Trim(),
            e.DpaEffectiveDate.Value.ToUniversalTime(),
            status,
            e.ParentVisible ?? true,
            hostnames,
            string.IsNullOrWhiteSpace(e.Notes) ? null : e.Notes.Trim());
    }

    private static void Require(string? value, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new SubProcessorRegistryLoadException($"{path} is required");
    }

    private sealed class RawRegistry
    {
        public string? RegistryVersion { get; set; }
        public DateTimeOffset? UpdatedAtUtc { get; set; }
        public List<string>? DataCategoryTaxonomy { get; set; }
        public List<RawEntry>? SubProcessors { get; set; }
    }

    private sealed class RawEntry
    {
        public string? Id { get; set; }
        public string? Vendor { get; set; }
        public string? Category { get; set; }
        public string? Purpose { get; set; }
        public List<string>? DataCategories { get; set; }
        public string? DataResidency { get; set; }
        public string? SsoMethod { get; set; }
        public string? DpaLink { get; set; }
        public DateTimeOffset? DpaEffectiveDate { get; set; }
        public string? Status { get; set; }
        public bool? ParentVisible { get; set; }
        public List<string>? Hostnames { get; set; }
        public string? Notes { get; set; }
    }
}
