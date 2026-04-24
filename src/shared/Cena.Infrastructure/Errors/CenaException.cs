namespace Cena.Infrastructure.Errors;

/// <summary>
/// Base exception for all domain errors in the Cena platform.
/// Carries a machine-readable error code and maps to an HTTP status code.
/// ERR-001.1
/// </summary>
public abstract class CenaException : Exception
{
    public string ErrorCode { get; }
    public ErrorCategory Category { get; }
    public int StatusCode { get; }
    public Dictionary<string, object>? Details { get; }

    protected CenaException(
        string errorCode,
        string message,
        ErrorCategory category,
        int statusCode,
        Dictionary<string, object>? details = null,
        Exception? inner = null)
        : base(message, inner)
    {
        ErrorCode = errorCode;
        Category = category;
        StatusCode = statusCode;
        Details = details;
    }

    /// <summary>Converts this exception to the canonical CenaError record.</summary>
    public CenaError ToCenaError(string? correlationId) =>
        new(ErrorCode, Message, Category, Details, correlationId);
}

/// <summary>404 — entity does not exist or is inaccessible to the caller.</summary>
public sealed class EntityNotFoundException : CenaException
{
    public EntityNotFoundException(
        string errorCode,
        string message,
        Dictionary<string, object>? details = null,
        Exception? inner = null)
        : base(errorCode, message, ErrorCategory.NotFound, 404, details, inner) { }

    public EntityNotFoundException(string message)
        : base(ErrorCodes.CENA_INTERNAL_ERROR, message, ErrorCategory.NotFound, 404) { }
}

/// <summary>400 — request payload failed domain validation.</summary>
public sealed class ValidationException : CenaException
{
    public ValidationException(
        string errorCode,
        string message,
        Dictionary<string, object>? details = null,
        Exception? inner = null)
        : base(errorCode, message, ErrorCategory.Validation, 400, details, inner) { }

    public ValidationException(string message)
        : base(ErrorCodes.CENA_INTERNAL_VALIDATION, message, ErrorCategory.Validation, 400) { }
}

/// <summary>409 — operation conflicts with current resource state.</summary>
public sealed class ConflictException : CenaException
{
    public ConflictException(
        string errorCode,
        string message,
        Dictionary<string, object>? details = null,
        Exception? inner = null)
        : base(errorCode, message, ErrorCategory.Conflict, 409, details, inner) { }
}

/// <summary>403 — caller lacks required permission.</summary>
public sealed class ForbiddenException : CenaException
{
    public ForbiddenException(
        string errorCode,
        string message,
        Dictionary<string, object>? details = null,
        Exception? inner = null)
        : base(errorCode, message, ErrorCategory.Authorization, 403, details, inner) { }
}

/// <summary>401 — caller is not authenticated or token is invalid/expired.</summary>
public sealed class AuthenticationException : CenaException
{
    public AuthenticationException(
        string errorCode,
        string message,
        Dictionary<string, object>? details = null,
        Exception? inner = null)
        : base(errorCode, message, ErrorCategory.Authentication, 401, details, inner) { }
}

/// <summary>429 — rate limit or budget exhausted.</summary>
public sealed class RateLimitException : CenaException
{
    public RateLimitException(
        string errorCode,
        string message,
        Dictionary<string, object>? details = null,
        Exception? inner = null)
        : base(errorCode, message, ErrorCategory.RateLimit, 429, details, inner) { }
}

/// <summary>502 — downstream external service failed.</summary>
public sealed class ExternalServiceException : CenaException
{
    public ExternalServiceException(
        string errorCode,
        string message,
        Dictionary<string, object>? details = null,
        Exception? inner = null)
        : base(errorCode, message, ErrorCategory.ExternalService, 502, details, inner) { }
}

/// <summary>504 — operation timed out waiting for a dependency.</summary>
public sealed class TimeoutException : CenaException
{
    public TimeoutException(
        string errorCode,
        string message,
        Dictionary<string, object>? details = null,
        Exception? inner = null)
        : base(errorCode, message, ErrorCategory.Timeout, 504, details, inner) { }
}
