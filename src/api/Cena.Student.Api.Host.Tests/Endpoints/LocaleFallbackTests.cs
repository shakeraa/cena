using Cena.Actors.Questions;
using Cena.Api.Host.Endpoints;

namespace Cena.Student.Api.Host.Tests.Endpoints;

public sealed class LocaleFallbackTests
{
    [Fact]
    public void Resolve_WhenRequestedLocaleExists_DoesNotFallback()
    {
        var question = new QuestionReadModel
        {
            Language = "he",
            Languages = new List<string> { "ar", "en" }
        };

        var result = LocaleFallback.Resolve("ar", question);

        Assert.False(result.UsedFallback);
        Assert.Equal("ar", result.RequestedLocale);
        Assert.Equal("ar", result.ServedLocale);
    }

    [Fact]
    public void Resolve_WhenRequestedLocaleMissing_FallsBackToPrimaryLanguage()
    {
        var question = new QuestionReadModel
        {
            Language = "he",
            Languages = new List<string> { "en" }
        };

        var result = LocaleFallback.Resolve("ar", question);

        Assert.True(result.UsedFallback);
        Assert.Equal("ar", result.RequestedLocale);
        Assert.Equal("he", result.ServedLocale);
    }
}
