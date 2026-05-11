namespace Accountant.Web.Areas.Administration.Services;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
    public bool HasPrev => Page > 1;
    public bool HasNext => Page < TotalPages;
}
