// =============================================================================
// Cena Platform -- Question Bank Service (Marten Event-Sourced)
// ADM-010: Question bank browser and management
// Replaces mock data with real event-sourced aggregates via Marten.
// =============================================================================

using System.Globalization;
using System.Text.Json;
using Cena.Actors.Events;
using Cena.Actors.Questions;
using Cena.Admin.Api.QualityGate;
using Marten;
using Microsoft.Extensions.Logging;
using DomainStatus = Cena.Actors.Questions.QuestionLifecycleStatus;

namespace Cena.Admin.Api;

public interface IQuestionBankService
{
    Task<QuestionListResponse> GetQuestionsAsync(
        string? subject, int? bloomsLevel,
        float? minDifficulty, float? maxDifficulty,
        string? status, string? language, string? conceptId, string? search,
        int page, int pageSize, string sortBy, string orderBy);

    Task<QuestionBankDetailResponse?> GetQuestionAsync(string id);
    Task<QuestionBankDetailResponse?> UpdateQuestionAsync(string id, UpdateBankQuestionRequest request);
    Task<bool> DeprecateQuestionAsync(string id, DeprecateBankQuestionRequest request);
    Task<QuestionFiltersResponse> GetFiltersAsync();
    Task<ConceptAutocompleteResponse> AutocompleteConceptsAsync(string query);
    Task<QuestionStats?> GetPerformanceAsync(string id);
    Task<bool> ApproveAsync(string id);
    Task<QuestionBankDetailResponse?> CreateQuestionAsync(CreateQuestionRequest request, string userId);
    Task<bool> PublishAsync(string id, string userId);
    Task<bool> AddLanguageVersionAsync(string id, AddLanguageVersionRequest request, string userId);
}

public sealed class QuestionBankService : IQuestionBankService
{
    private readonly IDocumentStore _store;
    private readonly IQualityGateService _qualityGate;
    private readonly ILogger<QuestionBankService> _logger;

    public QuestionBankService(
        IDocumentStore store,
        IQualityGateService qualityGate,
        ILogger<QuestionBankService> logger)
    {
        _store = store;
        _qualityGate = qualityGate;
        _logger = logger;
    }

    public async Task<QuestionListResponse> GetQuestionsAsync(
        string? subject, int? bloomsLevel,
        float? minDifficulty, float? maxDifficulty,
        string? status, string? language, string? conceptId, string? search,
        int page, int pageSize, string sortBy, string orderBy)
    {
        await using var session = _store.QuerySession();
        var query = session.Query<QuestionReadModel>().AsQueryable();

        if (!string.IsNullOrEmpty(subject))
            query = query.Where(q => q.Subject == subject);

        if (bloomsLevel.HasValue)
            query = query.Where(q => q.BloomsLevel == bloomsLevel.Value);

        if (minDifficulty.HasValue)
            query = query.Where(q => q.Difficulty >= minDifficulty.Value);

        if (maxDifficulty.HasValue)
            query = query.Where(q => q.Difficulty <= maxDifficulty.Value);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(q => q.Status == status);

        if (!string.IsNullOrEmpty(language))
            query = query.Where(q => q.Language == language);

        if (!string.IsNullOrEmpty(conceptId))
            query = query.Where(q => q.Concepts.Contains(conceptId));

        if (!string.IsNullOrEmpty(search))
            query = query.Where(q => q.StemPreview.Contains(search, StringComparison.OrdinalIgnoreCase));

        // Sorting
        query = sortBy?.ToLowerInvariant() switch
        {
            "difficulty" => orderBy == "desc"
                ? query.OrderByDescending(q => q.Difficulty)
                : query.OrderBy(q => q.Difficulty),
            "bloomslevel" => orderBy == "desc"
                ? query.OrderByDescending(q => q.BloomsLevel)
                : query.OrderBy(q => q.BloomsLevel),
            "createdat" => orderBy == "desc"
                ? query.OrderByDescending(q => q.CreatedAt)
                : query.OrderBy(q => q.CreatedAt),
            _ => orderBy == "asc"
                ? query.OrderBy(q => q.QualityScore)
                : query.OrderByDescending(q => q.QualityScore)
        };

        var total = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var mapped = items.Select(q => new QuestionListItem(
            q.Id, q.StemPreview, q.Subject, q.Concepts,
            q.BloomsLevel, q.Difficulty,
            Enum.TryParse<QuestionStatus>(q.Status, out var s) ? s : QuestionStatus.Draft,
            q.QualityScore, q.UsageCount, q.SuccessRate)).ToList();

        return new QuestionListResponse(mapped, total, page, pageSize);
    }

