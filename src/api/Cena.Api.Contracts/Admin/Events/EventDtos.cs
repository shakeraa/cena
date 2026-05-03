// =============================================================================
// Cena Platform -- Event Stream & DLQ DTOs
// ADM-013: Real-time event monitoring and dead letter queue
// =============================================================================

namespace Cena.Api.Contracts.Admin.Events;

// Live Event Stream
public sealed record EventStreamResponse(
    IReadOnlyList<DomainEvent> Events,
    string? ContinuationToken);

public sealed record DomainEvent(
    string Id,
    string EventType,
    string AggregateType,
    string AggregateId,
    DateTimeOffset Timestamp,
    string PayloadJson,
    int Version,
    string? CorrelationId);

public sealed record EventFilterRequest(
    IReadOnlyList<string>? EventTypes,
    string? AggregateType,
    string? AggregateId,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime);

// Event Rate Metrics
public sealed record EventRateResponse(
    float EventsPerSecond,
    IReadOnlyList<EventTypeCount> ByType);

public sealed record EventTypeCount(
    string EventType,
    int Count,
    float Percentage);

// Dead Letter Queue
public sealed record DeadLetterQueueResponse(
    IReadOnlyList<DeadLetterMessage> Messages,
    int TotalCount,
    bool HasMore);

public sealed record DeadLetterMessage(
    string Id,
    DateTimeOffset FailedAt,
    string Source,
    string EventType,
    string ErrorMessage,
    int RetryCount,
    string? PayloadPreview);

public sealed record DeadLetterDetailResponse(
    string Id,
    DateTimeOffset FailedAt,
    string Source,
    string EventType,
    string ErrorMessage,
    string FullPayload,
    string? StackTrace,
    int RetryCount,
    IReadOnlyList<RetryAttempt> RetryHistory);

public sealed record RetryAttempt(
    int AttemptNumber,
    DateTimeOffset AttemptedAt,
    string ErrorMessage);

// DLQ Actions
public sealed record RetryMessageResponse(
    string MessageId,
    bool Success,
    string? Error);

public sealed record BulkRetryRequest(
    IReadOnlyList<string> MessageIds);

public sealed record BulkRetryResponse(
    int SuccessCount,
    int FailCount,
    IReadOnlyList<string> FailedIds);

public sealed record DlqDepthAlert(
    bool IsAlerting,
    int CurrentDepth,
    int Threshold,
    string Severity);
