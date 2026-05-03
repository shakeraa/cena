// =============================================================================
// Cena Platform — Parametric Template Generate Endpoint (prr-200)
//
// POST /api/admin/templates/generate
// Body: { template: ParametricTemplateDto, baseSeed: long, count: int }
//
// Thin wrapper over ParametricCompiler.CompileAsync. Auth: content-author or
// super-admin (ModeratorOrAbove is over-broad here — we tighten to AdminOnly).
// Rate limited under the existing "ai" bucket because the endpoint fan-outs
// through the CAS sidecar.
//
// The endpoint is DRY-RUN only in prr-200: it returns the compiled variants
// in the response body but does not persist them to the question bank. prr-201
// owns the wet-run ingestion path via ICasVerificationGate + QuestionBankService.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.QuestionBank.Templates;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Templates;

// ── Request / response DTOs ──────────────────────────────────────────────

public sealed record ParametricSlotDto(
    string Name,
    string Kind,
    int IntegerMin,
    int IntegerMax,
    int[]? IntegerExclude,
    int NumeratorMin,
    int NumeratorMax,
    int DenominatorMin,
    int DenominatorMax,
    bool ReduceRational,
    string[]? Choices);

public sealed record DistractorRuleDto(
    string MisconceptionId,
    string FormulaExpr,
    string? LabelHint);

public sealed record SlotConstraintDto(string Description, string PredicateExpr);

public sealed record ParametricTemplateDto(
    string Id,
    int Version,
    string Subject,
    string Topic,
    string Track,        // "FourUnit" | "FiveUnit"
    string Difficulty,   // "Easy" | "Medium" | "Hard"
    string Methodology,  // "Halabi" | "Rabinovitch"
    int BloomsLevel,
    string Language,
    string StemTemplate,
    string SolutionExpr,
    string? VariableName,
    string[] AcceptShapes,
    ParametricSlotDto[] Slots,
    SlotConstraintDto[]? Constraints,
    DistractorRuleDto[]? DistractorRules);

public sealed record TemplateGenerateRequestDto(
    ParametricTemplateDto Template,
    long BaseSeed,
    int Count);

public sealed record RenderedVariantDto(
    long Seed,
    string Stem,
    string CanonicalAnswer,
    RenderedDistractorDto[] Distractors,
    ParametricSlotValueDto[] SlotSnapshot);

public sealed record RenderedDistractorDto(string MisconceptionId, string Text, string? Rationale);

public sealed record ParametricSlotValueDto(
    string Name,
    string Kind,
    long Numerator,
    long Denominator,
    string? ChoiceValue);

public sealed record DropRecordDto(string Kind, long Seed, string Detail, double LatencyMs);

public sealed record TemplateGenerateResponseDto(
    bool Success,
    string TemplateId,
    int RequestedCount,
    int AcceptedCount,
    int TotalAttempts,
    RenderedVariantDto[] Variants,
    DropRecordDto[] Drops,
    string? Error);

// ── Endpoint registration ────────────────────────────────────────────────

public static class TemplateGenerateEndpoint
{
    public static IEndpointRouteBuilder MapParametricTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/templates")
            .WithTags("Parametric Templates")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .RequireRateLimiting("ai");

