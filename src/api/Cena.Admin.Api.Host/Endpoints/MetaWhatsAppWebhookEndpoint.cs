// =============================================================================
// Cena Platform — Meta WhatsApp webhook endpoint (PRR-437)
//
// Two routes:
//   GET  /api/webhooks/meta-whatsapp  — Meta's verify-token handshake at
//                                        subscription time
//   POST /api/webhooks/meta-whatsapp  — inbound delivery-status callbacks
//
// Both are anonymous (by design — Meta doesn't carry our auth tokens).
// Security comes from:
//   · GET: exact-match on hub.verify_token against our MetaCloud:
//     WebhookVerifyToken config. Mismatch → 403.
//   · POST: HMAC-SHA256 signature verification over the raw body via
//     IMetaWebhookSignatureVerifier. Mismatch → 401.
//
// Raw-body handling: the signature is computed over the bytes Meta sent,
// not over a re-serialised JSON representation. We copy the request body
// to a MemoryStream with HttpRequest.EnableBuffering + ReadAllAsync so
// the verifier sees exactly what Meta signed.
//
// Idempotency: Meta retries failed POSTs. The status envelope carries a
// message id (entry[].changes[].value.statuses[].id) that we use as an
// idempotency key in a short-TTL Redis/memory cache. v1 ships an
// in-memory dedup set keyed by that id with a 48h TTL; a Marten-backed
// variant is a scale follow-up (follows the MartenProcessedWebhookLog
// pattern from Stripe).
//
// Wire into composition root: `app.MapMetaWhatsAppWebhook()` adjacent
// to the Stripe webhook mapping.
// =============================================================================

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Cena.Actors.ParentDigest;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Admin.Api.Host.Endpoints;

/// <summary>Dedup store for Meta webhook message ids.</summary>
public interface IMetaWebhookDedupStore
{
    /// <summary>
    /// True if <paramref name="messageId"/> was NOT seen before (newly
    /// claimed). False on replay. Idempotent.
    /// </summary>
    bool TryClaim(string messageId, DateTimeOffset nowUtc);
}

/// <summary>
/// In-memory dedup: ConcurrentDictionary&lt;string, DateTimeOffset&gt; with
/// a 48h TTL. Bounded memory at Meta's v21 delivery volume (bursts of
/// dozens/sec, steady-state hundreds/hour) × 48h TTL = ~10s of thousands
/// of rows, low MB footprint. A single pod is sufficient for v1; a shared
/// Redis store lands in the scale follow-up.
/// </summary>
public sealed class InMemoryMetaWebhookDedupStore : IMetaWebhookDedupStore
{
    /// <summary>TTL after which a claimed id is re-claimable (Meta retries ≤3d).</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(48);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool TryClaim(string messageId, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        // Evict stale entries lazily on touch so steady-state memory
        // stays bounded without a dedicated sweeper.
        EvictStale(nowUtc);

        return _seen.TryAdd(messageId, nowUtc);
    }

    private void EvictStale(DateTimeOffset nowUtc)
    {
        var cutoff = nowUtc - Ttl;
        foreach (var kv in _seen)
        {
            if (kv.Value < cutoff)
            {
                _seen.TryRemove(kv.Key, out _);
            }
        }
    }
}

/// <summary>Endpoint registration.</summary>
public static class MetaWhatsAppWebhookEndpoint
{
    /// <summary>Register GET + POST /api/webhooks/meta-whatsapp.</summary>
    public static IEndpointRouteBuilder MapMetaWhatsAppWebhook(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/webhooks/meta-whatsapp", HandleGetAsync)
            .WithTags("ParentDigest")
            .AllowAnonymous()
            .WithName("MetaWhatsAppWebhookVerify");

        app.MapPost("/api/webhooks/meta-whatsapp", HandlePostAsync)
            .WithTags("ParentDigest")
            .AllowAnonymous()
            .WithName("MetaWhatsAppWebhookInbound");

        return app;
    }

    /// <summary>
    /// GET handshake. Meta sends <c>hub.mode=subscribe</c>,
    /// <c>hub.verify_token</c>, <c>hub.challenge</c>. Match the
    /// verify-token config → echo challenge with 200. Mismatch → 403.
    /// </summary>
    public static IResult HandleGetAsync(HttpContext http)
    {
        var opts = http.RequestServices
            .GetService<IOptions<MetaCloudWhatsAppOptions>>()?.Value;
        if (opts is null || !opts.IsWebhookReady)
        {
            // Webhook not configured → route to 404 so a probe on a
            // mis-configured host doesn't pretend to be wired.
            return Results.NotFound();
        }

        var mode = http.Request.Query["hub.mode"].ToString();
        var token = http.Request.Query["hub.verify_token"].ToString();
        var challenge = http.Request.Query["hub.challenge"].ToString();

        if (!string.Equals(mode, "subscribe", StringComparison.Ordinal))
        {
            return Results.Forbid();
        }
        if (!string.Equals(token, opts.WebhookVerifyToken, StringComparison.Ordinal))
        {
            return Results.Forbid();
        }

        // Meta expects the challenge value in the body verbatim.
        http.Response.ContentType = "text/plain";
        return Results.Text(challenge, "text/plain");
    }

