using Microsoft.EntityFrameworkCore;

namespace PlateBack.Application.Common.Paging;

/// <summary>Envelope for every paged list (design doc 6.1).</summary>
public sealed record PageResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

/// <summary>Page number and size with the API defaults: 20 per page, at most 50.</summary>
public readonly record struct PageRequest(int Page, int PageSize)
{
    public const int DefaultSize = 20;
    public const int MaxSize = 50;

    public static PageRequest From(int? page, int? pageSize) =>
        new(Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? DefaultSize, 1, MaxSize));

    public int Skip => (Page - 1) * PageSize;
}

public static class PagingExtensions
{
    /// <summary>Counts and fetches one page in the database.</summary>
    public static async Task<PageResult<T>> ToPageAsync<T>(this IQueryable<T> query, PageRequest page, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        return new PageResult<T>(items, page.Page, page.PageSize, total);
    }

    /// <summary>Pages a list that is already in memory.</summary>
    public static PageResult<T> ToPage<T>(this IReadOnlyList<T> items, PageRequest page) =>
        new(items.Skip(page.Skip).Take(page.PageSize).ToList(), page.Page, page.PageSize, items.Count);
}