        group.MapPost("/generate", async (
            TemplateGenerateRequestDto body,
            ClaimsPrincipal user,
            ParametricCompiler compiler,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("Cena.Admin.Api.Templates.TemplateGenerateEndpoint");

            if (body is null)
                return Results.BadRequest(new TemplateGenerateResponseDto(
                    false, "", 0, 0, 0, Array.Empty<RenderedVariantDto>(),
                    Array.Empty<DropRecordDto>(), "request body required"));
            if (body.Count <= 0 || body.Count > 500)
                return Results.BadRequest(new TemplateGenerateResponseDto(
                    false, body.Template?.Id ?? "", body.Count, 0, 0,
                    Array.Empty<RenderedVariantDto>(), Array.Empty<DropRecordDto>(),
                    "count must be between 1 and 500"));

            ParametricTemplate template;
            try
            {
                template = MapTemplate(body.Template);
            }
            catch (Exception ex)
            {
                logger.LogInformation(ex, "[TEMPLATE_GENERATE_VALIDATION] template={Tid}", body.Template?.Id);
                return Results.BadRequest(new TemplateGenerateResponseDto(
                    false, body.Template?.Id ?? "", body.Count, 0, 0,
                    Array.Empty<RenderedVariantDto>(), Array.Empty<DropRecordDto>(),
                    ex.Message));
            }

            try
            {
                var report = await compiler.CompileAsync(template, body.BaseSeed, body.Count, ct);

                var variants = report.Variants.Select(v => new RenderedVariantDto(
                    Seed: v.Seed,
                    Stem: v.RenderedStem,
                    CanonicalAnswer: v.CanonicalAnswer,
                    Distractors: v.Distractors
                        .Select(d => new RenderedDistractorDto(d.MisconceptionId, d.Text, d.Rationale))
                        .ToArray(),
                    SlotSnapshot: v.SlotValues
                        .Select(s => new ParametricSlotValueDto(
                            s.Name, s.Kind.ToString(), s.Numerator, s.Denominator, s.ChoiceValue))
                        .ToArray())).ToArray();

                var drops = report.Drops.Select(d => new DropRecordDto(
                    d.Kind.ToString(), d.Seed, d.Detail, d.LatencyMs)).ToArray();

                return Results.Ok(new TemplateGenerateResponseDto(
                    Success: true,
                    TemplateId: report.TemplateId,
                    RequestedCount: report.RequestedCount,
                    AcceptedCount: report.AcceptedCount,
                    TotalAttempts: report.TotalAttempts,
                    Variants: variants,
                    Drops: drops,
                    Error: null));
            }
            catch (InsufficientSlotSpaceException ex)
            {
                logger.LogWarning(ex,
                    "[TEMPLATE_GENERATE_INSUFFICIENT] template={Tid} produced={Prod}/{Req}",
                    ex.TemplateId, ex.Produced, ex.Requested);
                return Results.UnprocessableEntity(new TemplateGenerateResponseDto(
                    false, ex.TemplateId, ex.Requested, ex.Produced, 0,
                    Array.Empty<RenderedVariantDto>(), Array.Empty<DropRecordDto>(),
                    ex.Message));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[TEMPLATE_GENERATE_UNEXPECTED] template={Tid}", template.Id);
                return Results.Problem(
                    title: "Template generation failed",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("GenerateParametricTemplateVariants")
        .Produces<TemplateGenerateResponseDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status422UnprocessableEntity)
        .Produces(StatusCodes.Status429TooManyRequests);

        return app;
    }

    // ── DTO → domain mapping ────────────────────────────────────────────

    internal static ParametricTemplate MapTemplate(ParametricTemplateDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.Slots is null || dto.Slots.Length == 0)
            throw new ArgumentException("Template must declare at least one slot");

        var slots = dto.Slots.Select(MapSlot).ToArray();
        var constraints = (dto.Constraints ?? Array.Empty<SlotConstraintDto>())
            .Select(c => new SlotConstraint(c.Description, c.PredicateExpr))
            .ToArray();
        var distractorRules = (dto.DistractorRules ?? Array.Empty<DistractorRuleDto>())
            .Select(r => new DistractorRule(r.MisconceptionId, r.FormulaExpr, r.LabelHint))
            .ToArray();

        return new ParametricTemplate
        {
            Id = dto.Id,
            Version = dto.Version < 1 ? 1 : dto.Version,
            Subject = dto.Subject,
            Topic = dto.Topic,
            Track = ParseTrack(dto.Track),
            Difficulty = ParseDifficulty(dto.Difficulty),
            Methodology = ParseMethodology(dto.Methodology),
            BloomsLevel = dto.BloomsLevel,
            Language = dto.Language,
            StemTemplate = dto.StemTemplate,
            SolutionExpr = dto.SolutionExpr,
            VariableName = dto.VariableName,
            AcceptShapes = ParseAcceptShapes(dto.AcceptShapes),
            Slots = slots,
            Constraints = constraints,
            DistractorRules = distractorRules
        };
    }

    internal static ParametricSlot MapSlot(ParametricSlotDto s) => s.Kind.ToLowerInvariant() switch
    {
        "integer" => new ParametricSlot
        {
            Name = s.Name, Kind = ParametricSlotKind.Integer,
            IntegerMin = s.IntegerMin, IntegerMax = s.IntegerMax,
            IntegerExclude = s.IntegerExclude ?? Array.Empty<int>()
        },
        "rational" => new ParametricSlot
        {
            Name = s.Name, Kind = ParametricSlotKind.Rational,
            NumeratorMin = s.NumeratorMin, NumeratorMax = s.NumeratorMax,
            DenominatorMin = s.DenominatorMin, DenominatorMax = s.DenominatorMax,
            ReduceRational = s.ReduceRational
        },
        "choice" => new ParametricSlot
        {
            Name = s.Name, Kind = ParametricSlotKind.Choice,
            Choices = s.Choices ?? Array.Empty<string>()
        },
        _ => throw new ArgumentException($"Unknown slot kind '{s.Kind}'")
    };

    internal static TemplateTrack ParseTrack(string v) => v?.ToLowerInvariant() switch
    {
        "fourunit" or "four_unit" or "4unit" or "4-unit" or "4" => TemplateTrack.FourUnit,
        "fiveunit" or "five_unit" or "5unit" or "5-unit" or "5" => TemplateTrack.FiveUnit,
        _ => throw new ArgumentException($"Unknown track '{v}'")
    };

    internal static TemplateDifficulty ParseDifficulty(string v) => v?.ToLowerInvariant() switch
    {
        "easy" => TemplateDifficulty.Easy,
        "medium" => TemplateDifficulty.Medium,
        "hard" => TemplateDifficulty.Hard,
        _ => throw new ArgumentException($"Unknown difficulty '{v}'")
    };

    internal static TemplateMethodology ParseMethodology(string v) => v?.ToLowerInvariant() switch
    {
        "halabi" => TemplateMethodology.Halabi,
        "rabinovitch" => TemplateMethodology.Rabinovitch,
        _ => throw new ArgumentException($"Unknown methodology '{v}'")
    };

    internal static AcceptShape ParseAcceptShapes(string[]? shapes)
    {
        if (shapes is null || shapes.Length == 0) return AcceptShape.Any;
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
                _ => throw new ArgumentException($"Unknown accept-shape '{s}'")
            };
        }
        return result;
    }
}
