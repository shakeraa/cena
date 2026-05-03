// =============================================================================
// Cena Platform — Generate-Similar Handler (RDY-058)
//
// Pure, testable handler behind POST /api/admin/questions/{id}/generate-similar.
// No HttpRequest, no model binding — caller pre-parses and hands in the id
// + body. Returns an IResult mapping the outcome surface.
//
// Responsibilities:
//   - Load the source QuestionReadModel (404 if missing)
//   - Synthesise an AiGenerateRequest from its Subject/Topic/Grade/Bloom +
//     curator-supplied or source-inherited difficulty band + language
//   - Delegate to IAiGenerationService.BatchGenerateAsync (which runs
//     QualityGate + CAS gate per candidate)
//   - Emit QuestionSimilarGenerated_V1 on the parent stream for provenance
//   - Return the BatchGenerateResponse unchanged so the existing UI renderer
//     works without modification
//
// The service surface keeps every rule defined elsewhere:
//   - CAS gate is AiGenerationService's responsibility, not ours
//   - Final question-save routes through CasGatedQuestionPersister, not here
//   - Rate-limit + auth enforced at the endpoint, not the handler
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Questions;
using Cena.Actors.Variants;
using Cena.Admin.Api.QualityGate;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.RateLimiting;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Questions;

public sealed record GenerateSimilarRequest(
    int Count = 3,
    float? MinDifficulty = null,
    float? MaxDifficulty = null,
    string? Language = null,
    string? SourcePaperCode = null);

/// <summary>
/// Curator pseudonym formatting for the per-student scope of
/// <see cref="IVariantGenerationGate"/>. Curators do NOT have a
/// SubscriptionTier; we route them through the gate as
/// <c>curator:{userId}</c> so per-student counters don't collide
/// with real student ids and the gate path stays uniform with the
/// future PRR-245 student-facing endpoint.
/// </summary>
internal static class CuratorPseudonym
{
    public const string Prefix = "curator:";
    public static string Format(string userId) => Prefix + userId;
}

public static class GenerateSimilarHandler
{
    // How far below/above the source difficulty the default band spans when
    // the curator doesn't supply explicit bounds. 0.15 gives a useful
    // "same ballpark" range without dropping the band to 0 or ceiling at 1.
    internal const float DefaultDifficultyWidth = 0.15f;

    // Inherited from AiGenerationService (1-20). Out-of-range requests are
    // silently clamped — the endpoint also rejects anything outside this
    // band with a 400, but the handler is defensive in case a future caller
    // invokes it directly.
    internal const int MinCount = 1;
    internal const int MaxCount = 20;

