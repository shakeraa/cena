// =============================================================================
// Cena Platform — Claude classifier response parsing tests (RDY-063)
//
// Covers the safety boundary: no matter what the LLM returns, the
// parser must never throw and must refuse any output that contains
// math leak signatures.
// =============================================================================

using Cena.Actors.Diagnosis;

namespace Cena.Actors.Tests.Diagnosis;

public class LlmResponseParsingTests
{
    [Fact]
    public void WellFormedJson_Parsed()
    {
        var raw = """
            {"primary":"Procedural","primaryConfidence":0.8,"secondary":"Recall","secondaryConfidence":0.3,"reasonCode":"stuck_on_final_step"}
            """;
        var r = ClaudeStuckClassifierLlm.ParseResponse(raw);
        Assert.True(r.Success);
        Assert.Equal(StuckType.Procedural, r.Primary);
        Assert.Equal(StuckType.Recall, r.Secondary);
        Assert.Equal(0.8f, r.PrimaryConfidence, precision: 3);
        Assert.Equal("stuck_on_final_step", r.RawReason);
    }

    [Fact]
    public void JsonWithPreamble_ExtractsFirstObject()
    {
        var raw = "Here's my answer:\n" +
                  """
                  {"primary":"Encoding","primaryConfidence":0.7,"secondary":"Unknown","secondaryConfidence":0,"reasonCode":"unfamiliar_notation"}
                  """ + "\n\nThat's it.";
        var r = ClaudeStuckClassifierLlm.ParseResponse(raw);
        Assert.True(r.Success);
        Assert.Equal(StuckType.Encoding, r.Primary);
    }

    [Fact]
    public void MalformedJson_ReturnsInvalid()
    {
        var raw = "{not-json}";
        var r = ClaudeStuckClassifierLlm.ParseResponse(raw);
        Assert.False(r.Success);
        Assert.NotNull(r.ErrorCode);
    }

    [Fact]
    public void EmptyResponse_ReturnsInvalid()
    {
        var r = ClaudeStuckClassifierLlm.ParseResponse("");
        Assert.False(r.Success);
        Assert.Equal("empty_response", r.ErrorCode);
    }

    [Fact]
    public void NoJsonObject_ReturnsInvalid()
    {
        var r = ClaudeStuckClassifierLlm.ParseResponse("just prose, no json");
        Assert.False(r.Success);
        Assert.Equal("no_json_object", r.ErrorCode);
    }

    [Fact]
    public void ContentLeak_MathInBody_Rejected()
    {
        // LLM went off-spec and tried to include a math claim in the JSON.
        var raw = """
            {"primary":"Procedural","primaryConfidence":0.8,"secondary":"Unknown","secondaryConfidence":0,"reasonCode":"x = 5"}
            """;
        var r = ClaudeStuckClassifierLlm.ParseResponse(raw);
        Assert.False(r.Success);
        Assert.Equal("content_leak", r.ErrorCode);
    }

    [Fact]
    public void ContentLeak_LatexInBody_Rejected()
    {
        var raw = """
            {"primary":"Recall","primaryConfidence":0.6,"secondary":"Unknown","secondaryConfidence":0,"reasonCode":"\\frac{a}{b}"}
            """;
        var r = ClaudeStuckClassifierLlm.ParseResponse(raw);
        Assert.False(r.Success);
        Assert.Equal("content_leak", r.ErrorCode);
    }

    [Fact]
    public void UnknownEnumValue_CoercedToUnknown()
    {
        // Defensive: an LLM that emits an out-of-spec label doesn't crash.
        var raw = """
            {"primary":"Hallucinated","primaryConfidence":0.5,"secondary":"Unknown","secondaryConfidence":0,"reasonCode":"na"}
            """;
        var r = ClaudeStuckClassifierLlm.ParseResponse(raw);
        Assert.True(r.Success);
        Assert.Equal(StuckType.Unknown, r.Primary);
    }

    [Fact]
    public void ConfidenceOutOfRange_Clamped()
    {
        var raw = """
            {"primary":"Procedural","primaryConfidence":1.7,"secondary":"Unknown","secondaryConfidence":-0.2,"reasonCode":"na"}
            """;
        var r = ClaudeStuckClassifierLlm.ParseResponse(raw);
        Assert.True(r.Success);
        Assert.Equal(1.0f, r.PrimaryConfidence);
        Assert.Equal(0.0f, r.SecondaryConfidence);
    }
}
