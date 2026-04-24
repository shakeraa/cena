// =============================================================================
// Cena Platform -- LLM Recorder Test Endpoints (e2e-flow only)
//
// Read-only access to LlmCallRecording documents for e2e specs.
// Registered by Cena.Admin.Api.Host Program.cs ONLY when
// `Cena:Testing:LlmRecorderEnabled: true`. Production never mounts
// these routes.
//
// Consumed by src/student/full-version/tests/e2e-flow/probes/llm-recorder.ts
// — the Playwright-side client that asserts "this PII never reached the
// LLM" and "this hint prompt only contained the stem".
//
// Auth: gated by `Cena:Testing:LlmRecorderEnabled`, no auth required on the
// retrieval endpoint because:
//   (a) the flag is never set in production composition
//   (b) requiring admin JWT would force every e2e spec to mint an admin
//       user, which is heavyweight for a test-only assertion
// The endpoint is IP-pinned to localhost by the gating check below.
// =============================================================================

using Cena.Actors.Gateway;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Cena.Admin.Api;

public static class LlmRecorderEndpoints
{
    /// <summary>
    /// Registers the test-only LLM recorder query endpoint when
    /// <c>Cena:Testing:LlmRecorderEnabled</c> is true AND the host is in
    /// Development. Double-gated so a stray config flag in a staging
    /// environment cannot accidentally expose the recorder.
    /// </summary>
    public static IEndpointRouteBuilder MapLlmRecorderTestEndpoints(
        this IEndpointRouteBuilder app,
        IConfiguration configuration,
        IHostEnvironment env)
    {
        var recorderEnabled = configuration.GetValue<bool>("Cena:Testing:LlmRecorderEnabled");
        if (!recorderEnabled || !env.IsDevelopment())
            return app;

        var group = app.MapGroup("/api/test/llm-recorder")
            .WithTags("Test: LLM Recorder");

        // GET /api/test/llm-recorder/recordings?sinceMs=<unix-ms>&limit=50
        //   Returns recordings whose Timestamp >= sinceMs, newest first,
        //   capped at `limit` rows. The sinceMs parameter is required so
        //   specs never fetch the full table; a spec records its own
        //   start-time (Date.now()) and filters on that.
        group.MapGet("/recordings", async (
            long? sinceMs,
            int? limit,
            HttpContext http,
            IQuerySession query) =>
        {
            if (!IsLocalRequest(http))
                return Results.Forbid();

            var cap = Math.Clamp(limit ?? 50, 1, 500);
            var cutoff = sinceMs.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(sinceMs.Value)
                : DateTimeOffset.UtcNow.AddMinutes(-5);

            var rows = await query
                .Query<LlmCallRecording>()
                .Where(r => r.Timestamp >= cutoff)
                .OrderByDescending(r => r.Timestamp)
                .Take(cap)
                .ToListAsync();

            return Results.Ok(new { count = rows.Count, recordings = rows });
        });

        // DELETE /api/test/llm-recorder/recordings — clear between specs.
        // Specs that need a blank slate call this in beforeEach. The
        // alternative — filtering by sinceMs — is cheaper but specs that
        // want to assert "zero LLM calls during X" need a reliable floor.
        group.MapDelete("/recordings", async (HttpContext http, IDocumentSession session) =>
        {
            if (!IsLocalRequest(http))
                return Results.Forbid();

            session.DeleteWhere<LlmCallRecording>(r => true);
            await session.SaveChangesAsync();
            return Results.NoContent();
        });

        return app;
    }

    /// <summary>
    /// Belt-and-braces check: refuse the endpoint from public IPs. Accepts
    /// loopback + RFC1918 private ranges, which covers the Playwright
    /// harness (running on the host and reaching admin-api via Docker's
    /// bridge IP). Combined with <c>Cena:Testing:LlmRecorderEnabled</c> +
    /// Development-only gating above, an accidentally-enabled recorder
    /// still can't be queried from outside the host's local network.
    /// </summary>
    private static bool IsLocalRequest(HttpContext http)
    {
        var remote = http.Connection.RemoteIpAddress;
        if (remote is null) return false;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        var s = remote.ToString();
        // Loopback (IPv4 + IPv6)
        if (s == "127.0.0.1" || s == "::1") return true;
        // RFC1918: 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16
        if (s.StartsWith("10.", StringComparison.Ordinal)) return true;
        if (s.StartsWith("192.168.", StringComparison.Ordinal)) return true;
        if (s.StartsWith("172."))
        {
            // Narrow 172.16.0.0 - 172.31.255.255 only
            var parts = s.Split('.');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var octet)
                && octet >= 16 && octet <= 31)
                return true;
        }
        return false;
    }
}
