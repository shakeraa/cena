// =============================================================================
// Cena Platform — Parent-bind endpoints (TASK-E2E-A-04-BE)
//
// Two endpoints:
//
//   POST /api/me/parent-bind-invite
//     RequireAuthorization (the student requesting the invite)
//     Body: { parentEmail, relationship? }
//     Response 200: { token, jti, expiresAt }
//     The token is a single-use HS256 JWT carrying student + tenant + email.
//     Production wires this up to an email sender; the response also returns
//     the token plaintext so the e2e-flow spec can drive the consume side
//     without scraping email.
//
//   POST /api/parent/bind/{token}
//     RequireAuthorization (the parent consuming the invite — Firebase user)
//     200: ParentChildBoundV1 emitted, ParentChildBindingDocument written
//     401: token signature/format invalid or expired
//     403: tenant mismatch (cross-institute) OR email mismatch
//     409: jti already consumed (replay)
//
// Tenant safety: even though the JWT carries the institute claim, the
// consume endpoint cross-checks against the caller's own validated
// `tenant_id` Firebase claim. A signature compromise still fails at this
// gate. Mirrors the pattern PRR-436 / on-first-sign-in established.
// =============================================================================

using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Cena.Actors.Bus;
using Cena.Actors.Parent;
using Cena.Actors.Parent.Events;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Cena.Api.Host.Endpoints;

public static class ParentBindEndpoints
{
    internal sealed class ParentBindLoggerMarker { }

    /// <summary>Default invite TTL when the caller doesn't override.</summary>
    public static readonly TimeSpan DefaultInviteLifetime = TimeSpan.FromDays(7);

    public sealed record IssueInviteRequest(
        [property: Required, EmailAddress] string ParentEmail,
        string? Relationship);

    public sealed record IssueInviteResponse(
        string Token,
        string Jti,
        DateTimeOffset ExpiresAt);

    public sealed record BindResponse(
        string ParentUid,
        string StudentSubjectId,
        string TenantId,
        string Relationship,
        DateTimeOffset BoundAt,
        string InviteJti);

    public static IEndpointRouteBuilder MapParentBindEndpoints(this IEndpointRouteBuilder app)
    {
        var meGroup = app.MapGroup("/api/me")
            .WithTags("Parent")
            .RequireAuthorization()
            .RequireRateLimiting("api");

        meGroup.MapPost("/parent-bind-invite", IssueInvite)
            .WithName("PostParentBindInvite")
            .WithSummary("Student-driven issuance of a parent-bind invite token (single-use, signed, time-bounded).")
            .Produces<IssueInviteResponse>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        var parentGroup = app.MapGroup("/api/parent")
            .WithTags("Parent")
            .RequireAuthorization()
            .RequireRateLimiting("api");

        parentGroup.MapPost("/bind/{token}", ConsumeInvite)
            .WithName("PostParentBind")
            .WithSummary("Consume a parent-bind invite token: write ParentChildBindingDocument, emit ParentChildBoundV1.")
            .Produces<BindResponse>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden)
            .Produces<CenaError>(StatusCodes.Status409Conflict)
            .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    // ── Issue ──

