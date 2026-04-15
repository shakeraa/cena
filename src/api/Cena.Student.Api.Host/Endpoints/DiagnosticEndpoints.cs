// =============================================================================
// Cena Platform — Diagnostic Quiz Endpoints (RDY-023)
//
// POST /api/diagnostic/estimate — accepts quiz responses, returns IRT theta
// per subject and the corresponding BKT P_Initial values.
//
// GET /api/diagnostic/items?subjects=math,physics — returns diagnostic items
// (easy/medium/hard bands) for the given subjects.
// =============================================================================

using Cena.Actors.Services;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Student.Api.Host.Endpoints;

public static class DiagnosticEndpoints
{
    public static void MapDiagnosticEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/diagnostic")
            .RequireAuthorization()
            .WithTags("Diagnostic");

        group.MapPost("/estimate", EstimateAbility);
        group.MapGet("/items", GetDiagnosticItems);
    }

    // ─────────────────────────────────────────────────────────────────────
    // POST /api/diagnostic/estimate
    // ─────────────────────────────────────────────────────────────────────

    private static IResult EstimateAbility(
        HttpContext ctx,
        IIrtCalibrationPipeline irt,
        DiagnosticEstimateRequest request,
        ILogger<DiagnosticLogMarker> logger)
    {
        var studentId = ctx.User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        if (request.Responses is null || request.Responses.Length == 0)
            return Results.BadRequest(new { error = "At least one response is required." });

        // Group responses by subject
        var bySubject = request.Responses.GroupBy(r => r.Subject);
        var estimates = new List<SubjectEstimate>();

        foreach (var subjectGroup in bySubject)
        {
            var subject = subjectGroup.Key;
            var responses = subjectGroup
                .Select(r => new IrtResponse(studentId, r.QuestionId, r.Correct, null))
                .ToList();

            // Build item parameters from request (each response carries its difficulty)
            var itemParams = subjectGroup
                .Select(r => new IrtItemParameters(
                    r.QuestionId, r.Difficulty, 1.0, 0.0,
                    100, 0.0, CalibrationConfidence.Moderate, DateTimeOffset.UtcNow))
                .DistinctBy(p => p.QuestionId)
                .ToList();

            var ability = irt.EstimateAbility(studentId, subject, responses, itemParams);
            var pInitial = ThetaMasteryMapper.ThetaToPInitial(ability.Theta);

            estimates.Add(new SubjectEstimate(
                Subject: subject,
                Theta: Math.Round(ability.Theta, 3),
                StandardError: Math.Round(ability.StandardError, 3),
                PInitial: Math.Round(pInitial, 3),
                ItemsAnswered: ability.ItemsAnswered));
        }

        logger.LogInformation(
            "Diagnostic estimate for {StudentId}: {Subjects}",
            studentId, string.Join(", ", estimates.Select(e => $"{e.Subject}={e.Theta:F2}")));

        return Results.Ok(new DiagnosticEstimateResponse(
            StudentId: studentId,
            Estimates: estimates.ToArray()));
    }

    // ─────────────────────────────────────────────────────────────────────
    // GET /api/diagnostic/items?subjects=math,physics
    // ─────────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetDiagnosticItems(
        HttpContext ctx,
        IDocumentStore store,
        string subjects,
        ILogger<DiagnosticLogMarker> logger)
    {
        var studentId = ctx.User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(subjects))
            return Results.BadRequest(new { error = "subjects query parameter is required." });

        var subjectList = subjects.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (subjectList.Length == 0 || subjectList.Length > 10)
            return Results.BadRequest(new { error = "Provide 1-10 comma-separated subjects." });

        await using var session = store.QuerySession();

        var items = new List<DiagnosticItem>();

        foreach (var subject in subjectList)
        {
            // Query active questions for this subject, ordered by Elo difficulty
            var questions = await session.Query<QuestionDocument>()
                .Where(q => q.Subject == subject && q.IsActive)
                .OrderBy(q => q.DifficultyElo)
                .Take(30) // fetch extra to select across bands
                .ToListAsync();

            if (questions.Count == 0)
            {
                logger.LogWarning("No diagnostic items found for subject {Subject}", subject);
                continue;
            }

            // Select across difficulty bands using Elo: easy < 1400, medium 1400-1600, hard > 1600
            var easy = questions.Where(q => q.DifficultyElo < 1400).Take(3);
            var medium = questions.Where(q => q.DifficultyElo >= 1400 && q.DifficultyElo <= 1600).Take(4);
            var hard = questions.Where(q => q.DifficultyElo > 1600).Take(3);

            var selected = easy.Concat(medium).Concat(hard).ToList();

            // If band selection is sparse, fill from what's available
            if (selected.Count < 5)
                selected = questions.Take(Math.Min(10, questions.Count)).ToList();

            items.AddRange(selected.Select(q => new DiagnosticItem(
                QuestionId: q.Id,
                Subject: subject,
                Difficulty: Math.Round(Cena.Actors.Services.IrtEloConversion.EloToIrt(q.DifficultyElo), 3),
                Band: Cena.Actors.Services.IrtEloConversion.DifficultyBand(
                    Cena.Actors.Services.IrtEloConversion.EloToIrt(q.DifficultyElo)),
                QuestionText: q.Prompt,
                Options: q.Choices?.Select((c, i) => new DiagnosticOption($"opt_{i}", c)).ToArray()
                    ?? Array.Empty<DiagnosticOption>(),
                CorrectOptionKey: q.CorrectAnswer)));
        }

        return Results.Ok(new DiagnosticItemsResponse(items.ToArray()));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DTOs
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class DiagnosticLogMarker;

public record DiagnosticEstimateRequest(DiagnosticResponseItem[] Responses);

public record DiagnosticResponseItem(
    string QuestionId,
    string Subject,
    bool Correct,
    double Difficulty);

public record DiagnosticEstimateResponse(
    string StudentId,
    SubjectEstimate[] Estimates);

public record SubjectEstimate(
    string Subject,
    double Theta,
    double StandardError,
    double PInitial,
    int ItemsAnswered);

public record DiagnosticItemsResponse(DiagnosticItem[] Items);

public record DiagnosticItem(
    string QuestionId,
    string Subject,
    double Difficulty,
    string Band,
    string QuestionText,
    DiagnosticOption[] Options,
    string CorrectOptionKey);

public record DiagnosticOption(string Key, string Text);
