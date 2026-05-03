// =============================================================================
// Cena Platform — Coverage Target Manifest (prr-209 / prr-210 reader)
//
// In-process loader for contracts/coverage/coverage-targets.yml — the single
// source of truth for the per-cell SLO that prr-210 enforces in CI and that
// prr-209 surfaces in the admin heatmap.
//
// The shape mirrors the node parser in scripts/shipgate/coverage-slo.mjs and
// the xUnit parser in src/actors/Cena.Actors.Tests/Architecture/
// CoverageSloGateTest.cs so runtime + CI + architecture tests agree on which
// cells are declared and what N they require. A custom minimal parser is
// intentional — pulling YamlDotNet into Cena.Actors for a 150-line contract
// file would be overkill and contradicts the "keep files under 500 lines"
// non-negotiable.
//
// Precedence when resolving required N for a cell:
//   1. The `min:` field on the literal match if present.
//   2. `defaults.questionType[<qt>]` if the cell's question-type is listed.
//   3. `defaults.methodology[<methodology>]` if the methodology is listed.
//   4. `defaults.global.min`.
//
// Loader is resilient: it returns a structured `CoverageTargetManifest` that
// the heatmap service treats as the authoritative target list. Cells that
// show up in the live variant snapshot but are not declared here are
// surfaced as `undeclared` in the response so admins can chase them down.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.QuestionBank.Coverage;

/// <summary>
/// Resolves per-cell SLO targets from the coverage-targets.yml contract.
/// The manifest is immutable once loaded; re-load by calling <see cref="Load"/>.
/// </summary>
public interface ICoverageTargetManifestProvider
{
    CoverageTargetManifest Current { get; }
}

/// <summary>
/// Parsed view of <c>contracts/coverage/coverage-targets.yml</c>. Cells are
/// indexed by <see cref="CoverageCell.Address"/> so heatmap lookups are O(1).
/// </summary>
public sealed class CoverageTargetManifest
{
    public int Version { get; }
    public int GlobalMin { get; }
    public IReadOnlyDictionary<string, int> MethodologyDefaults { get; }
    public IReadOnlyDictionary<string, int> QuestionTypeDefaults { get; }
    public IReadOnlyList<CoverageTargetCell> Cells { get; }
    private readonly IReadOnlyDictionary<string, CoverageTargetCell> _byAddress;

