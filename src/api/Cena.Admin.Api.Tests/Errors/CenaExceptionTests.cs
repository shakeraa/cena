using Cena.Infrastructure.Errors;

namespace Cena.Admin.Api.Tests.Errors;

/// <summary>
/// Tests for the CenaException hierarchy and ToCenaError conversion.
/// ERR-001.1
/// </summary>
public class CenaExceptionTests
{
    [Fact]
    public void EntityNotFoundException_HasStatusCode404()
    {
        var ex = new EntityNotFoundException(ErrorCodes.CENA_SESSION_NOT_FOUND, "Session not found");

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal(ErrorCategory.NotFound, ex.Category);
        Assert.Equal(ErrorCodes.CENA_SESSION_NOT_FOUND, ex.ErrorCode);
    }

    [Fact]
    public void ValidationException_HasStatusCode400()
    {
        var ex = new ValidationException(ErrorCodes.CENA_INTERNAL_VALIDATION, "Bad input");

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(ErrorCategory.Validation, ex.Category);
    }

    [Fact]
    public void ConflictException_HasStatusCode409()
    {
        var ex = new ConflictException(ErrorCodes.CENA_ACTOR_VERSION_CONFLICT, "Version mismatch");

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(ErrorCategory.Conflict, ex.Category);
    }

    [Fact]
    public void ForbiddenException_HasStatusCode403()
    {
        var ex = new ForbiddenException(ErrorCodes.CENA_AUTH_INSUFFICIENT_ROLE, "Admin required");

        Assert.Equal(403, ex.StatusCode);
        Assert.Equal(ErrorCategory.Authorization, ex.Category);
    }

    [Fact]
    public void AuthenticationException_HasStatusCode401()
    {
        var ex = new AuthenticationException(ErrorCodes.CENA_AUTH_TOKEN_EXPIRED, "Token expired");

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal(ErrorCategory.Authentication, ex.Category);
    }

    [Fact]
    public void RateLimitException_HasStatusCode429()
    {
        var ex = new RateLimitException(ErrorCodes.CENA_LLM_BUDGET_EXHAUSTED, "Budget exhausted");

        Assert.Equal(429, ex.StatusCode);
        Assert.Equal(ErrorCategory.RateLimit, ex.Category);
    }

    [Fact]
    public void ExternalServiceException_HasStatusCode502()
    {
        var ex = new ExternalServiceException(ErrorCodes.CENA_LLM_PROVIDER_UNAVAILABLE, "Provider down");

        Assert.Equal(502, ex.StatusCode);
        Assert.Equal(ErrorCategory.ExternalService, ex.Category);
    }

    [Fact]
    public void CenaException_ToCenaError_IncludesCorrelationId()
    {
        const string correlationId = "test-corr-001";
        var ex = new EntityNotFoundException(
            ErrorCodes.CENA_SESSION_NOT_FOUND,
            "Session abc not found",
            new Dictionary<string, object> { ["sessionId"] = "abc" });

        var error = ex.ToCenaError(correlationId);

        Assert.Equal(correlationId, error.CorrelationId);
        Assert.Equal(ErrorCodes.CENA_SESSION_NOT_FOUND, error.Code);
        Assert.Equal(ErrorCategory.NotFound, error.Category);
        Assert.Equal("abc", error.Details!["sessionId"]);
    }

    [Fact]
    public void CenaException_ToCenaError_NullCorrelationId_IsAllowed()
    {
        var ex = new ValidationException("Bad data");
        var error = ex.ToCenaError(null);

        Assert.Null(error.CorrelationId);
    }
}
