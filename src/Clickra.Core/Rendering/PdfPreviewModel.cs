using System;

namespace Clickra.Core.Rendering;

/// <summary>
/// Pure state model managing PDF page navigation, zoom calculation, and viewport sizing.
/// Shared by visual splitters, print preview dialogs, and standalone PDF viewers.
/// </summary>
public class PdfPreviewModel
{
    public const float ZoomMinFactor = 1f;
    public const float ZoomMaxFactor = 8f;
    public const float ZoomStepFactor = 1.25f;

    public PdfPreviewModel(int totalPages, int initialPage = 1)
    {
        TotalPages = Math.Max(1, totalPages);
        CurrentPageNumber = Math.Clamp(initialPage, 1, TotalPages);
    }

    public int TotalPages { get; }

    public int CurrentPageNumber { get; protected set; }

    public int CurrentPageIndex => CurrentPageNumber - 1;

    public bool CanNavigatePrevious => CurrentPageNumber > 1;

    public bool CanNavigateNext => CurrentPageNumber < TotalPages;

    public virtual bool Navigate(int delta)
    {
        int next = Math.Clamp(CurrentPageNumber + delta, 1, TotalPages);
        if (next == CurrentPageNumber) return false;

        CurrentPageNumber = next;
        return true;
    }

    public virtual bool GoToPage(int pageNumber)
    {
        int next = Math.Clamp(pageNumber, 1, TotalPages);
        if (next == CurrentPageNumber) return false;

        CurrentPageNumber = next;
        return true;
    }

    public static float ClampZoomFactor(float factor) =>
        Math.Clamp(factor, ZoomMinFactor, ZoomMaxFactor);

    public static float ZoomFactorAfter(float factor, int direction) =>
        ClampZoomFactor(factor * (direction >= 0 ? ZoomStepFactor : 1f / ZoomStepFactor));

    public static int ZoomPercent(float factor) =>
        (int)Math.Round(ClampZoomFactor(factor) * 100f);

    public static int ZoomRenderWidth(float factor, int baseWidth, int minWidth, int maxWidth) =>
        (int)Math.Clamp(baseWidth * ClampZoomFactor(factor), minWidth, maxWidth);
}
