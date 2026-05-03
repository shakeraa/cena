// =============================================================================
// Cena Platform — Cultural Context Review Board Service tests (prr-034)
//
// Unit-level coverage for the MVP DLQ service:
//   - EnqueueAsync validates required fields.
//   - EnqueueAsync sets default category when blank.
//   - EnqueueAsync publishes to the per-category DLQ subject.
//   - EnqueueAsync survives NATS publish failure (DB is source of truth).
//   - SanitiseSubjectSegment normalises category strings.
//
// Marten + NATS are substituted — this is a plain unit test.
// =============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Cena.Api.Contracts.Admin.Cultural;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Cena.Admin.Api.Tests;

public sealed class CulturalContextReviewBoardServiceTests
{
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IDocumentSession _writeSession = Substitute.For<IDocumentSession>();
    private readonly INatsConnection _nats = Substitute.For<INatsConnection>();

    public CulturalContextReviewBoardServiceTests()
    {
        _store.LightweightSession().Returns(_writeSession);
        _writeSession.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    private CulturalContextReviewBoardService CreateSut()
        => new(_store, _nats, NullLogger<CulturalContextReviewBoardService>.Instance);

    // ── Sanitiser ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("language-register", "language-register")]
    [InlineData("Cultural-Reference-Clarity", "cultural-reference-clarity")]
    [InlineData("identity respect", "identity_respect")]
    [InlineData("region.context.mismatch", "region_context_mismatch")]
    [InlineData("", "unknown")]
    [InlineData("   ", "unknown")]
    public void SanitiseSubjectSegment_NormalisesAsExpected(string input, string expected)
    {
        Assert.Equal(
            expected,
            CulturalContextReviewBoardService.SanitiseSubjectSegment(input));
    }

    // ── Validation ────────────────────────────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_ThrowsOnNullRequest()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.EnqueueAsync(null!));
    }

    [Fact]
    public async Task EnqueueAsync_RequiresSchoolId()
    {
        var sut = CreateSut();
        var req = new CulturalContextEnqueueRequest(
            SchoolId: "",
            SubjectKind: "question",
            SubjectId: "q-1",
            ConcernCategory: "language-register",
            Reason: "r",
            Source: "scanner-miss",
            CorrelationId: null,
            EnqueuedByOperatorId: null);
        await Assert.ThrowsAsync<ArgumentException>(() => sut.EnqueueAsync(req));
    }

    [Fact]
    public async Task EnqueueAsync_RequiresSubjectKind()
    {
        var sut = CreateSut();
        var req = new CulturalContextEnqueueRequest(
            SchoolId: "school-x",
            SubjectKind: "",
            SubjectId: "q-1",
            ConcernCategory: "",
            Reason: "r",
            Source: "scanner-miss",
            CorrelationId: null,
            EnqueuedByOperatorId: null);
        await Assert.ThrowsAsync<ArgumentException>(() => sut.EnqueueAsync(req));
    }

    [Fact]
    public async Task EnqueueAsync_RequiresSource()
    {
        var sut = CreateSut();
        var req = new CulturalContextEnqueueRequest(
            SchoolId: "school-x",
            SubjectKind: "question",
            SubjectId: "q-1",
            ConcernCategory: "",
            Reason: "r",
            Source: "",
            CorrelationId: null,
            EnqueuedByOperatorId: null);
        await Assert.ThrowsAsync<ArgumentException>(() => sut.EnqueueAsync(req));
    }

    // ── Happy path ────────────────────────────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_StoresDocAndPublishesOnCategorySubject()
    {
        var sut = CreateSut();
        var req = new CulturalContextEnqueueRequest(
            SchoolId: "school-x",
            SubjectKind: "question",
            SubjectId: "q-7",
            ConcernCategory: "language-register",
            Reason: "machine scanner miss",
            Source: "scanner-miss",
            CorrelationId: "corr-1",
            EnqueuedByOperatorId: null);

        var id = await sut.EnqueueAsync(req);

        Assert.NotNull(id);
        Assert.StartsWith("ccdlq_", id);

        _writeSession.Received(1).Store(Arg.Is<CulturalContextReviewBoardDocument>(d =>
            d.SchoolId == "school-x" &&
            d.SubjectKind == "question" &&
            d.SubjectId == "q-7" &&
            d.ConcernCategory == "language-register" &&
            d.Source == "scanner-miss" &&
            d.Status == "pending" &&
            d.AuditTrail.Count == 1));

        await _writeSession.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        await _nats.Received(1).PublishAsync(
            Arg.Is<string>(s => s == $"{CulturalContextReviewBoardService.DlqSubjectRoot}.language-register"),
            Arg.Any<byte[]>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnqueueAsync_DefaultsCategoryWhenBlank()
    {
        var sut = CreateSut();
        var req = new CulturalContextEnqueueRequest(
            SchoolId: "school-x",
            SubjectKind: "question",
            SubjectId: "q-8",
            ConcernCategory: "",
            Reason: "r",
            Source: "scanner-miss",
            CorrelationId: null,
            EnqueuedByOperatorId: null);

        await sut.EnqueueAsync(req);

        _writeSession.Received(1).Store(Arg.Is<CulturalContextReviewBoardDocument>(d =>
            d.ConcernCategory == "unknown"));

        await _nats.Received(1).PublishAsync(
            $"{CulturalContextReviewBoardService.DlqSubjectRoot}.unknown",
            Arg.Any<byte[]>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnqueueAsync_SurvivesNatsPublishFailure_BecauseMartenIsSourceOfTruth()
    {
        // INatsConnection.PublishAsync returns ValueTask — NSubstitute's
        // .ThrowsAsync extension only targets Task, so we wire the throw
        // via When(..).Do(..) for a ValueTask-returning method.
        _nats.When(n => n.PublishAsync(
                Arg.Any<string>(),
                Arg.Any<byte[]>(),
                cancellationToken: Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("nats down"));

        var sut = CreateSut();
        var req = new CulturalContextEnqueueRequest(
            SchoolId: "school-x",
            SubjectKind: "question",
            SubjectId: "q-9",
            ConcernCategory: "cultural-reference-clarity",
            Reason: "r",
            Source: "moderator-flagged",
            CorrelationId: null,
            EnqueuedByOperatorId: "mod-1");

        var id = await sut.EnqueueAsync(req);

        Assert.StartsWith("ccdlq_", id);
        _writeSession.Received(1).Store(Arg.Any<CulturalContextReviewBoardDocument>());
        await _writeSession.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnqueueAsync_RecordsOperatorInAuditTrailWhenProvided()
    {
        var sut = CreateSut();
        var req = new CulturalContextEnqueueRequest(
            SchoolId: "school-x",
            SubjectKind: "question",
            SubjectId: "q-10",
            ConcernCategory: "identity-respect",
            Reason: "review requested",
            Source: "moderator-flagged",
            CorrelationId: null,
            EnqueuedByOperatorId: "mod-7");

        await sut.EnqueueAsync(req);

        _writeSession.Received(1).Store(Arg.Is<CulturalContextReviewBoardDocument>(d =>
            d.EnqueuedByOperatorId == "mod-7" &&
            d.AuditTrail[0].ActorId == "mod-7" &&
            d.AuditTrail[0].Action == "enqueued" &&
            d.AuditTrail[0].Note == "moderator-flagged"));
    }
}
