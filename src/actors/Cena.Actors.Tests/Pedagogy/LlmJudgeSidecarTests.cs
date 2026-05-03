// =============================================================================
// RDY-074 Phase 1B — LlmJudgeSidecar tests.
// =============================================================================

using System.Net;
using System.Net.Http;
using System.Text;
using Cena.Actors.Pedagogy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cena.Actors.Tests.Pedagogy;

public class LlmJudgeSidecarMappingTests
{
    [Theory]
    [InlineData("pass", ExplainJudgment.Pass)]
    [InlineData("PASS", ExplainJudgment.Pass)]
    [InlineData("partial", ExplainJudgment.PartialPass)]
    [InlineData("partial-pass", ExplainJudgment.PartialPass)]
    [InlineData("retry", ExplainJudgment.Retry)]
    [InlineData("unknown", ExplainJudgment.Unavailable)]
    [InlineData(null, ExplainJudgment.Unavailable)]
    [InlineData("", ExplainJudgment.Unavailable)]
    public void Parses_judgment_string(string? raw, ExplainJudgment expected)
    {
        Assert.Equal(expected, LlmJudgeSidecar.ParseJudgment(raw));
    }

    [Theory]
    [InlineData("StatesRuleCorrectly", ExplainRubricCategory.StatesRuleCorrectly)]
    [InlineData("StatesRuleButMisnames", ExplainRubricCategory.StatesRuleButMisnames)]
    [InlineData("RestatesProblemWithoutRule", ExplainRubricCategory.RestatesProblemWithoutRule)]
    [InlineData("GivesNumericAnswerOnly", ExplainRubricCategory.GivesNumericAnswerOnly)]
    [InlineData("OffTopic", ExplainRubricCategory.OffTopic)]
    [InlineData("Bogus", ExplainRubricCategory.Unknown)]
    public void Parses_category_string(string raw, ExplainRubricCategory expected)
    {
        Assert.Equal(expected, LlmJudgeSidecar.ParseCategory(raw));
    }

    [Theory]
    [InlineData(ExplainJudgment.Pass, CopyBucket.Celebrate)]
    [InlineData(ExplainJudgment.PartialPass, CopyBucket.NudgeMissing)]
    [InlineData(ExplainJudgment.Retry, CopyBucket.Redirect)]
    [InlineData(ExplainJudgment.Unavailable, CopyBucket.SilentSkip)]
    public void Chooses_copy_bucket_from_judgment(
        ExplainJudgment judgment, CopyBucket expected)
    {
        Assert.Equal(expected, LlmJudgeSidecar.ChooseBucket(judgment));
    }
}

public class LlmJudgeSidecarOptionsTests
{
    [Fact]
    public void IsComplete_requires_base_url()
    {
        Assert.False(new LlmJudgeSidecarOptions().IsComplete);
        Assert.True(new LlmJudgeSidecarOptions { BaseUrl = "http://localhost:7101" }.IsComplete);
    }
}

public class LlmJudgeSidecar_UnconfiguredTests
{
    private static ExplainRequest NewRequest() => new(
        StudentAnonId: "stu-anon-1",
        ConceptSlug: "derivatives",
        ExpectedRulePlainLanguage: "bring the exponent down and subtract 1",
        StudentExplanationEphemeral: "you use the power rule",
        Locale: "en");

    [Fact]
    public async Task Returns_unavailable_when_base_url_missing()
    {
        var sender = new LlmJudgeSidecar(
            Options.Create(new LlmJudgeSidecarOptions()),
            new HttpClient(),
            NullLogger<LlmJudgeSidecar>.Instance);
        var result = await sender.JudgeAsync(NewRequest());
        Assert.Equal(ExplainJudgment.Unavailable, result.Judgment);
        Assert.Equal("null:sidecar-not-configured", result.JudgeBackend);
    }

    [Fact]
    public async Task Returns_unavailable_when_circuit_open()
    {
        var sender = new LlmJudgeSidecar(
            Options.Create(new LlmJudgeSidecarOptions
            {
                BaseUrl = "http://localhost:7101",
                CircuitOpen = true,
            }),
            new HttpClient(new NoOpHandler(HttpStatusCode.OK)),
            NullLogger<LlmJudgeSidecar>.Instance);
        var result = await sender.JudgeAsync(NewRequest());
        Assert.Equal(ExplainJudgment.Unavailable, result.Judgment);
        Assert.Equal("null:circuit-open", result.JudgeBackend);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "null:rate-limited")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "null:sidecar-unavailable")]
    [InlineData(HttpStatusCode.InternalServerError, "null:http-500")]
    public async Task Maps_http_errors_to_unavailable(HttpStatusCode status, string expectedBackend)
    {
        var sender = new LlmJudgeSidecar(
            Options.Create(new LlmJudgeSidecarOptions { BaseUrl = "http://localhost:7101" }),
            new HttpClient(new NoOpHandler(status)),
            NullLogger<LlmJudgeSidecar>.Instance);
        var result = await sender.JudgeAsync(NewRequest());
        Assert.Equal(ExplainJudgment.Unavailable, result.Judgment);
        Assert.Equal(expectedBackend, result.JudgeBackend);
    }

    [Fact]
    public async Task Returns_pass_on_200_with_pass_body()
    {
        var body = "{\"judgment\":\"pass\",\"category\":\"StatesRuleCorrectly\"}";
        var sender = new LlmJudgeSidecar(
            Options.Create(new LlmJudgeSidecarOptions { BaseUrl = "http://localhost:7101" }),
            new HttpClient(new JsonReplyHandler(body)),
            NullLogger<LlmJudgeSidecar>.Instance);
        var result = await sender.JudgeAsync(NewRequest());
        Assert.Equal(ExplainJudgment.Pass, result.Judgment);
        Assert.Equal(ExplainRubricCategory.StatesRuleCorrectly, result.Category);
        Assert.Equal(CopyBucket.Celebrate, result.Bucket);
        Assert.Equal("llm-sidecar", result.JudgeBackend);
    }

    private sealed class NoOpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        public NoOpHandler(HttpStatusCode status) => _status = status;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(_status));
    }

    private sealed class JsonReplyHandler : HttpMessageHandler
    {
        private readonly string _body;
        public JsonReplyHandler(string body) => _body = body;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
