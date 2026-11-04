using System.Linq.Expressions;

namespace Hrms.Application.Common;

public static class QueryExtensions
{
    /// <summary>Applies an allow-listed sort ("field,-other"). Unknown fields are ignored (never passed to SQL).</summary>
    public static IQueryable<T> ApplySort<T>(this IQueryable<T> query, string? sort, IReadOnlyDictionary<string, Expression<Func<T, object>>> map, Expression<Func<T, object>> defaultSort)
    {
        IOrderedQueryable<T>? ordered = null;
        foreach (var raw in (sort ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var desc = raw.StartsWith('-');
            var key = desc ? raw[1..] : raw;
            if (!map.TryGetValue(key, out var selector))
            {
                continue;
            }

            ordered = ordered is null
                ? desc ? query.OrderByDescending(selector) : query.OrderBy(selector)
                : desc ? ordered.ThenByDescending(selector) : ordered.ThenBy(selector);
        }

        return ordered ?? query.OrderBy(defaultSort);
    }
}