    public CoverageTargetManifest(
        int version,
        int globalMin,
        IReadOnlyDictionary<string, int> methodologyDefaults,
        IReadOnlyDictionary<string, int> questionTypeDefaults,
        IReadOnlyList<CoverageTargetCell> cells)
    {
        Version = version;
        GlobalMin = globalMin;
        MethodologyDefaults = methodologyDefaults ?? new Dictionary<string, int>();
        QuestionTypeDefaults = questionTypeDefaults ?? new Dictionary<string, int>();
        Cells = cells ?? Array.Empty<CoverageTargetCell>();

        // Index by the non-subject composite key — snapshot rows and shipgate
        // contract both key on (topic, difficulty, methodology, track,
        // questionType, language) so the heatmap joins the live counter's
        // CoverageCell entries on the same 6-tuple while ignoring subject.
        var map = new Dictionary<string, CoverageTargetCell>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in Cells)
        {
            map[c.MatchKey] = c;
        }
        _byAddress = map;
    }

    /// <summary>
    /// Lookup a target by the composite match key
    /// <c>topic/difficulty/methodology/track/questionType/language</c>. The
    /// shape matches the snapshot file + shipgate script so every audit path
    /// agrees on which target applies to a given snapshot row.
    /// </summary>
    public CoverageTargetCell? FindByMatchKey(string matchKey)
        => _byAddress.TryGetValue(matchKey, out var c) ? c : null;

    /// <summary>
    /// Build the composite match key for a live <see cref="CoverageCell"/>.
    /// Dropping the subject axis aligns the C# runtime view with the
    /// contract in <c>contracts/coverage/coverage-targets.yml</c> which
    /// keys on the 6 non-subject axes.
    /// </summary>
    public static string MatchKeyFor(CoverageCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return string.Join(
            '/',
            cell.Topic,
            cell.Difficulty.ToString(),
            cell.Methodology.ToString(),
            cell.Track.ToString(),
            cell.QuestionType,
            cell.Language ?? "en");
    }

    /// <summary>
    /// Resolve the required N for a cell according to the documented precedence.
    /// Falls back to <see cref="GlobalMin"/> when no override matches.
    /// </summary>
    public int ResolveRequiredN(CoverageTargetCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (cell.Min.HasValue) return cell.Min.Value;
        if (QuestionTypeDefaults.TryGetValue(cell.QuestionType, out var qt)) return qt;
        if (MethodologyDefaults.TryGetValue(cell.Methodology, out var m)) return m;
        return GlobalMin;
    }

    /// <summary>
    /// Resolve the required N for a live cell. Returns
    /// <see cref="GlobalMin"/> when the cell is not declared so callers can
    /// surface "undeclared but tracked" rungs without crashing.
    /// </summary>
    public int ResolveRequiredN(CoverageCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        var target = FindByMatchKey(MatchKeyFor(cell));
        return target is null ? GlobalMin : ResolveRequiredN(target);
    }

    /// <summary>
    /// Try find the declared target for a live cell.
    /// </summary>
    public CoverageTargetCell? FindFor(CoverageCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return FindByMatchKey(MatchKeyFor(cell));
    }

    /// <summary>
    /// Load the manifest from the given path. Throws
    /// <see cref="FileNotFoundException"/> when the file is missing and
    /// <see cref="InvalidDataException"/> when parsing fails.
    /// </summary>
    public static CoverageTargetManifest Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("path required", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException($"coverage-targets.yml not found at {path}", path);

        var yaml = File.ReadAllText(path);
        return Parse(yaml);
    }

    /// <summary>
    /// Parse an in-memory YAML string. Public for tests that want to feed
    /// fixture strings without touching the filesystem.
    /// </summary>
    public static CoverageTargetManifest Parse(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var lines = yaml.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        int version = 0;
        int globalMin = 0;
        var meth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var qt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var cells = new List<CoverageTargetCell>();

        string? section = null;
        CellBuilder? current = null;
        bool inDefaultsGlobal = false;
        bool inDefaultsMethodology = false;
        bool inDefaultsQuestionType = false;

        foreach (var raw in lines)
        {
            var line = StripComment(raw);
            if (string.IsNullOrWhiteSpace(line)) continue;

            var indent = line.Length - line.TrimStart(' ').Length;
            var trimmed = line.Trim();

            if (indent == 0)
            {
                // Flush any in-progress cell before switching sections.
                if (current is not null)
                {
                    cells.Add(current.Build());
                    current = null;
                }
                section = null;
                inDefaultsGlobal = inDefaultsMethodology = inDefaultsQuestionType = false;

                var m = TopLevel.Match(trimmed);
                if (!m.Success) continue;
                var key = m.Groups[1].Value;
                var val = m.Groups[2].Value.Trim();

                if (string.Equals(key, "version", StringComparison.Ordinal))
                {
                    if (!int.TryParse(val, out version))
                        throw new InvalidDataException("coverage-targets.yml: non-integer version");
                }
                else if (string.Equals(key, "defaults", StringComparison.Ordinal))
                {
                    section = "defaults";
                }
                else if (string.Equals(key, "cells", StringComparison.Ordinal))
                {
                    section = "cells";
                }
                continue;
            }

            if (section == "defaults")
            {
                if (indent == 2)
                {
                    inDefaultsGlobal = trimmed.StartsWith("global:", StringComparison.Ordinal);
                    inDefaultsMethodology = trimmed.StartsWith("methodology:", StringComparison.Ordinal);
                    inDefaultsQuestionType = trimmed.StartsWith("questionType:", StringComparison.Ordinal);
                    continue;
                }
                if (indent >= 4)
                {
                    var m = KvInt.Match(trimmed);
                    if (!m.Success) continue;
                    var key = m.Groups[1].Value;
                    var val = int.Parse(m.Groups[2].Value);
                    if (inDefaultsGlobal && string.Equals(key, "min", StringComparison.Ordinal))
                        globalMin = val;
                    else if (inDefaultsMethodology)
                        meth[key] = val;
                    else if (inDefaultsQuestionType)
                        qt[key] = val;
                }
                continue;
            }

            if (section == "cells")
            {
                if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    if (current is not null) cells.Add(current.Build());
                    current = new CellBuilder();
                    ApplyKv(current, trimmed[2..].Trim());
                    continue;
                }
                if (current is not null)
                {
                    ApplyKv(current, trimmed);
                }
            }
        }

        if (current is not null) cells.Add(current.Build());

        if (globalMin <= 0)
            throw new InvalidDataException("coverage-targets.yml: defaults.global.min must be > 0");

        return new CoverageTargetManifest(version, globalMin, meth, qt, cells);
    }

    private static string StripComment(string l)
    {
        var idx = l.IndexOf('#', StringComparison.Ordinal);
        return idx < 0 ? l : l[..idx];
    }

    private static void ApplyKv(CellBuilder c, string kv)
    {
        var m = KvAny.Match(kv);
        if (!m.Success) return;
        var key = m.Groups[1].Value;
        var raw = m.Groups[2].Value.Trim();

        // Strip matched quotes — schema uses bare strings but be defensive.
        if ((raw.StartsWith('"') && raw.EndsWith('"')) ||
            (raw.StartsWith('\'') && raw.EndsWith('\'')))
        {
            raw = raw.Substring(1, raw.Length - 2);
        }

        switch (key)
        {
            case "topic": c.Topic = raw; break;
            case "difficulty": c.Difficulty = raw; break;
            case "methodology": c.Methodology = raw; break;
            case "track": c.Track = raw; break;
            case "questionType": c.QuestionType = raw; break;
            case "language": c.Language = raw; break;
            case "min":
                if (int.TryParse(raw, out var n)) c.Min = n;
                break;
            case "active":
                c.Active = string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
                break;
            case "notes": c.Notes = raw; break;
        }
    }

    private static readonly Regex TopLevel = new(
        @"^([A-Za-z_][\w\-.]*):\s*(.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex KvInt = new(
        @"^([A-Za-z][\w\-]*):\s*(\d+)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex KvAny = new(
        @"^([A-Za-z][\w\-]*):\s*(.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private sealed class CellBuilder
    {
        public string Topic = "";
        public string Difficulty = "";
        public string Methodology = "";
        public string Track = "";
        public string QuestionType = "";
        public string Language = "en";
        public int? Min;
        public bool Active;
        public string? Notes;

        public CoverageTargetCell Build() => new(
            Topic, Difficulty, Methodology, Track, QuestionType, Language, Min, Active, Notes);
    }
}

