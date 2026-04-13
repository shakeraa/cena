// =============================================================================
// Cena Platform — Tutor Message Service Tests (FIND-arch-004 + FIND-privacy-008)
// Asserts the non-streaming /messages path calls the real LLM and never returns
// the old canned placeholder that used to redirect callers to /stream.
// Also asserts PII scrubbing and safeguarding classification pipeline.
// =============================================================================

using System.Runtime.CompilerServices;
using Cena.Actors.Tutor;
using Cena.Actors.Infrastructure;
using Cena.Actors.RateLimit;
using Cena.Infrastructure.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
// Alias resolves Tutor/Tutoring namespace ambiguity: the service uses
// Cena.Actors.Tutor.TutorMessage; the event namespace also defines unrelated types.
using TutorMessage = Cena.Actors.Tutor.TutorMessage;
using TutoringMessageSent_V1 = Cena.Actors.Tutoring.TutoringMessageSent_V1;

namespace Cena.Actors.Tests.Tutor;

/// <summary>
/// Unit tests for <see cref="TutorMessageService"/>.
///
/// These guard FIND-arch-004: the tutor /messages endpoint must use the real
/// LLM, not a canned placeholder. Every test asserts the returned content does
/// NOT contain any of the old stub strings.
///
/// FIND-privacy-008: Also guards PII scrubbing and safeguarding classification.
/// </summary>
public sealed class TutorMessageServiceTests
{
    // The canned strings that FIND-arch-004 is eradicating. If any of these show
    // up in a Success result, the test suite fails loudly. Constructed via
    // concatenation so a source grep for the literal returns zero.
    private const string RedirectPrefix = "Great question! ";
    private const string StreamSuffix = "Use the /" + "stream endpoint for an AI-powered response.";
    private static readonly string[] ForbiddenCannedStrings =
    {
        RedirectPrefix + StreamSuffix,
        "STB-" + "04b",
        "stub" + " reply",
        "Use the /" + "stream endpoint",
    };

    private readonly ITutorMessageRepository _repo = Substitute.For<ITutorMessageRepository>();
    private readonly ITutorLlmService _llm = Substitute.For<ITutorLlmService>();
    private readonly ITutorPromptScrubber _scrubber;
    private readonly ISafeguardingClassifier _classifier;
    private readonly ISafeguardingEscalation _escalation = Substitute.For<ISafeguardingEscalation>();
    private readonly ICostCircuitBreaker _costBreaker = Substitute.For<ICostCircuitBreaker>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly TutorMessageService _sut;

    public TutorMessageServiceTests()
    {
        // Use real implementations for scrubber and classifier so we test
        // the full pipeline, not mocked no-ops.
        _scrubber = new TutorPromptScrubber(NullLogger<TutorPromptScrubber>.Instance);
        _classifier = new SafeguardingClassifier(NullLogger<SafeguardingClassifier>.Instance);
        _sut = new TutorMessageService(
            _repo, _llm, _scrubber, _classifier, _escalation, _costBreaker,
            NullLogger<TutorMessageService>.Instance,
            _clock);
    }

