// =============================================================================
// Cena Platform — /api/me/self-assessment endpoints (RDY-057)
//
// POST → save/update the student's onboarding self-assessment
// GET  → read back the stored assessment (null-204 when not set)
// DELETE → hard-delete the document (GDPR art 17 parity; student initiates)
//
// Auth: student-owned; the student_id claim on the JWT keys the document.
// Validation:
//   - FreeText capped at 200 chars (FTC v. Edmodo / COPPA — free-text from
//     a minor is stored as-is but never exposed in aggregate / NLP'd)
//   - Likert ∈ [1,5] per subject
//   - Strengths / friction lists capped at 20 entries each (defensive)
//   - TopicFeelings enum-validated
//
// Retention:
//   - Default ExpiresAt = CapturedAt + 90d
//   - OptInPersistent=true removes the expiry. Must be explicit; no flag
//     flip without an opt-in request body.
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Errors;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Api.Host.Endpoints;

public static class SelfAssessmentEndpoints
{
    private sealed class LoggerMarker { }

    public static IEndpointRouteBuilder MapSelfAssessmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me/self-assessment")
            .WithTags("Me")
            .RequireAuthorization()
            .RequireRateLimiting("api");

        group.MapGet("", GetAsync)
            .WithName("GetSelfAssessment")
            .Produces<SelfAssessmentResponseDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("", SaveAsync)
            .WithName("SaveSelfAssessment")
            .Produces<SelfAssessmentResponseDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest);

        group.MapDelete("", DeleteAsync)
            .WithName("DeleteSelfAssessment")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }

    // ── Handlers ─────────────────────────────────────────────────────────

    private static async Task<IResult> GetAsync(
        ClaimsPrincipal user,
        IDocumentStore store,
        CancellationToken ct)
    {
        var studentId = GetStudentId(user);
        if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

        await using var session = store.QuerySession();
        var doc = await session.LoadAsync<OnboardingSelfAssessmentDocument>(studentId, ct);
        if (doc is null) return Results.NoContent();

        return Results.Ok(MapToDto(doc));
    }

    private static async Task<IResult> SaveAsync(
        [FromBody] SelfAssessmentRequestDto req,
        ClaimsPrincipal user,
        IDocumentStore store,
        ILogger<LoggerMarker> logger,
        CancellationToken ct)
    {
        var studentId = GetStudentId(user);
        if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

        if (!TryValidate(req, out var err))
        {
            return Results.BadRequest(new CenaError(
                ErrorCodes.CENA_INTERNAL_VALIDATION, err,
                ErrorCategory.Validation, null, null));
        }

        var now = DateTimeOffset.UtcNow;
        var doc = new OnboardingSelfAssessmentDocument
        {
            Id = studentId,
            StudentId = studentId,
            CapturedAt = now,
            ExpiresAt = req.OptInPersistent
                ? null
                : now.AddDays(OnboardingSelfAssessmentDocument.DefaultRetentionDays),
            Skipped = req.Skipped,
            SubjectConfidence = (req.SubjectConfidence ?? new()).ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            Strengths = (req.Strengths ?? new()).Take(20).ToList(),
            FrictionPoints = (req.FrictionPoints ?? new()).Take(20).ToList(),
            TopicFeelings = (req.TopicFeelings ?? new()).ToDictionary(
                kvp => kvp.Key,
                kvp => ParseFeeling(kvp.Value)),
            FreeText = TrimFreeText(req.FreeText),
            OptInPersistent = req.OptInPersistent,
        };

        await using var session = store.LightweightSession();
        session.Store(doc);
        await session.SaveChangesAsync(ct);

        logger.LogInformation(
            "[SELF_ASSESSMENT] student={StudentId} captured skipped={Skipped} subjects={Subjects} " +
            "strengths={Str} friction={Fri} topicFeelings={Top} persistent={Persistent}",
            studentId, doc.Skipped, doc.SubjectConfidence.Count,
            doc.Strengths.Count, doc.FrictionPoints.Count,
            doc.TopicFeelings.Count, doc.OptInPersistent);

        return Results.Ok(MapToDto(doc));
    }

    private static async Task<IResult> DeleteAsync(
        ClaimsPrincipal user,
        IDocumentStore store,
        ILogger<LoggerMarker> logger,
        CancellationToken ct)
    {
        var studentId = GetStudentId(user);
        if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

        await using var session = store.LightweightSession();
        session.Delete<OnboardingSelfAssessmentDocument>(studentId);
        await session.SaveChangesAsync(ct);

        logger.LogInformation(
            "[SELF_ASSESSMENT] student={StudentId} deleted by-student-request",
            studentId);
        return Results.NoContent();
    }

    // ── Validation helpers (exposed internal for unit tests) ─────────────

    internal static bool TryValidate(SelfAssessmentRequestDto req, out string error)
    {
        error = "";
        if (req is null) { error = "Request body is required."; return false; }

        // Likert range
        if (req.SubjectConfidence is not null)
        {
            foreach (var (subject, score) in req.SubjectConfidence)
            {
                if (string.IsNullOrWhiteSpace(subject))
                { error = "SubjectConfidence contains an empty subject key."; return false; }
                if (score is < 1 or > 5)
                { error = $"SubjectConfidence '{subject}' must be 1..5 (was {score})."; return false; }
            }
        }

        // TopicFeeling enum parse
        if (req.TopicFeelings is not null)
        {
            foreach (var (topic, feeling) in req.TopicFeelings)
            {
                if (string.IsNullOrWhiteSpace(topic))
                { error = "TopicFeelings contains an empty topic key."; return false; }
                if (!IsValidFeeling(feeling))
                { error = $"TopicFeelings '{topic}' has unknown feeling '{feeling}'."; return false; }
            }
        }

        // Free text cap
        if (!string.IsNullOrEmpty(req.FreeText) && req.FreeText.Length > 200)
        { error = "FreeText exceeds 200-character cap."; return false; }

        // Strength / friction chip tag shape — lowercase kebab only, keeps
        // the storage stable and prevents PII leaking into tag ids.
        foreach (var list in new[] { req.Strengths, req.FrictionPoints })
        {
            if (list is null) continue;
            foreach (var tag in list)
            {
                if (string.IsNullOrWhiteSpace(tag) || tag.Length > 48)
                { error = $"Tag '{tag}' is empty or exceeds 48 chars."; return false; }
                foreach (var ch in tag)
                {
                    if (!char.IsLower(ch) && !char.IsDigit(ch) && ch != '-')
                    { error = $"Tag '{tag}' must be lowercase kebab (a-z, 0-9, '-')."; return false; }
                }
            }
        }

        return true;
    }

    private static bool IsValidFeeling(string value) =>
        Enum.TryParse<TopicFeeling>(value, ignoreCase: true, out _);

    private static TopicFeeling ParseFeeling(string value) =>
        Enum.TryParse<TopicFeeling>(value, ignoreCase: true, out var v) ? v : TopicFeeling.New;

    private static string? TrimFreeText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var trimmed = input.Trim();
        return trimmed.Length > 200 ? trimmed[..200] : trimmed;
    }

    private static SelfAssessmentResponseDto MapToDto(OnboardingSelfAssessmentDocument d) =>
        new(
            CapturedAt: d.CapturedAt,
            ExpiresAt: d.ExpiresAt,
            Skipped: d.Skipped,
            SubjectConfidence: d.SubjectConfidence,
            Strengths: d.Strengths,
            FrictionPoints: d.FrictionPoints,
            TopicFeelings: d.TopicFeelings.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString()),
            FreeText: d.FreeText,
            OptInPersistent: d.OptInPersistent);

    private static string? GetStudentId(ClaimsPrincipal user)
        => user.FindFirstValue("student_id")
           ?? user.FindFirstValue("sub")
           ?? user.FindFirstValue("user_id")
           ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
}

// ── Wire DTOs ────────────────────────────────────────────────────────────

public sealed record SelfAssessmentRequestDto(
    bool Skipped,
    Dictionary<string, int>? SubjectConfidence,
    List<string>? Strengths,
    List<string>? FrictionPoints,
    Dictionary<string, string>? TopicFeelings,
    string? FreeText,
    bool OptInPersistent = false);

public sealed record SelfAssessmentResponseDto(
    DateTimeOffset CapturedAt,
    DateTimeOffset? ExpiresAt,
    bool Skipped,
    Dictionary<string, int> SubjectConfidence,
    List<string> Strengths,
    List<string> FrictionPoints,
    Dictionary<string, string> TopicFeelings,
    string? FreeText,
    bool OptInPersistent);
