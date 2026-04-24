using Cena.Actors.Messaging;

namespace Cena.Actors.Tests.Messaging;

public sealed class MessageClassifierTests
{
    private readonly MessageClassifier _classifier = new();

    // ── Learning Signals: Numeric Answers ──

    [Theory]
    [InlineData("42")]
    [InlineData("3.14")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("  7  ")]
    public void NumericAnswers_AreLearningSignals(string text)
    {
        var result = _classifier.Classify(text, "en");
        Assert.True(result.IsLearningSignal);
        Assert.Equal("quiz-answer", result.Intent);
        Assert.True(result.Confidence >= 0.9);
    }

    // ── Learning Signals: Multiple Choice Letters ──

    [Theory]
    [InlineData("a")]
    [InlineData("B")]
    [InlineData("c")]
    [InlineData("D")]
    public void SingleLetterAnswers_AreLearningSignals(string text)
    {
        var result = _classifier.Classify(text, "en");
        Assert.True(result.IsLearningSignal);
        Assert.Equal("quiz-answer", result.Intent);
        Assert.True(result.Confidence >= 0.9);
    }

    // ── Learning Signals: Confirmations ──

    [Theory]
    [InlineData("yes", "en")]
    [InlineData("no", "en")]
    [InlineData("כן", "he")]
    [InlineData("לא", "he")]
    [InlineData("نعم", "ar")]
    [InlineData("لا", "ar")]
    [InlineData("yep", "en")]
    [InlineData("nope", "en")]
    public void Confirmations_AreLearningSignals(string text, string locale)
    {
        var result = _classifier.Classify(text, locale);
        Assert.True(result.IsLearningSignal);
        Assert.Equal("confirmation", result.Intent);
        Assert.True(result.Confidence >= 0.8);
    }

    // ── Learning Signals: Concept Questions ──

    [Theory]
    [InlineData("what is a fraction?", "en")]
    [InlineData("how do I solve this?", "en")]
    [InlineData("explain derivatives", "en")]
    [InlineData("מה זה שבר?", "he")]
    [InlineData("كيف أحل هذا؟", "ar")]
    [InlineData("اشرح المشتقات", "ar")]
    public void ConceptQuestions_AreLearningSignals(string text, string locale)
    {
        var result = _classifier.Classify(text, locale);
        Assert.True(result.IsLearningSignal);
        Assert.Equal("concept-question", result.Intent);
    }

    // ── Communication: Social Messages ──

    [Theory]
    [InlineData("Good morning teacher!")]
    [InlineData("Thanks for the help!")]
    [InlineData("כל הכבוד!")]
    [InlineData("أحسنت!")]
    [InlineData("I'll study tonight, promise")]
    [InlineData("How is your day going?")]
    public void SocialMessages_AreCommunication(string text)
    {
        var result = _classifier.Classify(text, "en");
        Assert.False(result.IsLearningSignal);
    }

    // ── Communication: Resource Links ──

    [Fact]
    public void UrlsDetected_AreResourceSharing()
    {
        var result = _classifier.Classify(
            "Check this: https://www.youtube.com/watch?v=abc123", "en");
        Assert.False(result.IsLearningSignal);
        Assert.Equal("resource-share", result.Intent);
    }

    // ── Communication: Greetings ──

    [Theory]
    [InlineData("hello everyone")]
    [InlineData("שלום לכולם")]
    [InlineData("مرحبا")]
    public void Greetings_AreCommunication(string text)
    {
        var result = _classifier.Classify(text, "en");
        Assert.False(result.IsLearningSignal);
        Assert.Equal("greeting", result.Intent);
    }

    // ── Edge Cases ──

    [Fact]
    public void EmptyString_DefaultsToCommunication()
    {
        var result = _classifier.Classify("", "en");
        Assert.False(result.IsLearningSignal);
        Assert.Equal("general", result.Intent);
        Assert.Equal(1.0, result.Confidence);
    }

    [Fact]
    public void Whitespace_DefaultsToCommunication()
    {
        var result = _classifier.Classify("   ", "en");
        Assert.False(result.IsLearningSignal);
        Assert.Equal("general", result.Intent);
    }

    [Fact]
    public void MixedContent_NotPureNumeric_DefaultsToCommunication()
    {
        var result = _classifier.Classify("42 thanks for the help!", "en");
        Assert.False(result.IsLearningSignal);
    }

    [Fact]
    public void LongText_IsCommunication()
    {
        var result = _classifier.Classify(
            "I really enjoyed the lesson today and learned a lot about fractions", "en");
        Assert.False(result.IsLearningSignal);
        Assert.Equal("general", result.Intent);
    }

    [Theory]
    [InlineData("e")]  // Not a-d
    [InlineData("f")]
    [InlineData("z")]
    public void LettersOutsideRange_AreCommunication(string text)
    {
        var result = _classifier.Classify(text, "en");
        Assert.False(result.IsLearningSignal);
    }
}
