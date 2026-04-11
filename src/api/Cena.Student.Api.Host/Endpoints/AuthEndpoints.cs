// =============================================================================
// Cena Platform -- Student Auth REST Endpoints (FIND-ux-006b)
// Anonymous endpoints that back the self-service authentication recovery
// flows on the student web. The only route today is POST
// /api/auth/password-reset, which forwards to Firebase Admin SDK via
// IFirebaseAdminService and ALWAYS returns 204 No Content regardless of
// whether the email is known (OWASP account-enumeration defence).
// =============================================================================

using System.ComponentModel.DataAnnotations;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Firebase;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Api.Host.Endpoints;

public static class AuthEndpoints
{
    // Marker type for logger (static classes cannot be used as type arguments).
    // Internal so the handler method signature (also internal for testability)
    // does not run into CS0051 accessibility errors.
    internal sealed class AuthLoggerMarker { }

    /// <summary>
    /// Shape of the JSON body POSTed to <c>/api/auth/password-reset</c>.
    /// Kept inline because this endpoint group owns a single public surface
    /// and pulling it into Cena.Api.Contracts would be over-engineered.
    /// </summary>
    public sealed record PasswordResetRequest(
        [property: Required, EmailAddress] string Email);

    /// <summary>
    /// Error body returned by the endpoint on malformed input. Kept as a
    /// typed record (not an anonymous object) so tests can assert against
    /// <see cref="Microsoft.AspNetCore.Http.HttpResults.BadRequest{T}"/>
    /// without binding to a compiler-generated type.
    /// </summary>
    public sealed record AuthErrorResponse(string Error);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Anonymous group — these routes back public forms on the unauthed
        // student web (login, forgot-password). Rate limiting is the primary
        // abuse guard since there is no user identity to attach quotas to.
        var group = app.MapGroup("/api/auth")
            .WithTags("Auth")
            .AllowAnonymous()
            .RequireRateLimiting("password-reset");

        group.MapPost("/password-reset", PasswordReset)
            .WithName("PostPasswordReset")
            .WithSummary("Initiate a Firebase password-reset email for the supplied address.");

        return app;
    }

    /// <summary>
    /// POST /api/auth/password-reset
    ///
    /// Returns 204 No Content for BOTH known and unknown emails to prevent
    /// account enumeration. Malformed input is the only case that returns a
    /// 4xx — the endpoint will surface 400 for a missing/invalid email and
    /// 503 for a hard Firebase outage (because in that case no email is
    /// actually being sent and replying 204 would be a lie).
    /// </summary>
    /// <remarks>
    /// OWASP reference: Authentication Cheat Sheet, "Forgot password /
    /// password reset" — "Return a consistent message regardless of whether
    /// the user's account exists".
    /// </remarks>
    internal static async Task<IResult> PasswordReset(
        [FromBody] PasswordResetRequest request,
        [FromServices] IFirebaseAdminService firebase,
        [FromServices] ILogger<AuthLoggerMarker> logger,
        HttpContext ctx,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Email))
        {
            // Malformed input is not "does the account exist?" — it is an
            // invalid request. No information leaked by rejecting it.
            return TypedResults.BadRequest(new AuthErrorResponse("Email is required."));
        }

        // Lightweight shape check — the full email grammar is enormous but
        // "has an @ with text on both sides" catches everything the frontend
        // would reasonably send and lets Firebase reject subtler problems.
        var email = request.Email.Trim();
        if (email.Length > 320 || email.IndexOf('@') <= 0 || email.IndexOf('@') == email.Length - 1)
        {
            return TypedResults.BadRequest(new AuthErrorResponse("Email is not in a recognized format."));
        }

        var emailHash = EmailHasher.Hash(email);

        // Capture the client IP once so failure and success branches log the
        // same correlation data. X-Forwarded-For is honoured by the ambient
        // reverse proxy; fall back to the direct RemoteIpAddress otherwise.
        var clientIp = ctx.Request.Headers.TryGetValue("X-Forwarded-For", out var fwd) && fwd.Count > 0
            ? fwd[0]
            : ctx.Connection.RemoteIpAddress?.ToString();

        try
        {
            var outcome = await firebase
                .GeneratePasswordResetLinkAsync(email, cancellationToken)
                .ConfigureAwait(false);

            switch (outcome)
            {
                case PasswordResetOutcome.LinkGenerated:
                    logger.LogInformation(
                        "Password reset initiated for {EmailHash} from {ClientIp}",
                        emailHash,
                        clientIp ?? "unknown");
                    return TypedResults.NoContent();

                case PasswordResetOutcome.UserNotFound:
                    // OWASP defence — SAME response as the happy path.
                    logger.LogInformation(
                        "Password reset requested for unknown account {EmailHash} from {ClientIp}",
                        emailHash,
                        clientIp ?? "unknown");
                    return TypedResults.NoContent();

                case PasswordResetOutcome.FirebaseUnavailable:
                    // Real server error — do not pretend it succeeded.
                    logger.LogError(
                        "Password reset dropped for {EmailHash} from {ClientIp}: Firebase Admin SDK unavailable",
                        emailHash,
                        clientIp ?? "unknown");
                    return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);

                default:
                    logger.LogError(
                        "Password reset for {EmailHash} produced unexpected outcome {Outcome}",
                        emailHash,
                        outcome);
                    return TypedResults.StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client disconnected mid-request. No reply body will reach
            // them; returning 499 (nginx "client closed request") so any
            // reverse proxy can tag the trace appropriately.
            return TypedResults.StatusCode(499);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Password reset failed unexpectedly for {EmailHash} from {ClientIp}",
                emailHash,
                clientIp ?? "unknown");
            return TypedResults.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
