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
    public const int DefaultClientWidth = 960;
    public const int DefaultClientHeight = 640;
    public const int ClientWidth = DefaultClientWidth;
    public const int ClientHeight = DefaultClientHeight;

    // Minimum dimensions
    public const int MinClientWidth = 840;
    public const int MinClientHeight = 520;

    // Left Column: Rules (Width: 360, X: 32)
    public const int LeftColX = 32;
    public const int LeftColW = 360;
    public const int InputColW = 172;
    public const int ColGap = 16;
    public const int RightInputX = LeftColX + InputColW + ColGap; // 220

    public const int RulesTitleY = 24;

    // 1. Prefix & Suffix (Paired side-by-side)
    public const int PrefixLabelY = 56;
    public const int PrefixInputY = 76;
    public const int InputH = 28;
    public static LayoutRect PrefixRect => new(LeftColX, PrefixInputY, InputColW, InputH);
    public static LayoutRect SuffixRect => new(RightInputX, PrefixInputY, InputColW, InputH);

    // 2. Find and Replace (Paired side-by-side)
    public const int FindLabelY = 118;
    public const int FindInputY = 138;
    public static LayoutRect FindRect => new(LeftColX, FindInputY, InputColW, InputH);
    public static LayoutRect ReplaceRect => new(RightInputX, FindInputY, InputColW, InputH);

    public const int MatchCaseY = 174;
    public static LayoutRect MatchCaseRect => new(LeftColX, MatchCaseY, InputColW, 22);
    public static LayoutRect RegexRect => new(RightInputX, MatchCaseY, InputColW, 22);

    // 3. Numbering section (Paired & Compact)
    public const int SeqCheckY = 208;
    public static LayoutRect SeqCheckRect => new(LeftColX, SeqCheckY, InputColW, 24);
    public static LayoutRect SeqPosToggleRect => new(RightInputX, SeqCheckY, InputColW, 24);

    public const int SeqParamLabelY = 240;
    public const int SeqParamInputY = 260;
    public static LayoutRect SeqStartRect => new(LeftColX, SeqParamInputY, InputColW, InputH);
    public static LayoutRect SeqPadRect => new(RightInputX, SeqParamInputY, InputColW, InputH);

    // 4. Case & Ext transforms
    public const int CaseLabelY = 302;
    public const int CaseBtnY = 322;
    public const int CaseBtnW = 85;
    public const int CaseBtnGap = 6;
    public static LayoutRect CaseBtnRect(int index) =>
        new(LeftColX + (index * (CaseBtnW + CaseBtnGap)), CaseBtnY, CaseBtnW, 26);

    public const int ExtLabelY = 360;
    public const int ExtBtnY = 380;
    public const int ExtBtnW = 114;
    public const int ExtBtnGap = 9;
    public static LayoutRect ExtBtnRect(int index) =>
        new(LeftColX + (index * (ExtBtnW + ExtBtnGap)), ExtBtnY, ExtBtnW, 26);

    // Divider
    public const int DividerX = 416;

    // Right Column: Live Preview (Responsive)
    public const int RightColX = 440;
    public const int PreviewTitleY = 24;
    public const int PreviewSummaryY = 54;
    public const int PreviewListY = 84;

    public static int GetRightColW(int clientW) => Math.Max(360, clientW - RightColX - 32);
    public static int GetPreviewListH(int clientH) => Math.Max(260, clientH - PreviewListY - 80);

    public static LayoutRect PreviewListRect(int clientW, int clientH) =>
        new(RightColX, PreviewListY, GetRightColW(clientW), GetPreviewListH(clientH));

    // Bottom Action Bar (Responsive)
    public const int BottomBtnH = 34;

    public static int GetBottomBarY(int clientH) => clientH - 56;
    public static int GetSeparatorY(int clientH) => clientH - 70;

    public static LayoutRect AddFilesRect(int clientH) => new(LeftColX, GetBottomBarY(clientH), 120, BottomBtnH);
    public static LayoutRect ClearRect(int clientH) => new(LeftColX + 130, GetBottomBarY(clientH), 90, BottomBtnH);

    public static LayoutRect ExecuteRect(int clientW, int clientH) =>
        new(clientW - 32 - 130 - 100, GetBottomBarY(clientH), 130, BottomBtnH);

    public static LayoutRect CancelRect(int clientW, int clientH) =>
        new(clientW - 32 - 90, GetBottomBarY(clientH), 90, BottomBtnH);
}
