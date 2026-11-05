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

    public const int RulesTitleY = 24;

    // 1. Prefix & Suffix (Paired side-by-side)
    public const int PrefixLabelY = 54;
    public const int PrefixInputY = 74;
    public const int InputH = 26;
    public static LayoutRect PrefixRect => new(LeftColX, PrefixInputY, InputColW, InputH);
    public static LayoutRect SuffixRect => new(RightInputX, PrefixInputY, InputColW, InputH);

    // 2. Find and Replace (Paired side-by-side)
    public const int FindLabelY = 110;
    public const int FindInputY = 130;
    public static LayoutRect FindRect => new(LeftColX, FindInputY, InputColW, InputH);
    public static LayoutRect ReplaceRect => new(RightInputX, FindInputY, InputColW, InputH);

    public const int MatchCaseY = 166;
    public static LayoutRect MatchCaseRect => new(LeftColX, MatchCaseY, InputColW, 22);
    public static LayoutRect RegexRect => new(RightInputX, MatchCaseY, InputColW, 22);

    // 3. Numbering section (Paired & Compact)
    public const int SeqCheckY = 198;
    public static LayoutRect SeqCheckRect => new(LeftColX, SeqCheckY, InputColW, 24);
    public static LayoutRect SeqPosToggleRect => new(RightInputX, SeqCheckY, InputColW, 24);

    public const int SeqParamLabelY = 230;
    public const int SeqParamInputY = 250;
    public static LayoutRect SeqStartRect => new(LeftColX, SeqParamInputY, InputColW, InputH);
    public static LayoutRect SeqPadRect => new(RightInputX, SeqParamInputY, InputColW, InputH);

    // 4. Case & Ext transforms (Unified segmented controls)
    public const int CaseLabelY = 288;
    public const int CaseBtnY = 308;
    public static LayoutRect CaseSegmentRect => new(LeftColX, CaseBtnY, LeftColW, 28);

    public const int ExtLabelY = 348;
    public const int ExtBtnY = 368;
    public static LayoutRect ExtSegmentRect => new(LeftColX, ExtBtnY, LeftColW, 28);

    public const int RuleOrderY = 406;

    // Divider
    public const int DividerX = 400;

    // Right Column: Live Preview (Responsive)
    public const int RightColX = 420;
    public const int PreviewTitleY = 24;
    public const int PreviewSummaryY = 52;
    public const int PreviewListY = 80;

    public static int GetRightColW(int clientW) => Math.Max(320, clientW - RightColX - 32);
    public static int GetPreviewListH(int clientH) => Math.Max(240, clientH - PreviewListY - 74);

    public static LayoutRect PreviewListRect(int clientW, int clientH) =>
        new(RightColX, PreviewListY, GetRightColW(clientW), GetPreviewListH(clientH));

    // Bottom Action Bar (Responsive)
    public const int BottomBtnH = 34;

    public static int GetBottomBarY(int clientH) => clientH - 56;
    public static int GetSeparatorY(int clientH) => clientH - 70;

    public static LayoutRect AddFilesRect(int clientH) => new(LeftColX, GetBottomBarY(clientH), 110, BottomBtnH);
    public static LayoutRect ClearRect(int clientH) => new(LeftColX + 120, GetBottomBarY(clientH), 80, BottomBtnH);
    public static LayoutRect CancelRect(int clientH) => new(LeftColX + 210, GetBottomBarY(clientH), 80, BottomBtnH);

    public static LayoutRect ExecuteRect(int clientW, int clientH) =>
        new(clientW - 32 - 140, GetBottomBarY(clientH), 140, BottomBtnH);
}
