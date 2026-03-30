namespace Cena.Infrastructure.Errors;

/// <summary>
/// Exhaustive set of machine-readable error codes for the Cena platform.
/// Format: CENA_{CONTEXT}_{SPECIFIC}
/// ERR-001.1
/// </summary>
public static class ErrorCodes
{
    // ---- Authentication ----
    public const string CENA_AUTH_TOKEN_EXPIRED         = "CENA_AUTH_TOKEN_EXPIRED";
    public const string CENA_AUTH_TOKEN_INVALID         = "CENA_AUTH_TOKEN_INVALID";
    public const string CENA_AUTH_INSUFFICIENT_ROLE     = "CENA_AUTH_INSUFFICIENT_ROLE";
    /// <summary>SEC-002: Cross-student data access attempt (IDOR). Always 403.</summary>
    public const string CENA_AUTH_IDOR_VIOLATION        = "CENA_AUTH_IDOR_VIOLATION";

    // ---- Session ----
    public const string CENA_SESSION_NOT_FOUND          = "CENA_SESSION_NOT_FOUND";
    public const string CENA_SESSION_ALREADY_ACTIVE     = "CENA_SESSION_ALREADY_ACTIVE";
    public const string CENA_SESSION_EXPIRED            = "CENA_SESSION_EXPIRED";

    // ---- Actor ----
    public const string CENA_ACTOR_VERSION_CONFLICT     = "CENA_ACTOR_VERSION_CONFLICT";
    public const string CENA_ACTOR_ACTIVATION_FAILED    = "CENA_ACTOR_ACTIVATION_FAILED";
    public const string CENA_ACTOR_PASSIVATED           = "CENA_ACTOR_PASSIVATED";

    // ---- LLM ----
    public const string CENA_LLM_BUDGET_EXHAUSTED       = "CENA_LLM_BUDGET_EXHAUSTED";
    public const string CENA_LLM_PROVIDER_UNAVAILABLE   = "CENA_LLM_PROVIDER_UNAVAILABLE";
    public const string CENA_LLM_TIMEOUT                = "CENA_LLM_TIMEOUT";
    public const string CENA_LLM_CONTENT_FILTER         = "CENA_LLM_CONTENT_FILTER";

    // ---- NATS ----
    public const string CENA_NATS_PUBLISH_FAILED        = "CENA_NATS_PUBLISH_FAILED";
    public const string CENA_NATS_CONSUMER_LAG          = "CENA_NATS_CONSUMER_LAG";

    // ---- Content ----
    public const string CENA_CONTENT_CONCEPT_NOT_FOUND  = "CENA_CONTENT_CONCEPT_NOT_FOUND";
    public const string CENA_CONTENT_QUESTION_RETIRED   = "CENA_CONTENT_QUESTION_RETIRED";

    // ---- Payment ----
    public const string CENA_PAYMENT_FAILED             = "CENA_PAYMENT_FAILED";
    public const string CENA_PAYMENT_REFUND_FAILED      = "CENA_PAYMENT_REFUND_FAILED";

    // ---- Event Store (DATA-010) ----
    /// <summary>Optimistic concurrency conflict on event stream append.</summary>
    public const string CENA_EVENTSTORE_CONCURRENCY     = "CENA_EVENTSTORE_CONCURRENCY";

    // ---- Internal ----
    public const string CENA_INTERNAL_ERROR             = "CENA_INTERNAL_ERROR";
    public const string CENA_INTERNAL_VALIDATION        = "CENA_INTERNAL_VALIDATION";

    // ---- Mapping to SignalR error codes (from signalr-messages.ts SignalRErrorCode) ----
    /// <summary>
    /// Maps a Cena error code to the SignalR wire protocol error code.
    /// </summary>
    public static string ToSignalRCode(string cenaCode) => cenaCode switch
    {
        CENA_SESSION_NOT_FOUND        => "SESSION_NOT_FOUND",
        CENA_SESSION_ALREADY_ACTIVE   => "SESSION_ALREADY_ACTIVE",
        CENA_SESSION_EXPIRED          => "SESSION_EXPIRED",
        CENA_AUTH_TOKEN_EXPIRED       => "UNAUTHORIZED",
        CENA_AUTH_TOKEN_INVALID       => "UNAUTHORIZED",
        CENA_AUTH_INSUFFICIENT_ROLE   => "FORBIDDEN",
        CENA_LLM_BUDGET_EXHAUSTED     => "RATE_LIMITED",
        CENA_LLM_PROVIDER_UNAVAILABLE => "INTERNAL_ERROR",
        CENA_LLM_TIMEOUT              => "INTERNAL_ERROR",
        CENA_LLM_CONTENT_FILTER       => "CONTENT_FILTERED",
        CENA_ACTOR_VERSION_CONFLICT   => "CONFLICT",
        CENA_EVENTSTORE_CONCURRENCY   => "CONFLICT",
        CENA_ACTOR_ACTIVATION_FAILED  => "INTERNAL_ERROR",
        CENA_ACTOR_PASSIVATED         => "INTERNAL_ERROR",
        CENA_NATS_PUBLISH_FAILED      => "INTERNAL_ERROR",
        CENA_NATS_CONSUMER_LAG        => "INTERNAL_ERROR",
        CENA_CONTENT_CONCEPT_NOT_FOUND => "NOT_FOUND",
        CENA_CONTENT_QUESTION_RETIRED  => "NOT_FOUND",
        CENA_PAYMENT_FAILED           => "PAYMENT_FAILED",
        CENA_PAYMENT_REFUND_FAILED    => "PAYMENT_FAILED",
        _                             => "INTERNAL_ERROR"
    };
}