    public async Task<QuestionBankDetailResponse?> GetQuestionAsync(string id)
    {
        await using var session = _store.QuerySession();
        var state = await session.Events.AggregateStreamAsync<QuestionState>(id);
        return state == null ? null : MapToDetail(state);
    }

    public async Task<QuestionBankDetailResponse?> UpdateQuestionAsync(
        string id, UpdateBankQuestionRequest request)
    {
        await using var session = _store.LightweightSession();
        var state = await session.Events.AggregateStreamAsync<QuestionState>(id);
        if (state == null) return null;

        var events = new List<object>();
        var now = DateTimeOffset.UtcNow;
        const string editor = "admin"; // TODO: extract from auth context

        // Diff stem
        if (!string.IsNullOrEmpty(request.Stem) && request.Stem != state.Stem)
        {
            events.Add(new QuestionStemEdited_V1(
                id, state.Stem, request.Stem, $"<p>{request.Stem}</p>", editor, now));
        }

        // Diff options
        if (request.Options != null)
        {
            foreach (var opt in request.Options)
            {
                var existing = state.Options.FirstOrDefault(o => o.Label == opt.Id);
                if (existing != null && existing.Text != opt.Text)
                {
                    events.Add(new QuestionOptionChanged_V1(
                        id, opt.Id, existing.Text, opt.Text, $"<p>{opt.Text}</p>",
                        opt.IsCorrect, null, editor, now));
                }
            }
        }

        // Diff metadata
        if (request.Difficulty.HasValue && Math.Abs(request.Difficulty.Value - state.Difficulty) > 0.001f)
        {
            events.Add(new QuestionMetadataUpdated_V1(
                id, "difficulty",
                state.Difficulty.ToString(CultureInfo.InvariantCulture),
                request.Difficulty.Value.ToString(CultureInfo.InvariantCulture),
                editor, now));
        }

        if (request.ConceptIds != null && !request.ConceptIds.SequenceEqual(state.ConceptIds))
        {
            events.Add(new QuestionMetadataUpdated_V1(
                id, "conceptIds",
                JsonSerializer.Serialize(state.ConceptIds),
                JsonSerializer.Serialize(request.ConceptIds),
                editor, now));
        }

        if (events.Count > 0)
        {
            // Re-run quality gate after edits
            var gateResult = EvaluateQualityGate(state, request.Stem ?? state.Stem);
            events.Add(MapGateEvent(id, gateResult, now));

            session.Events.Append(id, state.EventVersion + events.Count, events.ToArray());
            await session.SaveChangesAsync();
        }

        // Re-fetch updated state
        return await GetQuestionAsync(id);
    }

    public async Task<bool> DeprecateQuestionAsync(string id, DeprecateBankQuestionRequest request)
    {
        await using var session = _store.LightweightSession();
        var state = await session.Events.AggregateStreamAsync<QuestionState>(id);
        if (state == null) return false;

        var evt = new QuestionDeprecated_V1(
            id, request.Reason, request.RemoveFromServing,
            "admin", DateTimeOffset.UtcNow);

        session.Events.Append(id, state.EventVersion + 1, evt);
        await session.SaveChangesAsync();
        _logger.LogInformation("Deprecated question {QuestionId}: {Reason}", id, request.Reason);
        return true;
    }

