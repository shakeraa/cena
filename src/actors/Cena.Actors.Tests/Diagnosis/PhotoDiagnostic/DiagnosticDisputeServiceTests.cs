// =============================================================================
// Cena Platform — DiagnosticDisputeService tests (EPIC-PRR-J PRR-385/390)
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class DiagnosticDisputeServiceTests
{
    private static DiagnosticDisputeService NewService(
        out InMemoryDiagnosticDisputeRepository repo,
        TimeProvider? clock = null)
    {
        var metrics = new PhotoDiagnosticMetrics(new DummyMeterFactory());
        repo = new InMemoryDiagnosticDisputeRepository();
        return new DiagnosticDisputeService(
            repo,
            metrics,
            NullLogger<DiagnosticDisputeService>.Instance,
            clock ?? TimeProvider.System);
    }

    [Fact]
    public async Task SubmitReturnsNewDisputeWithStatusNew()
    {
        var svc = NewService(out _);
        var view = await svc.SubmitAsync(new SubmitDiagnosticDisputeCommand(
            DiagnosticId: "d-100",
            StudentSubjectIdHash: "hash",
            Reason: DisputeReason.WrongNarration,
            StudentComment: "You said sign-flip but I actually got the factoring wrong."), default);

        Assert.Equal(DisputeStatus.New, view.Status);
        Assert.Equal("d-100", view.DiagnosticId);
        Assert.Equal(DisputeReason.WrongNarration, view.Reason);
        Assert.NotNull(view.DisputeId);
    }

    [Fact]
    public async Task SubmitPersistsSoGetAsyncReturnsIt()
    {
        var svc = NewService(out var repo);
        var view = await svc.SubmitAsync(new SubmitDiagnosticDisputeCommand(
            "d-200", "hash", DisputeReason.Other, null), default);

        var fetched = await svc.GetAsync(view.DisputeId, default);
        Assert.NotNull(fetched);
        Assert.Equal(view.DisputeId, fetched!.DisputeId);
        Assert.NotNull(await repo.GetAsync(view.DisputeId, default));
    }

    [Fact]
    public async Task SubmitRejectsOverLongComment()
    {
        var svc = NewService(out _);
        var longComment = new string('x', DiagnosticDisputeService.MaxCommentLength + 1);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.SubmitAsync(new SubmitDiagnosticDisputeCommand(
                "d", "hash", DisputeReason.Other, longComment), default));
    }

    [Fact]
    public async Task SubmitRejectsEmptyIds()
    {
        var svc = NewService(out _);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.SubmitAsync(new SubmitDiagnosticDisputeCommand("", "hash", DisputeReason.Other, null), default));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.SubmitAsync(new SubmitDiagnosticDisputeCommand("d", "", DisputeReason.Other, null), default));
    }

    [Fact]
    public async Task ReviewAdvancesStatusAndTimestamps()
    {
        var svc = NewService(out _);
        var view = await svc.SubmitAsync(new SubmitDiagnosticDisputeCommand(
            "d-300", "hash", DisputeReason.OcrMisread, null), default);

        var reviewed = await svc.ReviewAsync(view.DisputeId, DisputeStatus.Upheld, "Template calibration opened.", default);

        Assert.Equal(DisputeStatus.Upheld, reviewed.Status);
        Assert.NotNull(reviewed.ReviewedAt);
        Assert.Equal("Template calibration opened.", reviewed.ReviewerNote);
    }

    [Fact]
    public async Task ReviewRejectsTransitionBackToNew()
    {
        var svc = NewService(out _);
        var view = await svc.SubmitAsync(new SubmitDiagnosticDisputeCommand(
            "d-400", "hash", DisputeReason.Other, null), default);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.ReviewAsync(view.DisputeId, DisputeStatus.New, null, default));
    }

    [Fact]
    public async Task ListByStudentReturnsMostRecentFirst()
    {
        var svc = NewService(out _);
        await svc.SubmitAsync(new SubmitDiagnosticDisputeCommand("d-a", "hashX", DisputeReason.WrongNarration, null), default);
        await Task.Delay(5);
        await svc.SubmitAsync(new SubmitDiagnosticDisputeCommand("d-b", "hashX", DisputeReason.WrongStepIdentified, null), default);
        await Task.Delay(5);
        var third = await svc.SubmitAsync(new SubmitDiagnosticDisputeCommand("d-c", "hashX", DisputeReason.OcrMisread, null), default);

        var list = await svc.ListByStudentAsync("hashX", 10, default);

        Assert.Equal(3, list.Count);
        Assert.Equal(third.DisputeId, list[0].DisputeId);
    }

    [Fact]
    public async Task ListRecentByStatusFiltersNewOnly()
    {
        var svc = NewService(out _);
        var a = await svc.SubmitAsync(new SubmitDiagnosticDisputeCommand("d-a", "hashY", DisputeReason.Other, null), default);
        var b = await svc.SubmitAsync(new SubmitDiagnosticDisputeCommand("d-b", "hashY", DisputeReason.Other, null), default);
        await svc.ReviewAsync(a.DisputeId, DisputeStatus.Rejected, null, default);

        var newList = await svc.ListRecentAsync(DisputeStatus.New, 10, default);
        Assert.Single(newList);
        Assert.Equal(b.DisputeId, newList[0].DisputeId);
    }

    private sealed class DummyMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
