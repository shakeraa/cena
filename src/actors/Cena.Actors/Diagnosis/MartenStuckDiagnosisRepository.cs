// =============================================================================
// Cena Platform — MartenStuckDiagnosisRepository (RDY-063 Phase 1)
// =============================================================================

using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagnosis;

public sealed class MartenStuckDiagnosisRepository : IStuckDiagnosisRepository
{
    private readonly IDocumentStore _store;
    private readonly StuckClassifierMetrics _metrics;
    private readonly ILogger<MartenStuckDiagnosisRepository> _logger;

    public MartenStuckDiagnosisRepository(
        IDocumentStore store,
        StuckClassifierMetrics metrics,
        ILogger<MartenStuckDiagnosisRepository> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PersistAsync(
        string sessionId,
        string studentAnonId,
        string questionId,
        StuckDiagnosis diagnosis,
        int retentionDays,
        CancellationToken ct = default)
    {
        try
        {
            var diagnosedAt = diagnosis.DiagnosedAt;
            var doc = new StuckDiagnosisDocument
            {
                Id = Guid.NewGuid().ToString("N"),
                SessionId = sessionId,
                StudentAnonId = studentAnonId,
                QuestionId = questionId,
                ChapterId = diagnosis.FocusChapterId,
                Primary = diagnosis.Primary,
                PrimaryConfidence = diagnosis.PrimaryConfidence,
                Secondary = diagnosis.Secondary,
                SecondaryConfidence = diagnosis.SecondaryConfidence,
                SuggestedStrategy = diagnosis.SuggestedStrategy,
                ShouldInvolveTeacher = diagnosis.ShouldInvolveTeacher,
                Source = diagnosis.Source,
                ClassifierVersion = diagnosis.ClassifierVersion,
                SourceReasonCode = diagnosis.SourceReasonCode,
                LatencyMs = diagnosis.LatencyMs,
                DiagnosedAt = diagnosedAt,
                DayBucket = new DateTimeOffset(diagnosedAt.UtcDateTime.Date, TimeSpan.Zero),
                ExpiresAt = diagnosedAt.AddDays(Math.Max(1, retentionDays)),
            };

            await using var session = _store.LightweightSession();
            session.Store(doc);
            await session.SaveChangesAsync(ct);

            _metrics.RecordPersistSuccess(diagnosis.Primary);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Persistence is best-effort. The classifier's live output
            // (returned up the call stack) is authoritative for the
            // hint-ladder decision — we log and metricise the failure
            // so it's visible, but never bubble.
            _logger.LogWarning(ex,
                "StuckDiagnosis persist failed for session {Session} question {Question} (non-fatal)",
                sessionId, questionId);
            _metrics.RecordPersistFailure(ex.GetType().Name);
        }
    }

    public async Task<IReadOnlyList<StuckDiagnosisDocument>> GetRecentByQuestionAsync(
        string questionId, int limit, CancellationToken ct = default)
    {
        if (limit <= 0) return Array.Empty<StuckDiagnosisDocument>();
        await using var session = _store.QuerySession();
        var docs = await session.Query<StuckDiagnosisDocument>()
            .Where(d => d.QuestionId == questionId)
            .OrderByDescending(d => d.DiagnosedAt)
            .Take(limit)
            .ToListAsync(ct);
        return docs.ToList();
    }
}
