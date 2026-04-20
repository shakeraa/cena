// =============================================================================
// Cena Platform -- Session Lifecycle REST Endpoints (SES-002)
// Student-facing REST endpoints for session history, resume, and replay.
// All reads go directly to Marten; resume sends a NATS command to the actor.
// =============================================================================

using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Cena.Actors.Accommodations;
using Cena.Actors.Bus;
using Cena.Actors.Diagnosis;
using Cena.Actors.Events;
using Cena.Actors.Mastery;
using Cena.Actors.Projections;
using Cena.Actors.Questions;
using Cena.Actors.Services;
using Cena.Actors.Serving;
using Cena.Actors.Tutoring;
using Cena.Api.Contracts.Sessions;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Localization;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NATS.Client.Core;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Cena.Api.Host.Endpoints;

public static class SessionEndpoints
{
    private sealed class SessionLogMarker { }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    // HARDEN SessionEndpoints: Removed Phase 1b in-memory state tracking
    // - Deleted ConcurrentDictionary<string, SessionState> SessionStates
    // - Deleted CannedQuestion[] literal
    // Now uses LearningSessionQueueProjection + QuestionDocument from Marten

    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sessions")
            .WithTags("Sessions")
            .RequireAuthorization()
            .RequireRateLimiting("api");

        // POST /api/sessions/start — start a new learning session (STB-01)
        // FIND-pedagogy-016: inject IAdaptiveQuestionPool to seed the question queue
        // prr-149: inject ISessionPlanGenerator/Writer/Notifier so every new
        //   session gets an AdaptiveScheduler plan written to its session
        //   stream before the /start call returns.
        group.MapPost("/start", async (
            HttpContext ctx,
            IDocumentStore store,
            [FromServices] IAdaptiveQuestionPool adaptivePool,
            [FromServices] Cena.Actors.Sessions.ISessionPlanGenerator planGenerator,
            [FromServices] Cena.Actors.Sessions.ISessionPlanWriter planWriter,
            [FromServices] Cena.Actors.Sessions.ISessionPlanNotifier planNotifier,
            ILogger<SessionLogMarker> logger,
            SessionStartRequest request) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            // Validate request
            if (request.Subjects.Length == 0)
                return Results.BadRequest(new { error = "At least one subject is required" });

            if (request.DurationMinutes is not (5 or 10 or 15 or 30 or 45 or 60))
                return Results.BadRequest(new { error = "Invalid duration. Must be 5, 10, 15, 30, 45, or 60 minutes" });

            if (request.Mode is not ("practice" or "challenge" or "review" or "diagnostic"))
                return Results.BadRequest(new { error = "Invalid mode. Must be 'practice', 'challenge', 'review', or 'diagnostic'" });

            await using var session = store.LightweightSession();

            // Check for existing active session (idempotency)
            var existingActive = await session.LoadAsync<ActiveSessionSnapshot>(studentId);
            if (existingActive is not null)
            {
                // Return existing session instead of creating duplicate
                return Results.Ok(new SessionStartResponse(
                    SessionId: existingActive.SessionId,
                    HubGroupName: $"session-{existingActive.SessionId}",
                    FirstQuestionId: existingActive.CurrentQuestionId));
            }

            // Create new session
            var sessionId = Guid.NewGuid().ToString("N")[..16];
            var startedAt = DateTimeOffset.UtcNow;

            var startedEvent = new LearningSessionStarted_V1(
                StudentId: studentId,
                SessionId: sessionId,
                Subjects: request.Subjects,
                Mode: request.Mode,
                DurationMinutes: request.DurationMinutes,
                StartedAt: startedAt);

            // Append event to student stream
            session.Events.Append(studentId, startedEvent);

            // Create active session snapshot
            var activeSnapshot = new ActiveSessionSnapshot
            {
                Id = studentId,
                StudentId = studentId,
                SessionId = sessionId,
                Subjects = request.Subjects,
                Mode = request.Mode,
                DurationMinutes = request.DurationMinutes,
                StartedAt = startedAt.UtcDateTime
            };
            session.Store(activeSnapshot);

            await session.SaveChangesAsync();

            // ── FIND-pedagogy-016: Seed the adaptive question queue ──
            // InitializeSessionAsync creates the LearningSessionQueueProjection
            // document in Marten. Then we load a MartenQuestionPool for the
            // requested subjects and call GetNextQuestionAsync to trigger the
            // first refill (5 questions). The first question's ID is returned
            // in the response so GET /current-question never hits an empty queue.
            string? firstQuestionId = null;
            try
            {
                var queueProjection = await adaptivePool.InitializeSessionAsync(
                    studentId, sessionId, request.Subjects, request.Mode);

                // RDY-057c — load onboarding self-assessment and copy any
                // self-reported anxious concepts onto the session queue
                // so the selection path can tie-break with them. Empty
                // list = no assessment / no anxious topics. Failure here
                // is non-fatal — the session continues without the
                // affective signal.
                try
                {
                    var selfAssessment = await session.LoadAsync<Cena.Infrastructure.Documents.OnboardingSelfAssessmentDocument>(studentId);
                    if (selfAssessment is not null && !selfAssessment.Skipped)
                    {
                        var anxious = selfAssessment.TopicFeelings
                            .Where(kvp => kvp.Value == Cena.Infrastructure.Documents.TopicFeeling.Anxious)
                            .Select(kvp => kvp.Key)
                            .ToList();
                        if (anxious.Count > 0)
                        {
                            queueProjection.AnxiousConceptIds = anxious;
                            session.Store(queueProjection);
                            await session.SaveChangesAsync();
                            logger.LogInformation(
                                "[RDY-057c] Session {SessionId} inherited {Count} anxious concept(s) from self-assessment",
                                sessionId, anxious.Count);
                        }
                    }
                }
                catch (Exception sxErr)
                {
                    logger.LogWarning(sxErr,
                        "[RDY-057c] Failed to load self-assessment for session {SessionId} (non-fatal)",
                        sessionId);
                }

                // Load a Marten-backed question pool for the requested subjects
                var pool = await MartenQuestionPool.LoadAsync(
                    store, request.Subjects, logger);

                if (pool.ItemCount > 0)
                {
                    var firstQuestion = await adaptivePool.GetNextQuestionAsync(
                        sessionId, pool);
                    firstQuestionId = firstQuestion?.QuestionId;
                }
                else
                {
                    logger.LogWarning(
                        "FIND-pedagogy-016: No published questions found for subjects [{Subjects}]. " +
                        "Session {SessionId} started with empty queue.",
                        string.Join(", ", request.Subjects), sessionId);
                }
            }
            catch (Exception ex)
            {
                // Log but do not fail the session start — the queue can be
                // seeded lazily on the first GET /current-question call.
                logger.LogError(ex,
                    "FIND-pedagogy-016: Failed to seed question queue for session {SessionId}. " +
                    "GET /current-question will retry seeding.",
                    sessionId);
            }

            // ── prr-149: compute the AdaptiveScheduler plan for this session ──
            //
            // Session-scoped — plan goes on the `session-{id}` stream + read
            // doc, never on StudentProfileSnapshot. Failure here logs + swallows:
            // the session is already created and the student UI will work
            // without a plan (the GET /plan endpoint returns 404 until the plan
            // is written). A stale plan beats a blocked session.
            try
            {
                var genResult = await planGenerator.GenerateAsync(
                    studentAnonId: studentId,
                    sessionId: sessionId,
                    nowUtc: DateTimeOffset.UtcNow,
                    ct: ctx.RequestAborted);
                await planWriter.WriteAsync(
                    genResult.Snapshot, genResult.InputsSource, ctx.RequestAborted);
                await planNotifier.NotifyAsync(
                    genResult.Snapshot, genResult.InputsSource, ctx.RequestAborted);

                logger.LogInformation(
                    "prr-149: session {SessionId} plan generated ({TopicCount} topics, source={Source})",
                    sessionId,
                    genResult.Snapshot.PriorityOrdered.Length,
                    genResult.InputsSource);
            }
            catch (Exception planEx)
            {
                // Observability only — plan is a non-critical enrichment of the
                // session surface. The student can still answer questions; the
                // trajectory UI just shows "no plan yet" until the next session.
                logger.LogWarning(planEx,
                    "prr-149: scheduler plan generation failed for session {SessionId}. " +
                    "Session continues; GET /plan will 404 until the next session.",
                    sessionId);
            }

            return Results.Ok(new SessionStartResponse(
                SessionId: sessionId,
                HubGroupName: $"session-{sessionId}",
                FirstQuestionId: firstQuestionId));
        })
        .WithName("StartSession");

        // GET /api/sessions/active — check if student has an active session (STB-01)
        group.MapGet("/active", async (
            HttpContext ctx,
            IDocumentStore store) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            await using var session = store.QuerySession();
            var active = await session.LoadAsync<ActiveSessionSnapshot>(studentId);

            if (active is null)
                return Results.NotFound(new { error = "No active session" });

            var dto = new ActiveSessionDto(
                SessionId: active.SessionId,
                Subjects: active.Subjects,
                Mode: active.Mode,
                StartedAt: active.StartedAt,
                DurationMinutes: active.DurationMinutes,
                ProgressPercent: active.GetProgressPercent(DateTime.UtcNow),
                CurrentQuestionId: active.CurrentQuestionId);

            return Results.Ok(dto);
        })
        .WithName("GetActiveSessionV2")
    .Produces<ActiveSessionDto>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/sessions — list student's sessions (paginated, filterable)
        group.MapGet("/", async (HttpContext ctx, IDocumentStore store, string? subject, DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            var validPage     = Math.Max(1, page ?? 1);
            var validPageSize = Math.Clamp(pageSize ?? 20, 1, 100);

            await using var session = store.QuerySession();
            var query = session.Query<TutoringSessionDocument>()
                .Where(d => d.StudentId == studentId)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(subject))
                query = query.Where(d => d.Subject == subject);

            if (from.HasValue)
                query = query.Where(d => d.StartedAt >= from.Value);

            if (to.HasValue)
                query = query.Where(d => d.StartedAt <= to.Value);

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(d => d.StartedAt)
                .Skip((validPage - 1) * validPageSize)
                .Take(validPageSize)
                .ToListAsync();

            var dtos = items.Select(MapToSummary).ToList();
            return Results.Ok(new SessionListResponse(dtos, totalCount, validPage, validPageSize));
        })
        .WithName("GetStudentSessions")
    .Produces<SessionListResponse>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/sessions/{sessionId} — full session detail
        group.MapGet("/{sessionId}", async (
            string sessionId,
            HttpContext ctx,
            IDocumentStore store,
            [FromServices] IFlowStateService flowState,
            [FromServices] ICognitiveLoadService cognitiveLoad) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            await using var session = store.QuerySession();
            var doc = await session.Query<TutoringSessionDocument>()
                .FirstOrDefaultAsync(d => d.Id == sessionId || d.SessionId == sessionId);

            if (doc is null)
                return Results.NotFound(new { error = $"Session '{sessionId}' not found" });

            // IDOR: ensure the session belongs to the requesting student
            if (doc.StudentId != studentId)
                return Results.Forbid();

            // FIND-arch-023: Use projection instead of event stream query
            var attemptHistory = await session.LoadAsync<SessionAttemptHistoryDocument>(doc.SessionId);

            var questionsAttempted = attemptHistory?.TotalAttempts ?? 0;
            var questionsCorrect = attemptHistory?.CorrectAttempts ?? 0;
            var accuracy = attemptHistory?.Accuracy ?? 0;
            var masteryDeltas = attemptHistory?.MasteryDeltas ?? new Dictionary<string, double>();

            var durationSeconds = doc.EndedAt.HasValue
                ? (int)(doc.EndedAt.Value - doc.StartedAt).TotalSeconds
                : (int)(DateTimeOffset.UtcNow - doc.StartedAt).TotalSeconds;

            var status = doc.EndedAt.HasValue ? "completed" : "active";

            // RDY-034 slice 2: compute real fatigue + flow state from session signals.
            // Replaces the hardcoded FatigueScore=0.0 with the ICognitiveLoadService
            // 3-factor model, and surfaces the authoritative flow state so clients
            // agree with the backend instead of recomputing locally.
            var (flowAssessment, fatigueScore) = ComputeSessionFlowState(
                attemptHistory,
                doc.StartedAt,
                doc.EndedAt,
                flowState,
                cognitiveLoad);

            return Results.Ok(new SessionDetailDto(
                Id: doc.Id,
                SessionId: doc.SessionId,
                Subject: doc.Subject,
                ConceptId: doc.ConceptId,
                Methodology: doc.Methodology,
                Status: status,
                QuestionsAttempted: questionsAttempted,
                QuestionsCorrect: questionsCorrect,
                Accuracy: Math.Round(accuracy, 3),
                FatigueScore: Math.Round(fatigueScore, 3),
                DurationSeconds: durationSeconds,
                StartedAt: doc.StartedAt,
                EndedAt: doc.EndedAt,
                MasteryDeltas: masteryDeltas,
                FlowState: FlowStateEndpoints.ToResponse(flowAssessment)));
        })
        .WithName("GetSessionDetail")
    .Produces<SessionDetailDto>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/sessions/{sessionId}/history — session question history (STB-01c)
        group.MapGet("/{sessionId}/history", async (
            string sessionId,
            HttpContext ctx,
            IDocumentStore store) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            await using var session = store.QuerySession();
            
            // Load the session queue projection
            var queue = await session.LoadAsync<LearningSessionQueueProjection>(sessionId);
            if (queue == null)
            {
                // Fall back to querying by sessionId field if Id doesn't match
                queue = await session.Query<LearningSessionQueueProjection>()
                    .FirstOrDefaultAsync(q => q.SessionId == sessionId);
            }

            if (queue == null)
                return Results.NotFound(new { error = $"Session '{sessionId}' not found" });

            if (queue.StudentId != studentId)
                return Results.Forbid();

            // Build history response
            var history = new SessionHistoryDto(
                SessionId: queue.SessionId,
                StartedAt: queue.StartedAt,
                EndedAt: queue.EndedAt,
                Mode: queue.Mode,
                Subjects: queue.Subjects,
                TotalQuestionsAttempted: queue.TotalQuestionsAttempted,
                CorrectAnswers: queue.CorrectAnswers,
                Accuracy: Math.Round(queue.GetAccuracy(), 3),
                CurrentStreak: queue.StreakCount,
                QuestionHistory: queue.AnsweredQuestions.Select(h => new QuestionHistoryItemDto(
                    QuestionId: h.QuestionId,
                    AnsweredAt: h.AnsweredAt,
                    IsCorrect: h.IsCorrect,
                    TimeSpentSeconds: h.TimeSpentSeconds,
                    SelectedOption: h.SelectedOption)).ToList(),
                RemainingInQueue: queue.QuestionQueue.Count);

            return Results.Ok(history);
        })
        .WithName("GetSessionHistory");

        // GET /api/sessions/{sessionId}/replay — question-by-question replay
        group.MapGet("/{sessionId}/replay", async (
            string sessionId,
            HttpContext ctx,
            IDocumentStore store) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            await using var session = store.QuerySession();
            var doc = await session.Query<TutoringSessionDocument>()
                .FirstOrDefaultAsync(d => d.Id == sessionId || d.SessionId == sessionId);

            if (doc is null)
                return Results.NotFound(new { error = $"Session '{sessionId}' not found" });

            if (doc.StudentId != studentId)
                return Results.Forbid();

            // FIND-arch-023: Use projection instead of event stream query
            var attemptHistory = await session.LoadAsync<SessionAttemptHistoryDocument>(doc.SessionId);
            var attempts = attemptHistory?.Attempts
                .OrderBy(a => a.Timestamp)
                .Select((a, i) => new QuestionAttemptDto(
                    Sequence: i + 1,
                    QuestionId: a.QuestionId,
                    ConceptId: a.ConceptId,
                    QuestionType: a.QuestionType,
                    IsCorrect: a.IsCorrect,
                    ResponseTimeMs: a.ResponseTimeMs,
                    HintCountUsed: a.HintCountUsed,
                    WasSkipped: a.WasSkipped,
                    PriorMastery: Math.Round(a.PriorMastery, 4),
                    PosteriorMastery: Math.Round(a.PosteriorMastery, 4),
                    Timestamp: a.Timestamp))
                .ToList() ?? new List<QuestionAttemptDto>();

            return Results.Ok(new SessionReplayDto(
                SessionId: doc.SessionId,
                Subject: doc.Subject,
                Methodology: doc.Methodology,
                StartedAt: doc.StartedAt,
                EndedAt: doc.EndedAt,
                Attempts: attempts));
        })
        .WithName("GetSessionReplay")
    .Produces<SessionReplayDto>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // POST /api/sessions/{sessionId}/resume — resume an interrupted session
        group.MapPost("/{sessionId}/resume", async (
            string sessionId,
            HttpContext ctx,
            IDocumentStore store,
            INatsConnection nats) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            await using var querySession = store.QuerySession();
            var doc = await querySession.Query<TutoringSessionDocument>()
                .FirstOrDefaultAsync(d => d.Id == sessionId || d.SessionId == sessionId);

            if (doc is null)
                return Results.NotFound(new { error = $"Session '{sessionId}' not found" });

            if (doc.StudentId != studentId)
                return Results.Forbid();

            if (doc.EndedAt.HasValue)
                return Results.Conflict(new { error = $"Session '{sessionId}' is already completed and cannot be resumed" });

            // 24-hour expiry check
            var hoursSinceStart = (DateTimeOffset.UtcNow - doc.StartedAt).TotalHours;
            if (hoursSinceStart > 24)
            {
                // Return 410 Gone with a summary of the expired session
                var durationSeconds = (int)(DateTimeOffset.UtcNow - doc.StartedAt).TotalSeconds;
                return Results.Json(
                    new
                    {
                        error = "Session expired. Cannot resume sessions more than 24 hours after they started.",
                        summary = new
                        {
                            sessionId = doc.SessionId,
                            subject   = doc.Subject,
                            startedAt = doc.StartedAt,
                            turns     = doc.TotalTurns,
                            durationSeconds
                        }
                    },
                    statusCode: 410);
            }

            // Publish resume command to NATS — the actor host will handle it
            var busCmd  = new BusResumeSession(StudentId: studentId, SessionId: doc.SessionId);
            var envelope = BusEnvelope<BusResumeSession>.Create(
                NatsSubjects.SessionResume, busCmd, "api-host");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOpts);
            await nats.PublishAsync(NatsSubjects.SessionResume, bytes);

            return Results.Ok(new
            {
                sessionId  = doc.SessionId,
                subject    = doc.Subject,
                methodology = doc.Methodology,
                questionsAttempted = doc.TotalTurns,
                startedAt  = doc.StartedAt,
                resumedAt  = DateTimeOffset.UtcNow,
                message    = "Resume command published. Connect via SignalR to receive the next question."
            });
        })
        .WithName("ResumeSession")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status409Conflict)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // ═════════════════════════════════════════════════════════════════════════
        // STB-01b: In-Session Question + Answer Endpoints
        // ═════════════════════════════════════════════════════════════════════════

        // GET /api/sessions/{sessionId}/current-question — get current question
        // FIND-pedagogy-016: inject IAdaptiveQuestionPool for lazy refill
        // PRR-151 R-22: inject IAccommodationProfileService so the DTO
        // carries the parent-consented accommodation flags (TTS,
        // extended-time multiplier, distraction-reduced / graph-paper).
        group.MapGet("/{sessionId}/current-question", async (string sessionId, HttpContext ctx, IDocumentStore store, [FromServices] IQuestionBank questionBank, [FromServices] IScaffoldingService scaffoldingService, [FromServices] IAdaptiveQuestionPool adaptivePool, [FromServices] IAccommodationProfileService accommodationProfiles, ILogger<SessionLogMarker> logger) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            await using var session = store.QuerySession();

            // Get the session queue projection
            var queue = await session.LoadAsync<LearningSessionQueueProjection>(sessionId);
            if (queue == null)
                return Results.NotFound(new { error = "Session not found" });

            // Check if session has ended
            if (queue.EndedAt.HasValue)
            {
                return Results.Ok(new SessionQuestionDto(
                    QuestionId: "completed",
                    QuestionIndex: queue.TotalQuestionsAttempted,
                    TotalQuestions: queue.TotalQuestionsAttempted,
                    Prompt: "Session completed! No more questions.",
                    QuestionType: "completed",
                    Choices: Array.Empty<string>(),
                    Subject: "",
                    ExpectedTimeSeconds: 0));
            }

            // FIND-pedagogy-016: If queue is empty but session not ended, try refill
            var currentQuestion = queue.PeekNext();
            if (currentQuestion == null)
            {
                // Attempt adaptive refill before declaring completion
                try
                {
                    var pool = await MartenQuestionPool.LoadAsync(
                        store, queue.Subjects, logger);

                    if (pool.ItemCount > 0)
                    {
                        var refilled = await adaptivePool.GetNextQuestionAsync(
                            sessionId, pool);
                        if (refilled != null)
                        {
                            // Re-load queue after refill (GetNextQuestionAsync persisted it)
                            queue = await session.LoadAsync<LearningSessionQueueProjection>(sessionId);
                            currentQuestion = queue?.PeekNext();
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "FIND-pedagogy-016: Refill failed for session {SessionId}",
                        sessionId);
                }

                // If still empty after refill attempt, session is genuinely done
                if (currentQuestion == null)
                {
                    logger.LogInformation(
                        "FIND-pedagogy-016: Session {SessionId} exhausted question pool " +
                        "after {Attempted} questions. Returning completed.",
                        sessionId, queue?.TotalQuestionsAttempted ?? 0);

                    return Results.Ok(new SessionQuestionDto(
                        QuestionId: "completed",
                        QuestionIndex: queue?.TotalQuestionsAttempted ?? 0,
                        TotalQuestions: queue?.TotalQuestionsAttempted ?? 0,
                        Prompt: "Session completed! No more questions.",
                        QuestionType: "completed",
                        Choices: Array.Empty<string>(),
                        Subject: "",
                        ExpectedTimeSeconds: 0));
                }
            }

            // Dequeue the question for display
            queue!.DequeueNext(DateTime.UtcNow);

            // Load full question details from question bank
            var questionDoc = await questionBank.GetQuestionAsync(currentQuestion.QuestionId);
            if (questionDoc == null)
                return Results.NotFound(new { error = "Question not found" });
            var questionMeta = await session.LoadAsync<QuestionReadModel>(currentQuestion.QuestionId);

            var localeDecision = LocaleFallback.Resolve(
                ctx.User.FindFirstValue("locale"),
                questionMeta);
            if (localeDecision.UsedFallback)
            {
                await AppendQuestionFallbackLanguageAsync(
                    store,
                    studentId,
                    sessionId,
                    currentQuestion.QuestionId,
                    localeDecision);
            }

            // Get student's concept mastery from queue.ConceptMasterySnapshot
            var effectiveMastery = (float)queue.ConceptMasterySnapshot.GetValueOrDefault(questionDoc.ConceptId, 0.0);

            // Calculate PSI (prerequisite satisfaction index) if prerequisites available
            float psi = 1.0f;
            if (questionDoc.Prerequisites?.Count > 0)
            {
                var prereqMasteries = questionDoc.Prerequisites
                    .Select(p => queue.ConceptMasterySnapshot.GetValueOrDefault(p, 0.0))
                    .ToList();
                psi = prereqMasteries.Count > 0
                    ? (float)prereqMasteries.Average()
                    : 1.0f;
            }

            // Determine scaffolding level and metadata
            var level = scaffoldingService.DetermineLevel(effectiveMastery, psi);
            var metadata = scaffoldingService.GetScaffoldingMetadata(level);

            // Get hints used for this question
            var hintsUsed = queue.HintsUsedByQuestion.GetValueOrDefault(questionDoc.QuestionId, 0);

            // ═════════════════════════════════════════════════════════════════
            // PRR-151 R-22 — consult the student's accommodation profile and
            // populate the render-time flags on the question DTO. Before this
            // wiring, the parent-console endpoint persisted
            // AccommodationProfileAssignedV1 events but NO session-rendering
            // code path consulted them, so parents could sign legal consent
            // for TTS / extended-time / distraction-reduced layout and the
            // platform would never technically render the accommodation —
            // a Ministry-reportable compliance defect.
            //
            // The service returns AccommodationProfile.Default (everything
            // false, multiplier 1.0) when the student has never had a
            // profile assigned, so this is a pure additive wiring that does
            // not change behaviour for students without accommodations.
            // ═════════════════════════════════════════════════════════════════
            var accommodations = await accommodationProfiles.GetCurrentAsync(studentId, ctx.RequestAborted);
            var ttsEnabled = accommodations.TtsForProblemStatementsEnabled;
            var timeMultiplier = accommodations.ExtendedTimeMultiplier;
            var graphPaperRequired = accommodations.DistractionReducedLayoutEnabled;
            var noComparative = accommodations.NoComparativeStatsRequired;
            const int baseExpectedTimeSeconds = 60;
            var expectedTimeSeconds = (int)Math.Round(baseExpectedTimeSeconds * timeMultiplier);

            if (ttsEnabled || graphPaperRequired || timeMultiplier > 1.0 || noComparative)
            {
                logger.LogInformation(
                    "[PRR-151 R-22] Session {SessionId} question {QuestionId}: "
                    + "applying accommodations tts={Tts} graphPaper={GraphPaper} "
                    + "timeMultiplier={TimeMultiplier:F2} noComparative={NoComparative}",
                    sessionId, questionDoc.QuestionId,
                    ttsEnabled, graphPaperRequired, timeMultiplier, noComparative);
            }

            return Results.Ok(new SessionQuestionDto(
                QuestionId: questionDoc.QuestionId,
                QuestionIndex: queue.TotalQuestionsAttempted + 1,
                TotalQuestions: queue.TotalQuestionsAttempted + queue.QuestionQueue.Count + 1,
                Prompt: questionDoc.Prompt,
                QuestionType: questionDoc.QuestionType,
                Choices: questionDoc.Choices ?? Array.Empty<string>(),
                Subject: questionDoc.Subject,
                ExpectedTimeSeconds: expectedTimeSeconds,
                ScaffoldingLevel: level.ToString(),
                WorkedExample: metadata.ShowWorkedExample ? questionDoc.WorkedExample : null,
                HintsAvailable: metadata.MaxHints,
                HintsRemaining: Math.Max(0, metadata.MaxHints - hintsUsed),
                TtsEnabled: ttsEnabled,
                ExtendedTimeMultiplier: timeMultiplier,
                GraphPaperRequired: graphPaperRequired,
                NoComparativeStats: noComparative));
        })
        .WithName("GetCurrentQuestion")
    .Produces<SessionQuestionDto>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status409Conflict)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // POST /api/sessions/{sessionId}/answer — submit an answer
        group.MapPost("/{sessionId}/answer", async (string sessionId, HttpContext ctx, IDocumentStore store, [FromServices] IQuestionBank questionBank, [FromServices] IBktService bktService, [FromServices] IErrorClassificationService errorClassifier, [FromServices] IMisconceptionDetectionService misconceptionDetector, [FromServices] IEloDifficultyService eloService, [FromServices] ILoggerFactory loggerFactory, [FromServices] Cena.Infrastructure.Compliance.EncryptedFieldAccessor encryptedFieldAccessor, SessionAnswerRequest request) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            if (string.IsNullOrWhiteSpace(request.QuestionId))
                return Results.BadRequest(new { error = "QuestionId is required" });

            await using var session = store.LightweightSession();

            // Get session queue
            var queue = await session.LoadAsync<LearningSessionQueueProjection>(sessionId);
            if (queue == null)
                return Results.NotFound(new { error = "Session not found" });

            if (queue.EndedAt.HasValue)
                return Results.Conflict(new { error = "Session already completed" });

            // Get current question
            var currentQuestion = queue.PeekNext();
            if (currentQuestion == null)
                return Results.Conflict(new { error = "No current question" });

            // Load question details
            var questionDoc = await questionBank.GetQuestionAsync(currentQuestion.QuestionId);
            if (questionDoc == null)
                return Results.NotFound(new { error = "Question not found" });

            // ─── RDY-026: Arabic input normalization ───
            // Normalize Arabic letters/digits/operators before comparison.
            // Preserve raw input for misconception analysis.
            var rawStudentInput = request.Answer?.Trim() ?? "";
            var normalizedAnswer = rawStudentInput;
            if (ArabicMathNormalizer.NeedsNormalization(rawStudentInput))
            {
                var normContext = questionDoc.Subject?.Equals("physics", StringComparison.OrdinalIgnoreCase) == true
                    ? NormalizationContext.Physics
                    : NormalizationContext.Mathematics;
                normalizedAnswer = ArabicMathNormalizer.Normalize(rawStudentInput, normContext);

                // RDY-026: Structured log for analytics and debugging
                var normLogger = loggerFactory.CreateLogger("SessionEndpoints.InputNormalization");
                normLogger.LogInformation(
                    "[INPUT_NORMALIZED] StudentId={StudentId} SessionId={SessionId} QuestionId={QuestionId} " +
                    "Context={Context} Raw={RawInput} Normalized={NormalizedInput}",
                    studentId, sessionId, currentQuestion.QuestionId,
                    normContext, rawStudentInput, normalizedAnswer);
            }

            var isCorrect = string.Equals(normalizedAnswer, questionDoc.CorrectAnswer, StringComparison.OrdinalIgnoreCase);

            // Record answer in queue
            queue.RecordAnswer(currentQuestion.QuestionId, isCorrect, TimeSpan.FromMilliseconds(request.TimeSpentMs), normalizedAnswer, DateTime.UtcNow);

            // Append QuestionAnsweredInSession event
            var answeredEvent = new QuestionAnsweredInSession_V1(
                StudentId: studentId,
                SessionId: sessionId,
                QuestionId: currentQuestion.QuestionId,
                IsCorrect: isCorrect,
                TimeSpentSeconds: (int)(request.TimeSpentMs / 1000),
                SelectedOption: request.Answer,
                AnsweredAt: DateTimeOffset.UtcNow);

            session.Events.Append(studentId, answeredEvent);

            // ─── FIND-pedagogy-003: Real BKT posterior via BktService ───
            //
            // The prior comes from the in-flight session's concept mastery
            // snapshot (or 0.5 — neutral — when the student has never seen
            // this concept). The BKT parameters are sourced from the question
            // document (per-concept slip/guess/learning rates) with a fallback
            // to BktParameters.Default. The posterior is written BOTH into the
            // ConceptAttempted_V1 event AND back into the queue's snapshot so
            // the next question on the same concept uses the updated prior.
            var priorMastery = queue.ConceptMasterySnapshot.GetValueOrDefault(
                questionDoc.ConceptId, 0.5);

            var bktParams = BuildBktParameters(questionDoc);
            var bktResult = bktService.Update(new BktUpdateInput(
                PriorMastery: priorMastery,
                IsCorrect: isCorrect,
                Parameters: bktParams));
            var posteriorMastery = bktResult.PosteriorMastery;

            // Update in-session snapshot so the next attempt sees the new prior.
            queue.ConceptMasterySnapshot[questionDoc.ConceptId] = posteriorMastery;
            session.Store(queue);

            // ─── FIND-pedagogy-002/007: ConceptAttempted_V1 on EVERY answer with ErrorType ───
            //
            // Previously this append was wrapped in `if (isCorrect)` and the
            // IsCorrect field was hard-coded to `true`. Both bugs broke the
            // actor-side BKT pipeline (BktTracer.Update has a correct
            // P(L|incorrect) branch that was never fed). The append now runs
            // for every answer and carries the real outcome. The XP append
            // (below) remains gated on isCorrect — this is intentional.
            //
            // FIND-pedagogy-007: On wrong answers, classify the error type using
            // LLM-based ErrorClassificationService. On correct answers, ErrorType is "None".
            var errorType = isCorrect 
                ? "None" 
                : await ClassifyErrorAsync(errorClassifier, questionDoc, normalizedAnswer, priorMastery);

            var conceptAttempt = BuildConceptAttempt(
                studentId: studentId,
                sessionId: sessionId,
                questionDoc: questionDoc,
                currentQuestionId: currentQuestion.QuestionId,
                methodology: queue.Mode,
                isCorrect: isCorrect,
                responseTimeMs: request.TimeSpentMs,
                priorMastery: priorMastery,
                posteriorMastery: posteriorMastery,
                errorType: errorType,
                rawStudentInput: rawStudentInput != normalizedAnswer ? rawStudentInput : null);

            // STB-03b: Append XP event and concept attempt.
            //
            // FIND-data-007: StudentProfileSnapshot is registered as an Inline
            // SnapshotProjection in MartenConfiguration (SnapshotLifecycle.Inline).
            // Marten rebuilds the snapshot from the event stream on every
            // SaveChangesAsync by replaying Apply(...) handlers. The endpoint MUST
            // NOT manually Store() a StudentProfileSnapshot — doing so races the
            // inline projection daemon and the write order is undefined.
            //
            // All mutations to TotalXp / ConceptMastery / etc. must flow through
            // event append + Apply handler. Every event going to the student's
            // stream is appended in a single call so Marten's inline projection
            // sees them as one rebuild unit.
            // FIND-pedagogy-009 (enriched): load the profile ONCE up front.
            // It feeds both the absolute TotalXp stamp on the XP event (when
            // isCorrect) and the Elo dual update below (regardless of
            // correctness). Wrong-answer path pays one extra doc load to
            // avoid two round-trips.
            var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);

            if (isCorrect)
            {
                var currentXp = profile?.TotalXp ?? 0;
                var newTotalXp = currentXp + 10;

                // XP event — absolute TotalXp snapshot
                var xpEvent = new XpAwarded_V1(
                    StudentId: studentId,
                    XpAmount: 10,
                    Source: "correct_answer",
                    TotalXp: newTotalXp,
                    DifficultyLevel: questionDoc.Difficulty,
                    DifficultyMultiplier: 1);

                // Correct path: ConceptAttempted + XpAwarded in one Append.
                session.Events.Append(studentId, conceptAttempt, xpEvent);
            }
            else
            {
                // RDY-033b + ADR-0003: On a wrong answer, run the CAS-backed
                // misconception detector against the question/answer triple.
                // When a buggy rule fires with confidence ≥ threshold, append
                // a MisconceptionDetected_V1 event alongside ConceptAttempted.
                //
                // The event is [MlExcluded] and session-scoped (ADR-0003);
                // the 100 ms budget inside the matcher engine bounds any
                // latency impact on the /answer path.
                var misconception = DetectMisconception(
                    misconceptionDetector,
                    questionDoc,
                    rawStudentInput,
                    normalizedAnswer,
                    errorType,
                    loggerFactory.CreateLogger("SessionEndpoints.Misconception"));

                if (misconception is { Detected: true, BuggyRuleId: not null })
                {
                    // ADR-0038: encrypt StudentAnswer PII under per-subject key.
                    var misconceptionEvent = new MisconceptionDetected_V1(
                        StudentId: studentId,
                        SessionId: sessionId,
                        BuggyRuleId: misconception.BuggyRuleId,
                        TopicId: questionDoc.ConceptId ?? string.Empty,
                        QuestionId: currentQuestion.QuestionId,
                        StudentAnswer: await encryptedFieldAccessor.EncryptAsync(rawStudentInput ?? normalizedAnswer ?? string.Empty, studentId) ?? (rawStudentInput ?? normalizedAnswer ?? string.Empty),
                        ExpectedPattern: misconception.CounterExample ?? string.Empty,
                        DetectedAt: DateTimeOffset.UtcNow);

                    session.Events.Append(studentId, conceptAttempt, misconceptionEvent);
                }
                else
                {
                    // Wrong path (no misconception detected): ConceptAttempted only.
                    session.Events.Append(studentId, conceptAttempt);
                }
            }

            // FIND-pedagogy-009 (enriched): dual Elo update on the SAME
            // session — one SaveChangesAsync, no race. The service reads the
            // REAL StudentProfileSnapshot.EloRating (not priorMastery, which
            // the original pedagogy-009 passed as theta — BKT posterior is
            // on [0,1] and Elo is on [500,2500], so every expected value
            // collapsed to ~0). The service appends StudentEloRatingUpdated_V1
            // to the student stream AND stages the question document on the
            // caller's session (cf. the FIND-data-007 CQRS lesson).
            if (profile is not null)
            {
                eloService.UpdateRatings(
                    session: session,
                    profile: profile,
                    questionDoc: questionDoc,
                    isCorrect: isCorrect,
                    timestamp: DateTimeOffset.UtcNow);
            }

            await session.SaveChangesAsync();

            // FIND-pedagogy-016: Refill queue if needed after answer
            if (queue.NeedsRefill)
            {
                try
                {
                    var adaptivePool = ctx.RequestServices.GetRequiredService<IAdaptiveQuestionPool>();
                    var answerLogger = loggerFactory.CreateLogger("SessionEndpoints.Answer");
                    var pool = await MartenQuestionPool.LoadAsync(
                        store, queue.Subjects, answerLogger);

                    if (pool.ItemCount > 0)
                    {
                        // GetNextQuestionAsync will refill and persist
                        await adaptivePool.GetNextQuestionAsync(sessionId, pool);

                        // Reload queue after refill
                        queue = (await session.LoadAsync<LearningSessionQueueProjection>(sessionId))!;
                    }
                }
                catch (Exception ex)
                {
                    var answerLogger = loggerFactory.CreateLogger("SessionEndpoints.Answer");
                    answerLogger.LogError(ex,
                        "FIND-pedagogy-016: Queue refill failed after answer for session {SessionId}",
                        sessionId);
                }
            }

            // Determine next question ID
            string? nextQuestionId = null;
            var nextQuestion = queue.PeekNext();
            if (nextQuestion != null)
            {
                nextQuestionId = nextQuestion.QuestionId;
            }

            // ─── FIND-pedagogy-001: Formative feedback with explanation ───
            // FIND-pedagogy-013: Extract student locale from claims for localized content
            var studentLocale = ctx.User.FindFirstValue("locale") ?? "en";
            var logger = loggerFactory.CreateLogger<SessionLogMarker>();
            var response = BuildAnswerFeedback(
                questionDoc: questionDoc,
                studentAnswer: normalizedAnswer,
                isCorrect: isCorrect,
                priorMastery: priorMastery,
                posteriorMastery: posteriorMastery,
                nextQuestionId: nextQuestionId,
                studentLocale: studentLocale,
                logger: logger);

            // FIND-pedagogy-017: structured log for re-regression detection.
            // If the Feedback field ever ships non-empty again, this warning
            // fires in production telemetry so the bilingual mash-up is caught
            // before users report it.
            if (!string.IsNullOrEmpty(response.Feedback))
            {
                logger.LogWarning(
                    "FIND-pedagogy-017 re-regression: Feedback field is non-empty " +
                    "({Feedback}) for student {StudentId}, question {QuestionId}. " +
                    "The UI heading is i18n-translated; this field must remain empty.",
                    response.Feedback,
                    studentId,
                    request.QuestionId);
            }

            return Results.Ok(response);
        })
        .WithName("SubmitAnswer");

        // POST /api/sessions/{sessionId}/complete — complete the session
        group.MapPost("/{sessionId}/complete", async (
            string sessionId,
            HttpContext ctx,
            IDocumentStore store) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            await using var session = store.LightweightSession();

            // Get session queue
            var queue = await session.LoadAsync<LearningSessionQueueProjection>(sessionId);
            if (queue == null)
                return Results.NotFound(new { error = "Session not found" });

            if (queue.EndedAt.HasValue)
                return Results.Conflict(new { error = "Session already completed" });

            // End the session
            queue.EndedAt = DateTime.UtcNow;
            session.Store(queue);

            // Append LearningSessionEnded event
            var endedEvent = new LearningSessionEnded_V1(
                StudentId: studentId,
                SessionId: sessionId,
                EndedAt: DateTimeOffset.UtcNow,
                QuestionsAttempted: queue.TotalQuestionsAttempted,
                QuestionsCorrect: queue.CorrectAnswers);

            session.Events.Append(studentId, endedEvent);
            await session.SaveChangesAsync();

            var totalAnswered = queue.TotalQuestionsAttempted;
            var accuracyPercent = totalAnswered > 0
                ? (int)(queue.GetAccuracy() * 100)
                : 0;

            var durationSeconds = (int)(queue.EndedAt.Value - queue.StartedAt).TotalSeconds;

            var wrongAnswers = queue.TotalQuestionsAttempted - queue.CorrectAnswers;
            
            return Results.Ok(new SessionCompletedDto(
                SessionId: sessionId,
                TotalCorrect: queue.CorrectAnswers,
                TotalWrong: wrongAnswers,
                TotalXpAwarded: queue.CorrectAnswers * 10, // 10 XP per correct answer
                AccuracyPercent: accuracyPercent,
                DurationSeconds: durationSeconds));
        })
        .WithName("CompleteSession")
    .Produces<SessionCompletedDto>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status409Conflict)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // POST /api/sessions/{sessionId}/question/{questionId}/hint — request a progressive hint
        group.MapPost("/{sessionId}/question/{questionId}/hint", async (string sessionId, string questionId, HttpContext ctx, IDocumentStore store, [FromServices] IQuestionBank questionBank, [FromServices] IHintGenerator hintGenerator, [FromServices] IHintStuckDecisionService stuckDecision, ILogger<SessionLogMarker> logger, SessionHintRequest request) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            // 1. Verify student owns the session
            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            // Validate hint level
            if (request.HintLevel is < 1 or > 3)
                return Results.BadRequest(new { error = "HintLevel must be between 1 and 3" });

            await using var session = store.LightweightSession();

            // 2. Load LearningSessionQueueProjection by sessionId
            var queue = await session.LoadAsync<LearningSessionQueueProjection>(sessionId);
            if (queue == null)
                return Results.NotFound(new { error = "Session not found" });

            if (queue.StudentId != studentId)
                return Results.Forbid();

            // 3. Verify questionId matches queue.CurrentQuestionId
            if (queue.CurrentQuestionId != questionId)
            {
                logger.LogWarning("[SIEM] HintRequested: Question mismatch for student {StudentId}, session {SessionId}. Expected: {ExpectedQuestionId}, Got: {ActualQuestionId}",
                    studentId, sessionId, queue.CurrentQuestionId, questionId);
                return Results.BadRequest(new { error = "Question is not the current active question" });
            }

            // Load the question document
            var questionDoc = await questionBank.GetQuestionAsync(questionId);
            if (questionDoc == null)
                return Results.NotFound(new { error = "Question not found" });

            // 4. Get hintsUsed from queue.HintsUsedByQuestion
            var hintsUsed = queue.HintsUsedByQuestion.GetValueOrDefault(questionId, 0);

            // 5. Determine scaffolding level from student mastery
            var priorMastery = queue.ConceptMasterySnapshot.GetValueOrDefault(questionDoc.ConceptId, 0.5);
            var effectiveMastery = (float)priorMastery;
            var psi = 1.0f; // PSI=1.0 for REST path (no prerequisite graph lookup)
            var scaffoldingLevel = ScaffoldingService.DetermineLevel(effectiveMastery, psi);

            // 6. Get metadata = IScaffoldingService.GetScaffoldingMetadata(level)
            var metadata = ScaffoldingService.GetScaffoldingMetadata(scaffoldingLevel);

            // 7. If hintsUsed >= metadata.MaxHints, return 429 Too Many Requests
            if (hintsUsed >= metadata.MaxHints)
            {
                logger.LogWarning("[SIEM] HintBudgetExceeded: Student {StudentId}, session {SessionId}, question {QuestionId}. Used: {HintsUsed}, Max: {MaxHints}",
                    studentId, sessionId, questionId, hintsUsed, metadata.MaxHints);
                return Results.StatusCode(429); // Too Many Requests
            }

            // 8. Increment queue.HintsUsedByQuestion[questionId]
            queue.HintsUsedByQuestion[questionId] = hintsUsed + 1;
            session.Store(queue);

            // 9. Save the projection (will be saved with SaveChangesAsync at the end)

            // Get student's last attempt for this question (if any)
            string? studentAnswer = null;
            var lastAttempt = queue.AnsweredQuestions.LastOrDefault(q => q.QuestionId == questionId);
            if (lastAttempt != null)
            {
                studentAnswer = lastAttempt.SelectedOption;
            }

            // Build QuestionOptionState list from question choices
            var options = BuildHintOptionStates(questionDoc);

            // Get student profile for ConceptState
            var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);
            Cena.Actors.Mastery.ConceptMasteryState? conceptState = null;
            if (profile?.ConceptMastery.TryGetValue(questionDoc.ConceptId, out var state) == true)
            {
                // Map from snapshot ConceptMasteryState to domain ConceptMasteryState
                conceptState = new Cena.Actors.Mastery.ConceptMasteryState
                {
                    MasteryProbability = (float)state.PKnown,
                    AttemptCount = state.TotalAttempts,
                    CorrectCount = state.CorrectCount,
                };
            }

            // Build prerequisite edges (empty for REST path - no graph lookup)
            IReadOnlyList<MasteryPrerequisiteEdge> prerequisites = Array.Empty<MasteryPrerequisiteEdge>();

            // RDY-063 Phase 2b: classifier-driven hint-level adjustment.
            // Synchronous call with bounded internal timeout (default
            // 500ms); NEVER throws. When Cena:StuckClassifier:Enabled is
            // false, returns request.HintLevel immediately. When Enabled
            // but HintAdjustmentEnabled is false (Phase 2a shadow mode),
            // kicks off fire-and-forget persistence and returns unchanged
            // level. When both flags true, awaits the classifier within
            // the timeout and may clamp/bump the level per ADR-0036
            // rules (e.g., MetaStuck → 1, Misconception → max(req, 2)).
            // Architecture test `DecisionService_IsAwaited_WithRequestCancellation`
            // locks the await pattern + cancellation-token plumbing.
            var locale = ctx.Request.Headers.AcceptLanguage.ToString().Split(',').FirstOrDefault()?.Split(';').FirstOrDefault()?.Trim() ?? "en";
            var decision = await stuckDecision.DecideAsync(
                studentId, sessionId, questionId, queue, questionDoc,
                request.HintLevel, metadata.MaxHints, locale, ctx.RequestAborted);
            var effectiveHintLevel = decision.AdjustedLevel;

            if (decision.Adjusted)
            {
                logger.LogInformation(
                    "[HINT_LEVEL_ADJUSTED] student={StudentId} session={SessionId} q={QuestionId} " +
                    "from={From} to={To} reason={Reason} primary={Primary}",
                    studentId, sessionId, questionId, request.HintLevel, decision.AdjustedLevel,
                    decision.ReasonCode, decision.Primary);
            }

            // 10. Call IHintGenerator.Generate()
            var hintRequest = new HintRequest(
                HintLevel: effectiveHintLevel,
                QuestionId: questionId,
                ConceptId: questionDoc.ConceptId,
                PrerequisiteConceptNames: Array.Empty<string>(), // REST path doesn't resolve prereq names
                Options: options,
                Explanation: questionDoc.Explanation,
                StudentAnswer: studentAnswer,
                Prerequisites: prerequisites,
                ConceptState: conceptState);

            var hintContent = hintGenerator.Generate(hintRequest);

            logger.LogInformation("[SIEM] HintGenerated: Student {StudentId}, session {SessionId}, question {QuestionId}, level {HintLevel}",
                studentId, sessionId, questionId, effectiveHintLevel);

            logger.LogInformation("[SIEM] HintRequested: Student {StudentId}, session {SessionId}, question {QuestionId}, level {HintLevel}, hintsUsed {HintsUsed}",
                studentId, sessionId, questionId, effectiveHintLevel, hintsUsed + 1);

            // 11. Return SessionHintResponseDto
            var response = new SessionHintResponseDto(
                HintLevel: request.HintLevel,
                HintText: hintContent.Text,
                HasMoreHints: hintContent.HasMoreHints && (hintsUsed + 1) < metadata.MaxHints,
                HintsRemaining: metadata.MaxHints - (hintsUsed + 1));

            await session.SaveChangesAsync();

            return Results.Ok(response);
        })
        .WithName("RequestHint")
    .Produces<SessionHintResponseDto>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    // ── Helpers ──

    private static string? GetStudentId(ClaimsPrincipal user)
        => user.FindFirstValue("student_id")
           ?? user.FindFirstValue("sub")
           ?? user.FindFirstValue("user_id");

    private static SessionSummaryDto MapToSummary(TutoringSessionDocument doc)
    {
        var durationSeconds = doc.EndedAt.HasValue
            ? (int)(doc.EndedAt.Value - doc.StartedAt).TotalSeconds
            : (int)(DateTimeOffset.UtcNow - doc.StartedAt).TotalSeconds;

        var status = doc.EndedAt.HasValue ? "completed" : "active";

        return new SessionSummaryDto(
            Id: doc.Id,
            SessionId: doc.SessionId,
            Subject: doc.Subject,
            ConceptId: doc.ConceptId,
            Methodology: doc.Methodology,
            Status: status,
            TurnCount: doc.TotalTurns,
            DurationSeconds: durationSeconds,
            StartedAt: doc.StartedAt,
            EndedAt: doc.EndedAt);
    }

    private static int ExtractInt(dynamic evt, string property)
    {
        try
        {
            object? data = evt.Data;
            if (data is null) return 0;
            var json = JsonDocument.Parse(JsonSerializer.Serialize(data));
            if (json.RootElement.TryGetProperty(property, out var prop) ||
                json.RootElement.TryGetProperty(ToPascalCase(property), out prop))
                return prop.TryGetInt32(out var val) ? val : 0;
        }
        catch { /* best-effort */ }
        return 0;
    }

    private static double ExtractDouble(dynamic evt, string property)
    {
        try
        {
            object? data = evt.Data;
            if (data is null) return 0;
            var json = JsonDocument.Parse(JsonSerializer.Serialize(data));
            if (json.RootElement.TryGetProperty(property, out var prop) ||
                json.RootElement.TryGetProperty(ToPascalCase(property), out prop))
                return prop.TryGetDouble(out var val) ? val : 0;
        }
        catch { /* best-effort */ }
        return 0;
    }

    private static bool ExtractBool(dynamic evt, string property)
    {
        try
        {
            object? data = evt.Data;
            if (data is null) return false;
            var json = JsonDocument.Parse(JsonSerializer.Serialize(data));
            if (json.RootElement.TryGetProperty(property, out var prop) ||
                json.RootElement.TryGetProperty(ToPascalCase(property), out prop))
                return prop.ValueKind == JsonValueKind.True;
        }
        catch { /* best-effort */ }
        return false;
    }

    private static string ExtractString(dynamic evt, string property)
    {
        try
        {
            object? data = evt.Data;
            if (data is null) return "";
            var json = JsonDocument.Parse(JsonSerializer.Serialize(data));
            if (json.RootElement.TryGetProperty(property, out var prop) ||
                json.RootElement.TryGetProperty(ToPascalCase(property), out prop))
                return prop.GetString() ?? "";
        }
        catch { /* best-effort */ }
        return "";
    }

    private static string ToPascalCase(string camelCase)
    {
        if (string.IsNullOrEmpty(camelCase)) return camelCase;
        return char.ToUpperInvariant(camelCase[0]) + camelCase[1..];
    }

    // ═════════════════════════════════════════════════════════════════════════
    // HARDEN SessionEndpoints: Production-grade helper methods
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gets the real concept ID from a question using the question bank.
    /// </summary>
    private static async Task<string> GetConceptIdForQuestionAsync(
        string questionId,
        IQuestionBank questionBank)
    {
        var question = await questionBank.GetQuestionAsync(questionId);
        return question?.ConceptId ?? "unknown-concept";
    }

    // ═════════════════════════════════════════════════════════════════════════
    // FIND-pedagogy-001 / -002 / -003: Pure helpers for the answer path.
    // Extracted as `internal static` methods so xUnit tests in
    // FIND-arch-001: InternalsVisibleTo enabled in Cena.Student.Api.Host.csproj for Cena.Actors.Tests
    // can call them directly without spinning up an HTTP pipeline.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// FIND-pedagogy-003 — Build the BKT parameter set for a question. Uses
    /// per-concept slip / guess / learning rates from the question document
    /// when authored, otherwise falls back to the library default.
    /// </summary>
    internal static Cena.Actors.Services.BktParameters BuildBktParameters(QuestionDocument questionDoc)
    {
        var defaults = Cena.Actors.Services.BktParameters.Default;
        return new Cena.Actors.Services.BktParameters(
            PLearning: questionDoc.BktLearning ?? defaults.PLearning,
            PSlip: questionDoc.BktSlip ?? defaults.PSlip,
            PGuess: questionDoc.BktGuess ?? defaults.PGuess,
            PForget: defaults.PForget,
            PInitial: defaults.PInitial,
            ProgressionThreshold: defaults.ProgressionThreshold,
            PrerequisiteGateThreshold: defaults.PrerequisiteGateThreshold);
    }

    /// <summary>
    /// FIND-pedagogy-006 — Build hint option states from the authored
    /// question choices + distractor rationales. Used by the hint
    /// generation path so the progressive reveal uses the correct option's
    /// rationale as a fallback when no explanation is authored. If the
    /// question has no choices (free-text / numeric), returns an empty list
    /// and HintGenerator falls back gracefully.
    /// </summary>
    internal static IReadOnlyList<QuestionOptionState> BuildHintOptionStates(
        QuestionDocument questionDoc)
    {
        var choices = questionDoc.Choices;
        if (choices is null || choices.Length == 0)
            return Array.Empty<QuestionOptionState>();

        var rationales = questionDoc.DistractorRationales;
        var correctAnswer = questionDoc.CorrectAnswer ?? string.Empty;

        var result = new List<QuestionOptionState>(choices.Length);
        foreach (var choice in choices)
        {
            var isCorrect = string.Equals(choice, correctAnswer, StringComparison.OrdinalIgnoreCase);
            string? rationale = null;
            if (rationales is not null)
            {
                if (rationales.TryGetValue(choice, out var exact))
                {
                    rationale = exact;
                }
                else
                {
                    foreach (var kv in rationales)
                    {
                        if (string.Equals(kv.Key, choice, StringComparison.OrdinalIgnoreCase))
                        {
                            rationale = kv.Value;
                            break;
                        }
                    }
                }
            }

            result.Add(new QuestionOptionState(
                Label: choice,
                Text: choice,
                TextHtml: choice,
                IsCorrect: isCorrect,
                DistractorRationale: rationale));
        }

        return result;
    }

    /// <summary>
    /// FIND-pedagogy-002/007 — Build a ConceptAttempted_V1 event carrying the real
    /// answer outcome and error classification. Called for EVERY answer (correct or wrong) — the
    /// previous code hard-coded <c>IsCorrect: true</c> inside the
    /// <c>if (isCorrect)</c> branch, which starved BKT projections of failure
    /// signal. Tests assert <c>IsCorrect</c> mirrors the supplied flag.
    /// </summary>
    internal static ConceptAttempted_V1 BuildConceptAttempt(
        string studentId,
        string sessionId,
        QuestionDocument questionDoc,
        string currentQuestionId,
        string methodology,
        bool isCorrect,
        int responseTimeMs,
        double priorMastery,
        double posteriorMastery,
        string errorType,
        string? rawStudentInput = null)
    {
        return new ConceptAttempted_V1(
            StudentId: studentId,
            ConceptId: questionDoc.ConceptId,
            SessionId: sessionId,
            IsCorrect: isCorrect,
            ResponseTimeMs: responseTimeMs,
            QuestionId: currentQuestionId,
            QuestionType: questionDoc.QuestionType,
            MethodologyActive: methodology,
            // FIND-pedagogy-007: ErrorType is now populated via LLM classification
            // for wrong answers ("Procedural", "Conceptual", "Careless", "Systematic", "Transfer")
            // or "None" for correct answers.
            ErrorType: errorType,
            PriorMastery: priorMastery,
            PosteriorMastery: posteriorMastery,
            HintCountUsed: 0,
            WasSkipped: false,
            AnswerHash: "",
            BackspaceCount: 0,
            AnswerChangeCount: 0,
            WasOffline: false,
            Timestamp: DateTimeOffset.UtcNow,
            RawStudentInput: rawStudentInput);
    }

    /// <summary>
    /// RDY-033b + ADR-0003 — Run the CAS-backed misconception detector on a
    /// wrong answer. Delegates to <see cref="IMisconceptionDetectionService"/>
    /// which internally fans out to the registered
    /// <see cref="Cena.Actors.Services.ErrorPatternMatching.IErrorPatternMatcherEngine"/>.
    /// Returns null if the detector throws or degrades.
    ///
    /// Subject is mapped to the detector's vocabulary ("math" / "physics") —
    /// <see cref="QuestionDocument.Subject"/> may ship in either case form or
    /// with a different casing convention, so we lowercase for safety.
    /// </summary>
    internal static MisconceptionDetectionResult? DetectMisconception(
        IMisconceptionDetectionService detector,
        QuestionDocument questionDoc,
        string? rawStudentInput,
        string? normalizedAnswer,
        string errorType,
        ILogger logger)
    {
        try
        {
            var studentAnswer = !string.IsNullOrWhiteSpace(rawStudentInput)
                ? rawStudentInput
                : normalizedAnswer ?? string.Empty;

            var mappedErrorType = errorType switch
            {
                "Conceptual" => ExplanationErrorType.ConceptualMisunderstanding,
                "Procedural" => ExplanationErrorType.ProceduralError,
                "Careless" => ExplanationErrorType.CarelessMistake,
                "Motivational" => ExplanationErrorType.Guessing,
                "Transfer" => ExplanationErrorType.PartialUnderstanding,
                _ => (ExplanationErrorType?)null
            };

            return detector.Detect(
                questionStem: questionDoc.Prompt ?? string.Empty,
                correctAnswer: questionDoc.CorrectAnswer ?? string.Empty,
                studentAnswer: studentAnswer,
                subject: (questionDoc.Subject ?? "math").ToLowerInvariant(),
                conceptId: questionDoc.ConceptId,
                errorType: mappedErrorType);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Misconception detection failed for question {QuestionId}; continuing without misconception event",
                questionDoc.QuestionId);
            return null;
        }
    }

    /// <summary>
    /// FIND-pedagogy-007 — Classify the error type for a wrong answer.
    /// Uses LLM-based classification and maps ExplanationErrorType to ErrorType enum strings.
    /// </summary>
    private static async Task<string> ClassifyErrorAsync(
        IErrorClassificationService classifier,
        QuestionDocument questionDoc,
        string? studentAnswer,
        double priorMastery)
    {
        try
        {
            // Parse difficulty string to float (easy=0.3, medium=0.6, hard=0.9)
            var difficultyFloat = questionDoc.Difficulty?.ToLowerInvariant() switch
            {
                "easy" => 0.3f,
                "hard" => 0.9f,
                _ => 0.6f // medium default
            };

            var input = new ErrorClassificationInput(
                QuestionStem: questionDoc.Prompt,
                CorrectAnswer: questionDoc.CorrectAnswer ?? "",
                StudentAnswer: studentAnswer ?? "",
                DistractorRationale: questionDoc.DistractorRationales?.GetValueOrDefault(studentAnswer ?? ""),
                Subject: questionDoc.Subject,
                Language: "en", // Default to English; QuestionDocument doesn't have Language property
                QuestionDifficulty: difficultyFloat,
                StudentMastery: (float?)priorMastery);

            var classification = await classifier.ClassifyAsync(input, CancellationToken.None);

            // Map ExplanationErrorType to ErrorType enum strings
            return classification switch
            {
                ExplanationErrorType.ConceptualMisunderstanding => "Conceptual",
                ExplanationErrorType.ProceduralError => "Procedural",
                ExplanationErrorType.CarelessMistake => "Careless",
                ExplanationErrorType.Guessing => "Motivational",
                ExplanationErrorType.PartialUnderstanding => "Transfer",
                _ => "Conceptual" // Safe default
            };
        }
        catch
        {
            // If classification fails, default to Conceptual (safest pedagogical choice)
            return "Conceptual";
        }
    }

    /// <summary>
    /// FIND-pedagogy-001 / FIND-pedagogy-017 — Build the formative feedback response.
    ///
    /// The <c>Feedback</c> field is now deprecated (empty string). The UI
    /// renders its own translated heading via i18n keys
    /// (<c>session.runner.correct</c> / <c>session.runner.wrong</c>), so the
    /// server no longer ships a monolingual English pill. The field is kept
    /// for one release for backwards-compat — callers should stop reading it.
    ///
    /// The authored <c>Explanation</c> and (on wrong answers) the matching
    /// <c>DistractorRationale</c> are shipped alongside as dedicated fields.
    /// The UI renders the explanation block only when at least one of the
    /// fields is non-empty, so questions without authored feedback fall back
    /// to the translated heading only — no empty cards.
    /// </summary>
    internal static SessionAnswerResponseDto BuildAnswerFeedback(
        QuestionDocument questionDoc,
        string? studentAnswer,
        bool isCorrect,
        double priorMastery,
        double posteriorMastery,
        string? nextQuestionId,
        string studentLocale,  // NEW parameter — FIND-pedagogy-013
        ILogger logger)        // NEW parameter — FIND-pedagogy-013
    {
        // FIND-pedagogy-017 — the short English pill ("Correct" / "Not quite")
        // was rendered verbatim by AnswerFeedback.vue alongside the translated
        // heading, producing a bilingual mash-up for ar/he users. The UI now
        // uses its own i18n keys for the heading, so the server ships an empty
        // string. The field is kept for one release for backwards-compat.
        //
        // Structured log line for re-regression detection in production:
        // If a caller is still reading this field, their telemetry will show
        // empty feedback — which is the intended deprecation signal.

        // FIND-pedagogy-013: Get distractor rationale for the student's locale
        string? distractorRationale = null;
        if (!isCorrect && !string.IsNullOrWhiteSpace(studentAnswer))
        {
            distractorRationale = questionDoc.GetDistractorRationaleForLocale(studentAnswer.Trim(), studentLocale);
        }

        // FIND-pedagogy-013: Get explanation for the student's locale
        // Fallback chain: requested locale → en → null
        // NEVER fall back to a language the learner did not request
        var explanation = questionDoc.GetExplanationForLocale(studentLocale);

        // FIND-pedagogy-013: Structured logging for SIEM if explanation is not available
        // in the requested locale (but exists in some form)
        if (explanation == null && !string.IsNullOrEmpty(questionDoc.Explanation))
        {
            logger.LogWarning("[SIEM] ExplanationNotAvailableInLocale: QuestionId={QuestionId}, Locale={Locale}",
                questionDoc.QuestionId, studentLocale);
        }

        // Mastery delta is computed from the REAL BKT posterior, not a
        // hard-coded constant (FIND-pedagogy-003). Wrong answers can produce
        // negative deltas — the UI must handle non-positive values.
        var masteryDelta = (decimal)(posteriorMastery - priorMastery);

        return new SessionAnswerResponseDto(
            Correct: isCorrect,
            Feedback: string.Empty,
            XpAwarded: isCorrect ? 10 : 0,
            MasteryDelta: masteryDelta,
            NextQuestionId: nextQuestionId,
            Explanation: explanation,
            DistractorRationale: distractorRationale);
    }

    private static async Task AppendQuestionFallbackLanguageAsync(
        IDocumentStore store,
        string studentId,
        string sessionId,
        string questionId,
        LocaleFallbackDecision localeDecision)
    {
        await using var writeSession = store.LightweightSession();
        writeSession.Events.Append(studentId, new QuestionFallbackLanguage_V1(
            StudentId: studentId,
            SessionId: sessionId,
            QuestionId: questionId,
            RequestedLocale: localeDecision.RequestedLocale,
            ServedLocale: localeDecision.ServedLocale,
            Timestamp: DateTimeOffset.UtcNow));
        await writeSession.SaveChangesAsync();
    }

    // =============================================================================
    // RDY-034 slice 2 — Session flow-state assessment
    //
    // Pure computation over the attempt-history projection; no IO. Exposed as
    // internal static so SessionEndpointsFlowStateTests can drive the exact
    // same path without standing up a full HTTP harness.
    //
    // Signals consumed:
    //   • baselineAccuracy — overall session accuracy (all attempts)
    //   • rollingAccuracy5 — accuracy of the last 5 attempts
    //   • baselineRt       — mean response time across all attempts (ms)
    //   • rollingRt5       — mean response time over last 5 attempts (ms)
    //   • elapsedMin       — minutes from session start to now (or EndedAt)
    //   • maxSessionMin    — 45 (FlowStateService.MaxSessionMinutes parity)
    //   • consecutiveCorrect — trailing correct-answer streak
    //   • accuracyTrend    — rollingAccuracy5 − baselineAccuracy, clamped
    //
    // Returns both the full FlowStateAssessment AND the raw FatigueScore so
    // the caller can populate the existing SessionDetailDto.FatigueScore
    // field (previously hardcoded 0.0) without recomputing.
    // =============================================================================
    internal static (FlowStateAssessment Assessment, double FatigueScore) ComputeSessionFlowState(
        SessionAttemptHistoryDocument? history,
        DateTimeOffset sessionStartedAt,
        DateTimeOffset? sessionEndedAt,
        IFlowStateService flowState,
        ICognitiveLoadService cognitiveLoad)
    {
        // No attempts recorded yet → brand-new session; let the state machine
        // produce the canonical Warming result without synthetic fatigue.
        var attempts = history?.Attempts;
        if (attempts is null || attempts.Count == 0)
        {
            var warmingAssessment = flowState.Assess(
                fatigueLevel: 0.0,
                accuracyTrend: 0.0,
                consecutiveCorrect: 0,
                sessionDurationMinutes: ElapsedMinutes(sessionStartedAt, sessionEndedAt));
            return (warmingAssessment, 0.0);
        }

        // Ordered view so "last 5" really means the 5 most recent.
        var ordered = attempts.OrderBy(a => a.Timestamp).ToList();
        var total = ordered.Count;

        var baselineAccuracy = ordered.Count(a => a.IsCorrect) / (double)total;
        var baselineRt = ordered.Average(a => (double)a.ResponseTimeMs);

        var last5 = ordered.Skip(Math.Max(0, total - 5)).ToList();
        var rollingAccuracy5 = last5.Count(a => a.IsCorrect) / (double)last5.Count;
        var rollingRt5 = last5.Average(a => (double)a.ResponseTimeMs);

        var elapsedMin = ElapsedMinutes(sessionStartedAt, sessionEndedAt);
        const double maxSessionMin = 45.0; // FlowStateService.MaxSessionMinutes parity

        // 3-factor fatigue model (ICognitiveLoadService owns the formula).
        var fatigue = cognitiveLoad.ComputeFatigue(
            baselineAccuracy: baselineAccuracy,
            rollingAccuracy5: rollingAccuracy5,
            baselineRt: baselineRt,
            rollingRt5: rollingRt5,
            elapsedMin: elapsedMin,
            maxSessionMin: maxSessionMin);

        // Trailing correct-streak. Walk from the newest attempt back.
        var consecutiveCorrect = 0;
        for (int i = ordered.Count - 1; i >= 0; i--)
        {
            if (ordered[i].IsCorrect) consecutiveCorrect++;
            else break;
        }

        // Accuracy trend: simple rolling-vs-baseline delta, clamped to [-1, 1].
        var trend = Math.Clamp(rollingAccuracy5 - baselineAccuracy, -1.0, 1.0);

        var assessment = flowState.Assess(
            fatigueLevel: fatigue.FatigueScore,
            accuracyTrend: trend,
            consecutiveCorrect: consecutiveCorrect,
            sessionDurationMinutes: elapsedMin);

        return (assessment, fatigue.FatigueScore);
    }

    private static double ElapsedMinutes(DateTimeOffset startedAt, DateTimeOffset? endedAt)
    {
        var end = endedAt ?? DateTimeOffset.UtcNow;
        var delta = (end - startedAt).TotalMinutes;
        return delta < 0 ? 0.0 : delta;
    }
}
