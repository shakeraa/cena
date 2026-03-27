// =============================================================================
// Cena Platform -- Admin Dashboard DTOs
// BKD-004: Response types for dashboard overview, activity, alerts, charts
// =============================================================================

namespace Cena.Admin.Api;

// Overview Widgets
public sealed record DashboardOverviewResponse(
    int ActiveUsers,
    int ActiveUsersChange,
    int TotalStudents,
    int TotalStudentsChange,
    int ContentItems,
    int PendingReview,
    float AvgFocusScore,
    float AvgFocusScoreChange);

// Activity Time Series
public sealed record ActivityDataPoint(
    string Date,
    int Dau,
    int Wau,
    int Mau);

public sealed record ActivityTimeSeriesResponse(
    string Period,
    IReadOnlyList<ActivityDataPoint> Data);

// Content Pipeline Chart
public sealed record PipelinePoint(
    string Date,
    int Created,
    int Reviewed,
    int Approved,
    int Rejected);

public sealed record ContentPipelineResponse(
    IReadOnlyList<PipelinePoint> Data);

// Focus Distribution Histogram
public sealed record FocusDistributionPoint(
    string ScoreRange,
    int Count);

public sealed record FocusDistributionResponse(
    IReadOnlyList<FocusDistributionPoint> Distribution,
    float Average,
    float Median,
    int TotalStudents);

// Mastery Progress
public sealed record SubjectMasteryPoint(
    string Date,
    float Math,
    float Physics);

public sealed record MasteryProgressResponse(
    string Period,
    IReadOnlyList<SubjectMasteryPoint> Data);

// System Alerts
public sealed record SystemAlert(
    string Id,
    string Severity,    // info, warning, error, critical
    string Title,
    string Message,
    DateTimeOffset Timestamp,
    string Source);

// Recent Admin Activity
public sealed record RecentAdminAction(
    DateTimeOffset Timestamp,
    string UserId,
    string UserName,
    string Action,
    string? Target,
    string Description);

// Pending Review Summary
public sealed record PendingReviewSummary(
    int TotalPending,
    int OldestHours,
    string Priority); // low, medium, high, critical

// Dashboard Combined Response (for single request optimization)
public sealed record DashboardHomeResponse(
    DashboardOverviewResponse Overview,
    IReadOnlyList<SystemAlert> Alerts,
    PendingReviewSummary PendingReview,
    IReadOnlyList<RecentAdminAction> RecentActivity);
