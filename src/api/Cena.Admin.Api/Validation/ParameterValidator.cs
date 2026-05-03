// =============================================================================
// Cena Platform -- Parameter Validation Helper
// REV-011.2: Shared validation for query parameters across admin endpoints
// =============================================================================

using Microsoft.AspNetCore.Http;

namespace Cena.Admin.Api.Validation;

public static class ParameterValidator
{
    private static readonly HashSet<string> ValidPeriods = new() { "7d", "30d", "90d", "365d" };

    public static string ValidatePeriod(string? period)
        => ValidPeriods.Contains(period ?? "30d") ? (period ?? "30d")
           : throw new BadHttpRequestException($"Invalid period '{period}'. Valid: {string.Join(", ", ValidPeriods)}");

    public static int ValidateLimit(int? limit, int max = 100)
        => limit switch
        {
            null => 20,
            < 1 => throw new BadHttpRequestException("Limit must be at least 1"),
            _ when limit > max => throw new BadHttpRequestException($"Limit cannot exceed {max}"),
            _ => limit.Value
        };

    public static int ValidatePage(int? page)
        => page switch
        {
            null or < 1 => 1,
            _ => page.Value
        };

    public static int ValidatePageSize(int? pageSize, int max = 100)
        => pageSize switch
        {
            null => 20,
            < 1 => throw new BadHttpRequestException("PageSize must be at least 1"),
            _ when pageSize > max => throw new BadHttpRequestException($"PageSize cannot exceed {max}"),
            _ => pageSize.Value
        };
}
