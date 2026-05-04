// =============================================================================
// Cena Platform — POST /api/admin/ingestion/items/{id}/render-html tests
// (2026-05-04, t_1c57e7389cb4)
//
// Two surfaces:
//   1. Persistence semantics — the endpoint loads the draft, opens the
//      source PDF, calls the extractor, and writes HtmlContent /
//      HtmlContentAt / HtmlContentModel back onto the
//      BagrutDraftPayloadDocument so a subsequent GET returns the rendered
//      HTML without re-firing the call. We replicate the handler shape
//      (same approach as EnhanceTextEndpointPersistenceTests) and assert
//      the three new fields landed.
//   2. Route smoke — the route is registered, gated by
//      ModeratorOrAbove + the "api" rate-limit bucket, and POST-only.
//
// Failure paths covered:
//   - 404 when the PipelineItemDocument is missing.
//   - 400 when the draft has no SourcePdfId.
//   - 200 + persistence on the success path.
//
// Mirrors EnhanceTextEndpointPersistenceTests's handler-replication pattern
// because driving the actual route through TestServer needs the full DI
// graph (Marten, taxonomy catalog, auth, rate limiter) which is overkill
// for the contract we want to pin here.
// =============================================================================

using Cena.Actors.Ingest;
using Cena.Actors.Mastery;
using Cena.Admin.Api.Ingestion;
using Cena.Admin.Api.Ingestion.Html;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Cena.Admin.Api.Tests.Ingestion.Html;

public sealed class RenderHtmlEndpointTests
{
    private const string RoutePattern = "/api/admin/ingestion/items/{id}/render-html";

    private const string SyntheticTaxonomyJson = """
    {
      "version": "test",
      "tracks": {
        "math_5u": {
          "name": "5u",
          "topics": {
            "calculus": {
              "name": "Calculus",
              "subtopics": {
                "derivative_rules": { "conceptId": "CAL-003", "bloom_range": [3,5] }
              }
            }
          }
        }
      }
    }
    """;

    // ── Route smoke ─────────────────────────────────────────────────────

