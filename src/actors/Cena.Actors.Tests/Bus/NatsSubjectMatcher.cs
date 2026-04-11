// =============================================================================
// Cena Platform — NATS Subject Wildcard Matcher (test utility)
//
// Implements NATS subject matching rules so that publisher/subscriber subject
// contract tests can assert "this subscriber wildcard will actually receive
// this publisher subject". Not a full NATS parser — only the rules the Cena
// codebase uses today.
//
// NATS subject wildcard rules:
//   *   matches exactly one token (delimited by '.')
//   >   matches one or more tokens and must appear in the final position
//   all other tokens must match literally.
// =============================================================================

namespace Cena.Actors.Tests.Bus;

internal static class NatsSubjectMatcher
{
    /// <summary>
    /// Returns true if <paramref name="subject"/> would be delivered to a
    /// subscriber using the wildcard pattern <paramref name="pattern"/>.
    /// </summary>
    public static bool IsMatch(string pattern, string subject)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(subject))
            return false;

        var patternTokens = pattern.Split('.');
        var subjectTokens = subject.Split('.');

        for (var i = 0; i < patternTokens.Length; i++)
        {
            var pt = patternTokens[i];

            if (pt == ">")
            {
                // '>' is only valid as the final token and consumes 1+ tokens.
                if (i != patternTokens.Length - 1) return false;
                return subjectTokens.Length >= i + 1;
            }

            if (i >= subjectTokens.Length) return false;

            var st = subjectTokens[i];
            if (pt == "*")
            {
                if (string.IsNullOrEmpty(st)) return false;
                continue;
            }

            if (!string.Equals(pt, st, StringComparison.Ordinal)) return false;
        }

        return subjectTokens.Length == patternTokens.Length;
    }
}