/// <summary>
/// One declared cell in the coverage target contract.
/// </summary>
public sealed record CoverageTargetCell(
    string Topic,
    string Difficulty,
    string Methodology,
    string Track,
    string QuestionType,
    string Language,
    int? Min,
    bool Active,
    string? Notes)
{
    /// <summary>
    /// Composite match key shared with the snapshot contract and the
    /// shipgate script:
    ///   topic/difficulty/methodology/track/questionType/language
    ///
    /// Case-preserving — snapshot rows use "Halabi" / "FourUnit" etc., so we
    /// keep the declared casing. Lookups are case-insensitive via the
    /// dictionary comparer.
    /// </summary>
    public string MatchKey => string.Join(
        '/', Topic, Difficulty, Methodology, Track, QuestionType, Language);
}

/// <summary>
/// File-backed provider used in production wiring. Loads once at
/// construction; hosts that want live reloads re-register this provider.
/// </summary>
public sealed class FileCoverageTargetManifestProvider : ICoverageTargetManifestProvider
{
    public CoverageTargetManifest Current { get; }

    public FileCoverageTargetManifestProvider(string path)
    {
        Current = CoverageTargetManifest.Load(path);
    }

    public FileCoverageTargetManifestProvider(CoverageTargetManifest preloaded)
    {
        Current = preloaded ?? throw new ArgumentNullException(nameof(preloaded));
    }
}

/// <summary>
/// In-memory provider for tests and local development — wrap a parsed
/// manifest without touching disk.
/// </summary>
public sealed class InMemoryCoverageTargetManifestProvider : ICoverageTargetManifestProvider
{
    public CoverageTargetManifest Current { get; }

    public InMemoryCoverageTargetManifestProvider(CoverageTargetManifest manifest)
    {
        Current = manifest ?? throw new ArgumentNullException(nameof(manifest));
    }
}