    /// <summary>
    /// POST inbound. Verify signature → dedup → map → (delegate routing
    /// to the handler — v1 logs the decision, a follow-up wires it to
    /// the dead-letter + sender-quality stores).
    /// </summary>
    public static async Task<IResult> HandlePostAsync(HttpContext http)
    {
        var services = http.RequestServices;
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("MetaWhatsAppWebhook");

        var opts = services.GetService<IOptions<MetaCloudWhatsAppOptions>>()?.Value;
        if (opts is null || !opts.IsWebhookReady)
        {
            logger.LogWarning(
                "[PRR-437] Meta webhook POST rejected: webhook not configured.");
            return Results.NotFound();
        }

        var verifier = services.GetService<IMetaWebhookSignatureVerifier>();
        if (verifier is null)
        {
            logger.LogWarning(
                "[PRR-437] Meta webhook POST rejected: verifier not registered.");
            return Results.NotFound();
        }

        // Read raw body bytes — we verify the signature against the
        // verbatim bytes Meta sent.
        http.Request.EnableBuffering();
        using var ms = new MemoryStream();
        await http.Request.Body.CopyToAsync(ms).ConfigureAwait(false);
        var bodyBytes = ms.ToArray();
        http.Request.Body.Position = 0;

        var signatureHeader = http.Request.Headers["X-Hub-Signature-256"].ToString();
        if (!verifier.IsValid(bodyBytes, signatureHeader))
        {
            logger.LogWarning(
                "[PRR-437] Meta webhook signature invalid; headerLen={HeaderLen}, bodyLen={BodyLen}",
                signatureHeader?.Length ?? 0,
                bodyBytes.Length);
            // Meta's convention: 401 on signature failure.
            return Results.Unauthorized();
        }

        // Parse the envelope just enough to extract status rows for
        // mapping. Keep the parse defensive — any malformed/missing path
        // returns 200 (Meta only cares about acks) + a structured log.
        List<WebhookStatusRow> rows;
        try
        {
            rows = ParseStatusRows(bodyBytes);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex,
                "[PRR-437] Meta webhook body not JSON-parseable; acking 200.");
            return Results.Ok();
        }

        var dedup = services.GetRequiredService<IMetaWebhookDedupStore>();
        var now = services.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        var processed = 0;
        var skippedDup = 0;

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Id))
            {
                continue;
            }
            if (!dedup.TryClaim(row.Id, now))
            {
                skippedDup++;
                continue;
            }
            var decision = MetaWebhookStatusMapper.Map(row.Status, row.ErrorCode);
            // v1: log the decision so ops sees the routing in the
            // structured log. Downstream wiring (dead-letter store +
            // sender-quality store + template pause) lands in a
            // follow-up that consumes this same decision record.
            logger.LogInformation(
                "[PRR-437] MetaStatus id={Id} recipient={RecipientHashPrefix} status={Status} "
                + "metaCode={MetaCode} action={Action} reason={ReasonCode}",
                row.Id,
                HashPrefix(row.RecipientId),
                row.Status,
                row.ErrorCode,
                decision.Action,
                decision.ReasonCode);
            processed++;
        }

        logger.LogInformation(
            "[PRR-437] MetaWebhookBatch processed={Processed} dup={Dup} total={Total}",
            processed, skippedDup, rows.Count);

        // Meta wants 200 regardless — retries get costlier when we
        // return non-2xx, and we've already claimed the ids we processed.
        return Results.Ok();
    }

    /// <summary>
    /// Parse Meta's envelope into a flat list of status rows. Tolerant
    /// of missing fields — returns what it can, logs through exceptions
    /// to the caller.
    /// </summary>
    public static List<WebhookStatusRow> ParseStatusRows(ReadOnlySpan<byte> body)
    {
        var result = new List<WebhookStatusRow>();
        using var doc = JsonDocument.Parse(body.ToArray());
        if (!doc.RootElement.TryGetProperty("entry", out var entryArr) ||
            entryArr.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var entry in entryArr.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changesArr) ||
                changesArr.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (var change in changesArr.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value) ||
                    !value.TryGetProperty("statuses", out var statusArr) ||
                    statusArr.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                foreach (var status in statusArr.EnumerateArray())
                {
                    var row = new WebhookStatusRow
                    {
                        Id = status.TryGetProperty("id", out var idProp)
                            ? idProp.GetString() ?? string.Empty
                            : string.Empty,
                        RecipientId = status.TryGetProperty("recipient_id", out var rProp)
                            ? rProp.GetString() ?? string.Empty
                            : string.Empty,
                        Status = status.TryGetProperty("status", out var sProp)
                            ? sProp.GetString() ?? string.Empty
                            : string.Empty,
                        ErrorCode = status.TryGetProperty("errors", out var errArr)
                            && errArr.ValueKind == JsonValueKind.Array
                            && errArr.GetArrayLength() > 0
                            && errArr[0].TryGetProperty("code", out var codeProp)
                            && codeProp.ValueKind == JsonValueKind.Number
                            ? codeProp.GetInt32()
                            : (int?)null,
                    };
                    result.Add(row);
                }
            }
        }
        return result;
    }

    private static string HashPrefix(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        // Don't log full recipient phone — truncate to avoid PII in logs.
        return raw.Length <= 4 ? "****" : raw[..4] + "****";
    }

    /// <summary>Flat wire-row extracted from the Meta envelope.</summary>
    public sealed class WebhookStatusRow
    {
        public string Id { get; set; } = "";
        public string RecipientId { get; set; } = "";
        public string Status { get; set; } = "";
        public int? ErrorCode { get; set; }
    }
}
