// =============================================================================
// Cena Platform — RecordingLlmClient (e2e-flow test harness)
//
// Decorator that captures every ILlmClient.CompleteAsync call into a Marten
// document before forwarding to the inner client. Exists for two
// end-to-end test pillars that can't be asserted any other way:
//
//   * EPIC-E2E-I-05 — "PII never appears in an LLM prompt". The test
//     enters PII-shaped strings into the SPA, waits for the flow to hit
//     an LLM path, then asserts the recorded payload contains
//     <redacted:*> tokens and not the original text.
//
//   * EPIC-E2E-D-05 — same test framing, PII scrubber angle.
//   * EPIC-E2E-D-07 — "stem-grounded hints only use stem + student
//     attempt". The test asserts the captured SystemPrompt/UserPrompt
//     contain only the expected context, not cross-student bleed.
//
// Why Marten (not an in-memory ring buffer): the e2e specs run in
// separate processes from the host; the recorder must survive the
// Playwright-to-host process boundary. A Marten doc + a test-only
// retrieval endpoint is the simplest survive-the-boundary shape.
//
// Enable via `Cena:Testing:LlmRecorderEnabled: true` in dev. Production
// composition never sets this flag, so the decorator is never wired.
// Per memory "No stubs — production grade": this is a test seam, not
// a production code path.
// =============================================================================

using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Gateway;

/// <summary>
/// Marten document representing one captured LLM request/response pair.
/// Persisted by <see cref="RecordingLlmClient"/> when the recorder is
/// enabled. Read by the test-only endpoint in <c>LlmRecorderEndpoints</c>.
/// </summary>
public sealed class LlmCallRecording
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string ModelId { get; set; } = "";
    public string SystemPrompt { get; set; } = "";
    public string UserPrompt { get; set; } = "";
    public string ResponseContent { get; set; } = "";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public double LatencyMs { get; set; }
    public bool FromCache { get; set; }
    /// <summary>Null when the upstream client threw; otherwise stores the exception type + message.</summary>
    public string? ErrorKind { get; set; }
    public string? ErrorMessage { get; set; }
    /// <summary>
    /// Optional test-supplied correlation tag so a spec can find its own calls
    /// without leaking tenant scope into test-only code paths. Set by passing
    /// an HTTP header or request property the recorder reads; today left null
    /// — filtering by time window is sufficient for a single-spec run.
    /// </summary>
    public string? Tag { get; set; }
}

/// <summary>
/// Wraps an <see cref="ILlmClient"/> and persists each call's request +
/// response to Marten before returning. On upstream failure, records the
/// exception and re-throws so the caller sees identical semantics to
/// the undecorated client.
/// </summary>
public sealed class RecordingLlmClient : ILlmClient
{
    private readonly ILlmClient _inner;
    private readonly IDocumentStore _store;
    private readonly ILogger<RecordingLlmClient> _logger;

    public RecordingLlmClient(
        ILlmClient inner,
        IDocumentStore store,
        ILogger<RecordingLlmClient> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        LlmResponse? response = null;
        Exception? failure = null;
        try
        {
            response = await _inner.CompleteAsync(request, ct);
            return response;
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            sw.Stop();
            // Fire-and-forget the persistence so a slow Marten write never
            // changes an LLM call's observable latency. Test helpers poll
            // the read endpoint with a timeout, so a little async delay is
            // fine. If the write itself fails we log but never rethrow —
            // the recorder must never impact the call it's recording.
            _ = PersistAsync(request, response, failure, sw.Elapsed);
        }
    }

    private async Task PersistAsync(
        LlmRequest request,
        LlmResponse? response,
        Exception? failure,
        TimeSpan elapsed)
    {
        try
        {
            await using var session = _store.LightweightSession();
            var record = new LlmCallRecording
            {
                Timestamp = DateTimeOffset.UtcNow,
                ModelId = response?.ModelId ?? request.ModelId ?? "unknown",
                SystemPrompt = request.SystemPrompt,
                UserPrompt = request.UserPrompt,
                ResponseContent = response?.Content ?? string.Empty,
                InputTokens = response?.InputTokens ?? 0,
                OutputTokens = response?.OutputTokens ?? 0,
                LatencyMs = elapsed.TotalMilliseconds,
                FromCache = response?.FromCache ?? false,
                ErrorKind = failure?.GetType().FullName,
                ErrorMessage = failure?.Message,
            };
            session.Store(record);
            await session.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LLM_RECORDER] failed to persist call; swallowing");
        }
    }
}