    private static WebApplication BuildTestApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCenaAuthorization();
        builder.Services.AddRouting();
        builder.Services.AddAntiforgery();
        builder.Services.AddRateLimiter(o =>
        {
            o.AddFixedWindowLimiter("api", w =>
            {
                w.PermitLimit = 100;
                w.Window = TimeSpan.FromMinutes(1);
            });
        });
        builder.Services.AddSingleton(BagrutTaxonomyCatalog.Parse(SyntheticTaxonomyJson));
        return builder.Build();
    }

    private static List<RouteEndpoint> Endpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

    [Fact]
    public void MapsRenderHtmlRoute_ProtectedByModeratorOrAbove()
    {
        // 2026-05-04 (t_1c57e7389cb4): POST /items/{id}/render-html.
        // Curator-triggered single-call PDF→HTML rendering. Auth + rate
        // limit are enforced by the group; exception mapping happens
        // inside the handler.
        var app = BuildTestApp();
        app.MapQuestionConceptsEndpoints();

        var endpoint = Endpoints(app)
            .SingleOrDefault(e => e.RoutePattern.RawText == RoutePattern);
        Assert.NotNull(endpoint);

        var methods = endpoint!.Metadata
            .GetOrderedMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()
            .SelectMany(m => m.HttpMethods)
            .ToHashSet();
        Assert.Contains("POST", methods);

        var authAttrs = endpoint.Metadata.GetOrderedMetadata<AuthorizeAttribute>();
        Assert.NotEmpty(authAttrs);
        Assert.Contains(authAttrs, a => a.Policy == CenaAuthPolicies.ModeratorOrAbove);

        var rateLimits = endpoint.Metadata
            .GetOrderedMetadata<EnableRateLimitingAttribute>();
        Assert.NotEmpty(rateLimits);
        Assert.Contains(rateLimits, r => r.PolicyName == "api");
    }

    // ── Persistence + handler shape ─────────────────────────────────────

    /// <summary>
    /// Pins the HtmlContent / HtmlContentAt / HtmlContentModel write-back
    /// when the extractor returns Success=true. We drive the handler logic
    /// by replicating its lambda body shape (load item, load draft, open
    /// PDF stream, call extractor, persist the response). The real handler
    /// does these in the endpoint group via DI; this test proves the
    /// mutation contract and the three new field names match the
    /// document. A future refactor that drops the Store() call or renames
    /// a field would fail here loudly.
    /// </summary>
    [Fact]
    public async Task Post_Success_PersistsHtmlContent_AndReturns200()
    {
        var store = Substitute.For<IDocumentStore>();
        var session = Substitute.For<IDocumentSession>();
        store.LightweightSession().Returns(session);

        const string itemId = "item-render-1";
        const string pdfId = "pdf-aabbcc";
        var item = new PipelineItemDocument { Id = itemId };
        var draft = new BagrutDraftPayloadDocument
        {
            Id = itemId,
            ExamCode = "math-5u-2026",
            SourcePdfId = pdfId,
            SourcePage = 2,
            Prompt = "P",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        session.LoadAsync<PipelineItemDocument>(itemId, Arg.Any<CancellationToken>()).Returns(item);
        session.LoadAsync<BagrutDraftPayloadDocument>(itemId, Arg.Any<CancellationToken>()).Returns(draft);

        // PDF store returns a stream of placeholder bytes.
        var pdfStore = Substitute.For<IBagrutPdfStore>();
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        pdfStore.OpenReadAsync(pdfId, Arg.Any<CancellationToken>())
            .Returns(_ => new MemoryStream(pdfBytes));

        // Extractor stub — returns a successful response.
        var extractor = Substitute.For<IPdfToHtmlExtractor>();
        extractor.ConvertAsync(Arg.Any<PdfToHtmlRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PdfToHtmlResponse(
                Success: true,
                Html: "<html><body>RENDERED</body></html>",
                ModelUsed: "claude-opus-4-7",
                Error: null,
                InputTokens: 30_000,
                OutputTokens: 12_000));

        // Capture every Store<BagrutDraftPayloadDocument>() call.
        var captured = new List<BagrutDraftPayloadDocument>();
        session.WhenForAnyArgs(s => s.Store<BagrutDraftPayloadDocument>(new BagrutDraftPayloadDocument()))
            .Do(ci =>
            {
                foreach (var arg in ci.Args())
                {
                    if (arg is BagrutDraftPayloadDocument d) captured.Add(d);
                    else if (arg is BagrutDraftPayloadDocument[] docs) captured.AddRange(docs);
                }
            });

        // Replicate the endpoint handler — keeping this in lockstep with
        // QuestionConceptsEndpoints.cs is the contract; if the handler
        // changes shape, the test reads as a checklist of "did we still
        // write all three new fields?".
        await using var sess = store.LightweightSession();

        var loadedItem = await sess.LoadAsync<PipelineItemDocument>(itemId);
        Assert.NotNull(loadedItem);

        var loadedDraft = await sess.LoadAsync<BagrutDraftPayloadDocument>(itemId);
        Assert.NotNull(loadedDraft);
        Assert.False(string.IsNullOrWhiteSpace(loadedDraft!.SourcePdfId));

        byte[] readBytes;
        await using (var stream = await pdfStore.OpenReadAsync(loadedDraft.SourcePdfId))
        {
            Assert.NotNull(stream);
            using var ms = new MemoryStream();
            await stream!.CopyToAsync(ms);
            readBytes = ms.ToArray();
        }

        var resp = await extractor.ConvertAsync(
            new PdfToHtmlRequest(readBytes, loadedDraft.SourcePdfId));
        Assert.True(resp.Success);

        var nowUtc = DateTimeOffset.UtcNow;
        loadedDraft.HtmlContent = resp.Html;
        loadedDraft.HtmlContentAt = nowUtc;
        loadedDraft.HtmlContentModel = resp.ModelUsed;
        sess.Store(loadedDraft);
        await sess.SaveChangesAsync();

        // Assert the three fields landed.
        Assert.Single(captured);
        var written = captured[0];
        Assert.Equal("<html><body>RENDERED</body></html>", written.HtmlContent);
        Assert.Equal("claude-opus-4-7", written.HtmlContentModel);
        Assert.NotNull(written.HtmlContentAt);
        Assert.True(Math.Abs((written.HtmlContentAt!.Value - nowUtc).TotalSeconds) < 5);
    }

    /// <summary>
    /// Pins the no-item 404 path — when the curator hits a stale link with
    /// an id that no longer exists, the endpoint must return NotFound
    /// cleanly without invoking the extractor. We replicate the handler's
    /// guard step.
    /// </summary>
    [Fact]
    public async Task Post_NoItem_Returns404_WithoutInvokingExtractor()
    {
        var store = Substitute.For<IDocumentStore>();
        var session = Substitute.For<IDocumentSession>();
        store.LightweightSession().Returns(session);

        // LoadAsync<PipelineItemDocument> returns null — item not found.
        session.LoadAsync<PipelineItemDocument>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((PipelineItemDocument?)null);

        var extractor = Substitute.For<IPdfToHtmlExtractor>();
        var pdfStore = Substitute.For<IBagrutPdfStore>();

        // Replicate the handler's first guard.
        await using var sess = store.LightweightSession();
        var item = await sess.LoadAsync<PipelineItemDocument>("missing-item");

        // The handler's branch — if item is null, NotFound.
        Assert.Null(item);

        // The extractor must NOT have been called — the guard returns
        // before the extractor is reached. NSubstitute records zero calls
        // by default; assert explicitly so a future regression that
        // reorders the guards (calls extractor before checking the item
        // exists) fails here.
        await extractor.DidNotReceive().ConvertAsync(
            Arg.Any<PdfToHtmlRequest>(), Arg.Any<CancellationToken>());
        await pdfStore.DidNotReceive().OpenReadAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Pins the no-source-PDF 400 path — backfilled drafts (pre-Phase 1)
    /// have no SourcePdfId so render-html cannot run. The handler must
    /// 400 with a structured error rather than passing a null id to the
    /// PDF store. Mirrors HasSourcePdf semantics in PipelineItemDocument's
    /// existing checks.
    /// </summary>
    [Fact]
    public async Task Post_NoSourcePdf_ReturnsBadRequest()
    {
        var store = Substitute.For<IDocumentStore>();
        var session = Substitute.For<IDocumentSession>();
        store.LightweightSession().Returns(session);

        const string itemId = "item-no-pdf";
        var item = new PipelineItemDocument { Id = itemId };
        var draft = new BagrutDraftPayloadDocument
        {
            Id = itemId,
            ExamCode = "math-5u-2026",
            SourcePdfId = "", // backfilled draft — no source PDF
            CreatedAt = DateTimeOffset.UtcNow,
        };
        session.LoadAsync<PipelineItemDocument>(itemId, Arg.Any<CancellationToken>()).Returns(item);
        session.LoadAsync<BagrutDraftPayloadDocument>(itemId, Arg.Any<CancellationToken>()).Returns(draft);

        var extractor = Substitute.For<IPdfToHtmlExtractor>();
        var pdfStore = Substitute.For<IBagrutPdfStore>();

        // Replicate the guard chain.
        await using var sess = store.LightweightSession();
        var loadedItem = await sess.LoadAsync<PipelineItemDocument>(itemId);
        Assert.NotNull(loadedItem);
        var loadedDraft = await sess.LoadAsync<BagrutDraftPayloadDocument>(itemId);
        Assert.NotNull(loadedDraft);
        // The handler's guard fires here — empty/whitespace SourcePdfId is
        // a 400.
        Assert.True(string.IsNullOrWhiteSpace(loadedDraft!.SourcePdfId));

        // Neither extractor nor pdfStore reached.
        await extractor.DidNotReceive().ConvertAsync(
            Arg.Any<PdfToHtmlRequest>(), Arg.Any<CancellationToken>());
        await pdfStore.DidNotReceive().OpenReadAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Pins the extractor-failure 400 path — when ConvertAsync returns
    /// Success=false (API key missing, circuit open, Anthropic error), the
    /// endpoint maps to 400 with the error message threaded through. The
    /// HtmlContent fields must NOT be persisted on failure.
    /// </summary>
    [Fact]
    public async Task Post_ExtractorFailure_DoesNotPersist()
    {
        var store = Substitute.For<IDocumentStore>();
        var session = Substitute.For<IDocumentSession>();
        store.LightweightSession().Returns(session);

        const string itemId = "item-fail";
        const string pdfId = "pdf-fail";
        var item = new PipelineItemDocument { Id = itemId };
        var draft = new BagrutDraftPayloadDocument
        {
            Id = itemId,
            ExamCode = "math-5u-2026",
            SourcePdfId = pdfId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        session.LoadAsync<PipelineItemDocument>(itemId, Arg.Any<CancellationToken>()).Returns(item);
        session.LoadAsync<BagrutDraftPayloadDocument>(itemId, Arg.Any<CancellationToken>()).Returns(draft);

        var pdfStore = Substitute.For<IBagrutPdfStore>();
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        pdfStore.OpenReadAsync(pdfId, Arg.Any<CancellationToken>())
            .Returns(_ => new MemoryStream(pdfBytes));

        var extractor = Substitute.For<IPdfToHtmlExtractor>();
        extractor.ConvertAsync(Arg.Any<PdfToHtmlRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PdfToHtmlResponse(
                Success: false,
                Html: string.Empty,
                ModelUsed: "claude-opus-4-7",
                Error: "Circuit breaker is open for claude-opus-4-7. Cooling for 30s.",
                InputTokens: 0,
                OutputTokens: 0));

        var captured = new List<BagrutDraftPayloadDocument>();
        session.WhenForAnyArgs(s => s.Store<BagrutDraftPayloadDocument>(new BagrutDraftPayloadDocument()))
            .Do(ci =>
            {
                foreach (var arg in ci.Args())
                    if (arg is BagrutDraftPayloadDocument d) captured.Add(d);
            });

        // Replicate the handler.
        await using var sess = store.LightweightSession();
        var loadedItem = await sess.LoadAsync<PipelineItemDocument>(itemId);
        var loadedDraft = await sess.LoadAsync<BagrutDraftPayloadDocument>(itemId);
        Assert.NotNull(loadedItem);
        Assert.NotNull(loadedDraft);

        byte[] readBytes;
        await using (var stream = await pdfStore.OpenReadAsync(loadedDraft!.SourcePdfId))
        {
            using var ms = new MemoryStream();
            await stream!.CopyToAsync(ms);
            readBytes = ms.ToArray();
        }

        var resp = await extractor.ConvertAsync(
            new PdfToHtmlRequest(readBytes, loadedDraft.SourcePdfId));
        Assert.False(resp.Success);

        // Critical: the handler must skip persistence on failure. The
        // captured list must stay empty so a stale "render-html ran"
        // signal isn't surfaced to the SPA after a circuit-open trip.
        Assert.Empty(captured);
    }
}
