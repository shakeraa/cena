// =============================================================================
// Cena Platform -- Common API Contracts (DB-05)
// Shared pagination, sorting, and envelope types.
// =============================================================================

namespace Cena.Api.Contracts.Common;

public sealed record PaginationParams(
    int Page = 1,
    int PageSize = 20);

public sealed record PaginatedResponse<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages)
{
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

public sealed record ApiErrorResponse(
    string CorrelationId,
    string Code,
    string Message,
    DateTimeOffset Timestamp);
