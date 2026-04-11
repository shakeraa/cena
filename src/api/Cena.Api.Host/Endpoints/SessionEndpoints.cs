// =============================================================================
// Cena Platform -- Session Lifecycle REST Endpoints (SES-002)
// Student-facing REST endpoints for session history, resume, and replay.
// All reads go directly to Marten; resume sends a NATS command to the actor.
// =============================================================================

using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Cena.Actors.Bus;
using Cena.Actors.Events;
using Cena.Actors.Projections;
using Cena.Actors.Services;
using Cena.Actors.Serving;
using Cena.Actors.Tutoring;
using Cena.Api.Contracts.Sessions;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NATS.Client.Core;

namespace Cena.Api.Host.Endpoints;

public static class SessionEndpoints
{
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
        group.MapPost("/start", async (
            HttpContext ctx,
            IDocumentStore store,
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

            return Results.Ok(new SessionStartResponse(
                SessionId: sessionId,
                HubGroupName: $"session-{sessionId}",
                FirstQuestionId: null)); // Phase 1: null, wired in STB-01b
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
                ProgressPercent: active.GetProgressPercent(),
                CurrentQuestionId: active.CurrentQuestionId);

            return Results.Ok(dto);
        })
        .WithName("GetActiveSessionV2");

        // GET /api/sessions — list student's sessions (paginated, filterable)
        group.MapGet("/", async (
            HttpContext ctx,
            IDocumentStore store,
            string? subject,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? page,
            int? pageSize) =>
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
        .WithName("GetStudentSessions");

        // GET /api/sessions/{sessionId} — full session detail
        group.MapGet("/{sessionId}", async (
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

            // IDOR: ensure the session belongs to the requesting student
            if (doc.StudentId != studentId)
                return Results.Forbid();

            // FIND-data-009: Use FetchStreamAsync instead of QueryAllRawEvents full scan
            var events = await session.Events.FetchStreamAsync(studentId);
            var sessionEvents = events
                .Where(e => e.EventType == typeof(ConceptAttempted_V1) || e.EventType == typeof(ConceptAttempted_V2))
                .Select(e => e.Data)
                .Cast<ConceptAttempted_V1>()
                .Where(e => e.SessionId == doc.SessionId)
                .ToList();

            var questionsAttempted = sessionEvents.Count;
            var questionsCorrect   = sessionEvents.Count(e => e.IsCorrect);
            var accuracy           = questionsAttempted > 0
                ? (double)questionsCorrect / questionsAttempted
                : 0;

            // FIND-arch-009: FatigueScore removed - ConceptAttempted_V1 does not have this property
            var fatigueScore = 0.0;

            var masteryDeltas = sessionEvents
                .GroupBy(e => e.ConceptId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var ordered  = g.OrderBy(e => e.Timestamp).ToList();
                        var initial  = ordered.First().PriorMastery;
                        var terminal = ordered.Last().PosteriorMastery;
                        return terminal - initial;
                    });

            var durationSeconds = doc.EndedAt.HasValue
                ? (int)(doc.EndedAt.Value - doc.StartedAt).TotalSeconds
                : (int)(DateTimeOffset.UtcNow - doc.StartedAt).TotalSeconds;

            var status = doc.EndedAt.HasValue ? "completed" : "active";

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
                MasteryDeltas: masteryDeltas));
        })
        .WithName("GetSessionDetail");

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

            // FIND-data-009: Use FetchStreamAsync instead of QueryAllRawEvents full scan
            var events = await session.Events.FetchStreamAsync(studentId);
            var attempts = events
                .Where(e => e.EventType == typeof(ConceptAttempted_V1) || e.EventType == typeof(ConceptAttempted_V2))
                .Select((e, i) => {
                    if (e.Data is ConceptAttempted_V1 v1)
                    {
                        return new QuestionAttemptDto(
                            Sequence: i + 1,
                            QuestionId: v1.QuestionId,
                            ConceptId: v1.ConceptId,
                            QuestionType: v1.QuestionType,
                            IsCorrect: v1.IsCorrect,
                            ResponseTimeMs: (int)v1.ResponseTimeMs,
                            HintCountUsed: v1.HintCountUsed,
                            WasSkipped: v1.WasSkipped,
                            PriorMastery: Math.Round(v1.PriorMastery, 4),
                            PosteriorMastery: Math.Round(v1.PosteriorMastery, 4),
                            Timestamp: e.Timestamp);
                    }
                    return null;
                })
                .Where(dto => dto != null)
                .Cast<QuestionAttemptDto>()
                .ToList();

            return Results.Ok(new SessionReplayDto(
                SessionId: doc.SessionId,
                Subject: doc.Subject,
                Methodology: doc.Methodology,
                StartedAt: doc.StartedAt,
                EndedAt: doc.EndedAt,
                Attempts: attempts));
        })
        .WithName("GetSessionReplay");

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
        .WithName("ResumeSession");

        // ═════════════════════════════════════════════════════════════════════════
        // STB-01b: In-Session Question + Answer Endpoints
        // ═════════════════════════════════════════════════════════════════════════

        // GET /api/sessions/{sessionId}/current-question — get current question
        group.MapGet("/{sessionId}/current-question", async (
            string sessionId,
            HttpContext ctx,
            IDocumentStore store,
            IQuestionBank questionBank) =>
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

            // Get current question from queue
            var currentQuestion = queue.PeekNext();
            if (currentQuestion == null)
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

            // Dequeue the question for display
            queue.DequeueNext();

            // Load full question details from question bank
            var questionDoc = await questionBank.GetQuestionAsync(currentQuestion.QuestionId);
            if (questionDoc == null)
                return Results.NotFound(new { error = "Question not found" });

            return Results.Ok(new SessionQuestionDto(
                QuestionId: questionDoc.QuestionId,
                QuestionIndex: queue.TotalQuestionsAttempted + 1,
                TotalQuestions: queue.TotalQuestionsAttempted + queue.QuestionQueue.Count + 1,
                Prompt: questionDoc.Prompt,
                QuestionType: questionDoc.QuestionType,
                Choices: questionDoc.Choices ?? Array.Empty<string>(),
                Subject: questionDoc.Subject,
                ExpectedTimeSeconds: 60));
        })
        .WithName("GetCurrentQuestion");

        // POST /api/sessions/{sessionId}/answer — submit an answer
        group.MapPost("/{sessionId}/answer", async (
            string sessionId,
            HttpContext ctx,
            IDocumentStore store,
            IQuestionBank questionBank,
            IBktService bktService,
            SessionAnswerRequest request) =>
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

            var isCorrect = string.Equals(request.Answer?.Trim(), questionDoc.CorrectAnswer, StringComparison.OrdinalIgnoreCase);

            // Record answer in queue
            queue.RecordAnswer(currentQuestion.QuestionId, isCorrect, TimeSpan.FromMilliseconds(request.TimeSpentMs), request.Answer);

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

            // ─── FIND-pedagogy-002: ConceptAttempted_V1 on EVERY answer ───
            //
            // Previously this append was wrapped in `if (isCorrect)` and the
            // IsCorrect field was hard-coded to `true`. Both bugs broke the
            // actor-side BKT pipeline (BktTracer.Update has a correct
            // P(L|incorrect) branch that was never fed). The append now runs
            // for every answer and carries the real outcome. The XP append
            // (below) remains gated on isCorrect — this is intentional.
            //
            // ErrorType is left empty on this write path; the LLM-backed
            // ErrorClassificationService wiring is tracked separately by
            // FIND-pedagogy-007 and will enrich the event stream from an
            // async projection rather than the hot answer path.
            var conceptAttempt = BuildConceptAttempt(
                studentId: studentId,
                sessionId: sessionId,
                questionDoc: questionDoc,
                currentQuestionId: currentQuestion.QuestionId,
                methodology: queue.Mode,
                isCorrect: isCorrect,
                responseTimeMs: request.TimeSpentMs,
                priorMastery: priorMastery,
                posteriorMastery: posteriorMastery);

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
            if (isCorrect)
            {
                // Load current profile ONLY to compute the new absolute TotalXp
                // that we stamp into the XpAwarded_V1 event (the event contract
                // expects an absolute, not a delta).
                var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);
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
                // Wrong path: ConceptAttempted only (no XP award). This is the
                // fix for FIND-pedagogy-002 — the event is emitted with
                // IsCorrect=false so BKT projections see failure signals.
                session.Events.Append(studentId, conceptAttempt);
            }

            await session.SaveChangesAsync();

            // Determine next question ID
            string? nextQuestionId = null;
            var nextQuestion = queue.PeekNext();
            if (nextQuestion != null)
            {
                nextQuestionId = nextQuestion.QuestionId;
            }

            // ─── FIND-pedagogy-001: Formative feedback with explanation ───
            var response = BuildAnswerFeedback(
                questionDoc: questionDoc,
                studentAnswer: request.Answer,
                isCorrect: isCorrect,
                priorMastery: priorMastery,
                posteriorMastery: posteriorMastery,
                nextQuestionId: nextQuestionId);

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
        .WithName("CompleteSession");

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
    // Cena.Actors.Tests (InternalsVisibleTo enabled in Cena.Api.Host.csproj)
    // can call them directly without spinning up an HTTP pipeline.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// FIND-pedagogy-003 — Build the BKT parameter set for a question. Uses
    /// per-concept slip / guess / learning rates from the question document
    /// when authored, otherwise falls back to the library default.
    /// </summary>
    internal static BktParameters BuildBktParameters(QuestionDocument questionDoc)
    {
        var defaults = BktParameters.Default;
        return new BktParameters(
            PLearning: questionDoc.BktLearning ?? defaults.PLearning,
            PSlip: questionDoc.BktSlip ?? defaults.PSlip,
            PGuess: questionDoc.BktGuess ?? defaults.PGuess,
            PForget: defaults.PForget,
            PInitial: defaults.PInitial,
            ProgressionThreshold: defaults.ProgressionThreshold,
            PrerequisiteGateThreshold: defaults.PrerequisiteGateThreshold);
    }

    /// <summary>
    /// FIND-pedagogy-002 — Build a ConceptAttempted_V1 event carrying the real
    /// answer outcome. Called for EVERY answer (correct or wrong) — the
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
        double posteriorMastery)
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
            // ErrorType is left empty here and populated by the async
            // ErrorClassificationService pipeline tracked by FIND-pedagogy-007.
            // Synchronous LLM classification on the hot answer path would
            // block every student submission on an LLM round-trip.
            ErrorType: "",
            PriorMastery: priorMastery,
            PosteriorMastery: posteriorMastery,
            HintCountUsed: 0,
            WasSkipped: false,
            AnswerHash: "",
            BackspaceCount: 0,
            AnswerChangeCount: 0,
            WasOffline: false,
            Timestamp: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// FIND-pedagogy-001 — Build the formative feedback response.
    ///
    /// The short pill ("Correct" / "Not quite") is preserved so the UI has a
    /// one-line summary, but the authored <c>Explanation</c> and (on wrong
    /// answers) the matching <c>DistractorRationale</c> are shipped alongside
    /// as dedicated fields. The UI renders the explanation block only when
    /// at least one of the fields is non-empty, so questions without authored
    /// feedback fall back to the short pill only — no empty cards.
    /// </summary>
    internal static SessionAnswerResponseDto BuildAnswerFeedback(
        QuestionDocument questionDoc,
        string? studentAnswer,
        bool isCorrect,
        double priorMastery,
        double posteriorMastery,
        string? nextQuestionId)
    {
        // Short pill label. The previous "Correct! Great job!" /
        // "Not quite. The correct answer was: X" literal strings are gone —
        // the detailed worked explanation now travels in the dedicated field
        // so the UI can render it in a separate component below the pill.
        var label = isCorrect ? "Correct" : "Not quite";

        // Distractor rationale is ONLY surfaced for wrong answers, and ONLY
        // when the authored question has a rationale for the specific option
        // the student chose. The keys must match option values as authored.
        string? distractorRationale = null;
        if (!isCorrect && !string.IsNullOrWhiteSpace(studentAnswer)
            && questionDoc.DistractorRationales is { } rationales)
        {
            var trimmed = studentAnswer.Trim();
            if (rationales.TryGetValue(trimmed, out var direct))
            {
                distractorRationale = direct;
            }
            else
            {
                // Case-insensitive fallback for free-text answers that don't
                // exactly match the authored key casing.
                foreach (var kv in rationales)
                {
                    if (string.Equals(kv.Key, trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        distractorRationale = kv.Value;
                        break;
                    }
                }
            }
        }

        // Mastery delta is computed from the REAL BKT posterior, not a
        // hard-coded constant (FIND-pedagogy-003). Wrong answers can produce
        // negative deltas — the UI must handle non-positive values.
        var masteryDelta = (decimal)(posteriorMastery - priorMastery);

        return new SessionAnswerResponseDto(
            Correct: isCorrect,
            Feedback: label,
            XpAwarded: isCorrect ? 10 : 0,
            MasteryDelta: masteryDelta,
            NextQuestionId: nextQuestionId,
            Explanation: string.IsNullOrWhiteSpace(questionDoc.Explanation)
                ? null
                : questionDoc.Explanation,
            DistractorRationale: distractorRationale);
    }
}
