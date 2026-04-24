namespace Cena.Infrastructure.Errors;

/// <summary>
/// Categorizes a CenaError so protocol layers can map it to the correct HTTP/gRPC status code.
/// ERR-001.1
/// </summary>
public enum ErrorCategory
{
    Validation,
    Authentication,
    Authorization,
    NotFound,
    Conflict,
    RateLimit,
    ExternalService,
    Internal,
    Timeout
}
