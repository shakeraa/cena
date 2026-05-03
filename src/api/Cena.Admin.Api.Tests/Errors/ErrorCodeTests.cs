using System.Reflection;
using System.Text.Json;
using Cena.Infrastructure.Errors;

namespace Cena.Admin.Api.Tests.Errors;

/// <summary>
/// ERR-001.1 acceptance tests for error hierarchy and machine-readable error codes.
/// </summary>
public class ErrorCodeTests
{
    [Fact]
    public void ErrorCode_HasCorrectFormat()
    {
        var codes = typeof(ErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string?)f.GetValue(null))
            .Where(v => v is not null)
            .ToList();

        Assert.NotEmpty(codes);
        Assert.All(codes, code =>
        {
            Assert.Matches(@"^CENA_[A-Z]+_[A-Z_]+$", code!);
        });
    }

    [Fact]
    public void ErrorCode_MapsToSignalRCode()
    {
        Assert.Equal("SESSION_NOT_FOUND",    ErrorCodes.ToSignalRCode(ErrorCodes.CENA_SESSION_NOT_FOUND));
        Assert.Equal("UNAUTHORIZED",         ErrorCodes.ToSignalRCode(ErrorCodes.CENA_AUTH_TOKEN_EXPIRED));
        Assert.Equal("UNAUTHORIZED",         ErrorCodes.ToSignalRCode(ErrorCodes.CENA_AUTH_TOKEN_INVALID));
        Assert.Equal("SESSION_ALREADY_ACTIVE", ErrorCodes.ToSignalRCode(ErrorCodes.CENA_SESSION_ALREADY_ACTIVE));
        Assert.Equal("RATE_LIMITED",         ErrorCodes.ToSignalRCode(ErrorCodes.CENA_LLM_BUDGET_EXHAUSTED));
        Assert.Equal("INTERNAL_ERROR",       ErrorCodes.ToSignalRCode(ErrorCodes.CENA_ACTOR_ACTIVATION_FAILED));
    }

    [Fact]
    public void CenaError_SerializesWithAllFields()
    {
        var error = new CenaError(
            Code: ErrorCodes.CENA_LLM_BUDGET_EXHAUSTED,
            Message: "Daily LLM budget exhausted (25000/25000 tokens used)",
            Category: ErrorCategory.RateLimit,
            Details: new Dictionary<string, object>
            {
                ["tokens_used"]  = 25000,
                ["daily_limit"]  = 25000,
                ["reset_at"]     = "2026-03-27T00:00:00Z"
            },
            CorrelationId: "01JQWX5ABC123"
        );

        var json = JsonSerializer.Serialize(error);
        Assert.Contains("CENA_LLM_BUDGET_EXHAUSTED", json);
        Assert.Contains("01JQWX5ABC123", json);
        Assert.Contains("tokens_used", json);
        Assert.Contains("RateLimit", json);
    }

    [Fact]
    public void UnknownErrorCode_MapsToInternalError_InSignalR()
    {
        var result = ErrorCodes.ToSignalRCode("CENA_UNKNOWN_THING");
        Assert.Equal("INTERNAL_ERROR", result);
    }

    [Fact]
    public void AllRequiredErrorCodes_ArePresent()
    {
        // Spot-check the required codes from the task spec
        var requiredCodes = new[]
        {
            ErrorCodes.CENA_AUTH_TOKEN_EXPIRED,
            ErrorCodes.CENA_AUTH_TOKEN_INVALID,
            ErrorCodes.CENA_AUTH_INSUFFICIENT_ROLE,
            ErrorCodes.CENA_SESSION_NOT_FOUND,
            ErrorCodes.CENA_SESSION_ALREADY_ACTIVE,
            ErrorCodes.CENA_SESSION_EXPIRED,
            ErrorCodes.CENA_ACTOR_VERSION_CONFLICT,
            ErrorCodes.CENA_ACTOR_ACTIVATION_FAILED,
            ErrorCodes.CENA_ACTOR_PASSIVATED,
            ErrorCodes.CENA_LLM_BUDGET_EXHAUSTED,
            ErrorCodes.CENA_LLM_PROVIDER_UNAVAILABLE,
            ErrorCodes.CENA_LLM_TIMEOUT,
            ErrorCodes.CENA_LLM_CONTENT_FILTER,
            ErrorCodes.CENA_NATS_PUBLISH_FAILED,
            ErrorCodes.CENA_NATS_CONSUMER_LAG,
            ErrorCodes.CENA_CONTENT_CONCEPT_NOT_FOUND,
            ErrorCodes.CENA_CONTENT_QUESTION_RETIRED,
            ErrorCodes.CENA_PAYMENT_FAILED,
            ErrorCodes.CENA_PAYMENT_REFUND_FAILED,
        };

        Assert.All(requiredCodes, code => Assert.Matches(@"^CENA_[A-Z]+_[A-Z_]+$", code));
    }
}
