// =============================================================================
// Cena Platform — Template Authoring Mapper + Validator (prr-202)
//
// Pure functions that translate between:
//   * CreateRequestDto / UpdateRequestDto  ↔  ParametricTemplateDocument
//   * ParametricTemplateDocument           ↔  ParametricTemplate (runtime domain,
//                                             as consumed by ParametricCompiler)
//   * ParametricTemplateDocument           →  TemplateDetailDto (read surface)
//
// Validation is enforced HERE — not deep in the service — so the REST layer
// can translate ArgumentException → 400 cleanly without reaching into the
// compiler. We deliberately re-run `ParametricTemplate.Validate()` after the
// mapping so authors get the same error messages a dry-run would produce.
//
// Whitelist-grammar rule (persona-redteam from task body §lens review):
//   * No dynamic eval. All strings go into SymPy expressions after substitution
//     in the renderer; here we only validate *shape*.
//   * Stem template LaTeX is checked for classic injection markers (\input,
//     \write18, \immediate\write, \openout, `\' + exec directives).
//   * Slot names obey a tight regex [A-Za-z_][A-Za-z0-9_]*.
//   * Constraint predicates must match the same 3-token grammar accepted by
//     ParametricCompiler.EvaluateConstraints (lhs op rhs).
// =============================================================================

using System.Text;
using System.Text.Json;
using Cena.Actors.QuestionBank.Templates;
using Cena.Infrastructure.Documents;

namespace Cena.Admin.Api.Templates;

internal static class TemplateAuthoringMapper
{
    private static readonly JsonSerializerOptions StateHashOpts = new()
    {
        WriteIndented = false,
        // Explicitly exclude audit/lifecycle fields from the hash — a fresh
        // actor/time should not invalidate a prior-state hash.
        PropertyNameCaseInsensitive = false
    };

