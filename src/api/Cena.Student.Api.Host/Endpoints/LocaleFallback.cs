using Cena.Actors.Questions;

namespace Cena.Api.Host.Endpoints;

internal sealed record LocaleFallbackDecision(
    string RequestedLocale,
    string ServedLocale,
    bool UsedFallback);

internal static class LocaleFallback
{
    public static LocaleFallbackDecision Resolve(string? requestedLocale, QuestionReadModel? question)
    {
        if (string.IsNullOrWhiteSpace(requestedLocale))
        {
            return new LocaleFallbackDecision(string.Empty, string.Empty, false);
        }

        var normalizedRequested = requestedLocale.Trim().ToLowerInvariant();
        var primaryLanguage = Normalize(question?.Language) ?? "he";
        var availableLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            primaryLanguage
        };

        if (question?.Languages is not null)
        {
            foreach (var language in question.Languages)
            {
                var normalized = Normalize(language);
                if (normalized is not null)
                {
                    availableLanguages.Add(normalized);
                }
            }
        }

        if (availableLanguages.Contains(normalizedRequested))
        {
            return new LocaleFallbackDecision(
                RequestedLocale: normalizedRequested,
                ServedLocale: normalizedRequested,
                UsedFallback: false);
        }

        return new LocaleFallbackDecision(
            RequestedLocale: normalizedRequested,
            ServedLocale: primaryLanguage,
            UsedFallback: true);
    }

    private static string? Normalize(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return null;
        }

        return locale.Trim().ToLowerInvariant();
    }
}
