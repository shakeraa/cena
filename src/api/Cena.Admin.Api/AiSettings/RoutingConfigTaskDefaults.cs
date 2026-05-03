// =============================================================================
// Cena Platform — RoutingConfigTaskDefaults
//
// Read-only loader for routing-config.yaml § default_model_by_task: and
// § global_default_model_id. Singleton-loaded once at startup; the YAML is
// part of the immutable contract layer (ADR-0026) and must not change at
// runtime, so caching forever is safe.
//
// Why a YAML loader and not a hardcoded dictionary in code:
//
//   The whole point of the per-task override surface is that finance,
//   product, and engineering can change "what does concept_extraction
//   default to today?" without redeploying code. Hardcoding the defaults
//   in code defeats this — the YAML is the single source of truth.
//
// Why a dedicated loader and not a config binder:
//
//   routing-config.yaml is large (~700 lines, models + rate_limits +
//   circuit_breaker + caching policy + observability). Binding a typed
//   schema for the WHOLE file means any add/rename anywhere in YAML
//   risks breaking startup. Two scalar-only sections at top level are
//   trivial to parse with YamlDotNet and survive any other section
//   changing shape.
// =============================================================================

using YamlDotNet.RepresentationModel;

namespace Cena.Admin.Api.AiSettings;

/// <summary>
/// Per-task default model id table loaded from routing-config.yaml. Used by
/// <see cref="ModelResolver"/> as the second tier of the resolution chain
/// (after AiSettingsDocument override, before throwing).
/// </summary>
public sealed class RoutingConfigTaskDefaults
{
    private readonly IReadOnlyDictionary<string, string> _byTask;
    private readonly string? _globalDefault;

    internal RoutingConfigTaskDefaults(
        IReadOnlyDictionary<string, string> byTask,
        string? globalDefault)
    {
        _byTask = byTask;
        _globalDefault = globalDefault;
    }

    /// <summary>
    /// Try to resolve a default model id for <paramref name="taskName"/>.
    /// Returns false when the task is absent from the per-task map AND no
    /// global default is configured — caller throws ModelNotConfiguredException.
    /// </summary>
    public bool TryResolveDefault(string taskName, out string modelId, out string source)
    {
        if (string.IsNullOrWhiteSpace(taskName))
        {
            modelId = "";
            source = "";
            return false;
        }

        if (_byTask.TryGetValue(taskName, out var taskDefault)
            && !string.IsNullOrWhiteSpace(taskDefault))
        {
            modelId = taskDefault;
            source = "routing-config-task-default";
            return true;
        }

        if (!string.IsNullOrWhiteSpace(_globalDefault))
        {
            modelId = _globalDefault!;
            source = "routing-config-global-default";
            return true;
        }

        modelId = "";
        source = "";
        return false;
    }

    /// <summary>The set of task names with explicit default rows in YAML.</summary>
    public IReadOnlyCollection<string> KnownTaskNames =>
        (IReadOnlyCollection<string>)_byTask.Keys;

    /// <summary>The global fallback model id (or null when YAML omits it).</summary>
    public string? GlobalDefaultModelId => _globalDefault;

    // ── Loader ────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse a routing-config.yaml document into a defaults table. Both
    /// <c>default_model_by_task:</c> and <c>global_default_model_id:</c>
    /// are optional — a YAML missing both yields an empty table whose
    /// <see cref="TryResolveDefault"/> always returns false. Callers that
    /// want fail-loud at startup can assert the YAML's expected shape
    /// after loading.
    /// </summary>
    public static RoutingConfigTaskDefaults LoadFromYaml(string yamlText)
    {
        ArgumentNullException.ThrowIfNull(yamlText);

        var byTask = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? globalDefault = null;

        using var reader = new StringReader(yamlText);
        var stream = new YamlStream();
        stream.Load(reader);

        if (stream.Documents.Count == 0)
        {
            return new RoutingConfigTaskDefaults(byTask, null);
        }

        var root = (YamlMappingNode)stream.Documents[0].RootNode;

        foreach (var (k, v) in root.Children)
        {
            if (k is not YamlScalarNode scalar) continue;

            if (scalar.Value == "default_model_by_task" && v is YamlMappingNode taskMap)
            {
                foreach (var (taskKey, taskVal) in taskMap.Children)
                {
                    if (taskKey is YamlScalarNode taskScalar
                        && taskVal is YamlScalarNode valScalar
                        && !string.IsNullOrWhiteSpace(taskScalar.Value)
                        && !string.IsNullOrWhiteSpace(valScalar.Value))
                    {
                        byTask[taskScalar.Value!] = valScalar.Value!;
                    }
                }
            }
            else if (scalar.Value == "global_default_model_id"
                     && v is YamlScalarNode gScalar
                     && !string.IsNullOrWhiteSpace(gScalar.Value))
            {
                globalDefault = gScalar.Value;
            }
        }

        return new RoutingConfigTaskDefaults(byTask, globalDefault);
    }

    /// <summary>
    /// File-loader convenience. Throws when the file is missing — pricing
    /// already enforces the same fail-loud-on-missing-file contract via
    /// LlmPricingTable.LoadFromFile, so this matches.
    /// </summary>
    public static RoutingConfigTaskDefaults LoadFromFile(string yamlPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yamlPath);
        if (!File.Exists(yamlPath))
        {
            throw new FileNotFoundException(
                $"routing-config.yaml not found at '{yamlPath}'. " +
                "ModelResolver requires this file (ADR-0026 §1).",
                yamlPath);
        }
        return LoadFromYaml(File.ReadAllText(yamlPath));
    }
}
