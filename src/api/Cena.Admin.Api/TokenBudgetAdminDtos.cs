// Cena Platform -- Token Budget & NATS Admin DTOs (ADM-023)

namespace Cena.Admin.Api;

public sealed record TokenBudgetStatusResponse(
    List<StudentTokenUsageDto> Students,
    long TotalTokensToday,
    int TotalStudentsNearLimit,
    int DailyLimitPerStudent,
    long MonthlyLimitTotal);

public sealed record StudentTokenUsageDto(
    string StudentId,
    int TokensUsedToday,
    int DailyLimit,
    float PercentUsed,
    bool IsExhausted,
    float EstimatedCostUsd);

public sealed record TokenBudgetTrendResponse(
    List<DailyTokenUsageDto> Days);

public sealed record DailyTokenUsageDto(
    string Date,
    long TotalTokens,
    int UniqueStudents,
    float EstimatedCostUsd);

public sealed record UpdateBudgetLimitsRequest(
    int? DailyLimitPerStudent,
    long? MonthlyLimitTotal);

public sealed record NatsStatsResponse(
    List<NatsStreamDto> Streams,
    long TotalMessages,
    long TotalBytes,
    int TotalConsumers);

public sealed record NatsStreamDto(
    string Name,
    long MessageCount,
    long ByteSize,
    int ConsumerCount,
    long LastSequence,
    DateTimeOffset? FirstTimestamp,
    DateTimeOffset? LastTimestamp,
    List<NatsConsumerDto> Consumers);

public sealed record NatsConsumerDto(
    string Name,
    long PendingCount,
    long AckPending,
    long DeliveredCount);