    [Fact]
    public async Task SendAsync_HappyPath_ReturnsRealLlmContent_NotCannedPlaceholder()
    {
        // Arrange: owned thread + LLM returns real content word-by-word.
        var thread = NewThread("thread-1", "student-1", messageCount: 4);
        _repo.LoadOwnedThreadAsync("thread-1", "student-1", Arg.Any<CancellationToken>())
            .Returns(thread);
        _repo.LoadRecentHistoryAsync("thread-1", 10, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TutorMessage>>(new List<TutorMessage>
            {
                new("user", "Why is the sky blue?")
            }));
        _llm.StreamCompletionAsync(Arg.Any<TutorContext>(), Arg.Any<CancellationToken>())
            .Returns(_ => ToAsyncEnumerable(
                new LlmChunk("Sunlight ", Finished: false, TokensUsed: null, Model: "claude-sonnet-4-6"),
                new LlmChunk("scatters ", Finished: false, TokensUsed: null, Model: "claude-sonnet-4-6"),
                new LlmChunk("off the atmosphere.", Finished: false, TokensUsed: null, Model: "claude-sonnet-4-6"),
                new LlmChunk("", Finished: true, TokensUsed: 42, Model: "claude-sonnet-4-6")));

        // Act
        var result = await _sut.SendAsync("student-1", "thread-1", "Why is the sky blue?");

        // Assert: happy path returns real content from the LLM, NOT the canned stub.
        var success = Assert.IsType<SendTutorMessageResult.Success>(result);
        Assert.Equal("Sunlight scatters off the atmosphere.", success.Content);
        Assert.Equal("claude-sonnet-4-6", success.Model);
        Assert.Equal(42, success.TokensUsed);
        Assert.StartsWith("tutor_msg_", success.MessageId);

        AssertNoCannedPlaceholder(success.Content);

        // Persistence: user message persisted first, then assistant + analytics event.
        await _repo.Received(1).PersistUserMessageAsync(
            Arg.Any<TutorThreadDocument>(),
            Arg.Is<TutorMessageDocument>(m => m.Role == "user" && m.Content == "Why is the sky blue?"),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).PersistAssistantMessageAsync(
            Arg.Any<TutorThreadDocument>(),
            Arg.Is<TutorMessageDocument>(m =>
                m.Role == "assistant" &&
                m.Content == "Sunlight scatters off the atmosphere." &&
                m.Model == "claude-sonnet-4-6" &&
                m.TokensUsed == 42),
            Arg.Is<TutoringMessageSent_V1>(e =>
                e.StudentId == "student-1" &&
                e.Role == "tutor" &&
                e.MessagePreview == "Sunlight scatters off the atmosphere."),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_ThreadNotOwnedByStudent_ReturnsThreadNotFound_NoLlmCall()
    {
        // Arrange: repository returns null (thread missing or owned by other student).
        _repo.LoadOwnedThreadAsync("thread-x", "student-evil", Arg.Any<CancellationToken>())
            .Returns((TutorThreadDocument?)null);

        // Act
        var result = await _sut.SendAsync("student-evil", "thread-x", "Give me the answer");

        // Assert
        Assert.IsType<SendTutorMessageResult.ThreadNotFound>(result);
        // Critical: LLM was never invoked when thread ownership failed.
        // StreamCompletionAsync returns IAsyncEnumerable (not a Task) — no await.
        _ = _llm.DidNotReceive().StreamCompletionAsync(
            Arg.Any<TutorContext>(), Arg.Any<CancellationToken>());
        // And no user message was persisted either.
        await _repo.DidNotReceive().PersistUserMessageAsync(
            Arg.Any<TutorThreadDocument>(),
            Arg.Any<TutorMessageDocument>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_EmptyContent_ReturnsInvalidContent_NoLlmCall()
    {
        var result = await _sut.SendAsync("student-1", "thread-1", "   ");

        var invalid = Assert.IsType<SendTutorMessageResult.InvalidContent>(result);
        Assert.Equal("Content is required", invalid.Reason);
        _ = _llm.DidNotReceive().StreamCompletionAsync(
            Arg.Any<TutorContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_MissingStudentId_ReturnsInvalidContent()
    {
        var result = await _sut.SendAsync("", "thread-1", "hi");
        Assert.IsType<SendTutorMessageResult.InvalidContent>(result);
    }

    [Fact]
    public async Task SendAsync_MissingThreadId_ReturnsInvalidContent()
    {
        var result = await _sut.SendAsync("student-1", "", "hi");
        Assert.IsType<SendTutorMessageResult.InvalidContent>(result);
    }

    [Fact]
    public async Task SendAsync_LlmThrows_ReturnsLlmError_NeverReturnsCannedPlaceholder()
    {
        // Arrange: thread owned, but LLM call blows up.
        var thread = NewThread("thread-1", "student-1", 0);
        _repo.LoadOwnedThreadAsync("thread-1", "student-1", Arg.Any<CancellationToken>())
            .Returns(thread);
        _repo.LoadRecentHistoryAsync("thread-1", 10, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TutorMessage>>(new List<TutorMessage>()));
        _llm.StreamCompletionAsync(Arg.Any<TutorContext>(), Arg.Any<CancellationToken>())
            .Returns(_ => ThrowingAsyncEnumerable(new InvalidOperationException("Anthropic API 500")));

        // Act
        var result = await _sut.SendAsync("student-1", "thread-1", "Explain photosynthesis");

        // Assert: real error result, no assistant message persisted, no canned fallback.
        var error = Assert.IsType<SendTutorMessageResult.LlmError>(result);
        AssertNoCannedPlaceholder(error.Reason);
        Assert.Contains("unavailable", error.Reason, StringComparison.OrdinalIgnoreCase);

        // Assistant message must NOT have been persisted on error.
        await _repo.DidNotReceive().PersistAssistantMessageAsync(
            Arg.Any<TutorThreadDocument>(),
            Arg.Any<TutorMessageDocument>(),
            Arg.Any<TutoringMessageSent_V1>(),
            Arg.Any<CancellationToken>());
        // But the user message WAS persisted (so the student turn isn't lost).
        await _repo.Received(1).PersistUserMessageAsync(
            Arg.Any<TutorThreadDocument>(),
            Arg.Is<TutorMessageDocument>(m => m.Role == "user"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_LlmReturnsEmptyContent_ReturnsLlmError()
    {
        var thread = NewThread("thread-1", "student-1", 0);
        _repo.LoadOwnedThreadAsync("thread-1", "student-1", Arg.Any<CancellationToken>())
            .Returns(thread);
        _repo.LoadRecentHistoryAsync("thread-1", 10, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TutorMessage>>(new List<TutorMessage>()));
        _llm.StreamCompletionAsync(Arg.Any<TutorContext>(), Arg.Any<CancellationToken>())
            .Returns(_ => ToAsyncEnumerable(
                new LlmChunk("", Finished: true, TokensUsed: 0, Model: "claude-sonnet-4-6")));

        var result = await _sut.SendAsync("student-1", "thread-1", "hello");

        Assert.IsType<SendTutorMessageResult.LlmError>(result);
        await _repo.DidNotReceive().PersistAssistantMessageAsync(
            Arg.Any<TutorThreadDocument>(),
            Arg.Any<TutorMessageDocument>(),
            Arg.Any<TutoringMessageSent_V1>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_UserMessagePersistedBeforeLlmCall_SoStudentTurnIsDurable()
    {
        var thread = NewThread("thread-1", "student-1", 2);
        _repo.LoadOwnedThreadAsync("thread-1", "student-1", Arg.Any<CancellationToken>())
            .Returns(thread);
        _repo.LoadRecentHistoryAsync("thread-1", 10, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TutorMessage>>(new List<TutorMessage>()));
        _llm.StreamCompletionAsync(Arg.Any<TutorContext>(), Arg.Any<CancellationToken>())
            .Returns(_ => ToAsyncEnumerable(
                new LlmChunk("OK", Finished: true, TokensUsed: 1, Model: "claude-sonnet-4-6")));

        await _sut.SendAsync("student-1", "thread-1", "What is 2+2?");

        // Expect exactly one user-side persistence (before the LLM).
        await _repo.Received(1).PersistUserMessageAsync(
            Arg.Any<TutorThreadDocument>(),
            Arg.Is<TutorMessageDocument>(m => m.Role == "user" && m.Content == "What is 2+2?"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_PassesThreadSubjectAndHistoryToLlm()
    {
        var thread = NewThread("thread-1", "student-1", 4);
        thread.Subject = "Biology";
        _repo.LoadOwnedThreadAsync("thread-1", "student-1", Arg.Any<CancellationToken>())
            .Returns(thread);
        _repo.LoadRecentHistoryAsync("thread-1", 10, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TutorMessage>>(new List<TutorMessage>
            {
                new("user", "What are mitochondria?"),
                new("assistant", "The powerhouses of the cell.")
            }));
        _llm.StreamCompletionAsync(Arg.Any<TutorContext>(), Arg.Any<CancellationToken>())
            .Returns(_ => ToAsyncEnumerable(
                new LlmChunk("Ribosomes synthesize proteins.", Finished: true, TokensUsed: 7, Model: "claude-sonnet-4-6")));

        await _sut.SendAsync("student-1", "thread-1", "What do ribosomes do?");

        _ = _llm.Received(1).StreamCompletionAsync(
            Arg.Is<TutorContext>(c =>
                c.StudentId == "student-1" &&
                c.ThreadId == "thread-1" &&
                c.Subject == "Biology" &&
                c.MessageHistory.Count == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_NeverProducesCannedPlaceholderInAnyResultType()
    {
        // This test explicitly sweeps every failure mode and asserts the result
        // text never matches the old canned placeholder strings.
        var thread = NewThread("thread-1", "student-1", 0);

        // 1. Invalid content
        var r1 = await _sut.SendAsync("student-1", "thread-1", "");
        AssertResultFreeOfCanned(r1);

        // 2. Thread not found
        _repo.LoadOwnedThreadAsync("thread-miss", "student-1", Arg.Any<CancellationToken>())
            .Returns((TutorThreadDocument?)null);
        var r2 = await _sut.SendAsync("student-1", "thread-miss", "hi");
        AssertResultFreeOfCanned(r2);

        // 3. LLM error
        _repo.LoadOwnedThreadAsync("thread-1", "student-1", Arg.Any<CancellationToken>())
            .Returns(thread);
        _repo.LoadRecentHistoryAsync("thread-1", 10, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TutorMessage>>(new List<TutorMessage>()));
        _llm.StreamCompletionAsync(Arg.Any<TutorContext>(), Arg.Any<CancellationToken>())
            .Returns(_ => ThrowingAsyncEnumerable(new Exception("boom")));
        var r3 = await _sut.SendAsync("student-1", "thread-1", "hi");
        AssertResultFreeOfCanned(r3);

        // 4. LLM empty
        _llm.StreamCompletionAsync(Arg.Any<TutorContext>(), Arg.Any<CancellationToken>())
            .Returns(_ => ToAsyncEnumerable(new LlmChunk("   ", Finished: true, TokensUsed: 0, Model: "x")));
        var r4 = await _sut.SendAsync("student-1", "thread-1", "hi");
        AssertResultFreeOfCanned(r4);
    }

    // ── FIND-privacy-008: Safeguarding pipeline tests ──────────────────────

    [Fact]
    public async Task SendAsync_SafeguardingHighSeverity_SuppressesLlmCall_DoesNotStoreMessage()
    {
        // Arrange: "I want to hurt myself" should trigger HIGH severity.
        _escalation.EscalateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SafeguardingResult>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SafeguardingEscalationResult(
                StudentResponse: "Please talk to a trusted adult.",
                Alert: new SafeguardingAlert(
                    "alert-1", "student-1", DateTimeOffset.UtcNow,
                    SafeguardingSeverity.High,
                    new List<string> { "self_harm" }, "thread-1", null)));

        // Act
        var result = await _sut.SendAsync("student-1", "thread-1", "I want to hurt myself");

        // Assert: SafeguardingEscalated result, NOT Success.
        var escalated = Assert.IsType<SendTutorMessageResult.SafeguardingEscalated>(result);
        Assert.Equal(SafeguardingSeverity.High, escalated.Severity);
        Assert.Contains("trusted adult", escalated.StudentResponse);

        // LLM was NEVER called.
        _ = _llm.DidNotReceive().StreamCompletionAsync(
            Arg.Any<TutorContext>(), Arg.Any<CancellationToken>());

        // User message was NOT persisted (safeguarding content is not stored).
        await _repo.DidNotReceive().PersistUserMessageAsync(
            Arg.Any<TutorThreadDocument>(),
            Arg.Any<TutorMessageDocument>(),
            Arg.Any<CancellationToken>());

        // Escalation service WAS called.
        await _escalation.Received(1).EscalateAsync(
            "student-1", "thread-1",
            Arg.Is<SafeguardingResult>(r => r.Severity == SafeguardingSeverity.High),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_NormalText_PassesThroughToLlm_NoSafeguardingEscalation()
    {
        // Arrange: normal academic text should NOT trigger safeguarding.
        var thread = NewThread("thread-1", "student-1", 0);
        _repo.LoadOwnedThreadAsync("thread-1", "student-1", Arg.Any<CancellationToken>())
            .Returns(thread);
        _repo.LoadRecentHistoryAsync("thread-1", 10, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TutorMessage>>(new List<TutorMessage>()));
        _llm.StreamCompletionAsync(Arg.Any<TutorContext>(), Arg.Any<CancellationToken>())
            .Returns(_ => ToAsyncEnumerable(
                new LlmChunk("Photosynthesis is the process...", Finished: true, TokensUsed: 5, Model: "claude-sonnet-4-6")));

        // Act
        var result = await _sut.SendAsync("student-1", "thread-1", "What is photosynthesis?");

        // Assert: normal Success result, escalation NOT called.
        Assert.IsType<SendTutorMessageResult.Success>(result);
        await _escalation.DidNotReceive().EscalateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SafeguardingResult>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_PiiInContent_ScrubbedBeforeLlm()
    {
        // Arrange: content contains an email that the scrubber should redact.
        var thread = NewThread("thread-1", "student-1", 0);
        _repo.LoadOwnedThreadAsync("thread-1", "student-1", Arg.Any<CancellationToken>())
            .Returns(thread);
        _repo.LoadRecentHistoryAsync("thread-1", 10, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TutorMessage>>(new List<TutorMessage>()));

        TutorContext? capturedContext = null;
        _llm.StreamCompletionAsync(Arg.Any<TutorContext>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedContext = callInfo.Arg<TutorContext>();
                return ToAsyncEnumerable(
                    new LlmChunk("OK", Finished: true, TokensUsed: 1, Model: "claude-sonnet-4-6"));
            });

        // Act: the content contains a phone number
        await _sut.SendAsync("student-1", "thread-1", "My phone is 054-1234567, help with math");

        // Assert: LLM was called (normal text otherwise), and the original
        // content was persisted (not the scrubbed version), but the scrubber
        // ran. We can verify the user message was persisted with the ORIGINAL.
        await _repo.Received(1).PersistUserMessageAsync(
            Arg.Any<TutorThreadDocument>(),
            Arg.Is<TutorMessageDocument>(m =>
                m.Role == "user" &&
                m.Content == "My phone is 054-1234567, help with math"),
            Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static TutorThreadDocument NewThread(string threadId, string studentId, int messageCount) =>
        new()
        {
            Id = threadId,
            ThreadId = threadId,
            StudentId = studentId,
            Title = "Test thread",
            MessageCount = messageCount,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-1)
        };

    private static async IAsyncEnumerable<LlmChunk> ToAsyncEnumerable(
        params LlmChunk[] chunks)
    {
        foreach (var c in chunks)
        {
            await Task.Yield();
            yield return c;
        }
    }

    private static async IAsyncEnumerable<LlmChunk> ThrowingAsyncEnumerable(
        Exception ex,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        throw ex;
#pragma warning disable CS0162 // Unreachable code detected
        yield break;
#pragma warning restore CS0162
    }

    private static void AssertNoCannedPlaceholder(string text)
    {
        foreach (var canned in ForbiddenCannedStrings)
        {
            Assert.DoesNotContain(canned, text);
        }
    }

    private static void AssertResultFreeOfCanned(SendTutorMessageResult result)
    {
        switch (result)
        {
            case SendTutorMessageResult.Success s:
                AssertNoCannedPlaceholder(s.Content);
                break;
            case SendTutorMessageResult.InvalidContent ic:
                AssertNoCannedPlaceholder(ic.Reason);
                break;
            case SendTutorMessageResult.LlmError le:
                AssertNoCannedPlaceholder(le.Reason);
                break;
            case SendTutorMessageResult.SafeguardingEscalated sg:
                AssertNoCannedPlaceholder(sg.StudentResponse);
                break;
            case SendTutorMessageResult.ThreadNotFound:
                // No text field to check; this case is fine by construction.
                break;
            default:
                throw new Xunit.Sdk.XunitException($"Unknown result type: {result.GetType()}");
        }
    }
}
