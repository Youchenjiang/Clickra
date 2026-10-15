namespace Clickra.UI.Layout;

/// <summary>Bounds eager and on-demand PDF preview bitmap retention.</summary>
public static class VisualSplitThumbnailPolicy
{
    public const int PreloadedPages = 20;
    public const int DynamicCacheLimit = 12;

    public static bool NeedsRender(int page, int totalPages, bool cached, bool pending) =>
        page >= 1 && page <= totalPages && !cached && !pending;
}
