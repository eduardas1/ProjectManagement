using System.Linq.Expressions;

namespace ProjectManagement.Infrastructure.Repositories;

internal static class TableOrdering
{
    public static IOrderedQueryable<T> SortBy<T, TKey>(this IQueryable<T> query,
        Expression<Func<T, TKey>> key, bool descending)
        => descending ? query.OrderByDescending(key) : query.OrderBy(key);
}
