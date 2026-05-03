// =============================================================================
// Cena Platform — BagrutTaxonomyCatalog (ADR-0062 Phase 0)
//
// Closed-set canonicalizer for the math taxonomy published in
// `scripts/bagrut-taxonomy.json`. Concept extraction (ADR-0062 Phase 1)
// emits free-form skill suggestions; this catalog maps every accepted
// alias back to a single canonical `SkillCode` keyed on the taxonomy
// leaf so BKT mastery rows stay aligned with the catalog.
//
// What "canonicalize" means here:
//   - Input  : a string the LLM extractor / curator UI / older code emits.
//              Could be a conceptId ("CAL-003"), a track-relative leaf path
//              ("calculus.derivative_rules"), the existing TaxonomyCache
//              leaf id ("math_5u.calculus.derivative_rules"), or the
//              already-canonical SkillCode form ("math.calculus.derivative-rules").
//   - Output : (SkillCode, LeafEntry) so BKT keys on the SkillCode and
//              callers can read taxonomy metadata (Bloom range, track) off
//              the leaf.
//
// What this catalog DOES NOT do:
//   - Fuzzy matching of free-form English/Hebrew/Arabic text to leaves.
//     That's the LLM extractor's job in Phase 1; the canonicalizer
//     validates the result, it does not tag.
//   - Open-set behaviour. If `TryCanonicalize` rejects, the caller falls
//     back to `unlinked` (same pattern as
//     `ContentExtractorService.LinkConcepts`). Silent acceptance of
//     unknown skills would silently fork the catalog and pollute
//     mastery posteriors per ADR-0062 §Risks.
//
// Loader: walks up from `AppContext.BaseDirectory` to find the first
// `scripts/bagrut-taxonomy.json`. Mirrors the existing TaxonomyCache
// loader so dev / test / container all resolve the same file.
//
// Constructor accepts an `IReadOnlyList<LeafEntry>` so unit tests can
// build synthetic catalogs without touching disk.
// =============================================================================

using System.Text.Json;

namespace Cena.Actors.Mastery;

