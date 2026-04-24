// Cena Platform -- Token Budget Admin Service (ADM-023)

using System.Text.Json;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api;

public interface ITokenBudgetAdminService
{
    Task<TokenBudgetStatusResponse> GetBudgetStatusAsync(string? classId, DateTimeOffset? date);
    Task<TokenBudgetTrendResponse> GetTrendAsync(int days);
    Task<bool> UpdateLimitsAsync(UpdateBudgetLimitsRequest request);
}

public sealed class TokenBudgetAdminService : ITokenBudgetAdminService
{
    private const int DefaultDailyLimit = 25_000;
    private const long DefaultMonthlyLimit = 500_000;
    private const float CostPerToken = 0.000003f; // Claude Sonnet pricing estimate

    private readonly IDocumentStore _store;
    private readonly ILogger<TokenBudgetAdminService> _logger;

    // In-memory overrides (production would use a system settings table)
    private static int _dailyLimitOverride = DefaultDailyLimit;
    private static long _monthlyLimitOverride = DefaultMonthlyLimit;

    public TokenBudgetAdminService(IDocumentStore store, ILogger<TokenBudgetAdminService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<TokenBudgetStatusResponse> GetBudgetStatusAsync(string? classId, DateTimeOffset? date)
    {
        await using var session = _store.QuerySession();

        var targetDate = (date ?? DateTimeOffset.UtcNow).Date;
        var nextDay = targetDate.AddDays(1);

        // FIND-data-021: Use real token counts from TutorMessageDocument
        var messageDocs = await session.Query<TutorMessageDocument>()
            .Where(m => m.CreatedAt >= targetDate && m.CreatedAt < nextDay && m.TokensUsed.HasValue)
            .ToListAsync();

        var dailyLimit = _dailyLimitOverride;

        var students = messageDocs
            .GroupBy(m => m.StudentId)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g =>
            {
                var tokensUsed = g.Sum(m => m.TokensUsed ?? 0);

                var percentUsed = (float)tokensUsed / dailyLimit * 100f;
                var estimatedCost = tokensUsed * CostPerToken;

                return new StudentTokenUsageDto(
                    StudentId: g.Key,
                    TokensUsedToday: tokensUsed,
                    DailyLimit: dailyLimit,
                    PercentUsed: MathF.Round(percentUsed, 1),
                    IsExhausted: tokensUsed >= dailyLimit,
                    EstimatedCostUsd: MathF.Round(estimatedCost, 4));
            })
            .OrderByDescending(s => s.PercentUsed)
            .ToList();

        var totalTokensToday = students.Sum(s => (long)s.TokensUsedToday);
        var nearLimitCount = students.Count(s => s.PercentUsed >= 80f);

        return new TokenBudgetStatusResponse(
            Students: students,
            TotalTokensToday: totalTokensToday,
            TotalStudentsNearLimit: nearLimitCount,
            DailyLimitPerStudent: dailyLimit,
            MonthlyLimitTotal: _monthlyLimitOverride);
    }

    public async Task<TokenBudgetTrendResponse> GetTrendAsync(int days)
    {
        await using var session = _store.QuerySession();

        var endDate = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).AddDays(1);
        var startDate = endDate.AddDays(-days);

        // FIND-data-021: Use real token counts from TutorMessageDocument
        var messageDocs = await session.Query<TutorMessageDocument>()
            .Where(m => m.CreatedAt >= startDate && m.CreatedAt < endDate && m.TokensUsed.HasValue)
            .ToListAsync();

        var dailyData = messageDocs
            .GroupBy(m => m.CreatedAt.Date)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var totalTokens = g.Sum(m => (long)(m.TokensUsed ?? 0));
                var uniqueStudents = g.Select(m => m.StudentId)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Distinct()
                    .Count();
                var estimatedCost = totalTokens * CostPerToken;

                return new DailyTokenUsageDto(
                    Date: g.Key.ToString("yyyy-MM-dd"),
                    TotalTokens: totalTokens,
                    UniqueStudents: uniqueStudents,
                    EstimatedCostUsd: MathF.Round(estimatedCost, 4));
            })
            .ToList();

        return new TokenBudgetTrendResponse(Days: dailyData);
    }

    public Task<bool> UpdateLimitsAsync(UpdateBudgetLimitsRequest request)
    {
        if (request.DailyLimitPerStudent.HasValue)
        {
            _dailyLimitOverride = request.DailyLimitPerStudent.Value;
            _logger.LogInformation("Daily token limit updated to {Limit}", _dailyLimitOverride);
        }

        if (request.MonthlyLimitTotal.HasValue)
        {
            _monthlyLimitOverride = request.MonthlyLimitTotal.Value;
            _logger.LogInformation("Monthly token limit updated to {Limit}", _monthlyLimitOverride);
        }

        return Task.FromResult(true);
    }

    private static string ExtractString(dynamic evt, string property)
    {
        try
        {
            object? data = evt.Data;
            if (data is null) return "";
            var json = JsonDocument.Parse(JsonSerializer.Serialize(data));
            JsonElement prop;
            if (json.RootElement.TryGetProperty(property, out prop) ||
                json.RootElement.TryGetProperty(ToPascalCase(property), out prop))
                return prop.GetString() ?? "";
        }
        catch { /* best-effort extraction */ }
        return "";
    }

    private static string ToPascalCase(string camelCase)
    {
        if (string.IsNullOrEmpty(camelCase)) return camelCase;
        return char.ToUpperInvariant(camelCase[0]) + camelCase[1..];
    }
}