    public async Task<QuestionFiltersResponse> GetFiltersAsync()
    {
        await using var session = _store.QuerySession();
        var subjects = await session.Query<QuestionReadModel>()
            .Select(q => q.Subject).Distinct().ToListAsync();

        var concepts = await session.Query<QuestionReadModel>()
            .SelectMany(q => q.Concepts).Distinct().ToListAsync();

        var grades = await session.Query<QuestionReadModel>()
            .Select(q => q.Grade).Where(g => g != "").Distinct().ToListAsync();

        var conceptFilters = concepts.Select(c =>
            new ConceptFilter(c, SlugToName(c), "")).ToList();

        return new QuestionFiltersResponse(
            subjects.Where(s => !string.IsNullOrEmpty(s)).ToList(),
            conceptFilters,
            grades);
    }

    public async Task<ConceptAutocompleteResponse> AutocompleteConceptsAsync(string query)
    {
        await using var session = _store.QuerySession();
        var allConcepts = await session.Query<QuestionReadModel>()
            .SelectMany(q => q.Concepts).Distinct().ToListAsync();

        var queryLower = query.ToLowerInvariant();
        var matches = allConcepts
            .Where(c => SlugToName(c).ToLowerInvariant().Contains(queryLower))
            .Select(c => new ConceptMatch(c, SlugToName(c), "", 0))
            .Take(20)
            .ToList();

        return new ConceptAutocompleteResponse(matches);
    }

    public async Task<QuestionStats?> GetPerformanceAsync(string id)
    {
        // Performance data comes from student analytics (ConceptAttempted_V1 projections).
        // Not yet wired — return null until the feedback loop is implemented.
        await using var session = _store.QuerySession();
        var exists = await session.Events.AggregateStreamAsync<QuestionState>(id);
        return exists == null ? null : null;
    }

    public async Task<bool> ApproveAsync(string id)
    {
        await using var session = _store.LightweightSession();
        var state = await session.Events.AggregateStreamAsync<QuestionState>(id);
        if (state == null) return false;
        if (state.Status == DomainStatus.Published) return true;

        var evt = new QuestionApproved_V1(id, "admin", DateTimeOffset.UtcNow);
        session.Events.Append(id, state.EventVersion + 1, evt);
        await session.SaveChangesAsync();
        _logger.LogInformation("Approved question {QuestionId}", id);
        return true;
    }

