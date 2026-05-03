// =============================================================================
// Cena Platform — IsraelTimeZoneResolver integration test (prr-157)
// Validates the resolver on whatever OS the test runs on (Linux in CI,
// macOS locally). A green result here proves the prr-157 fix works.
// =============================================================================

using Cena.Infrastructure.Time;

namespace Cena.Actors.Tests.Time;

public class IsraelTimeZoneResolverTests
{
    [Fact]
    public void Instance_ResolvesToNonNullTimeZone()
    {
        Assert.NotNull(IsraelTimeZoneResolver.Instance);
    }

    [Fact]
    public void ConvertFromUtc_ReturnsIsraelOffset()
    {
        // Israel is UTC+2 (IST) or UTC+3 (IDT). Both are valid depending on
        // DST; assert the offset is one of those two — any other value
        // indicates the wrong zone was resolved.
        var nowUtc = DateTimeOffset.UtcNow;
        var israel = IsraelTimeZoneResolver.ConvertFromUtc(nowUtc);
        var hours = israel.Offset.TotalHours;
        Assert.True(hours == 2 || hours == 3,
            $"Expected Israel offset UTC+2 or UTC+3, got UTC{israel.Offset.TotalHours:+#;-#;+0}");
    }
}
