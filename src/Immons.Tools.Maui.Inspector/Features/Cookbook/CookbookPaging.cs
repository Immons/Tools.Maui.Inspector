namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>One section at a time, twenty tiles per page — the device never holds more than that.</summary>
internal static class CookbookPaging
{
    public const int PageSize = 20;

    public static int PageCount(int itemCount) => Math.Max(1, (itemCount + PageSize - 1) / PageSize);

    public static int PageOf(int index) => index / PageSize;

    public static int IndexOnPage(int index) => index % PageSize;

    public static IReadOnlyList<CookbookItem> Slice(IReadOnlyList<CookbookItem> items, int page) =>
        items.Skip(page * PageSize).Take(PageSize).ToList();
}