    public static async Task<IResult> HandleAsync(
        string questionId,
        GenerateSimilarRequest body,
        IDocumentStore store,
        IAiGenerationService ai,
        IQualityGateService qualityGate,
        string generatedBy,
        ILogger logger,
        CancellationToken ct = default,
        IVariantGenerationGate? variantGate = null,
        bool variantGateLegalFlagEnabled = true,
        string? variantGateInstituteId = null)
    {
        if (string.IsNullOrWhiteSpace(questionId))
            return Error("invalid_id", "questionId is required.",
                ErrorCategory.Validation, StatusCodes.Status400BadRequest);

        // PRR-265 / ADR-0059 §15.5 R1 backstop: if a gate is wired,
        // run it before any cost-bearing work. Endpoint-level
        // RequireRateLimiting("ai") is a per-minute burst guard; this
        // gate enforces the per-day + per-source caps from §15.5.
        if (variantGate is not null)
        {
            var gateContext = new VariantGenerationContext(
                StudentSubjectIdEncrypted: CuratorPseudonym.Format(generatedBy),
                InstituteId: variantGateInstituteId,
                SourcePaperCode: string.IsNullOrWhiteSpace(body.SourcePaperCode)
                    ? null : body.SourcePaperCode,
                Kind: VariantKind.Structural,   // GenerateSimilar is structural
                PaymentVerified: true,           // curators are pre-authorized
                LegalFlagEnabled: variantGateLegalFlagEnabled);

            var decision = await variantGate.CheckAsync(gateContext, DateTimeOffset.UtcNow, ct);
            if (!decision.Allowed && decision.DeniedReason is { } reason)
            {
                var status = VariantGenerationGateDecision.HttpStatusFor(reason);
                logger.LogInformation(
                    "[GENERATE_SIMILAR_GATE_DENIED] reason={Reason} curator={Curator} status={Status}",
                    decision.DeniedReasonCode, generatedBy, status);
                return BuildGateDenialResult(decision, status);
            }
        }

        var core = await RunCoreAsync(questionId, body, store, ai, qualityGate, generatedBy, logger, ct);
        var result = core.ErrorCode switch
        {
            "question_not_found" => Error(core.ErrorCode,
                core.ErrorMessage ?? "source question not found.",
                ErrorCategory.NotFound, StatusCodes.Status404NotFound),
            null when core.Response is not null => Results.Ok(core.Response),
            _ => Error(core.ErrorCode ?? "error",
                core.ErrorMessage ?? "unknown error",
                ErrorCategory.Internal, StatusCodes.Status500InternalServerError),
        };

        // Commit the gate counter ONLY on a successful generation —
        // failed/error attempts must not burn the cap.
        if (variantGate is not null && core.ErrorCode is null && core.Response is not null)
        {
            var commitContext = new VariantGenerationContext(
                StudentSubjectIdEncrypted: CuratorPseudonym.Format(generatedBy),
                InstituteId: variantGateInstituteId,
                SourcePaperCode: string.IsNullOrWhiteSpace(body.SourcePaperCode)
                    ? null : body.SourcePaperCode,
                Kind: VariantKind.Structural,
                PaymentVerified: true,
                LegalFlagEnabled: variantGateLegalFlagEnabled);
            try
            {
                await variantGate.CommitAsync(
                    commitContext,
                    commitId: $"{questionId}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                    asOfUtc: DateTimeOffset.UtcNow,
                    ct);
            }
            catch (Exception ex)
            {
                // Counter advancement is best-effort: a Redis hiccup must
                // not 500 a successful generation. Surfaced via metrics.
                logger.LogWarning(ex,
                    "[GENERATE_SIMILAR_GATE_COMMIT_FAIL] curator={Curator} questionId={QuestionId}",
                    generatedBy, questionId);
            }
        }

        return result;
    }

    /// <summary>
    /// Map a gate denial decision to an HTTP <see cref="IResult"/>. 429s
    /// carry a <c>Retry-After</c> header (whole seconds).
    /// </summary>
    private static IResult BuildGateDenialResult(VariantGenerationGateDecision decision, int status)
    {
        var category = status == StatusCodes.Status429TooManyRequests
            ? ErrorCategory.RateLimit
            : ErrorCategory.Authorization;
        var error = new CenaError(
            decision.DeniedReasonCode ?? "variant_generation_denied",
            decision.DeniedDetail ?? "Variant generation denied by rate-limit gate.",
            category,
            null, null);

        var retrySeconds = decision.RetryAfter.HasValue
            ? Math.Max(1, (int)Math.Ceiling(decision.RetryAfter.Value.TotalSeconds))
            : (int?)null;
        return new GateDenialResult(error, status, retrySeconds);
    }

    /// <summary>
    /// Custom <see cref="IResult"/> that writes the canonical
    /// <c>{ "error": { ... } }</c> envelope plus an optional <c>Retry-After</c>
    /// header. Implementing IResult directly is cleaner than wrapping
    /// Results.Json — the latter offers no header-set hook for the
    /// 429-specific Retry-After requirement.
    /// </summary>
    private sealed class GateDenialResult : IResult
    {
        private readonly CenaError _error;
        private readonly int _statusCode;
        private readonly int? _retryAfterSeconds;

        public GateDenialResult(CenaError error, int statusCode, int? retryAfterSeconds)
        {
            _error = error;
            _statusCode = statusCode;
            _retryAfterSeconds = retryAfterSeconds;
        }

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = _statusCode;
            if (_retryAfterSeconds.HasValue)
            {
                httpContext.Response.Headers["Retry-After"] =
                    _retryAfterSeconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            await Results.Json(new ErrorResponse(_error), statusCode: _statusCode)
                .ExecuteAsync(httpContext);
        }
    }

    /// <summary>
    /// Core routine that returns the structured outcome (no IResult wrapper)
    /// so higher-level coordinators (corpus expander, batch jobs) can call
    /// it in a loop without paying the HTTP-result encoding cost.
    /// </summary>
    public sealed record GenerateSimilarCoreResult(
        BatchGenerateResponse? Response,
        string? ErrorCode,
        string? ErrorMessage,
        QuestionReadModel? Source,
        BatchGenerateRequest? EffectiveRequest);

