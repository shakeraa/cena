// =============================================================================
// Cena Platform — Mastery Trajectory Endpoints (RDY-071 Phase 1B)
//
// Exposes the student's per-topic + overall mastery trajectory using
// the domain types shipped in Phase 1A (AbilityEstimate +
// MasteryTrajectory). The endpoint renders the honest-framed caption
// server-side and returns it to the client verbatim so the UI cannot
// drift into forward-extrapolation copy that the RDY-071 shipgate
// banned-phrase list would catch.
//
// NEVER emits a numeric Bagrut scaled-score prediction. The
// ConcordanceMapping.F8PointEstimateEnabled flag (RDY-080) is checked
// here before any point-estimate payload would be assembled — and in
// Phase 1B we refuse to emit point-estimate at all until that flag
// flips post-calibration.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Mastery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Api.Host.Endpoints;

public sealed record TrajectoryPointDto(
    DateTimeOffset AtUtc,
    double Theta,
    double StandardError,
    int SampleSize,
    string Bucket);

public sealed record TrajectoryResponseDto(
    string StudentAnonId,
    string TopicSlug,
    IReadOnlyList<TrajectoryPointDto> Points,
    string CurrentBucket,
    string CurrentCaption,
    int TotalSampleSize,
    int WindowWeeks);

/// <summary>
/// Provider abstraction so the endpoint can be unit-tested without
/// standing up Marten + the projection pipeline. Phase 1C wires a
/// Marten-backed implementation that reads the ability-estimate
/// projection; Phase 1B ships the interface + a null provider that
/// returns an empty trajectory so the endpoint surface is exercisable.
/// </summary>
public interface IMasteryTrajectoryProvider
{
    Task<MasteryTrajectory?> GetAsync(
        string studentAnonId, string topicSlug, CancellationToken ct);
}

/// <summary>
/// Graceful-disabled default. Returns null for every topic; the
/// endpoint translates null into an empty trajectory with the
/// "keep practicing" caption so the client always sees a valid shape.
/// </summary>
public sealed class NullMasteryTrajectoryProvider : IMasteryTrajectoryProvider
{
    public Task<MasteryTrajectory?> GetAsync(
        string studentAnonId, string topicSlug, CancellationToken ct)
        => Task.FromResult<MasteryTrajectory?>(null);
}

public static class TrajectoryEndpoints
{
    public const string Route = "/api/me/mastery/trajectory/{topicSlug}";

    public static IEndpointRouteBuilder MapTrajectoryEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, HandleAsync)
            .RequireAuthorization()
            .WithName("GetMyMasteryTrajectory")
            .WithTags("Mastery", "Trajectory")
            .WithSummary(
                "RDY-071 F8: per-topic mastery trajectory in HIGH/MED/LOW "
                + "bucket framing. Never a numeric Bagrut prediction.");

        return app;
    }

    private static async Task<IResult> HandleAsync(
        string topicSlug,
        HttpContext http,
        IMasteryTrajectoryProvider provider,
        ILogger<TrajectoryEndpointMarker> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(topicSlug))
            return Results.BadRequest(new { error = "missing-topic" });

        var studentAnonId = http.User.FindFirst("studentAnonId")?.Value
            ?? http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(studentAnonId))
            return Results.Unauthorized();

        var trajectory = await provider.GetAsync(studentAnonId, topicSlug, ct);
        var dto = ToResponse(studentAnonId, topicSlug, trajectory);

        logger.LogInformation(
            "[RDY-071] Trajectory served: student={Student} topic={Topic} "
            + "bucket={Bucket} samples={Samples}",
            studentAnonId,
            topicSlug,
            dto.CurrentBucket,
            dto.TotalSampleSize);

        return Results.Ok(dto);
    }

    internal static TrajectoryResponseDto ToResponse(
        string studentAnonId, string topicSlug, MasteryTrajectory? trajectory)
    {
        if (trajectory is null || trajectory.Points.IsDefaultOrEmpty)
        {
            return new TrajectoryResponseDto(
                StudentAnonId: studentAnonId,
                TopicSlug: topicSlug,
                Points: Array.Empty<TrajectoryPointDto>(),
                CurrentBucket: MasteryBucket.Inconclusive.ToString(),
                CurrentCaption: "Based on 0 problems over 0 weeks — we need "
                    + "more data for a clear read. Keep practicing.",
                TotalSampleSize: 0,
                WindowWeeks: 0);
        }

        return new TrajectoryResponseDto(
            StudentAnonId: studentAnonId,
            TopicSlug: topicSlug,
            Points: trajectory.Points
                .Select(p => new TrajectoryPointDto(
                    AtUtc: p.AtUtc,
                    Theta: p.Theta,
                    StandardError: p.StandardError,
                    SampleSize: p.SampleSize,
                    Bucket: p.Bucket.ToString()))
                .ToList(),
            CurrentBucket: trajectory.CurrentBucket.ToString(),
            CurrentCaption: trajectory.CurrentCaption,
            TotalSampleSize: trajectory.TotalSampleSize,
            WindowWeeks: trajectory.WindowWeeks);
    }

    private sealed class TrajectoryEndpointMarker { }
}
