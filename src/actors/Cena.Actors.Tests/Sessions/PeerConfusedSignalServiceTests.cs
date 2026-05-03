// =============================================================================
// Cena Platform — PeerConfusedSignalService unit tests (prr-159 / F5)
//
// Covers:
//   - Happy-path emission creates the aggregate with count=1.
//   - Second emission from a different student increments the count.
//   - Same student emitting twice is idempotent.
//   - Cross-tenant emission is rejected.
//   - k-anonymity floor hides the count below the threshold.
//   - GetVisibleCount returns count and floor flag above the threshold.
//   - Emitter hash is deterministic for the same (student, session, question)
//     triple AND different across any of the three components.
//   - Defensive argument-validation contracts.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cena.Actors.Sessions;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Cena.Actors.Tests.Sessions;

public sealed class PeerConfusedSignalServiceTests
{
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IQuerySession _querySession = Substitute.For<IQuerySession>();
    private readonly IDocumentSession _writeSession = Substitute.For<IDocumentSession>();
    private readonly Dictionary<string, PeerConfusedSignalAggregate> _docs = new();

    public PeerConfusedSignalServiceTests()
    {
        _store.QuerySession().Returns(_querySession);
        _store.LightweightSession().Returns(_writeSession);

        _querySession.LoadAsync<PeerConfusedSignalAggregate>(
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<PeerConfusedSignalAggregate?>(
                _docs.TryGetValue((string)call.Args()[0], out var d) ? d : null));

        _writeSession.LoadAsync<PeerConfusedSignalAggregate>(
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<PeerConfusedSignalAggregate?>(
                _docs.TryGetValue((string)call.Args()[0], out var d) ? d : null));

        // Marten's IDocumentSession.Store<T>(params T[] entities) — so
        // the captured argument is a PeerConfusedSignalAggregate[].
        _writeSession.When(s => s.Store(Arg.Any<PeerConfusedSignalAggregate[]>()))
            .Do(call =>
            {
                var docs = (PeerConfusedSignalAggregate[])call.Args()[0];
                foreach (var d in docs)
                {
                    _docs[d.Id] = d;
                }
            });

        _writeSession.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    private PeerConfusedSignalService CreateSut(int floor = 3)
        => new(_store, NullLogger<PeerConfusedSignalService>.Instance, floor);

    // ── Emission happy paths ───────────────────────────────────────────────

    [Fact]
    public async Task Emit_FirstEmission_CreatesDocWithCountOne()
    {
        var sut = CreateSut();

        var outcome = await sut.EmitAsync("s-alice", "sess-1", "q-1", "school-x");

        Assert.Equal(PeerConfusedEmitOutcome.Recorded, outcome);
        var doc = _docs["sess-1:q-1"];
        Assert.Equal(1, doc.ConfusedCount);
        Assert.Single(doc.EmitterHashes);
        Assert.Equal("school-x", doc.SchoolId);
        Assert.True(doc.MlExcluded);
    }

    [Fact]
    public async Task Emit_DoesNotStoreStudentId_AnywhereOnDoc()
    {
        var sut = CreateSut();
        await sut.EmitAsync("s-alice-very-unique-id", "sess-1", "q-1", "school-x");

        var doc = _docs["sess-1:q-1"];
        // Neither the document id nor any emitter hash should contain the
        // raw student id as a substring.
        Assert.DoesNotContain("s-alice-very-unique-id", doc.Id, StringComparison.OrdinalIgnoreCase);
        foreach (var hash in doc.EmitterHashes)
        {
            Assert.DoesNotContain("s-alice-very-unique-id", hash, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("alice", doc.Id, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Emit_DifferentStudentsSameQuestion_Increments()
    {
        var sut = CreateSut();

        await sut.EmitAsync("s-alice", "sess-1", "q-1", "school-x");
        await sut.EmitAsync("s-bob", "sess-1", "q-1", "school-x");
        await sut.EmitAsync("s-carol", "sess-1", "q-1", "school-x");

        Assert.Equal(3, _docs["sess-1:q-1"].ConfusedCount);
    }

    [Fact]
    public async Task Emit_SameStudentTwice_IsIdempotent()
    {
        var sut = CreateSut();

        var first = await sut.EmitAsync("s-alice", "sess-1", "q-1", "school-x");
        var second = await sut.EmitAsync("s-alice", "sess-1", "q-1", "school-x");

        Assert.Equal(PeerConfusedEmitOutcome.Recorded, first);
        Assert.Equal(PeerConfusedEmitOutcome.AlreadyEmitted, second);
        Assert.Equal(1, _docs["sess-1:q-1"].ConfusedCount);
    }

    [Fact]
    public async Task Emit_DifferentQuestionsSameSession_AreSeparate()
    {
        var sut = CreateSut();

        await sut.EmitAsync("s-alice", "sess-1", "q-1", "school-x");
        await sut.EmitAsync("s-alice", "sess-1", "q-2", "school-x");

        Assert.Equal(1, _docs["sess-1:q-1"].ConfusedCount);
        Assert.Equal(1, _docs["sess-1:q-2"].ConfusedCount);
    }

    [Fact]
    public async Task Emit_CrossTenant_IsRejected()
    {
        var sut = CreateSut();

        await sut.EmitAsync("s-alice", "sess-1", "q-1", "school-x");
        var outcome = await sut.EmitAsync("s-mallory", "sess-1", "q-1", "school-y");

        Assert.Equal(PeerConfusedEmitOutcome.TenantMismatch, outcome);
        Assert.Equal(1, _docs["sess-1:q-1"].ConfusedCount);
    }

    // ── k-anonymity floor ──────────────────────────────────────────────────

    [Fact]
    public async Task GetVisibleCount_BelowFloor_ReturnsNull()
    {
        var sut = CreateSut(floor: 3);
        await sut.EmitAsync("s-alice", "sess-1", "q-1", "school-x");
        await sut.EmitAsync("s-bob", "sess-1", "q-1", "school-x");

        var result = await sut.GetVisibleCountAsync("sess-1", "q-1");

        Assert.Null(result.Count);
        Assert.True(result.BelowAnonymityFloor);
        Assert.Equal(3, result.AnonymityFloor);
    }

    [Fact]
    public async Task GetVisibleCount_AtFloor_ReturnsCount()
    {
        var sut = CreateSut(floor: 3);
        await sut.EmitAsync("s-alice", "sess-1", "q-1", "school-x");
        await sut.EmitAsync("s-bob", "sess-1", "q-1", "school-x");
        await sut.EmitAsync("s-carol", "sess-1", "q-1", "school-x");

        var result = await sut.GetVisibleCountAsync("sess-1", "q-1");

        Assert.Equal(3, result.Count);
        Assert.False(result.BelowAnonymityFloor);
    }

    [Fact]
    public async Task GetVisibleCount_NoDoc_ReturnsBelowFloor()
    {
        var sut = CreateSut(floor: 3);
        var result = await sut.GetVisibleCountAsync("sess-never", "q-never");
        Assert.Null(result.Count);
        Assert.True(result.BelowAnonymityFloor);
    }

    [Fact]
    public void Constructor_RejectsFloorBelowTwo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PeerConfusedSignalService(_store, NullLogger<PeerConfusedSignalService>.Instance, anonymityFloor: 1));
    }

    // ── Hash properties ────────────────────────────────────────────────────

    [Fact]
    public void HashEmitter_DeterministicForSameInputs()
    {
        var a = PeerConfusedSignalService.HashEmitter("stu-1", "sess-1", "q-1");
        var b = PeerConfusedSignalService.HashEmitter("stu-1", "sess-1", "q-1");
        Assert.Equal(a, b);
    }

    [Fact]
    public void HashEmitter_DifferentForDifferentStudents()
    {
        var a = PeerConfusedSignalService.HashEmitter("stu-1", "sess-1", "q-1");
        var b = PeerConfusedSignalService.HashEmitter("stu-2", "sess-1", "q-1");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void HashEmitter_DifferentForDifferentSessions()
    {
        var a = PeerConfusedSignalService.HashEmitter("stu-1", "sess-1", "q-1");
        var b = PeerConfusedSignalService.HashEmitter("stu-1", "sess-2", "q-1");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void HashEmitter_DifferentForDifferentQuestions()
    {
        var a = PeerConfusedSignalService.HashEmitter("stu-1", "sess-1", "q-1");
        var b = PeerConfusedSignalService.HashEmitter("stu-1", "sess-1", "q-2");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void HashEmitter_ResistsSeparatorConfusion()
    {
        // Without a non-printable separator, ("ab", "c") and ("a", "bc")
        // would concatenate to the same string. We ensure they don't.
        var a = PeerConfusedSignalService.HashEmitter("ab", "c", "qq");
        var b = PeerConfusedSignalService.HashEmitter("a", "bc", "qq");
        Assert.NotEqual(a, b);
    }

    // ── Defensive contracts ────────────────────────────────────────────────

    [Fact]
    public async Task Emit_RejectsEmptyStudent()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.EmitAsync("", "sess-1", "q-1", "school-x"));
    }

    [Fact]
    public async Task Emit_RejectsEmptySession()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.EmitAsync("s", "", "q-1", "school-x"));
    }

    [Fact]
    public async Task Emit_RejectsEmptyQuestion()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.EmitAsync("s", "sess-1", "", "school-x"));
    }

    [Fact]
    public async Task GetVisibleCount_RejectsEmptySession()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.GetVisibleCountAsync("", "q-1"));
    }
}