    public async Task<QuestionBankDetailResponse?> CreateQuestionAsync(
        CreateQuestionRequest request, string userId)
    {
        var id = $"q-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var options = request.Options.Select(o => new QuestionOptionData(
            o.Label, o.Text, o.TextHtml ?? $"<p>{o.Text}</p>",
            o.IsCorrect, o.DistractorRationale)).ToList();

        object creationEvent = request.SourceType switch
        {
            "ingested" => new QuestionIngested_V1(
                id, request.Stem, request.StemHtml ?? $"<p>{request.Stem}</p>",
                options, request.Subject, request.Topic ?? "", request.Grade ?? "",
                request.BloomsLevel, request.Difficulty,
                request.ConceptIds ?? new List<string>(), request.Language,
                request.SourceDocId ?? "", request.SourceUrl ?? "",
                request.SourceFilename ?? "", request.OriginalText,
                userId, now),
            "ai-generated" => new QuestionAiGenerated_V1(
                id, request.Stem, request.StemHtml ?? $"<p>{request.Stem}</p>",
                options, request.Subject, request.Topic ?? "", request.Grade ?? "",
                request.BloomsLevel, request.Difficulty,
                request.ConceptIds ?? new List<string>(), request.Language,
                request.PromptText ?? "", request.ModelId ?? "",
                request.ModelTemperature ?? 0.7f, request.RawModelOutput ?? "",
                userId, request.Explanation, now),
            _ => new QuestionAuthored_V1(
                id, request.Stem, request.StemHtml ?? $"<p>{request.Stem}</p>",
                options, request.Subject, request.Topic ?? "", request.Grade ?? "",
                request.BloomsLevel, request.Difficulty,
                request.ConceptIds ?? new List<string>(), request.Language,
                userId, now)
        };

        var events = new List<object> { creationEvent };

        // For non-AI questions, persist explanation via separate event
        if (request.SourceType != "ai-generated" && !string.IsNullOrEmpty(request.Explanation))
        {
            events.Add(new ExplanationEdited_V1(id, null, request.Explanation, userId, now));
        }

        // Run quality gate
        var gateInput = BuildGateInput(id, request.Stem, request.Options,
            request.Subject, request.Language, request.BloomsLevel,
            request.Difficulty, request.Grade, request.ConceptIds);
        var gateResult = _qualityGate.Evaluate(gateInput);
        events.Add(MapGateEvent(id, gateResult, now));

        // Auto-approve if gate passes
        if (gateResult.Decision == GateDecision.AutoApproved)
            events.Add(new QuestionApproved_V1(id, "quality-gate", now));

        await using var session = _store.LightweightSession();
        session.Events.StartStream<QuestionState>(id, events.ToArray());
        await session.SaveChangesAsync();

        _logger.LogInformation(
            "Created question {QuestionId} ({SourceType}) — gate: {Decision}",
            id, request.SourceType, gateResult.Decision);

        return await GetQuestionAsync(id);
    }

    public async Task<bool> PublishAsync(string id, string userId)
    {
        await using var session = _store.LightweightSession();
        var state = await session.Events.AggregateStreamAsync<QuestionState>(id);
        if (state == null) return false;
        if (state.Status != DomainStatus.Approved) return false;

        var evt = new QuestionPublished_V1(id, userId, DateTimeOffset.UtcNow);
        session.Events.Append(id, state.EventVersion + 1, evt);
        await session.SaveChangesAsync();
        _logger.LogInformation("Published question {QuestionId}", id);
        return true;
    }

    public async Task<bool> AddLanguageVersionAsync(
        string id, AddLanguageVersionRequest request, string userId)
    {
        await using var session = _store.LightweightSession();
        var state = await session.Events.AggregateStreamAsync<QuestionState>(id);
        if (state == null) return false;

        var options = request.Options.Select(o => new QuestionOptionData(
            o.Label, o.Text, o.TextHtml ?? $"<p>{o.Text}</p>",
            o.IsCorrect, o.DistractorRationale)).ToList();

        var evt = new LanguageVersionAdded_V1(
            id, request.Language, request.Stem,
            request.StemHtml ?? $"<p>{request.Stem}</p>",
            options, userId, DateTimeOffset.UtcNow);

        session.Events.Append(id, state.EventVersion + 1, evt);
        await session.SaveChangesAsync();
        _logger.LogInformation("Added {Language} version to {QuestionId}", request.Language, id);
        return true;
    }

    // ── Private Helpers ──

    private QualityGate.QualityGateResult EvaluateQualityGate(QuestionState state, string stem)
    {
        var gateOptions = state.Options.Select(o =>
            new QualityGate.QualityGateOption(o.Label, o.Text, o.IsCorrect, o.DistractorRationale)).ToList();

        int correctIdx = gateOptions.FindIndex(o => o.IsCorrect);
        var input = new QualityGateInput(
            state.Id, stem, gateOptions, Math.Max(0, correctIdx),
            state.Subject, state.PrimaryLanguage, state.BloomsLevel,
            state.Difficulty, state.Grade, state.ConceptIds);

        return _qualityGate.Evaluate(input);
    }

    private static QualityGateInput BuildGateInput(
        string id, string stem, IReadOnlyList<CreateOptionRequest> options,
        string subject, string language, int bloom, float difficulty,
        string? grade, IReadOnlyList<string>? conceptIds)
    {
        var gateOptions = options.Select(o =>
            new QualityGate.QualityGateOption(o.Label, o.Text, o.IsCorrect, o.DistractorRationale)).ToList();
        int correctIdx = gateOptions.FindIndex(o => o.IsCorrect);
        return new QualityGateInput(
            id, stem, gateOptions, Math.Max(0, correctIdx),
            subject, language, bloom, difficulty, grade, conceptIds);
    }

    private static QuestionQualityEvaluated_V1 MapGateEvent(
        string id, QualityGate.QualityGateResult r, DateTimeOffset ts) =>
        new(id, r.CompositeScore,
            r.Scores.FactualAccuracy, r.Scores.LanguageQuality,
            r.Scores.PedagogicalQuality, r.Scores.DistractorQuality,
            r.Scores.StemClarity, r.Scores.BloomAlignment,
            r.Scores.StructuralValidity, r.Scores.CulturalSensitivity,
            r.Decision.ToString(), r.Violations.Count, ts);

    private static QuestionBankDetailResponse MapToDetail(QuestionState s) =>
        new(Id: s.Id,
            Stem: s.Stem,
            StemHtml: s.StemHtml,
            Options: s.Options.Select(o => new AnswerOptionDetail(
                o.Label, o.Label, o.Text, $"<p>{o.Text}</p>",
                o.IsCorrect, o.DistractorRationale)).ToList(),
            CorrectAnswers: s.Options.Where(o => o.IsCorrect)
                .Select(o => o.Label).ToList(),
            Subject: s.Subject,
            Topic: s.Topic,
            Grade: s.Grade,
            BloomsLevel: s.BloomsLevel,
            Difficulty: s.Difficulty,
            ConceptIds: s.ConceptIds,
            ConceptNames: s.ConceptIds.Select(SlugToName).ToList(),
            Status: MapStatus(s.Status),
            QualityScore: s.QualityScore,
            SourceType: s.SourceType,
            SourceItemId: s.Provenance?.SourceDocId,
            CreatedAt: s.CreatedAt,
            UpdatedAt: s.UpdatedAt,
            CreatedBy: s.CreatedBy,
            Explanation: s.Explanation,
            Performance: null,  // Populated later from student analytics
            Provenance: s.Provenance != null ? new QuestionProvenance(
                s.Provenance.SourceDocId, s.Provenance.SourceUrl,
                s.Provenance.ImportedAt, s.Provenance.ImportedBy,
                s.Provenance.OriginalText) : null,
            QualityGate: s.LastQualityEvaluation != null ? new QualityGateDetail(
                s.LastQualityEvaluation.CompositeScore,
                s.LastQualityEvaluation.GateDecision,
                s.LastQualityEvaluation.FactualAccuracy,
                s.LastQualityEvaluation.LanguageQuality,
                s.LastQualityEvaluation.PedagogicalQuality,
                s.LastQualityEvaluation.DistractorQuality,
                s.LastQualityEvaluation.StemClarity,
                s.LastQualityEvaluation.BloomAlignment,
                s.LastQualityEvaluation.StructuralValidity,
                s.LastQualityEvaluation.CulturalSensitivity,
                s.LastQualityEvaluation.ViolationCount,
                s.LastQualityEvaluation.EvaluatedAt) : null);

    private static QuestionStatus MapStatus(DomainStatus s) => s switch
    {
        DomainStatus.Draft => QuestionStatus.Draft,
        DomainStatus.InReview => QuestionStatus.InReview,
        DomainStatus.Approved => QuestionStatus.Approved,
        DomainStatus.Published => QuestionStatus.Published,
        DomainStatus.Deprecated => QuestionStatus.Deprecated,
        _ => QuestionStatus.Draft
    };

    private static string SlugToName(string slug) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
            slug.Replace("-", " ").Replace("_", " "));
}
