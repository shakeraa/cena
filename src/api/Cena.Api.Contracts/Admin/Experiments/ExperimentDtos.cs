// Cena Platform -- Experiment Admin DTOs (ADM-019)

namespace Cena.Api.Contracts.Admin.Experiments;

/// <summary>List of all known A/B experiments with summary metrics.</summary>
public sealed record ExperimentListResponse(
    IReadOnlyList<ExperimentSummaryDto> Experiments);

/// <summary>High-level summary row for each experiment.</summary>
public sealed record ExperimentSummaryDto(
    string Name,
    string Status,
    IReadOnlyList<string> Arms,
    int TreatmentCount,
    int ControlCount,
    DateTimeOffset StartDate,
    string Description);

/// <summary>Detailed view of a single experiment with cohort breakdown.</summary>
public sealed record ExperimentDetailDto(
    string Name,
    string Status,
    IReadOnlyList<string> Arms,
    string Description,
    IReadOnlyList<CohortDto> CohortBreakdown,
    int TotalStudents);

/// <summary>Per-arm cohort metrics derived from the event stream.</summary>
public sealed record CohortDto(
    string ArmName,
    int StudentCount,
    float AvgMasteryDelta,
    float ConfusionResolutionRate,
    float AvgTutoringTurns,
    float AvgTimeToMasteryHours);

/// <summary>Funnel analysis showing student progression through stages.</summary>
public sealed record ExperimentFunnelResponse(
    string ExperimentName,
    IReadOnlyList<FunnelStageDto> Stages);

/// <summary>Single stage in the experiment funnel.</summary>
public sealed record FunnelStageDto(
    string Name,
    int Count,
    float Rate);
