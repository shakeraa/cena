// =============================================================================
// Cena Platform — PhotoDiagnosticConfidenceTracker tests (EPIC-PRR-J PRR-420/421)
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class PhotoDiagnosticConfidenceTrackerTests
{
    private static DiagnosticObservation Obs(double ocr, double tpl, DateTimeOffset at) =>
        new(ocr, tpl, at);

    [Fact]
    public void EmptyWindowReturnsContinue()
    {
        var t = new PhotoDiagnosticConfidenceTracker();
        Assert.Equal(PhotoDiagnosticAdvice.Continue, t.GetAdvice("student-1"));
    }

    [Fact]
    public void UnknownStudentReturnsContinue()
    {
        var t = new PhotoDiagnosticConfidenceTracker();
        t.Record("student-1", Obs(0.9, 0.9, DateTimeOffset.UtcNow));
        Assert.Equal(PhotoDiagnosticAdvice.Continue, t.GetAdvice("student-other"));
    }

    [Fact]
    public void TwoConsecutivePoorTailReturnsSuggestRetake()
    {
        var t = new PhotoDiagnosticConfidenceTracker();
        var now = DateTimeOffset.UtcNow;
        t.Record("s", Obs(0.9, 0.9, now));
        t.Record("s", Obs(0.40, 0.9, now.AddSeconds(1)));
        t.Record("s", Obs(0.50, 0.9, now.AddSeconds(2)));
        Assert.Equal(PhotoDiagnosticAdvice.SuggestRetake, t.GetAdvice("s"));
    }

    [Fact]
    public void ThreeConsecutivePoorTailReturnsSuggestTypedInput()
    {
        var t = new PhotoDiagnosticConfidenceTracker();
        var now = DateTimeOffset.UtcNow;
        t.Record("s", Obs(0.40, 0.9, now));
        t.Record("s", Obs(0.30, 0.9, now.AddSeconds(1)));
        t.Record("s", Obs(0.20, 0.9, now.AddSeconds(2)));
        Assert.Equal(PhotoDiagnosticAdvice.SuggestTypedInput, t.GetAdvice("s"));
    }

    [Fact]
    public void AGoodSampleBreaksTheTail()
    {
        var t = new PhotoDiagnosticConfidenceTracker();
        var now = DateTimeOffset.UtcNow;
        t.Record("s", Obs(0.30, 0.9, now));
        t.Record("s", Obs(0.30, 0.9, now.AddSeconds(1)));
        t.Record("s", Obs(0.95, 0.9, now.AddSeconds(2))); // good sample breaks the streak
        Assert.Equal(PhotoDiagnosticAdvice.Continue, t.GetAdvice("s"));
    }

    [Fact]
    public void LowTemplateScoreCountsAsPoorEvenIfOcrIsHigh()
    {
        var t = new PhotoDiagnosticConfidenceTracker();
        var now = DateTimeOffset.UtcNow;
        t.Record("s", Obs(0.95, 0.30, now));
        t.Record("s", Obs(0.95, 0.20, now.AddSeconds(1)));
        t.Record("s", Obs(0.95, 0.10, now.AddSeconds(2)));
        Assert.Equal(PhotoDiagnosticAdvice.SuggestTypedInput, t.GetAdvice("s"));
    }

    [Fact]
    public void WindowEvictsOldestSampleBeyondSize()
    {
        var t = new PhotoDiagnosticConfidenceTracker();
        var now = DateTimeOffset.UtcNow;
        // Fill window with 3 poor samples, then pump 8 good samples through.
        // The 3 original poor samples must fall off.
        t.Record("s", Obs(0.2, 0.9, now));
        t.Record("s", Obs(0.2, 0.9, now.AddSeconds(1)));
        t.Record("s", Obs(0.2, 0.9, now.AddSeconds(2)));
        for (int i = 0; i < PhotoDiagnosticConfidenceTracker.WindowSize; i++)
            t.Record("s", Obs(0.95, 0.95, now.AddSeconds(3 + i)));
        Assert.Equal(PhotoDiagnosticAdvice.Continue, t.GetAdvice("s"));
    }

    [Fact]
    public void EvictStaleRemovesIdleWindows()
    {
        var t = new PhotoDiagnosticConfidenceTracker();
        var t0 = DateTimeOffset.UtcNow;
        t.Record("idle", Obs(0.9, 0.9, t0));
        t.Record("recent", Obs(0.9, 0.9, t0));
        // Idle is older than the TTL; recent has a fresh observation.
        t.Record("recent", Obs(0.9, 0.9, t0.Add(PhotoDiagnosticConfidenceTracker.SessionTtl + TimeSpan.FromMinutes(5))));
        var evicted = t.EvictStale(t0.Add(PhotoDiagnosticConfidenceTracker.SessionTtl + TimeSpan.FromMinutes(10)));
        Assert.Equal(1, evicted);
        Assert.Equal(PhotoDiagnosticAdvice.Continue, t.GetAdvice("idle"));
    }

    [Fact]
    public void RecordRejectsEmptyStudentId()
    {
        var t = new PhotoDiagnosticConfidenceTracker();
        Assert.Throws<ArgumentException>(() =>
            t.Record(" ", Obs(0.9, 0.9, DateTimeOffset.UtcNow)));
    }
}