    public static async Task<GenerateSimilarCoreResult> RunCoreAsync(
        string questionId,
        GenerateSimilarRequest body,
        IDocumentStore store,
        IAiGenerationService ai,
        IQualityGateService qualityGate,
        string generatedBy,
        ILogger logger,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(questionId))
            return new GenerateSimilarCoreResult(null, "invalid_id", "questionId is required.", null, null);

        QuestionReadModel? source;
        await using (var session = store.QuerySession())
        {
            source = await session.LoadAsync<QuestionReadModel>(questionId, ct);
        }
        if (source is null)
            return new GenerateSimilarCoreResult(
                null, "question_not_found",
                $"QuestionReadModel '{questionId}' does not exist.",
                null, null);

        var count = Math.Clamp(body.Count, MinCount, MaxCount);
        var (min, max) = ResolveDifficultyBand(source, body);
        var language = string.IsNullOrWhiteSpace(body.Language)
            ? (string.IsNullOrWhiteSpace(source.Language) ? "he" : source.Language)
            : body.Language.Trim();

        var batchRequest = new BatchGenerateRequest(
            Count:         count,
            Subject:       string.IsNullOrWhiteSpace(source.Subject) ? "math" : source.Subject,
            Topic:         string.IsNullOrWhiteSpace(source.Topic) ? null : source.Topic,
            Grade:         string.IsNullOrWhiteSpace(source.Grade) ? "5 Units" : source.Grade,
            BloomsLevel:   Math.Clamp(source.BloomsLevel, 1, 6),
            MinDifficulty: min,
            MaxDifficulty: max,
            Language:      language);

        logger.LogInformation(
            "[GENERATE_SIMILAR] parent={ParentId} subject={Subject} topic={Topic} bloom={Bloom} difficulty=[{Min:F2},{Max:F2}] count={Count} by={By}",
            questionId, batchRequest.Subject, batchRequest.Topic, batchRequest.BloomsLevel,
            min, max, count, generatedBy);

        BatchGenerateResponse response;
        try
        {
            response = await ai.BatchGenerateAsync(batchRequest, qualityGate);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[GENERATE_SIMILAR] BatchGenerateAsync failed for parent={ParentId}", questionId);
            return new GenerateSimilarCoreResult(
                null, "generation_failed", ex.Message, source, batchRequest);
        }

        // Best-effort provenance on the parent stream.
        try
        {
            await using var writeSession = store.LightweightSession();
            writeSession.Events.Append(
                questionId,
                new QuestionSimilarGenerated_V1(
                    ParentQuestionId: questionId,
                    Count:            response.TotalGenerated,
                    MinDifficulty:    min,
                    MaxDifficulty:    max,
                    Language:         language,
                    GeneratedBy:      generatedBy,
                    Timestamp:        DateTimeOffset.UtcNow));
            await writeSession.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[GENERATE_SIMILAR] failed to append QuestionSimilarGenerated_V1 for parent={ParentId}",
                questionId);
        }

        return new GenerateSimilarCoreResult(response, null, null, source, batchRequest);
    }

    internal static (float Min, float Max) ResolveDifficultyBand(
        QuestionReadModel source, GenerateSimilarRequest body)
    {
        float min, max;
        var sourceDifficulty = Math.Clamp(source.Difficulty, 0f, 1f);

        if (body.MinDifficulty is not null || body.MaxDifficulty is not null)
        {
            min = Math.Clamp(body.MinDifficulty ?? Math.Max(0f, sourceDifficulty - DefaultDifficultyWidth), 0f, 1f);
            max = Math.Clamp(body.MaxDifficulty ?? Math.Min(1f, sourceDifficulty + DefaultDifficultyWidth), 0f, 1f);
        }
        else
        {
            min = Math.Clamp(sourceDifficulty - DefaultDifficultyWidth, 0f, 1f);
            max = Math.Clamp(sourceDifficulty + DefaultDifficultyWidth, 0f, 1f);
        }

        if (max < min) (min, max) = (max, min);
        return (min, max);
    }

    private static IResult Error(string code, string message, ErrorCategory category, int statusCode) =>
        Results.Json(new CenaError(code, message, category, null, null), statusCode: statusCode);
}
