// =============================================================================
// Cena Platform -- Reindex Coordinator Service Tests
// FIND-ARCH-020: Tests for ReindexJobDocument and reindex coordination
// =============================================================================

using Cena.Actors.Services;
using Xunit;

namespace Cena.Actors.Tests;

public class ReindexJobDocumentTests
{
    [Fact]
    public void ReindexJobDocument_DefaultValues_AreCorrect()
    {
        var job = new ReindexJobDocument();

        Assert.Equal(ReindexJobStatus.Pending, job.Status);
        Assert.Equal(0, job.ProcessedBlocks);
        Assert.Equal(0, job.FailedBlocks);
        Assert.Equal(0, job.EstimatedBlocks);
        Assert.Equal(0, job.ProgressPercentage);
        Assert.False(job.IsComplete);
    }

    [Fact]
    public void ReindexJobDocument_ProgressPercentage_CalculatedCorrectly()
    {
        var job = new ReindexJobDocument
        {
            EstimatedBlocks = 100,
            ProcessedBlocks = 25
        };

        Assert.Equal(25.0, job.ProgressPercentage);
    }

    [Fact]
    public void ReindexJobDocument_ProgressPercentage_CappedAt100()
    {
        var job = new ReindexJobDocument
        {
            EstimatedBlocks = 100,
            ProcessedBlocks = 150
        };

        Assert.Equal(100.0, job.ProgressPercentage);
    }

    [Fact]
    public void ReindexJobDocument_ProgressPercentage_ZeroEstimatedBlocks()
    {
        var job = new ReindexJobDocument
        {
            EstimatedBlocks = 0,
            ProcessedBlocks = 50
        };

        Assert.Equal(0, job.ProgressPercentage);
    }

    [Theory]
    [InlineData(ReindexJobStatus.Pending, false)]
    [InlineData(ReindexJobStatus.Running, false)]
    [InlineData(ReindexJobStatus.Completed, true)]
    [InlineData(ReindexJobStatus.Failed, true)]
    [InlineData(ReindexJobStatus.Cancelled, true)]
    public void ReindexJobDocument_IsComplete_BasedOnStatus(ReindexJobStatus status, bool expected)
    {
        var job = new ReindexJobDocument { Status = status };

        Assert.Equal(expected, job.IsComplete);
    }

    [Fact]
    public void ReindexCommand_Properties_AreSetCorrectly()
    {
        var requestedAt = DateTimeOffset.UtcNow;
        var command = new ReindexCommand(
            JobId: "abc123",
            Scope: "subject",
            Filter: "math",
            EstimatedBlocks: 100,
            RequestedBy: "admin@example.com",
            RequestedAt: requestedAt);

        Assert.Equal("abc123", command.JobId);
        Assert.Equal("subject", command.Scope);
        Assert.Equal("math", command.Filter);
        Assert.Equal(100, command.EstimatedBlocks);
        Assert.Equal("admin@example.com", command.RequestedBy);
        Assert.Equal(requestedAt, command.RequestedAt);
    }

    [Fact]
    public void ReindexProgressEvent_Properties_AreSetCorrectly()
    {
        var evt = new ReindexProgressEvent(
            JobId: "abc123",
            ProcessedBlocks: 50,
            FailedBlocks: 2,
            TotalBlocks: 100,
            Status: ReindexJobStatus.Running,
            ErrorMessage: null);

        Assert.Equal("abc123", evt.JobId);
        Assert.Equal(50, evt.ProcessedBlocks);
        Assert.Equal(2, evt.FailedBlocks);
        Assert.Equal(100, evt.TotalBlocks);
        Assert.Equal(ReindexJobStatus.Running, evt.Status);
        Assert.Null(evt.ErrorMessage);
    }
}
