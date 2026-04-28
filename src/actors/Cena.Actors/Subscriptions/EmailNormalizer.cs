// =============================================================================
// Cena Platform — EmailNormalizer (per-user discount-codes feature)
//
// Pure static helper for canonicalising email addresses for equality
// comparison. Sourced from `docs/design/trial-recycle-defense-001-research.md`
// §1.5 (well-documented Gmail-alias normalisation rules).
//
// Rules applied (in order):
//   1. Trim whitespace.
//   2. Lowercase.
//   3. If the address has the form `local@domain`, lower-case the domain.
//   4. If domain ∈ {gmail.com, googlemail.com}:
//        a. canonicalise the domain to `gmail.com`
//        b. drop everything from `+` onward in the local part (Gmail aliases)
//        c. drop dots from the local part (Gmail ignores them)
//
// Used at:
//   - admin issue-discount endpoint (BEFORE persisting / before Stripe)
//   - student-side `/api/me/applicable-discount` lookup
//   - DiscountAssignmentService one-active-per-email check
//
// Why not just RFC-stricter parsing? Because the recycle-defense brief is
// explicit that the production-grade defense uses these specific rules and
// nothing fancier — a regex parse + dot-strip + plus-strip is exactly what
// Gmail itself documents (https://support.google.com/mail/answer/7436150).
// More aggressive normalisation (e.g. assuming + means alias on every
// provider) would WRONGLY fold non-Gmail addresses where the local part
// truly contains `+` or `.` as significant characters.
// =============================================================================

using System.Globalization;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Pure email canonicaliser. All methods are deterministic + side-effect-free.
/// </summary>
public static class EmailNormalizer
{
    /// <summary>Domains that follow Gmail's dot-/plus-stripping conventions.</summary>
    private static readonly string[] GmailDomains = ["gmail.com", "googlemail.com"];

    /// <summary>Canonical Gmail domain after folding.</summary>
    public const string GmailCanonicalDomain = "gmail.com";

    /// <summary>
    /// Return the canonical (lower-cased, Gmail-folded) form of
    /// <paramref name="email"/>, or <c>string.Empty</c> when input is null,
    /// blank, or fails the minimal local@domain check.
    /// </summary>
    /// <remarks>
    /// This is NOT a full RFC-5321 validator — caller has already validated
    /// shape via <see cref="IsValidShape"/>. The normalizer is permissive on
    /// purpose so that, e.g., an admin pasting a trailing newline doesn't
    /// silently produce a different bucket from a parent's clean signup.
    /// </remarks>
    public static string Normalize(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;
        var trimmed = email.Trim();
        var atIdx = trimmed.IndexOf('@');
        if (atIdx <= 0 || atIdx == trimmed.Length - 1) return string.Empty;

        var local = trimmed[..atIdx].ToLower(CultureInfo.InvariantCulture);
        var domain = trimmed[(atIdx + 1)..].ToLower(CultureInfo.InvariantCulture);

        // Gmail-family folding: dots are ignored in the local part, and
        // `local+anything` is an alias. Both googlemail.com and gmail.com
        // collapse to gmail.com (well-documented Google convention).
        if (Array.IndexOf(GmailDomains, domain) >= 0)
        {
            var plusIdx = local.IndexOf('+');
            if (plusIdx >= 0) local = local[..plusIdx];
            local = local.Replace(".", string.Empty);
            domain = GmailCanonicalDomain;
        }

        if (local.Length == 0) return string.Empty;
        return local + "@" + domain;
    }

    /// <summary>
    /// True iff <paramref name="email"/> has the bare-minimum
    /// <c>local@domain</c> shape with at least one character on each side and
    /// at least one '.' in the domain. Intended as a server-side defensive
    /// check; the admin UI does richer client-side validation. Returns false
    /// for null / whitespace / multi-@ / empty-local / empty-domain inputs.
    /// </summary>
    public static bool IsValidShape(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var trimmed = email.Trim();
        var atCount = 0;
        var atIdx = -1;
        for (var i = 0; i < trimmed.Length; i++)
        {
            if (trimmed[i] == '@')
            {
                atCount++;
                atIdx = i;
            }
            // Spaces inside the trimmed string are not allowed.
            if (char.IsWhiteSpace(trimmed[i])) return false;
        }
        if (atCount != 1) return false;
        if (atIdx <= 0 || atIdx == trimmed.Length - 1) return false;
        var domain = trimmed[(atIdx + 1)..];
        if (domain.IndexOf('.') < 0) return false;
        // Domain dot at start/end disqualifies.
        if (domain[0] == '.' || domain[^1] == '.') return false;
        return true;
    }
}
