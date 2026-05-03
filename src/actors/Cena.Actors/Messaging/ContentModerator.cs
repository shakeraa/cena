// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Content Moderator
// Layer: Domain Service | Runtime: .NET 9
// Blocks PII (phone, email) and non-allowlisted URLs.
// Flags but does not block excessive caps.
// ═══════════════════════════════════════════════════════════════════════

using System.Text.RegularExpressions;

namespace Cena.Actors.Messaging;

public interface IContentModerator
{
    ModerationResult Check(string text);
}

public sealed partial class ContentModerator : IContentModerator
{
    // Phone number: 7-15 digits, optional leading +
    [GeneratedRegex(@"\+?\d{7,15}", RegexOptions.Compiled)]
    private static partial Regex PhonePattern();

    // Email: simplified RFC 5322
    [GeneratedRegex(@"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled)]
    private static partial Regex EmailPattern();

    // URL extraction
    [GeneratedRegex(@"https?://([^\s/]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex UrlDomainPattern();

    private static readonly HashSet<string> AllowlistedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com", "www.youtube.com", "youtu.be",
        "khanacademy.org", "www.khanacademy.org",
        "desmos.com", "www.desmos.com",
        "geogebra.org", "www.geogebra.org",
        "wikipedia.org", "en.wikipedia.org", "he.wikipedia.org", "ar.wikipedia.org",
    };

    public ModerationResult Check(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new ModerationResult(true, null);

        // 1. Phone numbers — block
        if (PhonePattern().IsMatch(text))
            return new ModerationResult(false, "phone_number_detected");

        // 2. Email addresses — block
        if (EmailPattern().IsMatch(text))
            return new ModerationResult(false, "email_detected");

        // 3. URLs — block if not in allowlist
        var urlMatches = UrlDomainPattern().Matches(text);
        foreach (Match match in urlMatches)
        {
            var domain = match.Groups[1].Value.ToLowerInvariant();
            if (!IsAllowlisted(domain))
                return new ModerationResult(false, "url_not_allowlisted");
        }

        // 4. Excessive caps — flag but don't block
        if (text.Length > 20)
        {
            int upperCount = 0;
            foreach (char c in text)
                if (char.IsUpper(c))
                    upperCount++;

            int letterCount = 0;
            foreach (char c in text)
                if (char.IsLetter(c))
                    letterCount++;

            if (letterCount > 0 && (double)upperCount / letterCount > 0.5)
                return new ModerationResult(true, null, "excessive_caps");
        }

        return new ModerationResult(true, null);
    }

    private static bool IsAllowlisted(string domain)
    {
        if (AllowlistedDomains.Contains(domain))
            return true;

        // Check if it's a subdomain of an allowlisted domain
        foreach (var allowed in AllowlistedDomains)
        {
            if (domain.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
