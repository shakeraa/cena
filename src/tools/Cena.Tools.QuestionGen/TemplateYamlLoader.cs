// =============================================================================
// Cena Platform — Parametric Template YAML Loader (prr-200 CLI)
//
// Small YAML → ParametricTemplate converter. Uses YamlDotNet for parsing.
// Schema is the JSON Schema shipped at contracts/questions/parametric-template.schema.json;
// the YAML representation is a 1-to-1 transcription of that JSON shape.
// =============================================================================

using Cena.Actors.QuestionBank.Templates;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cena.Tools.QuestionGen;

internal static class TemplateYamlLoader
{
    public static ParametricTemplate LoadFromFile(string path)
    {
        var text = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var doc = deserializer.Deserialize<YamlTemplateDoc>(text)
            ?? throw new InvalidDataException($"Could not parse template YAML at {path}");

        return ToDomain(doc);
    }

    private static ParametricTemplate ToDomain(YamlTemplateDoc d)
    {
        var slots = (d.Slots ?? new()).Select(MapSlot).ToArray();
        var constraints = (d.Constraints ?? new())
            .Select(c => new SlotConstraint(c.Description ?? "", c.PredicateExpr ?? ""))
            .ToArray();
        var distractors = (d.DistractorRules ?? new())
            .Select(r => new DistractorRule(r.MisconceptionId ?? "", r.FormulaExpr ?? "", r.LabelHint))
            .ToArray();

        return new ParametricTemplate
        {
            Id = d.Id ?? throw new ArgumentException("template id required"),
            Version = d.Version < 1 ? 1 : d.Version,
            Subject = d.Subject ?? "math",
            Topic = d.Topic ?? "unspecified",
            Track = ParseTrack(d.Track),
            Difficulty = ParseDifficulty(d.Difficulty),
            Methodology = ParseMethodology(d.Methodology),
            BloomsLevel = d.BloomsLevel == 0 ? 3 : d.BloomsLevel,
            Language = string.IsNullOrEmpty(d.Language) ? "en" : d.Language!,
            StemTemplate = d.StemTemplate ?? throw new ArgumentException("stemTemplate required"),
            SolutionExpr = d.SolutionExpr ?? throw new ArgumentException("solutionExpr required"),
            VariableName = d.VariableName,
            AcceptShapes = ParseAcceptShapes(d.AcceptShapes),
            Slots = slots,
            Constraints = constraints,
            DistractorRules = distractors
        };
    }

    private static ParametricSlot MapSlot(YamlSlot s)
    {
        var name = s.Name ?? throw new ArgumentException("slot name required");
        return (s.Kind ?? "").ToLowerInvariant() switch
        {
            "integer" => new ParametricSlot
            {
                Name = name, Kind = ParametricSlotKind.Integer,
                IntegerMin = s.IntegerMin, IntegerMax = s.IntegerMax,
                IntegerExclude = s.IntegerExclude ?? new List<int>()
            },
            "rational" => new ParametricSlot
            {
                Name = name, Kind = ParametricSlotKind.Rational,
                NumeratorMin = s.NumeratorMin, NumeratorMax = s.NumeratorMax,
                DenominatorMin = s.DenominatorMin == 0 ? 1 : s.DenominatorMin,
                DenominatorMax = s.DenominatorMax == 0 ? 1 : s.DenominatorMax,
                ReduceRational = s.ReduceRational ?? true
            },
            "choice" => new ParametricSlot
            {
                Name = name, Kind = ParametricSlotKind.Choice,
                Choices = s.Choices ?? new List<string>()
            },
            _ => throw new ArgumentException($"Unknown slot kind '{s.Kind}' on slot '{name}'")
        };
    }

    private static TemplateTrack ParseTrack(string? v) => (v ?? "").ToLowerInvariant() switch
    {
        "fourunit" or "four_unit" or "4unit" or "4-unit" or "4" => TemplateTrack.FourUnit,
        "fiveunit" or "five_unit" or "5unit" or "5-unit" or "5" => TemplateTrack.FiveUnit,
        _ => throw new ArgumentException($"Unknown track '{v}'")
    };

    private static TemplateDifficulty ParseDifficulty(string? v) => (v ?? "").ToLowerInvariant() switch
    {
        "easy" => TemplateDifficulty.Easy,
        "medium" => TemplateDifficulty.Medium,
        "hard" => TemplateDifficulty.Hard,
        _ => throw new ArgumentException($"Unknown difficulty '{v}'")
    };

    private static TemplateMethodology ParseMethodology(string? v) => (v ?? "").ToLowerInvariant() switch
    {
        "halabi" => TemplateMethodology.Halabi,
        "rabinovitch" => TemplateMethodology.Rabinovitch,
        _ => throw new ArgumentException($"Unknown methodology '{v}'")
    };

    private static AcceptShape ParseAcceptShapes(List<string>? shapes)
    {
        if (shapes is null || shapes.Count == 0) return AcceptShape.Any;
        AcceptShape r = AcceptShape.None;
        foreach (var s in shapes)
        {
            r |= (s ?? "").ToLowerInvariant() switch
            {
                "integer" => AcceptShape.Integer,
                "rational" => AcceptShape.Rational,
                "decimal" => AcceptShape.Decimal,
                "symbolic" => AcceptShape.Symbolic,
                "any" => AcceptShape.Any,
                _ => throw new ArgumentException($"Unknown accept-shape '{s}'")
            };
        }
        return r;
    }

    // ── YAML DTOs (private to this loader) ───────────────────────────────

    private sealed class YamlTemplateDoc
    {
        public string? Id { get; set; }
        public int Version { get; set; } = 1;
        public string? Subject { get; set; }
        public string? Topic { get; set; }
        public string? Track { get; set; }
        public string? Difficulty { get; set; }
        public string? Methodology { get; set; }
        public int BloomsLevel { get; set; }
        public string? Language { get; set; }
        public string? StemTemplate { get; set; }
        public string? SolutionExpr { get; set; }
        public string? VariableName { get; set; }
        public List<string>? AcceptShapes { get; set; }
        public List<YamlSlot>? Slots { get; set; }
        public List<YamlConstraint>? Constraints { get; set; }
        public List<YamlDistractorRule>? DistractorRules { get; set; }
    }

    private sealed class YamlSlot
    {
        public string? Name { get; set; }
        public string? Kind { get; set; }
        public int IntegerMin { get; set; }
        public int IntegerMax { get; set; }
        public List<int>? IntegerExclude { get; set; }
        public int NumeratorMin { get; set; }
        public int NumeratorMax { get; set; }
        public int DenominatorMin { get; set; }
        public int DenominatorMax { get; set; }
        public bool? ReduceRational { get; set; }
        public List<string>? Choices { get; set; }
    }

    private sealed class YamlConstraint
    {
        public string? Description { get; set; }
        public string? PredicateExpr { get; set; }
    }

    private sealed class YamlDistractorRule
    {
        public string? MisconceptionId { get; set; }
        public string? FormulaExpr { get; set; }
        public string? LabelHint { get; set; }
    }
}
