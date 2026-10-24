namespace Hrms.Shared.Paging;

public sealed record PageRequest
{
    public const int MaxPageSize = 200;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    /// <summary>Comma separated list of fields, prefix with '-' for descending. Only allow-listed fields are honoured.</summary>
    public string? Sort { get; init; }

    public int SafePage => Page < 1 ? 1 : Page;
    public int SafePageSize => PageSize < 1 ? 25 : Math.Min(PageSize, MaxPageSize);
    public int Skip => (SafePage - 1) * SafePageSize;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
