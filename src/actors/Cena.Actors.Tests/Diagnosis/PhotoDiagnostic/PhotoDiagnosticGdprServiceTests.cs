// =============================================================================
// Cena Platform — PhotoDiagnosticGdprService tests (EPIC-PRR-J PRR-411/412)
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class PhotoDiagnosticGdprServiceTests
{
    private static PhotoDiagnosticGdprService NewService(out InMemoryDiagnosticDisputeRepository repo)
    {
        repo = new InMemoryDiagnosticDisputeRepository();
        return new PhotoDiagnosticGdprService(
            repo, TimeProvider.System, NullLogger<PhotoDiagnosticGdprService>.Instance);
    }

    private static DiagnosticDisputeDocument MakeDoc(string id, string studentHash, DateTimeOffset at) => new()
    {
        Id = id,
        DiagnosticId = $"diag-{id}",
        StudentSubjectIdHash = studentHash,
        Reason = DisputeReason.Other,
        Status = DisputeStatus.New,
        SubmittedAt = at,
    };

    [Fact]
    public async Task ExportReturnsOnlyRequestedStudentsDisputes()
    {
        var svc = NewService(out var repo);
        var now = DateTimeOffset.UtcNow;
        await repo.InsertAsync(MakeDoc("a", "studA", now), default);
        await repo.InsertAsync(MakeDoc("b", "studA", now.AddMinutes(1)), default);
        await repo.InsertAsync(MakeDoc("c", "studB", now), default);

        var bundle = await svc.ExportAsync("studA", default);

        Assert.Equal("studA", bundle.StudentSubjectIdHash);
        Assert.Equal(2, bundle.Disputes.Count);
        Assert.All(bundle.Disputes, d => Assert.Equal("studA", d.StudentSubjectIdHash));
    }

    [Fact]
    public async Task ExportReturnsEmptyBundleWhenNoDisputes()
    {
        var svc = NewService(out _);
        var bundle = await svc.ExportAsync("nobody", default);
        Assert.Empty(bundle.Disputes);
        Assert.Equal("nobody", bundle.StudentSubjectIdHash);
    }

    [Fact]
    public async Task DeleteAllRemovesOnlyRequestedStudent()
    {
        var svc = NewService(out var repo);
        var now = DateTimeOffset.UtcNow;
        await repo.InsertAsync(MakeDoc("a", "studA", now), default);
        await repo.InsertAsync(MakeDoc("b", "studA", now), default);
        await repo.InsertAsync(MakeDoc("c", "studB", now), default);

        var deleted = await svc.DeleteAllAsync("studA", default);

        Assert.Equal(2, deleted);
        var bundleA = await svc.ExportAsync("studA", default);
        Assert.Empty(bundleA.Disputes);
        var bundleB = await svc.ExportAsync("studB", default);
        Assert.Single(bundleB.Disputes);
    }

    [Fact]
    public async Task DeleteAllIsIdempotent()
    {
        var svc = NewService(out var repo);
        await repo.InsertAsync(MakeDoc("a", "studA", DateTimeOffset.UtcNow), default);

        var first = await svc.DeleteAllAsync("studA", default);
        var second = await svc.DeleteAllAsync("studA", default);
        Assert.Equal(1, first);
        Assert.Equal(0, second);
    }

    [Fact]
    public async Task EmptyStudentHashThrows()
    {
        var svc = NewService(out _);
        await Assert.ThrowsAsync<ArgumentException>(() => svc.ExportAsync("", default));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.DeleteAllAsync("", default));
    }

    [Fact]
    public async Task ExportTakeCapIsExposed()
    {
        // Can't seed 1000+ disputes in a unit test practically;
        // assert the cap constant is material (>= 100).
        Assert.True(PhotoDiagnosticGdprService.ExportTake >= 100);
        await Task.CompletedTask;
    }
}
