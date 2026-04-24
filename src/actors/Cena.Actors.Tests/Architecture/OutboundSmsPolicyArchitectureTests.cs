// =============================================================================
// Cena Platform — Outbound SMS policy architecture test (prr-018).
//
// Grep-ratchet for the rule: every outbound parent-nudge SMS must flow
// through IOutboundSmsGateway. Direct calls to ISmsSender.SendAsync or
// IWhatsAppSender.SendAsync are only permitted from a tiny, reviewed
// allowlist — the sender implementations themselves, the gateway, and
// the pre-prr-018 NotificationChannelService (student-device push notifications,
// which is a different surface than parent nudges).
//
// Adding a new SMS call site MUST either:
//   (a) route through IOutboundSmsGateway — preferred, or
//   (b) get explicitly added to AllowedCallers below with a justification.
//
// If this test fails, a new code path is bypassing the policy chain. Don't
// silence the test — route the new call through the gateway, or explain in
// the PR why an exception is warranted.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class OutboundSmsPolicyArchitectureTests
{
    /// <summary>
    /// Files permitted to call ISmsSender.SendAsync or IWhatsAppSender.SendAsync
    /// directly. Paths are relative to the repository root and use forward
    /// slashes; the ratchet normalises OS separators before comparison.
    /// </summary>
    private static readonly string[] AllowedCallers =
    {
        // The gateway IS the allowed surface. It is the single place that may
        // call ISmsSender.SendAsync for parent-nudge traffic.
        "src/actors/Cena.Actors/Notifications/OutboundSms/OutboundSmsGateway.cs",

        // Sender implementations define the interface and the adapters. Their
        // SendAsync is the INTERFACE, not a call — but the grep matches both
        // "public Task<..> SendAsync(" and "sender.SendAsync(". We allowlist
        // them so the test stays a ratchet on NEW unintended callers.
        "src/actors/Cena.Actors/Notifications/TwilioSmsSender.cs",
        "src/actors/Cena.Actors/ParentDigest/TwilioWhatsAppSender.cs",
        "src/actors/Cena.Actors/ParentDigest/WhatsAppChannel.cs",

        // prr-018 predates the policy chain for the in-app notification path
        // that sends SMS reminders about SESSION state (NOT parent nudges).
        // This is a deliberately grandfathered surface — it is student-device
        // targeted, rate-limited independently by MaxSmsPerStudentPerHour, and
        // outside the scope of prr-018 which explicitly targets parent-nudge
        // traffic. Revisit when NotificationChannelService grows parent fan-out.
        "src/actors/Cena.Actors/Notifications/NotificationChannelService.cs",

        // prr-108: WhatsApp opt-out decorator. Wraps an inner IWhatsAppSender
        // and consults ParentDigestPreferences BEFORE delegating to _inner.SendAsync.
        // The decorator IS the preferences gate for every downstream caller —
        // it cannot itself route through IOutboundSmsGateway because the
        // gateway is SMS-only; WhatsApp uses its own vendor path. Adding the
        // decorator to the allowlist is the correct architectural decision
        // because the whole point of the class is to be the single wrapper
        // that every IWhatsAppSender consumer gets from DI.
        "src/actors/Cena.Actors/ParentDigest/WhatsAppOptOutPolicy.cs",
    };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    /// <summary>
    /// Matches literal method invocations of SendAsync on an ISmsSender /
    /// IWhatsAppSender. We require the identifier on the left to be a concrete
    /// field/variable name (not "ISmsSender." which is a type reference).
    /// </summary>
    private static readonly Regex SendAsyncCallPattern = new(
        @"\b[_a-zA-Z][_a-zA-Z0-9]*\s*\.\s*SendAsync\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// Files that DECLARE a SendAsync method — identified by the method-decl
    /// pattern. These are always permitted (they are the INTERFACE definitions
    /// and concrete implementations, not call sites).
    /// </summary>
    private static readonly Regex MethodDeclPattern = new(
        @"(?m)^\s*(public|private|internal|protected)[^=\r\n]*\bSendAsync\s*\(",
        RegexOptions.Compiled);

    [Fact]
    public void NoNewSendAsyncCallers_OutsideAllowlist()
    {
        var root = FindRepoRoot();
        var actorsRoot = Path.Combine(root, "src", "actors", "Cena.Actors");
        Assert.True(Directory.Exists(actorsRoot), $"actors root not found at {actorsRoot}");

        // Normalise allowlist paths into the platform separator plus lowercase
        // for case-insensitive compare.
        var allow = AllowedCallers
            .Select(p => p.Replace('/', Path.DirectorySeparatorChar))
            .Select(p => p.ToLowerInvariant())
            .ToHashSet();

        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(actorsRoot, "*.cs", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);

            // Heuristic filter: only scan files that mention one of the two
            // sender interfaces. Every other file is irrelevant.
            var mentionsSms = content.Contains("ISmsSender") || content.Contains("IWhatsAppSender");
            if (!mentionsSms) continue;

            // Does this file CONTAIN a call-site (not just a method decl)?
            var hasCall = false;
            foreach (Match m in SendAsyncCallPattern.Matches(content))
            {
                // Skip if this match is inside a SendAsync method declaration.
                // Method decls match on "public Task<...> SendAsync(" — the
                // dot-based call pattern would not normally match a decl, but
                // we check the whole file for decls and exclude its matches.
                if (IsInsideMethodDecl(content, m.Index)) continue;

                hasCall = true;
                break;
            }
            if (!hasCall) continue;

            var relativePath = Path.GetRelativePath(root, file).ToLowerInvariant();
            if (!allow.Contains(relativePath))
            {
                violations.Add(Path.GetRelativePath(root, file));
            }
        }

        Assert.True(
            violations.Count == 0,
            "prr-018 architecture test: the following files call ISmsSender.SendAsync or " +
            "IWhatsAppSender.SendAsync outside the approved allowlist. Route the call " +
            "through IOutboundSmsGateway or add the file to AllowedCallers with a PR " +
            "justification.\n  " +
            string.Join("\n  ", violations));
    }

    [Fact]
    public void ServiceCollectionExtension_WiresAllFourPolicies()
    {
        var root = FindRepoRoot();
        var file = Path.Combine(root,
            "src", "actors", "Cena.Actors", "Notifications", "OutboundSms",
            "OutboundSmsServiceCollectionExtensions.cs");
        Assert.True(File.Exists(file), $"ServiceCollection extension missing at {file}");

        var src = File.ReadAllText(file);

        // All four policies must be registered, in order. We assert each by
        // name — the order test is below.
        Assert.Contains("SmsSanitizerPolicy", src);
        Assert.Contains("SmsShipgatePolicy", src);
        Assert.Contains("SmsRateLimitPolicy", src);
        Assert.Contains("SmsQuietHoursPolicy", src);
        Assert.Contains("OutboundSmsPolicyChain", src);
        Assert.Contains("OutboundSmsGateway", src);
    }

    [Fact]
    public void ServiceCollectionExtension_PoliciesRegisteredInLoadBearingOrder()
    {
        var root = FindRepoRoot();
        var file = Path.Combine(root,
            "src", "actors", "Cena.Actors", "Notifications", "OutboundSms",
            "OutboundSmsServiceCollectionExtensions.cs");
        var src = File.ReadAllText(file);

        var sanitizerIdx = src.IndexOf("SmsSanitizerPolicy", StringComparison.Ordinal);
        var shipgateIdx = src.IndexOf("SmsShipgatePolicy", StringComparison.Ordinal);
        var rateLimitIdx = src.IndexOf("SmsRateLimitPolicy", StringComparison.Ordinal);
        var quietHoursIdx = src.IndexOf("SmsQuietHoursPolicy", StringComparison.Ordinal);

        // Chain order: sanitizer < shipgate < rate-limit < quiet-hours.
        // See OutboundSmsPolicyChain.cs for the rationale.
        Assert.True(sanitizerIdx < shipgateIdx,
            "SmsSanitizerPolicy must be registered before SmsShipgatePolicy");
        Assert.True(shipgateIdx < rateLimitIdx,
            "SmsShipgatePolicy must be registered before SmsRateLimitPolicy");
        Assert.True(rateLimitIdx < quietHoursIdx,
            "SmsRateLimitPolicy must be registered before SmsQuietHoursPolicy");
    }

    private static bool IsInsideMethodDecl(string content, int matchIndex)
    {
        // Look for the nearest method declaration containing this index.
        foreach (Match decl in MethodDeclPattern.Matches(content))
        {
            if (decl.Index > matchIndex) break;
            // If the decl is before the match and on the same or an adjacent
            // line, treat the match as part of the decl signature rather than
            // a call site. Walk forward to find the end of the decl signature
            // (first '{' or ';').
            var tail = content.AsSpan(decl.Index);
            var brace = tail.IndexOf('{');
            var semi = tail.IndexOf(';');
            var terminator = brace >= 0 && (semi < 0 || brace < semi) ? brace : semi;
            if (terminator < 0) continue;
            var terminatorAbs = decl.Index + terminator;
            if (matchIndex < terminatorAbs) return true;
        }
        return false;
    }
}