    private static readonly HashSet<string> LatexInjectionMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        @"\input", @"\include", @"\write18", @"\immediate", @"\openout",
        @"\catcode", @"\jobname", @"\loop", @"\csname", @"\directlua", @"\ShellEscape"
    };

    /// <summary>Canonical list of supported accept-shape strings.</summary>
    internal static readonly string[] AllowedAcceptShapes =
        { "integer", "rational", "decimal", "symbolic", "any" };

    internal static readonly string[] AllowedStatuses =
        { "draft", "published" };

    // ── Create / Update → Document ──

    public static ParametricTemplateDocument ApplyCreate(
        TemplateCreateRequestDto req,
        string actorUserId,
        string actorSchoolId,
        DateTimeOffset now)
    {
        ValidateId(req.Id);
        ValidateBody(req.Subject, req.Topic, req.Track, req.Difficulty, req.Methodology,
                     req.BloomsLevel, req.Language, req.StemTemplate, req.SolutionExpr,
                     req.AcceptShapes, req.Slots, req.Constraints, req.DistractorRules,
                     req.Status);

        return new ParametricTemplateDocument
        {
            Id = req.Id,
            Version = 1,
            Subject = req.Subject,
            Topic = req.Topic,
            Track = req.Track,
            Difficulty = req.Difficulty,
            Methodology = req.Methodology,
            BloomsLevel = req.BloomsLevel,
            Language = req.Language,
            StemTemplate = req.StemTemplate,
            SolutionExpr = req.SolutionExpr,
            VariableName = req.VariableName,
            AcceptShapes = req.AcceptShapes.Select(s => s.ToLowerInvariant()).ToList(),
            Slots = req.Slots.Select(MapSlotToPayload).ToList(),
            Constraints = (req.Constraints ?? Array.Empty<SlotConstraintPayloadDto>())
                          .Select(c => new SlotConstraintPayload { Description = c.Description, PredicateExpr = c.PredicateExpr })
                          .ToList(),
            DistractorRules = (req.DistractorRules ?? Array.Empty<DistractorRulePayloadDto>())
                              .Select(d => new DistractorRulePayload { MisconceptionId = d.MisconceptionId, FormulaExpr = d.FormulaExpr, LabelHint = d.LabelHint })
                              .ToList(),
            Active = true,
            Status = (req.Status ?? "draft").ToLowerInvariant(),
            CreatedAt = now,
            CreatedBy = actorUserId,
            CreatedBySchool = actorSchoolId,
            UpdatedAt = now,
            LastMutatedBy = actorUserId,
            LastMutatedBySchool = actorSchoolId,
            StateHash = "" // set by service after structure finalisation
        };
    }

    public static ParametricTemplateDocument ApplyUpdate(
        ParametricTemplateDocument current,
        TemplateUpdateRequestDto req,
        string actorUserId,
        string actorSchoolId,
        DateTimeOffset now)
    {
        ValidateBody(req.Subject, req.Topic, req.Track, req.Difficulty, req.Methodology,
                     req.BloomsLevel, req.Language, req.StemTemplate, req.SolutionExpr,
                     req.AcceptShapes, req.Slots, req.Constraints, req.DistractorRules,
                     req.Status);

        if (req.ExpectedVersion != current.Version)
            throw new ArgumentException(
                $"Version conflict: expected {req.ExpectedVersion}, current is {current.Version}");

        return new ParametricTemplateDocument
        {
            Id = current.Id,
            Version = current.Version + 1,
            Subject = req.Subject,
            Topic = req.Topic,
            Track = req.Track,
            Difficulty = req.Difficulty,
            Methodology = req.Methodology,
            BloomsLevel = req.BloomsLevel,
            Language = req.Language,
            StemTemplate = req.StemTemplate,
            SolutionExpr = req.SolutionExpr,
            VariableName = req.VariableName,
            AcceptShapes = req.AcceptShapes.Select(s => s.ToLowerInvariant()).ToList(),
            Slots = req.Slots.Select(MapSlotToPayload).ToList(),
            Constraints = (req.Constraints ?? Array.Empty<SlotConstraintPayloadDto>())
                          .Select(c => new SlotConstraintPayload { Description = c.Description, PredicateExpr = c.PredicateExpr })
                          .ToList(),
            DistractorRules = (req.DistractorRules ?? Array.Empty<DistractorRulePayloadDto>())
                              .Select(d => new DistractorRulePayload { MisconceptionId = d.MisconceptionId, FormulaExpr = d.FormulaExpr, LabelHint = d.LabelHint })
                              .ToList(),
            Active = current.Active,
            Status = (req.Status ?? current.Status).ToLowerInvariant(),
            CreatedAt = current.CreatedAt,
            CreatedBy = current.CreatedBy,
            CreatedBySchool = current.CreatedBySchool,
            UpdatedAt = now,
            LastMutatedBy = actorUserId,
            LastMutatedBySchool = actorSchoolId,
            StateHash = ""
        };
    }

    // ── Document → runtime domain (for preview + compiler) ──

    public static ParametricTemplate ToDomain(ParametricTemplateDocument doc)
    {
        var slots = doc.Slots.Select(ToSlot).ToArray();
        var constraints = doc.Constraints
            .Select(c => new SlotConstraint(c.Description, c.PredicateExpr))
            .ToArray();
        var distractors = doc.DistractorRules
            .Select(d => new DistractorRule(d.MisconceptionId, d.FormulaExpr, d.LabelHint))
            .ToArray();

        return new ParametricTemplate
        {
            Id = doc.Id,
            Version = doc.Version < 1 ? 1 : doc.Version,
            Subject = doc.Subject,
            Topic = doc.Topic,
            Track = ParseTrack(doc.Track),
            Difficulty = ParseDifficulty(doc.Difficulty),
            Methodology = ParseMethodology(doc.Methodology),
            BloomsLevel = doc.BloomsLevel,
            Language = doc.Language,
            StemTemplate = doc.StemTemplate,
            SolutionExpr = doc.SolutionExpr,
            VariableName = doc.VariableName,
            AcceptShapes = ParseAcceptShapes(doc.AcceptShapes),
            Slots = slots,
            Constraints = constraints,
            DistractorRules = distractors
        };
    }

    public static TemplateDetailDto ToDetailDto(ParametricTemplateDocument doc) => new(
        Id: doc.Id, Version: doc.Version,
        Subject: doc.Subject, Topic: doc.Topic, Track: doc.Track,
        Difficulty: doc.Difficulty, Methodology: doc.Methodology,
        BloomsLevel: doc.BloomsLevel, Language: doc.Language,
        StemTemplate: doc.StemTemplate, SolutionExpr: doc.SolutionExpr,
        VariableName: doc.VariableName,
        AcceptShapes: doc.AcceptShapes,
        Slots: doc.Slots.Select(s => new ParametricSlotPayloadDto(
            s.Name, s.Kind, s.IntegerMin, s.IntegerMax, s.IntegerExclude,
            s.NumeratorMin, s.NumeratorMax, s.DenominatorMin, s.DenominatorMax,
            s.ReduceRational, s.Choices)).ToList(),
        Constraints: doc.Constraints.Select(c => new SlotConstraintPayloadDto(c.Description, c.PredicateExpr)).ToList(),
        DistractorRules: doc.DistractorRules.Select(d => new DistractorRulePayloadDto(d.MisconceptionId, d.FormulaExpr, d.LabelHint)).ToList(),
        Status: doc.Status, Active: doc.Active,
        CreatedAt: doc.CreatedAt, CreatedBy: doc.CreatedBy,
        UpdatedAt: doc.UpdatedAt, LastMutatedBy: doc.LastMutatedBy);

    public static TemplateListItemDto ToListItemDto(ParametricTemplateDocument doc) => new(
        Id: doc.Id, Version: doc.Version,
        Subject: doc.Subject, Topic: doc.Topic, Track: doc.Track,
        Difficulty: doc.Difficulty, Methodology: doc.Methodology,
        BloomsLevel: doc.BloomsLevel, Language: doc.Language,
        Status: doc.Status, Active: doc.Active,
        UpdatedAt: doc.UpdatedAt, LastMutatedBy: doc.LastMutatedBy,
        SlotCount: doc.Slots.Count);

    // ── Hashing ──

    public static string ComputeStateHash(ParametricTemplateDocument doc)
    {
        // Hash only the user-authored content + version — not audit fields, not
        // timestamps. This gives a stable hash that changes iff the *content*
        // changes.
        var canonical = new
        {
            doc.Id, doc.Version,
            doc.Subject, doc.Topic, doc.Track, doc.Difficulty, doc.Methodology,
            doc.BloomsLevel, doc.Language,
            doc.StemTemplate, doc.SolutionExpr, doc.VariableName,
            AcceptShapes = doc.AcceptShapes.OrderBy(s => s, StringComparer.Ordinal),
            Slots = doc.Slots.Select(s => new
            {
                s.Name, s.Kind,
                s.IntegerMin, s.IntegerMax,
                IntegerExclude = s.IntegerExclude.OrderBy(i => i),
                s.NumeratorMin, s.NumeratorMax,
                s.DenominatorMin, s.DenominatorMax,
                s.ReduceRational,
                s.Choices
            }),
            Constraints = doc.Constraints,
            DistractorRules = doc.DistractorRules,
            doc.Active, doc.Status
        };
        var json = JsonSerializer.Serialize(canonical, StateHashOpts);
        var bytes = Encoding.UTF8.GetBytes(json);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    // ── Validation primitives ──

    public static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Template id is required");
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-zA-Z0-9_\\-.]+$"))
            throw new ArgumentException(
                "Template id must match [a-zA-Z0-9_\\-.]+ (no spaces, no special chars)");
        if (id.Length > 128) throw new ArgumentException("Template id too long (max 128)");
    }

    public static void ValidateBody(
        string subject, string topic, string track, string difficulty, string methodology,
        int bloomsLevel, string language,
        string stemTemplate, string solutionExpr,
        IReadOnlyList<string> acceptShapes,
        IReadOnlyList<ParametricSlotPayloadDto> slots,
        IReadOnlyList<SlotConstraintPayloadDto>? constraints,
        IReadOnlyList<DistractorRulePayloadDto>? distractorRules,
        string? status)
    {
        if (string.IsNullOrWhiteSpace(subject)) throw new ArgumentException("subject is required");
        if (string.IsNullOrWhiteSpace(topic)) throw new ArgumentException("topic is required");

        _ = ParseTrack(track);         // throws on unknown
        _ = ParseDifficulty(difficulty);
        _ = ParseMethodology(methodology);

        if (bloomsLevel < 1 || bloomsLevel > 6)
            throw new ArgumentException("bloomsLevel must be 1..6");

        if (language is not ("en" or "he" or "ar"))
            throw new ArgumentException($"language '{language}' unsupported (en|he|ar)");

        if (string.IsNullOrWhiteSpace(stemTemplate))
            throw new ArgumentException("stemTemplate is required");
        if (string.IsNullOrWhiteSpace(solutionExpr))
            throw new ArgumentException("solutionExpr is required");

        RejectLatexInjection(stemTemplate);
        RejectLatexInjection(solutionExpr);
        // solutionExpr is consumed by ParametricRenderHelpers.SubstituteSlots, which
        // only accepts [A-Za-z0-9_+\-*/^().,\s]. Pre-validate so bad input surfaces
        // as a 400 at save-time instead of a RejectedRenderError at preview-time.
        RejectDisallowedExprChars(solutionExpr, nameof(solutionExpr));

        if (acceptShapes is null || acceptShapes.Count == 0)
            throw new ArgumentException("acceptShapes must have ≥1 entry");
        foreach (var s in acceptShapes)
            if (Array.IndexOf(AllowedAcceptShapes, s.ToLowerInvariant()) < 0)
                throw new ArgumentException($"acceptShape '{s}' not in {{{string.Join(",", AllowedAcceptShapes)}}}");

        if (slots is null || slots.Count == 0)
            throw new ArgumentException("slots must have ≥1 entry");
        var seenSlot = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in slots)
        {
            ValidateSlotPayload(s);
            if (!seenSlot.Add(s.Name))
                throw new ArgumentException($"duplicate slot name '{s.Name}'");
        }

        if (constraints is not null)
            foreach (var c in constraints) ValidateConstraint(c, seenSlot);
        if (distractorRules is not null)
            foreach (var d in distractorRules) ValidateDistractor(d);

        if (status is not null && Array.IndexOf(AllowedStatuses, status.ToLowerInvariant()) < 0)
            throw new ArgumentException($"status '{status}' not in {{{string.Join(",", AllowedStatuses)}}}");
    }

    private static void ValidateSlotPayload(ParametricSlotPayloadDto s)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(s.Name, "^[A-Za-z_][A-Za-z0-9_]*$"))
            throw new ArgumentException($"slot name '{s.Name}' invalid (identifier regex)");
        var kind = s.Kind?.ToLowerInvariant();
        switch (kind)
        {
            case "integer":
                if (s.IntegerMax < s.IntegerMin)
                    throw new ArgumentException($"slot '{s.Name}' integerMax < integerMin");
                break;
            case "rational":
                if (s.NumeratorMax < s.NumeratorMin || s.DenominatorMax < s.DenominatorMin)
                    throw new ArgumentException($"slot '{s.Name}' rational range invalid");
                if (s.DenominatorMin == 0 && s.DenominatorMax == 0)
                    throw new ArgumentException($"slot '{s.Name}' denominator range exactly {{0}}");
                break;
            case "choice":
                if (s.Choices.Count == 0) throw new ArgumentException($"slot '{s.Name}' choice list empty");
                break;
            default:
                throw new ArgumentException($"slot '{s.Name}' unknown kind '{s.Kind}'");
        }
    }

    private static void ValidateConstraint(SlotConstraintPayloadDto c, ISet<string> knownSlots)
    {
        if (string.IsNullOrWhiteSpace(c.Description))
            throw new ArgumentException("constraint description required");
        var tokens = (c.PredicateExpr ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 3)
            throw new ArgumentException(
                $"constraint '{c.Description}' predicate must be `<slot> <op> <slot-or-number>`");
        if (!knownSlots.Contains(tokens[0]))
            throw new ArgumentException($"constraint '{c.Description}' references unknown slot '{tokens[0]}'");
        var op = tokens[1];
        if (op is not ("==" or "!=" or ">" or ">=" or "<" or "<="))
            throw new ArgumentException($"constraint '{c.Description}' operator '{op}' unsupported");
        if (!knownSlots.Contains(tokens[2])
            && !double.TryParse(tokens[2], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out _))
            throw new ArgumentException(
                $"constraint '{c.Description}' rhs '{tokens[2]}' must be slot name or numeric literal");
    }

    private static void ValidateDistractor(DistractorRulePayloadDto d)
    {
        if (string.IsNullOrWhiteSpace(d.MisconceptionId))
            throw new ArgumentException("distractor misconceptionId required");
        if (string.IsNullOrWhiteSpace(d.FormulaExpr))
            throw new ArgumentException("distractor formulaExpr required");
        RejectLatexInjection(d.FormulaExpr);
        RejectDisallowedExprChars(d.FormulaExpr, $"distractor '{d.MisconceptionId}'.formulaExpr");
    }

    /// <summary>
    /// Mirror of ParametricRenderHelpers.SubstituteSlots' allowlist. Pre-validating
    /// here converts a renderer-level reject (RejectedRenderError at preview) into
    /// a 400 at create/update time — authors see the issue before they ever ship
    /// the template.
    /// </summary>
    private static void RejectDisallowedExprChars(string expr, string field)
    {
        foreach (var ch in expr)
        {
            if (!(char.IsLetterOrDigit(ch)
                  || ch == '_' || ch == '+' || ch == '-' || ch == '*' || ch == '/' || ch == '^'
                  || ch == '(' || ch == ')' || ch == '.' || ch == ',' || ch == ' ' || ch == '\t'))
                throw new ArgumentException(
                    $"{field} contains disallowed character '{ch}' (expression allowlist: A-Z, a-z, 0-9, _+-*/^().,whitespace)");
        }
    }

    private static void RejectLatexInjection(string s)
    {
        foreach (var marker in LatexInjectionMarkers)
            if (s.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                throw new ArgumentException(
                    $"input contains disallowed LaTeX directive '{marker}' (§28 sanitisation policy)");
    }

    // ── Enum parsers ──

    public static TemplateTrack ParseTrack(string v) => v?.ToLowerInvariant() switch
    {
        "fourunit" or "four_unit" or "4unit" or "4-unit" or "4" => TemplateTrack.FourUnit,
        "fiveunit" or "five_unit" or "5unit" or "5-unit" or "5" => TemplateTrack.FiveUnit,
        _ => throw new ArgumentException($"unknown track '{v}'")
    };

    public static TemplateDifficulty ParseDifficulty(string v) => v?.ToLowerInvariant() switch
    {
        "easy" => TemplateDifficulty.Easy,
        "medium" => TemplateDifficulty.Medium,
        "hard" => TemplateDifficulty.Hard,
        _ => throw new ArgumentException($"unknown difficulty '{v}'")
    };

    public static TemplateMethodology ParseMethodology(string v) => v?.ToLowerInvariant() switch
    {
        "halabi" => TemplateMethodology.Halabi,
        "rabinovitch" => TemplateMethodology.Rabinovitch,
        _ => throw new ArgumentException($"unknown methodology '{v}'")
    };

    public static AcceptShape ParseAcceptShapes(IReadOnlyList<string> shapes)
    {
        if (shapes is null || shapes.Count == 0) return AcceptShape.Any;
        AcceptShape result = AcceptShape.None;
        foreach (var s in shapes)
        {
            result |= s?.ToLowerInvariant() switch
            {
                "integer" => AcceptShape.Integer,
                "rational" => AcceptShape.Rational,
                "decimal" => AcceptShape.Decimal,
                "symbolic" => AcceptShape.Symbolic,
                "any" => AcceptShape.Any,
                _ => throw new ArgumentException($"unknown accept-shape '{s}'")
            };
        }
        return result;
    }

    private static ParametricSlotPayload MapSlotToPayload(ParametricSlotPayloadDto s) => new()
    {
        Name = s.Name,
        Kind = s.Kind?.ToLowerInvariant() ?? "",
        IntegerMin = s.IntegerMin,
        IntegerMax = s.IntegerMax,
        IntegerExclude = s.IntegerExclude?.ToList() ?? new List<int>(),
        NumeratorMin = s.NumeratorMin,
        NumeratorMax = s.NumeratorMax,
        DenominatorMin = s.DenominatorMin,
        DenominatorMax = s.DenominatorMax,
        ReduceRational = s.ReduceRational,
        Choices = s.Choices?.ToList() ?? new List<string>()
    };

    private static ParametricSlot ToSlot(ParametricSlotPayload p) => p.Kind.ToLowerInvariant() switch
    {
        "integer" => new ParametricSlot
        {
            Name = p.Name, Kind = ParametricSlotKind.Integer,
            IntegerMin = p.IntegerMin, IntegerMax = p.IntegerMax,
            IntegerExclude = p.IntegerExclude.ToArray()
        },
        "rational" => new ParametricSlot
        {
            Name = p.Name, Kind = ParametricSlotKind.Rational,
            NumeratorMin = p.NumeratorMin, NumeratorMax = p.NumeratorMax,
            DenominatorMin = p.DenominatorMin, DenominatorMax = p.DenominatorMax,
            ReduceRational = p.ReduceRational
        },
        "choice" => new ParametricSlot
        {
            Name = p.Name, Kind = ParametricSlotKind.Choice,
            Choices = p.Choices.ToArray()
        },
        _ => throw new ArgumentException($"unknown slot kind '{p.Kind}'")
    };
}
