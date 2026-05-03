// =============================================================================
// Cena Platform — SMS sanitizer policy tests (prr-018).
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Notifications.OutboundSms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Tests.Notifications.OutboundSms;

public sealed class SmsSanitizerPolicyTests
{
    private static OutboundSmsRequest Req(string body, string? instituteId = "inst-1") =>
        new(
            InstituteId: instituteId,
            ParentPhoneE164: "+972501234567",
            ParentPhoneHash: "hash-abcd",
            ParentTimezone: "Asia/Jerusalem",
            Body: body,
            TemplateId: "weekly-digest-v1",
            CorrelationId: "corr-1",
            ScheduledForUtc: new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero));

    private static SmsSanitizerPolicy NewPolicy(SmsSanitizerOptions? opts = null)
    {
        var options = Options.Create(opts ?? new SmsSanitizerOptions());
        return new SmsSanitizerPolicy(
            options,
            new DummyMeterFactory(),
            NullLogger<SmsSanitizerPolicy>.Instance);
    }

    [Fact]
    public async Task Allow_CleanBody_PassesThrough()
    {
        var policy = NewPolicy();
        var result = await policy.EvaluateAsync(Req("Hi Rami, Noa studied 3h this week."));
        var allow = Assert.IsType<SmsPolicyOutcome.Allow>(result);
        Assert.Contains("Noa studied", allow.PossiblyRewritten.Body);
    }

    [Fact]
    public async Task Block_EmptyAfterStrip()
    {
        var policy = NewPolicy();
        // All-control-chars body becomes empty after sanitisation.
        var result = await policy.EvaluateAsync(Req("\t\r\u200B\u202E"));
        var block = Assert.IsType<SmsPolicyOutcome.Block>(result);
        Assert.Equal("empty_body", block.Reason);
    }

    [Fact]
    public async Task Allow_StripsBidiOverride()
    {
        // Body has no URL so no allowlist is needed. The RLO (U+202E) must be
        // stripped so the rewritten body is plain text.
        var policy = NewPolicy();
        var result = await policy.EvaluateAsync(Req("Hi \u202ERami — nice work today"));
        var allow = Assert.IsType<SmsPolicyOutcome.Allow>(result);
        Assert.False(allow.PossiblyRewritten.Body.Contains('\u202E'),
            "RLO must be stripped by the sanitiser");
    }

    [Fact]
    public async Task Block_Gsm7BodyLongerThan160()
    {
        var policy = NewPolicy();
        var body = new string('A', 161);
        var result = await policy.EvaluateAsync(Req(body));
        var block = Assert.IsType<SmsPolicyOutcome.Block>(result);
        Assert.Equal("body_too_long", block.Reason);
    }

    [Fact]
    public async Task Allow_Gsm7BodyExactly160()
    {
        var policy = NewPolicy();
        var body = new string('A', 160);
        var result = await policy.EvaluateAsync(Req(body));
        Assert.IsType<SmsPolicyOutcome.Allow>(result);
    }

    [Fact]
    public async Task Block_Ucs2BodyLongerThan70()
    {
        var policy = NewPolicy();
        // Each Hebrew character is one UTF-16 code unit. 71 > 70 cap.
        var body = new string('א', 71);
        var result = await policy.EvaluateAsync(Req(body));
        var block = Assert.IsType<SmsPolicyOutcome.Block>(result);
        Assert.Equal("body_too_long", block.Reason);
    }

    [Fact]
    public async Task Allow_Ucs2BodyExactly70()
    {
        var policy = NewPolicy();
        var body = new string('א', 70);
        var result = await policy.EvaluateAsync(Req(body));
        Assert.IsType<SmsPolicyOutcome.Allow>(result);
    }

    [Fact]
    public async Task Block_UrlNotOnAllowlist()
    {
        var policy = NewPolicy(new SmsSanitizerOptions
        {
            GlobalUrlAllowlist = new() { "cena.app" },
        });
        var result = await policy.EvaluateAsync(Req("Open: https://evil.example.com/go"));
        var block = Assert.IsType<SmsPolicyOutcome.Block>(result);
        Assert.Equal("url_not_allowlisted", block.Reason);
    }

    [Fact]
    public async Task Allow_UrlOnAllowlist()
    {
        var policy = NewPolicy(new SmsSanitizerOptions
        {
            GlobalUrlAllowlist = new() { "cena.app" },
        });
        var result = await policy.EvaluateAsync(Req("Open: https://cena.app/parent"));
        Assert.IsType<SmsPolicyOutcome.Allow>(result);
    }

    [Fact]
    public async Task Allow_UrlOnSubdomainOfAllowlistedHost()
    {
        var policy = NewPolicy(new SmsSanitizerOptions
        {
            GlobalUrlAllowlist = new() { "cena.app" },
        });
        var result = await policy.EvaluateAsync(Req("https://parent.cena.app/go"));
        Assert.IsType<SmsPolicyOutcome.Allow>(result);
    }

    [Fact]
    public async Task Block_AllUrlsWhenAllowlistEmpty()
    {
        var policy = NewPolicy();   // empty allowlist = no URLs
        var result = await policy.EvaluateAsync(Req("Open: cena.app/parent"));
        var block = Assert.IsType<SmsPolicyOutcome.Block>(result);
        Assert.Equal("url_not_allowlisted", block.Reason);
    }

    [Fact]
    public async Task Allow_InstituteOverrideAddsHost()
    {
        var policy = NewPolicy(new SmsSanitizerOptions
        {
            GlobalUrlAllowlist = new(),
            InstituteUrlAllowlist = new()
            {
                ["inst-9"] = new() { "school.edu.il" },
            },
        });
        var req = Req("https://school.edu.il/grades", instituteId: "inst-9");
        var result = await policy.EvaluateAsync(req);
        Assert.IsType<SmsPolicyOutcome.Allow>(result);
    }

    [Fact]
    public async Task Block_InstituteOverrideDoesNotLeakToOtherInstitute()
    {
        var policy = NewPolicy(new SmsSanitizerOptions
        {
            GlobalUrlAllowlist = new(),
            InstituteUrlAllowlist = new()
            {
                ["inst-9"] = new() { "school.edu.il" },
            },
        });
        var req = Req("https://school.edu.il/grades", instituteId: "inst-OTHER");
        var result = await policy.EvaluateAsync(req);
        Assert.IsType<SmsPolicyOutcome.Block>(result);
    }

    [Fact]
    public void ExtractHost_HandlesSchemeless()
    {
        Assert.Equal("cena.app", SmsSanitizerPolicy.ExtractHost("cena.app/parent"));
        Assert.Equal("cena.app", SmsSanitizerPolicy.ExtractHost("https://cena.app/parent"));
        Assert.Equal("cena.app", SmsSanitizerPolicy.ExtractHost("cena.app"));
    }

    // ------------------------------------------------------------------
    // Infrastructure
    // ------------------------------------------------------------------

    private sealed class DummyMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
