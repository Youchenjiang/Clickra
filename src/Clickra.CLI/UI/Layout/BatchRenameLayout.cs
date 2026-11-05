using System;
using Clickra.UI.Layout;

namespace Clickra.UI.Layout;

/// <summary>
/// Logical-pixel geometry layout for the Batch Rename prompt and interactive preview.
/// Features a compact paired-columns design on the left rules pane to guarantee zero overlap
/// across all window dimensions and DPI scalings.
/// </summary>
public static class BatchRenameLayout
{
    public const int DefaultClientWidth = 820;
    public const int DefaultClientHeight = 500;
    public const int ClientWidth = DefaultClientWidth;
    public const int ClientHeight = DefaultClientHeight;

    // Minimum dimensions
    public const int MinClientWidth = 800;
    public const int MinClientHeight = 490;

    // Left Column: Rules (Width: 352, X: 32)
    public const int LeftColX = 32;
    public const int LeftColW = 352;
    public const int InputColW = 168;
    public const int ColGap = 16;
    public const int RightInputX = LeftColX + InputColW + ColGap; // 216

    public const int RulesTitleY = 20;
    public const int RuleOrderY = 44;

    // 1. Prefix & Suffix (Paired side-by-side)
    public const int PrefixLabelY = 68;
    public const int PrefixInputY = 88;
    public const int InputH = 26;
    public static LayoutRect PrefixRect => new(LeftColX, PrefixInputY, InputColW, InputH);
    public static LayoutRect SuffixRect => new(RightInputX, PrefixInputY, InputColW, InputH);

    // 2. Find and Replace (Paired side-by-side)
    public const int FindLabelY = 122;
    public const int FindInputY = 142;
    public static LayoutRect FindRect => new(LeftColX, FindInputY, InputColW, InputH);
    public static LayoutRect ReplaceRect => new(RightInputX, FindInputY, InputColW, InputH);

    // Checkboxes tightly nested under Find & Replace
    public const int MatchCaseY = 172;
    public static LayoutRect MatchCaseRect => new(LeftColX, MatchCaseY, InputColW, 20);
    public static LayoutRect RegexRect => new(RightInputX, MatchCaseY, InputColW, 20);

    // 3. Numbering section (Paired & Compact)
    public const int SeqCheckY = 206;
    public static LayoutRect SeqCheckRect => new(LeftColX, SeqCheckY, InputColW, 24);
    public static LayoutRect SeqPosToggleRect => new(RightInputX, SeqCheckY, InputColW, 24);

    public const int SeqParamLabelY = 236;
    public const int SeqParamInputY = 254;
    public static LayoutRect SeqStartRect => new(LeftColX, SeqParamInputY, InputColW, InputH);
    public static LayoutRect SeqPadRect => new(RightInputX, SeqParamInputY, InputColW, InputH);

    // 4. Case & Ext transforms (Unified segmented controls)
    public const int CaseLabelY = 292;
    public const int CaseBtnY = 312;
    public static LayoutRect CaseSegmentRect => new(LeftColX, CaseBtnY, LeftColW, 28);

    public const int ExtLabelY = 350;
    public const int ExtBtnY = 370;
    public static LayoutRect ExtSegmentRect => new(LeftColX, ExtBtnY, LeftColW, 28);

    // Divider
    public const int DividerX = 400;

    // Right Column: Live Preview (Responsive)
    public const int RightColX = 420;
    public const int PreviewTitleY = 20;
    public const int PreviewSummaryY = 46;
    public const int PreviewListY = 74;

    public static int GetRightColW(int clientW) => Math.Max(320, clientW - RightColX - 32);
    public static int GetPreviewListH(int clientH) => Math.Max(240, clientH - PreviewListY - 74);

    public static LayoutRect PreviewListRect(int clientW, int clientH) =>
        new(RightColX, PreviewListY, GetRightColW(clientW), GetPreviewListH(clientH));

    // Bottom Action Bar (Responsive)
    public const int BottomBtnH = 34;

    public static int GetBottomBarY(int clientH) => clientH - 56;
    public static int GetSeparatorY(int clientH) => clientH - 70;

    public static LayoutRect AddFilesRect(int clientH) => new(LeftColX, GetBottomBarY(clientH), 110, BottomBtnH);
    public static LayoutRect ClearRect(int clientH) => new(LeftColX + 125, GetBottomBarY(clientH), 80, BottomBtnH);
    public static LayoutRect CancelRect(int clientH) => new(LeftColX + 220, GetBottomBarY(clientH), 80, BottomBtnH);

    public static LayoutRect ExecuteRect(int clientW, int clientH) =>
        new(clientW - 32 - 140, GetBottomBarY(clientH), 140, BottomBtnH);
}
