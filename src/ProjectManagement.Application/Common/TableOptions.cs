namespace ProjectManagement.Application.Common;

public record TableOptions(string Sort = "", bool Descending = false, int PageSize = 20)
{
    public TableOptions Normalize(string defaultSort, params string[] fields)
        => new(fields.Contains(Sort, StringComparer.Ordinal) ? Sort : defaultSort,
            fields.Contains(Sort, StringComparer.Ordinal) ? Descending : defaultSort is "updated" or "saved" or "uploaded",
            new[] { 10, 20, 50, 100 }.Contains(PageSize) ? PageSize : 20);

    public int Pages(int total) => Math.Max(1, (int)(((long)total + PageSize - 1) / PageSize));
}