public sealed class BagrutTaxonomyCatalog
{
    /// <summary>One taxonomy leaf with its identifiers and Bloom metadata.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="TopicLabels"/> and <see cref="SubtopicLabels"/> carry display
    /// strings for the curator UI keyed by language code ("en" / "he" / "ar").
    /// English keys (e.g. <c>calculus.derivative_rules</c>) remain canonical
    /// for storage / event sourcing — labels are purely presentational and
    /// consumed only by the SPA via <see cref="ConceptCatalogEntry"/>.
    /// </para>
    /// <para>
    /// Both maps fall back to the English key when the language is missing
    /// (older taxonomy JSON without a labels block, or a new lang code added
    /// before translations land). The fallback happens in
    /// <see cref="LocalizedTopic"/> / <see cref="LocalizedSubtopic"/> so call
    /// sites don't have to write the same defensive lookup.
    /// </para>
    /// </remarks>
    public sealed record LeafEntry(
        SkillCode SkillCode,                // canonical SkillCode (e.g. math.calculus.derivative-rules)
        string ConceptId,                   // taxonomy seed id (e.g. "CAL-003")
        string TrackId,                     // "math_5u" / "math_4u" / "math_3u"
        string TopicKey,                    // "calculus"
        string SubtopicKey,                 // "derivative_rules"
        int BloomMin,
        int BloomMax,
        IReadOnlyDictionary<string, string> TopicLabels,    // { "en": "Calculus", "he": "...", "ar": "..." }
        IReadOnlyDictionary<string, string> SubtopicLabels) // { "en": "Derivative rules", "he": "...", "ar": "..." }
    {
        /// <summary>Track-relative leaf path: "calculus.derivative_rules".</summary>
        public string TrackRelativePath => $"{TopicKey}.{SubtopicKey}";

        /// <summary>Full leaf id used by the existing TaxonomyCache: "math_5u.calculus.derivative_rules".</summary>
        public string LeafId => $"{TrackId}.{TopicKey}.{SubtopicKey}";

        /// <summary>
        /// Topic display label for <paramref name="lang"/> (e.g. "he"); falls
        /// back to the English label if the language is missing, then to the
        /// English key as a last resort. Never returns null/empty.
        /// </summary>
        public string LocalizedTopic(string lang)
            => Lookup(TopicLabels, lang) ?? TopicKey;

        /// <summary>
        /// Subtopic display label for <paramref name="lang"/> (e.g. "ar");
        /// falls back to English then the underlying subtopic key. Never
        /// returns null/empty.
        /// </summary>
        public string LocalizedSubtopic(string lang)
            => Lookup(SubtopicLabels, lang) ?? SubtopicKey;

        private static string? Lookup(IReadOnlyDictionary<string, string> labels, string lang)
        {
            if (labels.TryGetValue(lang, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
            if (labels.TryGetValue("en", out var en) && !string.IsNullOrWhiteSpace(en)) return en;
            return null;
        }
    }

    private readonly IReadOnlyList<LeafEntry> _allLeaves;
    // Multi-leaf per alias: ADR-0050 SkillCode intentionally collapses
    // across tracks, so the same conceptId / SkillCode / track-relative
    // path resolves to multiple LeafEntries (one per track that contains
    // the skill). Only the full LeafId is track-unique; everything else
    // is potentially many-to-one.
    private readonly Dictionary<string, List<LeafEntry>> _byAlias;

    /// <summary>All leaves in the taxonomy. Stable order: by track then topic then subtopic.</summary>
    public IReadOnlyList<LeafEntry> AllLeaves => _allLeaves;

    public BagrutTaxonomyCatalog(IReadOnlyList<LeafEntry> leaves)
    {
        ArgumentNullException.ThrowIfNull(leaves);

        // Stable sort so AllLeaves is deterministic regardless of dictionary
        // enumeration order coming out of System.Text.Json on different
        // runtimes.
        _allLeaves = leaves
            .OrderBy(l => l.TrackId, StringComparer.Ordinal)
            .ThenBy(l => l.TopicKey, StringComparer.Ordinal)
            .ThenBy(l => l.SubtopicKey, StringComparer.Ordinal)
            .ToList();

        // Build the alias index. Each leaf is reachable via several keys.
        // Cross-track sharing is expected (ALG-004 lives in 5u + 4u + 3u
        // and they share a SkillCode), so the index stores a list and the
        // lookup picks via track hint or 5u→4u→3u fallback.
        _byAlias = new Dictionary<string, List<LeafEntry>>(StringComparer.Ordinal);
        foreach (var leaf in _allLeaves)
        {
            AddAlias(NormaliseLookupKey(leaf.ConceptId), leaf);              // "cal-003"
            AddAlias(NormaliseLookupKey(leaf.LeafId), leaf);                 // "math_5u.calculus.derivative_rules"
            AddAlias(NormaliseLookupKey(leaf.TrackRelativePath), leaf);      // "calculus.derivative_rules"
            AddAlias(NormaliseLookupKey(leaf.SkillCode.Value), leaf);        // "math.calculus.derivative-rules"
        }
    }

    private void AddAlias(string key, LeafEntry leaf)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (!_byAlias.TryGetValue(key, out var bucket))
        {
            bucket = new List<LeafEntry>(3);
            _byAlias[key] = bucket;
        }
        // Don't dedupe-by-reference; it's harmless to have a leaf appear
        // multiple times in its own bucket since the picker just looks at
        // TrackId.
        bucket.Add(leaf);
    }

    /// <summary>
    /// Map a free-form skill string to the canonical SkillCode + leaf,
    /// or return false when the input doesn't match any known alias.
    /// Callers should fall back to "unlinked" on false return rather than
    /// inventing a new SkillCode (see ADR-0062 §Risks).
    ///
    /// Track-relative paths ("calculus.derivative_rules") match across all
    /// tracks. When the same topic.subtopic exists in multiple tracks,
    /// pass <paramref name="trackHint"/> ("math_5u" / "math_4u" / "math_3u")
    /// to disambiguate. When trackHint is null and multiple tracks match,
    /// the 5u variant wins by convention (most permissive depth — Phase 1
    /// extraction always has a track from the variant being generated, so
    /// this fallback is only relevant for ad-hoc lookups).
    /// </summary>
    public bool TryCanonicalize(string? raw, string? trackHint, out SkillCode skillCode, out LeafEntry? leaf)
    {
        skillCode = default;
        leaf      = null;

        if (string.IsNullOrWhiteSpace(raw)) return false;

        var key = NormaliseLookupKey(raw);

        // Direct hit. Single bucket may contain leaves from multiple
        // tracks (the same SkillCode / ConceptId is typically shared
        // across 3u/4u/5u). Pick by track hint, falling back to
        // 5u → 4u → 3u when no hint is given.
        if (_byAlias.TryGetValue(key, out var bucket) && bucket.Count > 0)
        {
            var picked = PickByTrack(bucket, trackHint);
            if (picked is not null)
            {
                skillCode = picked.SkillCode;
                leaf      = picked;
                return true;
            }
            // Bucket exists but no leaf matches the hint AND fallback also
            // produced nothing — fall through to track-prefix lookup.
        }

        // Track-prefix lookup: callers passing a track-relative path with
        // an explicit trackHint hit this path. Even though the bucket
        // above should already contain those leaves, this is defence in
        // depth for callers feeding "math_4u.algebra.quadratic_equations"
        // directly (which hits the LeafId alias and is unique by track).
        if (trackHint is not null)
        {
            var prefixed = $"{trackHint}.{key}";
            if (_byAlias.TryGetValue(prefixed, out var prefBucket) && prefBucket.Count > 0)
            {
                var picked = prefBucket.FirstOrDefault(l =>
                    string.Equals(l.TrackId, trackHint, StringComparison.Ordinal))
                    ?? prefBucket[0];
                skillCode = picked.SkillCode;
                leaf      = picked;
                return true;
            }
        }
        else
        {
            // No hint: try each track-prefixed form in fallback order.
            foreach (var track in TrackFallbackOrder)
            {
                var prefixed = $"{track}.{key}";
                if (_byAlias.TryGetValue(prefixed, out var prefBucket) && prefBucket.Count > 0)
                {
                    var picked = prefBucket[0];
                    skillCode = picked.SkillCode;
                    leaf      = picked;
                    return true;
                }
            }
        }

        return false;
    }

    private static readonly string[] TrackFallbackOrder = ["math_5u", "math_4u", "math_3u"];

    private static LeafEntry? PickByTrack(IReadOnlyList<LeafEntry> bucket, string? trackHint)
    {
        if (trackHint is not null)
        {
            return bucket.FirstOrDefault(l =>
                string.Equals(l.TrackId, trackHint, StringComparison.Ordinal));
        }
        // No hint: try 5u → 4u → 3u.
        foreach (var track in TrackFallbackOrder)
        {
            var hit = bucket.FirstOrDefault(l =>
                string.Equals(l.TrackId, track, StringComparison.Ordinal));
            if (hit is not null) return hit;
        }
        return bucket.Count > 0 ? bucket[0] : null;
    }

    /// <summary>Convenience overload with no track hint.</summary>
    public bool TryCanonicalize(string? raw, out SkillCode skillCode, out LeafEntry? leaf)
        => TryCanonicalize(raw, trackHint: null, out skillCode, out leaf);

    /// <summary>
    /// Normalise an arbitrary alias to the dictionary lookup form: lower-case,
    /// trimmed, with underscores left intact. Hyphens and underscores both
    /// occur in the wild (the taxonomy uses underscores; SkillCode uses
    /// hyphens) so the normaliser folds them to a single canonical
    /// representation by replacing `-` → `_`. The canonical SkillCode form
    /// itself round-trips because we register both alias spellings on every
    /// leaf.
    /// </summary>
    private static string NormaliseLookupKey(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return raw.Trim().ToLowerInvariant().Replace('-', '_');
    }

    // -----------------------------------------------------------------------
    // Loader
    // -----------------------------------------------------------------------

    /// <summary>
    /// Load + parse `scripts/bagrut-taxonomy.json` from a file path, or
    /// from the conventional location relative to the running binary.
    /// </summary>
    public static BagrutTaxonomyCatalog LoadFromDisk(string? path = null)
    {
        path ??= ResolveDefaultPath();
        var json = File.ReadAllText(path);
        return Parse(json);
    }

    /// <summary>
    /// Parse the taxonomy JSON shape produced by `scripts/bagrut-taxonomy.json`.
    /// Exposed for unit tests that want to feed in a synthetic catalog.
    /// </summary>
    public static BagrutTaxonomyCatalog Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("tracks", out var tracksJson)
            || tracksJson.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "bagrut-taxonomy.json: missing or invalid 'tracks' object");
        }

        // Display-label maps — taxonomy v1.1+ ships an optional `labels`
        // block. Older taxonomy JSON (or synthetic test catalogs) omits it,
        // in which case we hand each leaf an EN-only fallback derived from
        // the English key so LocalizedTopic / LocalizedSubtopic still
        // return readable strings.
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> topicLabels =
            ReadLabelMap(root, "topics");
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> subtopicLabels =
            ReadLabelMap(root, "subtopics");

        var leaves = new List<LeafEntry>(80);
        foreach (var trackProperty in tracksJson.EnumerateObject())
        {
            var trackId = trackProperty.Name;
            if (!trackProperty.Value.TryGetProperty("topics", out var topicsJson))
                continue;

            foreach (var topicProperty in topicsJson.EnumerateObject())
            {
                var topicKey = topicProperty.Name;
                if (!topicProperty.Value.TryGetProperty("subtopics", out var subtopicsJson))
                    continue;

                foreach (var subProperty in subtopicsJson.EnumerateObject())
                {
                    var subKey   = subProperty.Name;
                    var subValue = subProperty.Value;
                    var conceptId = subValue.TryGetProperty("conceptId", out var c)
                        ? (c.GetString() ?? "")
                        : "";

                    int bloomMin = 1, bloomMax = 6;
                    if (subValue.TryGetProperty("bloom_range", out var bloom)
                        && bloom.ValueKind == JsonValueKind.Array
                        && bloom.GetArrayLength() == 2)
                    {
                        bloomMin = bloom[0].GetInt32();
                        bloomMax = bloom[1].GetInt32();
                    }

                    // Build the canonical SkillCode for the leaf:
                    //   math.<topic>.<subtopic-with-hyphens>
                    // Track is intentionally NOT in the SkillCode — per
                    // ADR-0050, mastery is keyed on (student, examTarget,
                    // skill) and the examTarget carries track. SkillCode
                    // collapses 3u/4u/5u for the same skill into one row.
                    var skillSlug = $"math.{topicKey}.{subKey.Replace('_', '-')}".ToLowerInvariant();
                    var skillCode = SkillCode.Parse(skillSlug);

                    var topicLbl = topicLabels.TryGetValue(topicKey, out var tl)
                        ? tl
                        : EnglishOnlyFallback(topicKey);
                    var subLbl = subtopicLabels.TryGetValue(subKey, out var sl)
                        ? sl
                        : EnglishOnlyFallback(subKey);

                    leaves.Add(new LeafEntry(
                        SkillCode:      skillCode,
                        ConceptId:      conceptId,
                        TrackId:        trackId,
                        TopicKey:       topicKey,
                        SubtopicKey:    subKey,
                        BloomMin:       bloomMin,
                        BloomMax:       bloomMax,
                        TopicLabels:    topicLbl,
                        SubtopicLabels: subLbl));
                }
            }
        }

        return new BagrutTaxonomyCatalog(leaves);
    }

    /// <summary>
    /// Read the optional <c>labels.{section}</c> map from the taxonomy
    /// JSON. Section is "topics" or "subtopics". Returns an empty map
    /// when the labels block is absent so callers fall through to the
    /// English-key fallback.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>
        ReadLabelMap(JsonElement root, string section)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        if (!root.TryGetProperty("labels", out var labelsJson)
            || labelsJson.ValueKind != JsonValueKind.Object)
        {
            return result;
        }
        if (!labelsJson.TryGetProperty(section, out var sectionJson)
            || sectionJson.ValueKind != JsonValueKind.Object)
        {
            return result;
        }
        foreach (var keyProp in sectionJson.EnumerateObject())
        {
            if (keyProp.Value.ValueKind != JsonValueKind.Object) continue;
            var langMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var langProp in keyProp.Value.EnumerateObject())
            {
                if (langProp.Value.ValueKind == JsonValueKind.String)
                {
                    var s = langProp.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                        langMap[langProp.Name] = s;
                }
            }
            if (langMap.Count > 0)
                result[keyProp.Name] = langMap;
        }
        return result;
    }

    /// <summary>
    /// Build an English-only label dictionary by humanising the key
    /// ("derivative_rules" → "Derivative rules"). Used as the fallback
    /// when the taxonomy JSON has no labels block (older test fixtures).
    /// </summary>
    private static IReadOnlyDictionary<string, string> EnglishOnlyFallback(string key)
    {
        // Replace underscores with spaces, capitalise first letter only —
        // Title Case isn't appropriate for "of" / "and" but for fallback
        // text this is good enough; the real translations live in the
        // labels block.
        var spaced = key.Replace('_', ' ').Trim();
        if (spaced.Length == 0)
            return new Dictionary<string, string>(StringComparer.Ordinal) { ["en"] = key };
        var humanised = char.ToUpperInvariant(spaced[0]) + spaced[1..];
        return new Dictionary<string, string>(StringComparer.Ordinal) { ["en"] = humanised };
    }

    private static string ResolveDefaultPath()
    {
        // Walk up from the running binary until we find scripts/bagrut-taxonomy.json.
        // Matches the existing TaxonomyCache.ResolveDefaultPath convention.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "bagrut-taxonomy.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "bagrut-taxonomy.json not found. Expected at <repo>/scripts/bagrut-taxonomy.json. "
            + "Set CENA_TAXONOMY_PATH to override (TODO).");
    }
}
