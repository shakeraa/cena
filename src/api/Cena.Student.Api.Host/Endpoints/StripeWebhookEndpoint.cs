// =============================================================================
// Cena Platform — Stripe webhook endpoint (EPIC-PRR-I PRR-301)
//
// POST /api/webhooks/stripe
// Anonymous (signature-verified). Raw body is REQUIRED (no body-parsing
// middleware); StripeWebhookHandler calls EventUtility.ConstructEvent which
// needs the verbatim bytes to verify the signature.
//
// Idempotency lives inside StripeWebhookHandler — replayed events return
// 200 OK with "duplicate" outcome, never throw.
// =============================================================================

using Cena.Actors.Subscriptions.Stripe;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stripe;

namespace Cena.Student.Api.Host.Endpoints;

/// <summary>Stripe webhook endpoint registration.</summary>
public static class StripeWebhookEndpoint
{
    /// <summary>
    /// Register <c>POST /api/webhooks/stripe</c>. No-op if the Stripe handler
    /// is not registered (i.e., Stripe isn't configured). Returns 404 in that
    /// case so probes from a misconfigured Stripe dashboard are still routed
    /// cleanly.
    /// </summary>
    public static IEndpointRouteBuilder MapStripeWebhook(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/webhooks/stripe", HandleAsync)
            .WithTags("Subscriptions")
            .AllowAnonymous()
            .WithName("StripeWebhook");
        return app;
    }

    private static async Task<IResult> HandleAsync(HttpContext http)
    {
        var handler = http.RequestServices.GetService<StripeWebhookHandler>();
        if (handler is null)
        {
            // Stripe not configured in this environment — ack with 404 so
            // the Stripe dashboard logs "endpoint missing" rather than
            // retrying with 5xx.
            return Results.NotFound(new { error = "stripe_not_configured" });
        }

        var logger = http.RequestServices.GetRequiredService<ILogger<StripeWebhookHandler>>();
        var signature = http.Request.Headers["Stripe-Signature"].ToString();

        string body;
        using (var reader = new StreamReader(http.Request.Body))
        {
            body = await reader.ReadToEndAsync(http.RequestAborted);
        }

        try
        {
            var outcome = await handler.HandleAsync(body, signature, http.RequestAborted);
            return Results.Ok(new { outcome = outcome.ToString() });
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Stripe webhook signature verification failed");
            return Results.BadRequest(new { error = "signature_verification_failed" });
        }
        catch (Exception ex)
        {
            // Do NOT return 5xx to Stripe for handler-internal issues — that
            // triggers retry storms. Log and accept; dispute queue will catch
            // dropped state via the accuracy-audit pipeline.
            logger.LogError(ex, "Stripe webhook handler failed internally; acking to prevent retry storm");
            return Results.Ok(new { outcome = "error_logged" });
        }
    }
}
