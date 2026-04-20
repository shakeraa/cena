// =============================================================================
// Cena Platform — PiiPromptScrubber tests (ADR-0046, prr-022)
//
// Guards:
//   - Empty / whitespace input → zero redactions, same string back, no metric.
//   - Synthetic PII (full name + phone + email + Israeli ID + address +
//     postal code) → every category redacted, counter increments with the
//     {feature, category} label.
//   - Multiple hits in the same prompt → counter increments by the actual
//     count, not by 1.
//   - Fail-closed contract (caller policy — the test verifies the signal the
//     caller needs: RedactionCount > 0).
//   - Math-word-problem false-positive baseline — "1,234,567 apples" must
//     NOT be mistaken for a phone number.
//   - [PiiPreScrubbedAttribute] construction validates non-empty reason.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Infrastructure.Tests.Llm;

public sealed class PiiPromptScrubberTests
{
    private sealed class CapturingMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public Meter Create(string name, string? version = null) =>
            new(new MeterOptions(name) { Version = version });
        public void Dispose() { }
    }

    private static (PiiPromptScrubber scrubber, List<(long value, Dictionary<string, object?> tags)> emitted, MeterListener listener)
        BuildScrubber()
    {
        var factory = new CapturingMeterFactory();
        var scrubber = new PiiPromptScrubber(factory, NullLogger<PiiPromptScrubber>.Instance);
        var emitted = new List<(long, Dictionary<string, object?>)>();

        var listener = new MeterListener
        {
            InstrumentPublished = (inst, lst) =>
            {
                if (inst.Name == PiiPromptScrubber.CounterName)
                    lst.EnableMeasurementEvents(inst);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var dict = new Dictionary<string, object?>();
            foreach (var t in tags) dict[t.Key] = t.Value;
            emitted.Add((value, dict));
        });
        listener.Start();

        return (scrubber, emitted, listener);
    }

    [Fact]
    public void Scrub_NullOrEmpty_IsNoOp_AndEmitsNothing()
    {
        var (scrubber, emitted, listener) = BuildScrubber();
        using var _ = listener;

        var r1 = scrubber.Scrub("", "socratic");
        Assert.Equal(0, r1.RedactionCount);
        Assert.Empty(r1.Categories);

        var r2 = scrubber.Scrub("   \t\n", "socratic");
        Assert.Equal(0, r2.RedactionCount);

        Assert.Empty(emitted);
    }

    [Fact]
    public void Scrub_CleanMathPrompt_IsNoOp()
    {
        var (scrubber, emitted, listener) = BuildScrubber();
        using var _ = listener;

        const string prompt =
            "A farmer has 1,234,567 apples. He sells 7/9 of them. " +
            "How many does he have left? (Answer as a fraction.)";

        var result = scrubber.Scrub(prompt, "classification");

        Assert.Equal(0, result.RedactionCount);
        Assert.Equal(prompt, result.ScrubbedText);
        Assert.Empty(emitted);
    }

    [Fact]
    public void Scrub_SyntheticPii_RedactsAllCategories_AndIncrementsCounter()
    {
        var (scrubber, emitted, listener) = BuildScrubber();
        using var _ = listener;

        // Full name + email + phone + Israeli ID + address + postal code.
        // The full-name pattern is NOT a regex target (names are governed by
        // the structured-placeholder rule, not the scrubber) — the scrubber
        // is responsible for the free-text PII categories.
        const string prompt =
            "Student emailed yael.cohen@example.com from +972-54-1234567 " +
            "(ID 123456789) at 12 Main Street, postal 12345-6789. " +
            "Please confirm the homework answer is correct.";

        var result = scrubber.Scrub(prompt, "socratic");

        Assert.True(result.RedactionCount >= 5,
            $"Expected ≥5 redactions (email, phone, id, address, postal); got {result.RedactionCount}. Text: {result.ScrubbedText}");
        Assert.Contains("email", result.Categories);
        Assert.Contains("phone", result.Categories);
        Assert.Contains("government_id", result.Categories);
        Assert.Contains("address", result.Categories);
        Assert.Contains("postal_code", result.Categories);

        Assert.DoesNotContain("yael.cohen@example.com", result.ScrubbedText);
        Assert.DoesNotContain("+972-54-1234567", result.ScrubbedText);
        Assert.DoesNotContain("123456789", result.ScrubbedText);
        Assert.DoesNotContain("12345-6789", result.ScrubbedText);

        // Every emitted measurement carries the {feature, category} labels.
        Assert.NotEmpty(emitted);
        foreach (var (_, tags) in emitted)
        {
            Assert.Equal("socratic", tags["feature"]);
            Assert.True(tags.ContainsKey("category"));
        }

        // Counter total equals RedactionCount (paranoia — ensures we emit the
        // actual count, not just a fixed `1` per category).
        var emittedSum = emitted.Sum(e => e.value);
        Assert.Equal(result.RedactionCount, emittedSum);
    }

    [Fact]
    public void Scrub_MultipleHitsInSameCategory_IncrementsCounterByActualCount()
    {
        var (scrubber, emitted, listener) = BuildScrubber();
        using var _ = listener;

        const string prompt =
            "Email me at a@b.com or b@c.com or c@d.com for more info.";

        var result = scrubber.Scrub(prompt, "explanation-l3");

        Assert.Equal(3, result.RedactionCount);
        Assert.Equal(3, emitted.Sum(e => e.value));
    }

    [Fact]
    public void Scrub_IntegrationFlow_SyntheticPromptReachingAnthropicClient_IsSanitised()
    {
        // Integration-style contract test: mimics the hot path where a
        // TutorContext-assembled prompt passes through the scrubber before
        // being handed to an AnthropicClient call.
        //
        // This test deliberately does NOT instantiate an AnthropicClient —
        // network IO is out of scope. What it DOES verify is that the string
        // the client would receive (i.e. the scrubber's output) contains no
        // synthetic PII.

        var (scrubber, _, listener) = BuildScrubber();
        using var _ = listener;

        const string rawPrompt =
            "System: you are a tutor.\n" +
            "User: My name is Yael Cohen (yael.cohen@school.il, 054-1234567). " +
            "The answer to 2x+3=9 is x=3. Is that right?";

        var result = scrubber.Scrub(rawPrompt, "socratic");
        var promptToAnthropic = result.ScrubbedText;

        // The caller's fail-closed policy would drop this call entirely —
        // but the scrubbed text must at minimum contain no synthetic PII in
        // case a defect causes the call to proceed anyway.
        Assert.DoesNotContain("yael.cohen@school.il", promptToAnthropic);
        Assert.DoesNotContain("054-1234567", promptToAnthropic);
        Assert.Contains("2x+3=9", promptToAnthropic); // math content preserved
        Assert.True(result.RedactionCount > 0, "Scrubber must signal redactions to caller so it can fail closed.");
    }

    [Fact]
    public void Scrub_ThrowsOnEmptyFeature()
    {
        var (scrubber, _, listener) = BuildScrubber();
        using var _ = listener;

        Assert.Throws<ArgumentException>(() => scrubber.Scrub("a@b.com", ""));
        Assert.Throws<ArgumentException>(() => scrubber.Scrub("a@b.com", "   "));
    }

    [Fact]
    public void NullPiiPromptScrubber_IsNoOp()
    {
        // Smoke test: the test-only null substitute must not throw for any
        // input. Production hosts do not wire this — it's for unit tests of
        // callers that want to assert on behaviour without meter plumbing.
        var r = NullPiiPromptScrubber.Instance.Scrub("a@b.com", "socratic");
        Assert.Equal(0, r.RedactionCount);
        Assert.Equal("a@b.com", r.ScrubbedText);
    }

    [Fact]
    public void PiiPreScrubbedAttribute_RequiresReason()
    {
        Assert.Throws<ArgumentException>(() => new PiiPreScrubbedAttribute(""));
        Assert.Throws<ArgumentException>(() => new PiiPreScrubbedAttribute("   "));

        var attr = new PiiPreScrubbedAttribute("TutorPromptScrubber.Scrub() runs upstream in TutorMessageService");
        Assert.Contains("TutorMessageService", attr.Reason);
    }
}
