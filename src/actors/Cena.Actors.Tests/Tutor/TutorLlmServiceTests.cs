// =============================================================================
// Cena Platform — Tutor LLM Service Tests (HARDEN TutorEndpoints)
// Tests for ITutorLlmService, TutorContext, LlmChunk
// =============================================================================

using Cena.Actors.Tutor;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Actors.Tests.Tutor;

/// <summary>
/// Tests for the Tutor LLM service types.
/// </summary>
public sealed class TutorLlmServiceTests
{
    [Fact]
    public void TutorContext_HasAllRequiredFields()
    {
        var history = new List<TutorMessage>
        {
            new("user", "What is 2+2?"),
            new("assistant", "Let me help you with that.")
        };

        var context = new TutorContext(
            StudentId: "student-001",
            ThreadId: "thread-001",
            MessageHistory: history,
            Subject: "Mathematics",
            CurrentGrade: 5
        );

        Assert.Equal("student-001", context.StudentId);
        Assert.Equal("thread-001", context.ThreadId);
        Assert.Equal(2, context.MessageHistory.Count);
        Assert.Equal("Mathematics", context.Subject);
        Assert.Equal(5, context.CurrentGrade);
    }

    [Fact]
    public void TutorMessage_StoresRoleAndContent()
    {
        var msg = new TutorMessage("user", "Hello, I need help!");

        Assert.Equal("user", msg.Role);
        Assert.Equal("Hello, I need help!", msg.Content);
    }

    [Fact]
    public void LlmChunk_HasAllProperties()
    {
        var chunk = new LlmChunk(
            Delta: "Hello ",
            Finished: false,
            TokensUsed: 42,
            Model: "claude-3-sonnet-20240229"
        );

        Assert.Equal("Hello ", chunk.Delta);
        Assert.False(chunk.Finished);
        Assert.Equal(42, chunk.TokensUsed);
        Assert.Equal("claude-3-sonnet-20240229", chunk.Model);
    }

    [Fact]
    public void LlmChunk_FinishedChunk_HasNoDelta()
    {
        var chunk = new LlmChunk(
            Delta: "",
            Finished: true,
            TokensUsed: 150,
            Model: "claude-3-sonnet-20240229"
        );

        Assert.Empty(chunk.Delta);
        Assert.True(chunk.Finished);
        Assert.Equal(150, chunk.TokensUsed);
    }

    [Fact]
    public void NullTutorLlmService_ReturnsErrorMessage()
    {
        var logger = NullLogger<NullTutorLlmService>.Instance;
        var service = new NullTutorLlmService(logger);

        var context = new TutorContext(
            StudentId: "student-001",
            ThreadId: "thread-001",
            MessageHistory: new List<TutorMessage>(),
            Subject: null,
            CurrentGrade: null
        );

        // Collect all chunks
        var chunks = service.StreamCompletionAsync(context).ToBlockingEnumerable().ToList();

        // Should return exactly one chunk with error message
        Assert.Single(chunks);
        Assert.Contains("not configured", chunks[0].Delta);
        Assert.True(chunks[0].Finished);
        Assert.Equal(0, chunks[0].TokensUsed);
        Assert.Equal("unconfigured", chunks[0].Model);
    }

    [Fact]
    public void TutorContext_CanHaveNullSubjectAndGrade()
    {
        var context = new TutorContext(
            StudentId: "student-001",
            ThreadId: "thread-001",
            MessageHistory: new List<TutorMessage>(),
            Subject: null,
            CurrentGrade: null
        );

        Assert.Null(context.Subject);
        Assert.Null(context.CurrentGrade);
    }

    [Fact]
    public void TutorContext_SupportsMultipleMessages()
    {
        var history = new List<TutorMessage>();
        for (int i = 0; i < 10; i++)
        {
            history.Add(new TutorMessage("user", $"Question {i}"));
            history.Add(new TutorMessage("assistant", $"Answer {i}"));
        }

        var context = new TutorContext(
            StudentId: "student-001",
            ThreadId: "thread-001",
            MessageHistory: history,
            Subject: "Science",
            CurrentGrade: 8
        );

        Assert.Equal(20, context.MessageHistory.Count);
    }

    [Fact]
    public void LlmChunk_SupportsNullTokens()
    {
        var chunk = new LlmChunk(
            Delta: "Some text",
            Finished: false,
            TokensUsed: null,
            Model: "claude-3-sonnet-20240229"
        );

        Assert.Null(chunk.TokensUsed);
    }
}
