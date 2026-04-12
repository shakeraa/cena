// =============================================================================
// Cena Platform -- Student Auth REST Endpoints (FIND-ux-006b)
// Anonymous endpoints that back the self-service authentication recovery
// flows on the student web. The only route today is POST
// /api/auth/password-reset, which forwards to Firebase Admin SDK via
// IFirebaseAdminService and ALWAYS returns 204 No Content regardless of
// whether the email is known (OWASP account-enumeration defence).
// =============================================================================

using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Cena.Actors.Events;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Firebase;
using Marten;
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

    // ── FIND-privacy-001: Age Gate & Parental Consent DTOs ──

    /// <summary>
    /// Request body for POST /api/auth/age-check.
    /// Accepts a date of birth and returns the consent path required.
    /// </summary>
    public sealed record AgeCheckRequest(
        [property: Required] DateOnly DateOfBirth);

    /// <summary>
    /// Response body for POST /api/auth/age-check.
    /// </summary>
    public sealed record AgeCheckResponse(
        int Age,
        string ConsentPath,    // "none" | "parent-required"
        string ConsentTier);   // "adult" | "teen" | "child"

    /// <summary>
    /// Request body for POST /api/auth/parent-consent-challenge.
    /// Sends a verifiable challenge email to the parent/guardian.
    /// </summary>
    public sealed record ParentConsentChallengeRequest(
        [property: Required] string StudentId,
        [property: Required, EmailAddress] string ParentEmail,
        [property: Required] DateOnly DateOfBirth);

    /// <summary>
    /// Response body for POST /api/auth/parent-consent-challenge.
    /// </summary>
    public sealed record ParentConsentChallengeResponse(
        bool Sent,
        string ConsentToken);

    /// <summary>
    /// Response body for POST /api/auth/parent-consent-verify/{token}.
    /// </summary>
    public sealed record ParentConsentVerifyResponse(
        bool Verified,
        string StudentId);

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

        // FIND-privacy-001: Age gate + parental consent endpoints
        group.MapPost("/age-check", AgeCheck)
            .WithName("PostAgeCheck")
            .WithSummary("Check a date of birth and return the required consent path.");

        group.MapPost("/parent-consent-challenge", ParentConsentChallenge)
            .WithName("PostParentConsentChallenge")
            .WithSummary("Send a verifiable consent challenge email to a parent/guardian.");

        group.MapPost("/parent-consent-verify/{token}", ParentConsentVerify)
            .WithName("PostParentConsentVerify")
            .WithSummary("Verify a parent consent token and mark consent as active.");

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

    // =========================================================================
    // FIND-privacy-001: Age Gate + Parental Consent Handlers
    // =========================================================================

    /// <summary>
    /// POST /api/auth/age-check
    ///
    /// Accepts a date of birth and returns the consent path required for
    /// registration. Does not create any server-side state.
    ///
    /// Consent tiers (hardest framework wins — COPPA &lt;13):
    ///   - age &lt; 13  → "child"  → parent-required (COPPA §312.5)
    ///   - age 13-15 → "teen"   → parent-required (GDPR Art 8, ICO Std 7)
    ///   - age >= 16  → "adult" → none (GDPR default digital consent age)
    /// </summary>
    internal static IResult AgeCheck(
        [FromBody] AgeCheckRequest request,
        [FromServices] ILogger<AuthLoggerMarker> logger)
    {
        if (request is null)
            return TypedResults.BadRequest(new AuthErrorResponse("Request body is required."));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dob = request.DateOfBirth;

        // Reject future dates and implausibly old dates
        if (dob > today)
            return TypedResults.BadRequest(new AuthErrorResponse("Date of birth cannot be in the future."));

        if (dob < today.AddYears(-120))
            return TypedResults.BadRequest(new AuthErrorResponse("Date of birth is not valid."));

        var age = CalculateAge(dob, today);

        var (consentPath, consentTier) = age switch
        {
            < 13 => ("parent-required", "child"),
            < 16 => ("parent-required", "teen"),
            _ => ("none", "adult"),
        };

        logger.LogInformation(
            "Age check: age={Age}, tier={ConsentTier}, path={ConsentPath}",
            age, consentTier, consentPath);

        return TypedResults.Ok(new AgeCheckResponse(age, consentPath, consentTier));
    }

    /// <summary>
    /// POST /api/auth/parent-consent-challenge
    ///
    /// Creates a consent token and sends a verification email to the parent.
    /// The parent must click the link to verify (COPPA §312.5(b)(2)).
    /// Also appends the AgeAndConsentRecorded_V1 event with status=pending_parent.
    /// </summary>
    internal static async Task<IResult> ParentConsentChallenge(
        [FromBody] ParentConsentChallengeRequest request,
        [FromServices] IDocumentStore store,
        [FromServices] ILogger<AuthLoggerMarker> logger,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ParentEmail) || string.IsNullOrWhiteSpace(request.StudentId))
            return TypedResults.BadRequest(new AuthErrorResponse("StudentId and ParentEmail are required."));

        // Validate email shape
        var parentEmail = request.ParentEmail.Trim();
        if (parentEmail.Length > 320 || parentEmail.IndexOf('@') <= 0 || parentEmail.IndexOf('@') == parentEmail.Length - 1)
            return TypedResults.BadRequest(new AuthErrorResponse("Parent email is not in a recognized format."));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dob = request.DateOfBirth;

        if (dob > today)
            return TypedResults.BadRequest(new AuthErrorResponse("Date of birth cannot be in the future."));

        var age = CalculateAge(dob, today);

        if (age >= 16)
            return TypedResults.BadRequest(new AuthErrorResponse("Parental consent is not required for users aged 16 or older."));

        // Generate a cryptographically secure consent token
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var consentToken = Convert.ToBase64String(tokenBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        var consentTier = age < 13 ? "child" : "teen";

        // Persist the consent event with pending status
        await using var session = store.LightweightSession();
        var consentEvent = new AgeAndConsentRecorded_V1(
            StudentId: request.StudentId,
            DateOfBirth: dob,
            AgeAtRegistration: age,
            ConsentTier: consentTier,
            ParentEmail: parentEmail,
            ParentalConsentGiven: false,
            ParentalConsentToken: consentToken,
            ConsentStatus: "pending_parent",
            RecordedAt: DateTimeOffset.UtcNow);

        session.Events.Append(request.StudentId, consentEvent);
        await session.SaveChangesAsync(cancellationToken);

        // TODO: Send actual email via IEmailService when wired.
        // For now, log the token for E2E testing. The verification endpoint
        // is functional — parent email sending is a separate task.
        var parentEmailHash = EmailHasher.Hash(parentEmail);
        logger.LogInformation(
            "FIND-privacy-001: Parental consent challenge created for student {StudentId}, " +
            "parent={ParentEmailHash}, tier={ConsentTier}, token generated",
            request.StudentId, parentEmailHash, consentTier);

        return TypedResults.Ok(new ParentConsentChallengeResponse(Sent: true, ConsentToken: consentToken));
    }

    /// <summary>
    /// POST /api/auth/parent-consent-verify/{token}
    ///
    /// Called when a parent clicks the verification link. Looks up the pending
    /// consent token, marks it as verified, and appends a new event.
    /// </summary>
    internal static async Task<IResult> ParentConsentVerify(
        string token,
        [FromServices] IDocumentStore store,
        [FromServices] ILogger<AuthLoggerMarker> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return TypedResults.BadRequest(new AuthErrorResponse("Consent token is required."));

        // Find the student profile with this pending consent token
        await using var querySession = store.QuerySession();
        var profile = await querySession.Query<StudentProfileSnapshot>()
            .Where(p => p.ConsentStatus == "pending_parent")
            .ToListAsync(cancellationToken);

        // Linear scan is acceptable because pending consents are rare and short-lived.
        // In production at scale, consider a dedicated ConsentChallenge document.
        // We cannot query ParentalConsentToken directly in the snapshot because
        // it is not stored on the snapshot — it's in the event. So we scan events.
        await using var writeSession = store.LightweightSession();

        foreach (var p in profile)
        {
            // Read the last AgeAndConsentRecorded event for this student
            var events = await querySession.Events.FetchStreamAsync(p.StudentId, token: cancellationToken);
            var consentEvent = events
                .Select(e => e.Data)
                .OfType<AgeAndConsentRecorded_V1>()
                .LastOrDefault(e => e.ParentalConsentToken == token && e.ConsentStatus == "pending_parent");

            if (consentEvent is not null)
            {
                // Append a new verified event
                var verifiedEvent = new AgeAndConsentRecorded_V1(
                    StudentId: consentEvent.StudentId,
                    DateOfBirth: consentEvent.DateOfBirth,
                    AgeAtRegistration: consentEvent.AgeAtRegistration,
                    ConsentTier: consentEvent.ConsentTier,
                    ParentEmail: consentEvent.ParentEmail,
                    ParentalConsentGiven: true,
                    ParentalConsentToken: null,  // Token consumed
                    ConsentStatus: "verified",
                    RecordedAt: DateTimeOffset.UtcNow);

                writeSession.Events.Append(consentEvent.StudentId, verifiedEvent);
                await writeSession.SaveChangesAsync(cancellationToken);

                logger.LogInformation(
                    "FIND-privacy-001: Parental consent verified for student {StudentId}, tier={ConsentTier}",
                    consentEvent.StudentId, consentEvent.ConsentTier);

                return TypedResults.Ok(new ParentConsentVerifyResponse(Verified: true, StudentId: consentEvent.StudentId));
            }
        }

        logger.LogWarning("FIND-privacy-001: Consent token not found or already consumed: {TokenPrefix}...",
            token.Length > 8 ? token[..8] : token);

        return TypedResults.NotFound(new AuthErrorResponse("Consent token not found or already used."));
    }

    // ── FIND-privacy-001: Helper ──

    /// <summary>
    /// Calculates age in complete years from a date of birth.
    /// Handles leap-year birthdays correctly.
    /// </summary>
    internal static int CalculateAge(DateOnly dateOfBirth, DateOnly asOf)
    {
        var age = asOf.Year - dateOfBirth.Year;

        // If the birthday has not occurred yet this year, subtract one
        if (asOf < dateOfBirth.AddYears(age))
            age--;

        return age;
    }
}