    internal static async Task<IResult> IssueInvite(
        [FromBody] IssueInviteRequest? request,
        [FromServices] IParentBindInviteService invites,
        [FromServices] ILogger<ParentBindLoggerMarker> logger,
        HttpContext ctx,
        CancellationToken ct)
    {
        var studentUid = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? ctx.User.FindFirst("sub")?.Value
            ?? ctx.User.FindFirst("user_id")?.Value;
        var tenantId = ctx.User.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrWhiteSpace(studentUid))
            return TypedResults.Unauthorized();
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return TypedResults.Json<CenaError>(
                Err("parent_bind_no_tenant", "Caller's idToken must carry a tenant_id claim before issuing invites.",
                    ErrorCategory.Authorization),
                statusCode: StatusCodes.Status401Unauthorized);
        }
        if (request is null || string.IsNullOrWhiteSpace(request.ParentEmail))
            return TypedResults.BadRequest(Err("parent_bind_email_required", "parentEmail is required.", ErrorCategory.Validation));

        var result = await invites.IssueAsync(
            studentSubjectId: studentUid,
            instituteId: tenantId,
            parentEmail: request.ParentEmail,
            relationship: request.Relationship ?? "parent",
            validFor: DefaultInviteLifetime,
            ct: ct).ConfigureAwait(false);

        if (result.Outcome != ParentBindIssueOutcome.Issued || result.Token is null)
        {
            logger.LogWarning("[PARENT_BIND] issue failed for student={StudentUid} outcome={Outcome}",
                studentUid, result.Outcome);
            return TypedResults.BadRequest(Err("parent_bind_issue_failed", "Invite could not be issued.", ErrorCategory.Validation));
        }

        return TypedResults.Ok(new IssueInviteResponse(
            Token: result.Token!,
            Jti: result.Jti!,
            ExpiresAt: result.ExpiresAt!.Value));
    }

    // ── Consume ──

    internal static async Task<IResult> ConsumeInvite(
        string token,
        [FromServices] IParentBindInviteService invites,
        [FromServices] IParentChildBindingStore bindingStore,
        [FromServices] INatsConnection nats,
        [FromServices] ILogger<ParentBindLoggerMarker> logger,
        HttpContext ctx,
        CancellationToken ct)
    {
        var parentUid = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? ctx.User.FindFirst("sub")?.Value
            ?? ctx.User.FindFirst("user_id")?.Value;
        var callerTenantId = ctx.User.FindFirst("tenant_id")?.Value;
        var callerEmail = ctx.User.FindFirst("email")?.Value
            ?? ctx.User.FindFirst(ClaimTypes.Email)?.Value;

        if (string.IsNullOrWhiteSpace(parentUid))
            return TypedResults.Unauthorized();
        if (string.IsNullOrWhiteSpace(callerTenantId))
        {
            return TypedResults.Json<CenaError>(
                Err("parent_bind_no_tenant", "Caller's idToken must carry a tenant_id claim before consuming invites.",
                    ErrorCategory.Authorization),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await invites.ConsumeAsync(
            token: token,
            callerParentUid: parentUid,
            callerInstituteId: callerTenantId,
            callerEmail: callerEmail,
            ct: ct).ConfigureAwait(false);

        switch (result.Outcome)
        {
            case ParentBindConsumeOutcome.Verified:
                break;
            case ParentBindConsumeOutcome.Expired:
            case ParentBindConsumeOutcome.InvalidSignature:
                logger.LogInformation("[PARENT_BIND] consume rejected: {Outcome}", result.Outcome);
                return TypedResults.Json<CenaError>(
                    Err($"parent_bind_{result.Outcome.ToString().ToLowerInvariant()}", "Invite token invalid or expired.",
                        ErrorCategory.Authentication),
                    statusCode: StatusCodes.Status401Unauthorized);
            case ParentBindConsumeOutcome.AlreadyConsumed:
                return TypedResults.Json<CenaError>(
                    Err("parent_bind_already_consumed", "This invite has already been used.", ErrorCategory.Conflict),
                    statusCode: StatusCodes.Status409Conflict);
            case ParentBindConsumeOutcome.Unknown:
                // Unknown jti — could be forged or for a different server.
                // 401 (not 404) so we don't help an attacker enumerate jtis.
                return TypedResults.Json<CenaError>(
                    Err("parent_bind_unknown", "Invite not recognized.", ErrorCategory.Authentication),
                    statusCode: StatusCodes.Status401Unauthorized);
            case ParentBindConsumeOutcome.TenantMismatch:
            case ParentBindConsumeOutcome.EmailMismatch:
                return TypedResults.Json<CenaError>(
                    Err($"parent_bind_{result.Outcome.ToString().ToLowerInvariant()}",
                        result.Outcome == ParentBindConsumeOutcome.TenantMismatch
                            ? "Invite belongs to a different institute."
                            : "Invite was issued for a different email address.",
                        ErrorCategory.Authorization),
                    statusCode: StatusCodes.Status403Forbidden);
            default:
                return TypedResults.StatusCode(StatusCodes.Status500InternalServerError);
        }

        var invite = result.Invite!;

        // ── Grant the binding ──
        var binding = await bindingStore.GrantAsync(
            parentActorId: parentUid,
            studentSubjectId: invite.StudentSubjectId,
            instituteId: invite.InstituteId,
            grantedAtUtc: invite.ConsumedAt!.Value,
            ct: ct).ConfigureAwait(false);

        // ── Emit ParentChildBoundV1 on NATS ──
        var correlationHeader = ctx.Request.Headers["X-Correlation-Id"].ToString();
        var correlationId = Guid.TryParse(correlationHeader, out var parsed) ? parsed : Guid.NewGuid();
        var evt = new ParentChildBoundV1(
            ParentUid: parentUid,
            StudentSubjectId: invite.StudentSubjectId,
            TenantId: invite.InstituteId,
            Relationship: invite.Relationship,
            BoundAt: binding.GrantedAtUtc,
            CorrelationId: correlationId,
            InviteJti: invite.Id);

        try
        {
            var subject = NatsSubjects.ParentEvent(parentUid, NatsSubjects.ParentChildBound);
            var data = JsonSerializer.SerializeToUtf8Bytes(evt, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            var headers = new NatsHeaders
            {
                ["Nats-Msg-Id"] = $"bound-{invite.Id}",
                ["Cena-Schema-Version"] = "1",
                ["tenant_id"] = invite.InstituteId,
                ["correlation_id"] = correlationId.ToString("N"),
            };
            await nats.PublishAsync(subject, data, headers: headers, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Marten is canonical; NATS publish is best-effort fanout.
            logger.LogError(ex, "[PARENT_BIND] NATS publish failed for jti={Jti}; binding still recorded", invite.Id);
        }

        return TypedResults.Ok(new BindResponse(
            ParentUid: parentUid,
            StudentSubjectId: invite.StudentSubjectId,
            TenantId: invite.InstituteId,
            Relationship: invite.Relationship,
            BoundAt: binding.GrantedAtUtc,
            InviteJti: invite.Id));
    }

    // ── Helpers ──

    private static CenaError Err(string code, string message, ErrorCategory category) =>
        new(Code: code, Message: message, Category: category, Details: null, CorrelationId: null);
}
