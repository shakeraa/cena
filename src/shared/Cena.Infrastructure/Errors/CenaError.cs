using System.Text.Json.Serialization;

namespace Cena.Infrastructure.Errors;

/// <summary>
/// Canonical error record used as the wire format across all Cena protocol boundaries
/// (REST Problem Details, SignalR ErrorEvent, gRPC trailing metadata, NATS DLQ headers).
/// ERR-001.1
/// </summary>
public sealed record CenaError(
    [property: JsonPropertyName("code")]
    string Code,

    [property: JsonPropertyName("message")]
    string Message,

    [property: JsonPropertyName("category")]
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    ErrorCategory Category,

    [property: JsonPropertyName("details")]
    Dictionary<string, object>? Details,

    [property: JsonPropertyName("correlationId")]
    string? CorrelationId
);

/// <summary>
/// REST response envelope: { "error": { ... } }
/// </summary>
public sealed record ErrorResponse(
    [property: JsonPropertyName("error")]
    CenaError Error
);
