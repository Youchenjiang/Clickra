using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clickra.Core;
using Clickra.Core.Application;
using Clickra.Core.Modules.BatchRename.Application;
using Clickra.Core.Modules.BatchRename.Domain;
using Clickra.Core.Modules.BatchRename.Processing;
using Clickra.UI.Layout;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI;

public partial class ProgressWindow
{
    private volatile bool _isPromptingBatchRename = false;
    private readonly List<string> _brFiles = new();
    private BatchRenamePlan? _brCurrentPlan;
    private int _brScrollOffset = 0;
    private int _brHoveredIndex = -1;

    // Responsive dimensions
    private int _brClientWidth = BatchRenameLayout.DefaultClientWidth;
    private int _brClientHeight = BatchRenameLayout.DefaultClientHeight;

    // Rules state
    private bool _brEnableSeq = true;
    private InsertPosition _brSeqPos = InsertPosition.Suffix;
    private CaseTransformMode _brCaseMode = CaseTransformMode.None;
    private ExtensionTransformMode _brExtMode = ExtensionTransformMode.KeepOriginal;
    private bool _brMatchCase = false;
    private bool _brUseRegex = false;
    private bool _brAdvancedExpanded = false;

    // Edit controls HWND
    private IntPtr _hwndBrPrefix = IntPtr.Zero;
    private IntPtr _hwndBrSuffix = IntPtr.Zero;
    private IntPtr _hwndBrSeqStart = IntPtr.Zero;
    private IntPtr _hwndBrSeqPad = IntPtr.Zero;
    private IntPtr _hwndBrFind = IntPtr.Zero;
    private IntPtr _hwndBrReplace = IntPtr.Zero;
    private IntPtr _hwndBrFocused = IntPtr.Zero;
    private static readonly string[] _brConsumedKeys = { "batch_rename_status_ok" };

    private void InitializeBatchRename()
    {
        _brFiles.Clear();
        if (_files != null)
        {
            _brFiles.AddRange(BatchRenameUseCase.ResolveTargetFiles(_files));
        }
        _brScrollOffset = 0;
        _brClientWidth = BatchRenameLayout.DefaultClientWidth;
        _brClientHeight = BatchRenameLayout.DefaultClientHeight;
        UpdateBatchRenamePlan();
    }

    private void CreateBatchRenameEditControls(IntPtr hwnd)
    {
        if (_hwndBrPrefix != IntPtr.Zero) return;

        float s = _dpiScale;
        IntPtr font = _hFont;

        _hwndBrPrefix = ModernControls.CreateSingleEdit(hwnd, BatchRenameLayout.PrefixRect, "", font, s);
        ModernControls.SetCueBanner(_hwndBrPrefix, Loc("batch_rename_cue_prefix"));
        _hwndBrSuffix = ModernControls.CreateSingleEdit(hwnd, BatchRenameLayout.SuffixRect, "", font, s);
        ModernControls.SetCueBanner(_hwndBrSuffix, Loc("batch_rename_cue_suffix"));
        _hwndBrSeqStart = ModernControls.CreateSingleEdit(hwnd, BatchRenameLayout.SeqStartRect, "1", font, s);
        int suggestedPad = Math.Max(2, (int)Math.Ceiling(Math.Log10(Math.Max(1, _brFiles.Count + 1))));
        _hwndBrSeqPad = ModernControls.CreateSingleEdit(hwnd, BatchRenameLayout.SeqPadRect, suggestedPad.ToString(), font, s);
        UpdateSequenceControlsEnabled();
        _hwndBrFind = ModernControls.CreateSingleEdit(hwnd, BatchRenameLayout.FindRect, "", font, s);
        ModernControls.SetCueBanner(_hwndBrFind, Loc("batch_rename_cue_find"));
        _hwndBrReplace = ModernControls.CreateSingleEdit(hwnd, BatchRenameLayout.ReplaceRect, "", font, s);
        ModernControls.SetCueBanner(_hwndBrReplace, Loc("batch_rename_cue_replace"));

        UpdateBatchRenamePlan();
    }


    private void UpdateSequenceControlsEnabled()
    {
        if (_hwndBrSeqStart != IntPtr.Zero)
            EnableWindow(_hwndBrSeqStart, _brEnableSeq);
        if (_hwndBrSeqPad != IntPtr.Zero)
            EnableWindow(_hwndBrSeqPad, _brEnableSeq);
    }

    private void DestroyOrHideBatchRenameControls()
    {
        IntPtr[] edits = { _hwndBrPrefix, _hwndBrSuffix, _hwndBrSeqStart, _hwndBrSeqPad, _hwndBrFind, _hwndBrReplace };
        foreach (var h in edits)
        {
            if (h != IntPtr.Zero)
            {
                ShowWindow(h, 0); // SW_HIDE
                DestroyWindow(h);
            }
        }
        _hwndBrPrefix = IntPtr.Zero;
        _hwndBrSuffix = IntPtr.Zero;
        _hwndBrSeqStart = IntPtr.Zero;
        _hwndBrSeqPad = IntPtr.Zero;
        _hwndBrFind = IntPtr.Zero;
        _hwndBrReplace = IntPtr.Zero;
        _hwndBrFocused = IntPtr.Zero;
    }

    private string GetEditText(IntPtr edit)
    {
        if (edit == IntPtr.Zero) return "";
        int len = GetWindowTextLengthW(edit);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowTextW(edit, sb, sb.Capacity);
        return sb.ToString();
    }

    private BatchRenameOptions BuildBatchRenameOptions()
    {
        string prefix = GetEditText(_hwndBrPrefix);
        string suffix = GetEditText(_hwndBrSuffix);
        string find = GetEditText(_hwndBrFind);
        string replace = GetEditText(_hwndBrReplace);

        NumberingOptions? num = null;
        if (_brEnableSeq)
        {
            int.TryParse(GetEditText(_hwndBrSeqStart), out int start);
            int.TryParse(GetEditText(_hwndBrSeqPad), out int pad);
            if (start < 0) start = 1;
            if (pad < 0) pad = 2;
            num = new NumberingOptions(start, 1, pad, _brSeqPos, "_");
        }

        ReplaceOptions? rep = null;
        if (!string.IsNullOrEmpty(find))
        {
            rep = new ReplaceOptions(find, replace, !_brMatchCase, _brUseRegex);
        }

        return new BatchRenameOptions(
            string.IsNullOrEmpty(prefix) ? null : prefix,
            string.IsNullOrEmpty(suffix) ? null : suffix,
            num,
            rep,
            null,
            _brCaseMode,
            _brExtMode);
    }

    private void UpdateBatchRenamePlan()
    {
        var options = BuildBatchRenameOptions();
        _brCurrentPlan = BatchRenameEngine.GeneratePlan(_brFiles, options);
    }

    internal void HandleBatchRenameSize(IntPtr hwnd, int pxW, int pxH)
    {
        if (!_isPromptingBatchRename) return;

        float s = _dpiScale;
        // Keep exact logical client dimension so footer bar aligns strictly with actual visible bottom
        _brClientWidth = (int)(pxW / s);
        _brClientHeight = (int)(pxH / s);

        _bufferGraphics?.Dispose();
        _bufferBmp?.Dispose();
        _bufferBmp = new Bitmap(pxW, pxH);
        _bufferGraphics = Graphics.FromImage(_bufferBmp);
        _bufferGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        _bufferGraphics.SmoothingMode = SmoothingMode.AntiAlias;

        InvalidateRect(hwnd, IntPtr.Zero, false);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    internal void HandleBatchRenameGetMinMaxInfo(IntPtr lParam)
    {
        if (!_isPromptingBatchRename) return;
        try
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            float s = _dpiScale;

            // Note: ptMinTrackSize is WINDOW size (including caption, menu and borders), not client size!
            // Calculate minimum window rectangle that guarantees sufficient client height
            var rect = new RECT
            {
                left = 0,
                top = 0,
                right = (int)(BatchRenameLayout.MinClientWidth * s),
                bottom = (int)(BatchRenameLayout.MinClientHeight * s)
            };
            AdjustWindowRectEx(ref rect, WS_OVERLAPPEDWINDOW, false, 0);

            mmi.ptMinTrackSize.x = rect.right - rect.left;
            mmi.ptMinTrackSize.y = rect.bottom - rect.top;
            Marshal.StructureToPtr(mmi, lParam, false);
        }
        catch
        {
            // Ignore marshalling fallback
        }
    }

    internal void InvalidateBatchRenamePreview(IntPtr hwnd)
    {
        float s = _dpiScale;
        var rect = new RECT
        {
            left = (int)(BatchRenameLayout.DividerX * s),
            top = 0,
            right = (int)(_brClientWidth * s),
            bottom = (int)(_brClientHeight * s)
        };
        IntPtr lpRect = Marshal.AllocHGlobal(Marshal.SizeOf<RECT>());
        Marshal.StructureToPtr(rect, lpRect, false);
        InvalidateRect(hwnd, lpRect, false);
        Marshal.FreeHGlobal(lpRect);
    }

    private void ResizeWindowForBatchRename(IntPtr hwnd, bool expand)
    {
        float s = _dpiScale;
        int clientW = (int)((expand ? _brClientWidth : 520) * s);
        int clientH = (int)((expand ? _brClientHeight : 280) * s);

        if (!expand)
        {
            // Switch back to fixed progress window style
            SetWindowLongPtr(hwnd, -16, (IntPtr)WS_OVERLAPPED_FIXED); // -16 = GWL_STYLE
        }

        ResizeClientSurface(hwnd, clientW, clientH);

        if (expand && GetWindowRect(hwnd, out RECT curRect))
        {
            int winW = curRect.right - curRect.left;
            int winH = curRect.bottom - curRect.top;
            int screenW = GetSystemMetrics(0);
            int screenH = GetSystemMetrics(1);
            int x = Math.Max(0, (screenW - winW) / 2);
            int y = Math.Max(0, (screenH - winH) / 2);
            SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004); // SWP_NOSIZE | SWP_NOZORDER
        }
    }

    private static RectangleF ScaleBatchRenameRect(LayoutRect rect, float scale) =>
        new(rect.X * scale, rect.Y * scale, rect.Width * scale, rect.Height * scale);

    private void PaintBatchRenamePrompt(Graphics g, float s)
    {
        if (_titleFont is null || _msgFont is null || _tipFont is null) return;

        int clientW = _brClientWidth;
        int clientH = _brClientHeight;

        using var bodyBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
        using var mutedBrush = new SolidBrush(Color.FromArgb(180, 180, 180));
        using var sectionFont = new Font(_msgFont.FontFamily, 16f * s, FontStyle.Bold, GraphicsUnit.Pixel);
        using var smallFont = new Font(_msgFont.FontFamily, 12f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        using var badgeFont = new Font("Segoe UI", 11f * s, FontStyle.Bold, GraphicsUnit.Pixel);

        Color accentColor = UIHelper.GetSystemColorizationColor();

        // --- Left Column: Rules ---
        g.DrawString(Loc("batch_rename_rules"), sectionFont, Brushes.White, 32 * s, BatchRenameLayout.RulesTitleY * s);
        using var hintBrush = new SolidBrush(Color.FromArgb(140, 140, 150));
        using var hintFont = new Font(_msgFont.FontFamily, 11f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        g.DrawString(Loc("batch_rename_rule_order"), hintFont, hintBrush, 32 * s, BatchRenameLayout.RuleOrderY * s);

        // 1. Prefix & Suffix (Paired side-by-side)
        g.DrawString(Loc("batch_rename_prefix"), smallFont, mutedBrush, 32 * s, BatchRenameLayout.PrefixLabelY * s);
        ModernControls.DrawTextBoxContainer(g, s, BatchRenameLayout.PrefixRect, _hwndBrPrefix, _hwndBrFocused, accentColor);

        g.DrawString(Loc("batch_rename_suffix"), smallFont, mutedBrush, BatchRenameLayout.RightInputX * s, BatchRenameLayout.PrefixLabelY * s);
        ModernControls.DrawTextBoxContainer(g, s, BatchRenameLayout.SuffixRect, _hwndBrSuffix, _hwndBrFocused, accentColor);

        // 2. Find & Replace (Paired side-by-side)
        g.DrawString(Loc("batch_rename_find"), smallFont, mutedBrush, 32 * s, BatchRenameLayout.FindLabelY * s);
        ModernControls.DrawTextBoxContainer(g, s, BatchRenameLayout.FindRect, _hwndBrFind, _hwndBrFocused, accentColor);

        g.DrawString(Loc("batch_rename_replace"), smallFont, mutedBrush, BatchRenameLayout.RightInputX * s, BatchRenameLayout.FindLabelY * s);
        ModernControls.DrawTextBoxContainer(g, s, BatchRenameLayout.ReplaceRect, _hwndBrReplace, _hwndBrFocused, accentColor);

        ModernControls.DrawCheckBox(g, s, BatchRenameLayout.MatchCaseRect, _brMatchCase, Loc("batch_rename_match_case"), smallFont, bodyBrush, accentColor);
        ModernControls.DrawCheckBox(g, s, BatchRenameLayout.RegexRect, _brUseRegex, Loc("batch_rename_regex"), smallFont, bodyBrush, accentColor);

        // 3. Sequence Numbering (Paired & Compact with disabled state)
        ModernControls.DrawCheckBox(g, s, BatchRenameLayout.SeqCheckRect, _brEnableSeq, Loc("batch_rename_numbering"), smallFont, bodyBrush, accentColor);
        ModernControls.DrawSegmentedToggle(g, s, BatchRenameLayout.SeqPosToggleRect, Loc("batch_rename_pos_prefix"), Loc("batch_rename_pos_suffix"), _brSeqPos == InsertPosition.Prefix, smallFont, accentColor, _brEnableSeq);

        using var seqLabelBrush = new SolidBrush(_brEnableSeq ? Color.FromArgb(145, 145, 145) : Color.FromArgb(85, 85, 85));
        g.DrawString(Loc("batch_rename_start_index"), smallFont, seqLabelBrush, 32 * s, BatchRenameLayout.SeqParamLabelY * s);
        ModernControls.DrawTextBoxContainer(g, s, BatchRenameLayout.SeqStartRect, _hwndBrSeqStart, _hwndBrFocused, accentColor, _brEnableSeq);

        g.DrawString(Loc("batch_rename_padding"), smallFont, seqLabelBrush, BatchRenameLayout.RightInputX * s, BatchRenameLayout.SeqParamLabelY * s);
        ModernControls.DrawTextBoxContainer(g, s, BatchRenameLayout.SeqPadRect, _hwndBrSeqPad, _hwndBrFocused, accentColor, _brEnableSeq);

        // 4. Collapsible Advanced Options
        bool hasActiveAdvanced = _brCaseMode != CaseTransformMode.None || _brExtMode != ExtensionTransformMode.KeepOriginal;
        ModernControls.DrawCollapsibleHeader(g, s, BatchRenameLayout.AdvancedToggleRect, Loc("batch_rename_advanced"), _brAdvancedExpanded, hasActiveAdvanced, Loc("batch_rename_advanced_active"), smallFont, accentColor);

        if (_brAdvancedExpanded)
        {
            g.DrawString(Loc("batch_rename_case_mode"), smallFont, mutedBrush, 32 * s, BatchRenameLayout.CaseLabelY * s);
            string[] caseLabels = { Loc("batch_rename_case_none"), Loc("batch_rename_case_lower"), Loc("batch_rename_case_upper"), Loc("batch_rename_case_title") };
            ModernControls.DrawMultiSegmentedToggle(g, s, BatchRenameLayout.CaseSegmentRect, caseLabels, (int)_brCaseMode, smallFont, accentColor);

            g.DrawString(Loc("batch_rename_ext_mode"), smallFont, mutedBrush, 32 * s, BatchRenameLayout.ExtLabelY * s);
            string[] extLabels = { Loc("batch_rename_ext_keep"), Loc("batch_rename_ext_lower"), Loc("batch_rename_ext_upper") };
            ModernControls.DrawMultiSegmentedToggle(g, s, BatchRenameLayout.ExtSegmentRect, extLabels, (int)_brExtMode, smallFont, accentColor);
        }



        // --- Divider between columns ---
        using var divPen = new Pen(Color.FromArgb(48, 48, 48), 1f * s);
        g.DrawLine(divPen, BatchRenameLayout.DividerX * s, 20 * s, BatchRenameLayout.DividerX * s, (clientH - 80) * s);

        // --- Right Column: Live Preview (Responsive) ---
        g.DrawString(Loc("batch_rename_preview"), sectionFont, Brushes.White, BatchRenameLayout.RightColX * s, BatchRenameLayout.PreviewTitleY * s);

        int totalCount = _brFiles.Count;
        int renameCount = _brCurrentPlan?.Items.Count(i => i.OriginalName != i.NewName) ?? 0;
        int conflictCount = _brCurrentPlan?.ConflictCount ?? 0;
        string summaryText = string.Format(Loc("batch_rename_summary"), totalCount, renameCount, conflictCount);

        string[] summaryParts = summaryText.Split('|');
        if (summaryParts.Length == 3)
        {
            string basePart = summaryParts[0].Trim() + " | " + summaryParts[1].Trim() + " | ";
            string conflictPart = summaryParts[2].Trim();
            float curX = BatchRenameLayout.RightColX * s;
            float curY = BatchRenameLayout.PreviewSummaryY * s;
            g.DrawString(basePart, smallFont, mutedBrush, curX, curY);

            var baseSize = g.MeasureString(basePart, smallFont);
            using var conflictBrush = new SolidBrush(conflictCount > 0 ? Color.FromArgb(255, 95, 95) : Color.FromArgb(160, 160, 160));
            using var conflictFont = new Font(smallFont.FontFamily, smallFont.Size, conflictCount > 0 ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            g.DrawString(conflictPart, conflictFont, conflictBrush, curX + baseSize.Width - (4 * s), curY);
        }
        else
        {
            g.DrawString(summaryText, smallFont, mutedBrush, BatchRenameLayout.RightColX * s, BatchRenameLayout.PreviewSummaryY * s);
        }

        // Preview list cards
        var previewLayoutRect = BatchRenameLayout.PreviewListRect(clientW, clientH);
        var listRect = ScaleBatchRenameRect(previewLayoutRect, s);
        var oldClip = g.Clip;
        g.SetClip(listRect);

        if (_brCurrentPlan != null && _brCurrentPlan.Items.Count > 0)
        {
            float rowH = 38 * s;
            float startY = listRect.Y - (_brScrollOffset * s);

            for (int i = 0; i < _brCurrentPlan.Items.Count; i++)
            {
                var item = _brCurrentPlan.Items[i];
                float y = startY + (i * rowH);
                if (y + rowH < listRect.Y || y > listRect.Bottom) continue;

                var cardRect = new RectangleF(listRect.X, y, listRect.Width, rowH - (4 * s));

                bool willChange = item.OriginalName != item.NewName;
                bool hasConflict = item.Status is RenameItemStatus.ConflictExistingFile or RenameItemStatus.ConflictDuplicateTarget;
                bool isCyclic = !hasConflict && _brCurrentPlan.RequiresTwoPhaseSwap;
                bool hasBadge = hasConflict || isCyclic;

                // Card styling based on change status:
                // Conflict: dark reddish tint with red border
                // Changed: distinct modern card with subtle border
                // Unchanged: dimmed card with minimal contrast
                Color cardBgColor = hasConflict
                    ? Color.FromArgb(46, 22, 22)
                    : (willChange ? Color.FromArgb(36, 36, 40) : Color.FromArgb(24, 24, 26));

                using var cardBg = new SolidBrush(cardBgColor);
                using var cardPath = UIHelper.GetRoundedRectPath(cardRect, 4 * s);
                g.FillPath(cardBg, cardPath);

                if (hasConflict)
                {
                    using var conflictBorder = new Pen(Color.FromArgb(160, 42, 42), 1f * s);
                    g.DrawPath(conflictBorder, cardPath);
                }
                else if (i == _brHoveredIndex)
                {
                    using var hoverBorder = new Pen(UIHelper.Lighten(accentColor, 0.25f), 1.2f * s);
                    g.DrawPath(hoverBorder, cardPath);
                }
                else if (willChange)
                {
                    using var cardBorder = new Pen(Color.FromArgb(56, 56, 62), 1f * s);
                    g.DrawPath(cardBorder, cardPath);
                }

                // Text positions
                float textY = y + (8 * s);
                float leftX = cardRect.X + (12 * s);
                float rightReserved = hasBadge ? (88 * s) : (16 * s);
                float availableW = cardRect.Width - (44 * s) - rightReserved;
                float maxNameW = Math.Max(120 * s, availableW / 2f);

                using var sfTypo = new StringFormat(StringFormat.GenericTypographic)
                {
                    FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces
                };

                // Original Name (middle truncated if long, dimmed if unchanged, red if conflict)
                Color origColor = hasConflict
                    ? Color.FromArgb(240, 130, 130)
                    : (willChange ? Color.FromArgb(180, 180, 180) : Color.FromArgb(115, 115, 115));
                using var origBrush = new SolidBrush(origColor);
                string origComp = ModernControls.CompactMiddle(item.OriginalName, smallFont, maxNameW, g, sfTypo);
                g.DrawString(origComp, smallFont, origBrush, leftX, textY, sfTypo);

                // Direction Arrow (accent blue if changed, muted gray if unchanged, red if conflict)
                float arrowX = leftX + maxNameW + (8 * s);
                Color arrowColor = hasConflict
                    ? Color.FromArgb(240, 80, 80)
                    : (willChange ? Color.FromArgb(0, 160, 240) : Color.FromArgb(75, 75, 75));
                using var arrowBrush = new SolidBrush(arrowColor);
                g.DrawString("→", smallFont, arrowBrush, arrowX, textY, sfTypo);

                // New Name with Diff Highlight
                float newX = arrowX + (22 * s);
                DrawPreviewItemNewName(g, s, item.OriginalName, item.NewName, smallFont, newX, textY, maxNameW, hasConflict, sfTypo);

                // Badge: Only displayed on Conflict or Cyclic Swap (clean view for normal items)
                if (hasBadge)
                {
                    string badgeText = hasConflict ? Loc("batch_rename_status_conflict") : Loc("batch_rename_status_cyclic");
                    Color badgeBgColor = hasConflict ? Color.FromArgb(196, 43, 28) : Color.FromArgb(216, 110, 0);

                    float badgeW = 68 * s;
                    float badgeH = 22 * s;
                    float badgeX = cardRect.Right - badgeW - (8 * s);
                    float badgeY = y + (5 * s);
                    var badgeRect = new RectangleF(badgeX, badgeY, badgeW, badgeH);

                    using var badgeBg = new SolidBrush(badgeBgColor);
                    using var badgePath = UIHelper.GetRoundedRectPath(badgeRect, 3 * s);
                    g.FillPath(badgeBg, badgePath);

                    using var badgeSf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(badgeText, badgeFont, Brushes.White, badgeRect, badgeSf);
                }
            }
        }
        else
        {
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(Loc("batch_rename_drag_hint"), _msgFont, mutedBrush, listRect, sf);
        }

        if (_brCurrentPlan != null && _brCurrentPlan.Items.Count > 0 && _brCurrentPlan.Items.Count <= 2)
        {
            int itemH = 38;
            float hintY = listRect.Y + (_brCurrentPlan.Items.Count * itemH * s) + (16 * s);
            float hintH = Math.Min(56 * s, listRect.Bottom - hintY - (8 * s));
            if (hintH > 24 * s)
            {
                var hintRect = new RectangleF(listRect.X + (8 * s), hintY, listRect.Width - (16 * s), hintH);
                using var hintDottedPen = new Pen(Color.FromArgb(60, 60, 70), 1f * s) { DashStyle = DashStyle.Dash };
                using var hintPath = UIHelper.GetRoundedRectPath(hintRect, 6 * s);
                g.DrawPath(hintDottedPen, hintPath);
                using var sfHint = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using var hintTxtBrush = new SolidBrush(Color.FromArgb(135, 135, 145));
                g.DrawString(Loc("batch_rename_drag_hint"), smallFont, hintTxtBrush, hintRect, sfHint);
            }
        }

        g.Clip = oldClip;

        // Bottom separator
        int sepY = BatchRenameLayout.GetSeparatorY(clientH);
        g.DrawLine(divPen, 32 * s, sepY * s, (clientW - 32) * s, sepY * s);

        // Bottom Action buttons (Dismissive/utility grouped on left, primary execution uniquely anchored on right)
        ModernControls.DrawButton(g, s, BatchRenameLayout.AddFilesRect(clientH), Loc("batch_rename_add_files"), _msgFont, false);
        ModernControls.DrawButton(g, s, BatchRenameLayout.ClearRect(clientH), Loc("batch_rename_clear"), _msgFont, false, enabled: _brFiles.Count > 0, isDestructive: true);
        ModernControls.DrawButton(g, s, BatchRenameLayout.CancelRect(clientH), Loc("batch_rename_cancel"), _msgFont, false);

        bool canExecute = _brFiles.Count > 0 && (_brCurrentPlan?.Items.Count(i => i.OriginalName != i.NewName) ?? 0) > 0;
        ModernControls.DrawButton(g, s, BatchRenameLayout.ExecuteRect(clientW, clientH), Loc("batch_rename_execute"), _msgFont, true, canExecute);

        // Draw floating hover popover on top of all elements
        DrawBatchRenameHoverTooltip(g, s, clientW, clientH);
    }

    /// <summary>
    /// Draws the preview target name with intelligent diff highlighting:
    /// - Unchanged parts are rendered in subtle gray.
    /// - Changed / added / sequence fragments are highlighted in vivid gold with an amber glow pill.
    /// - Long names have their unchanged prefix middle-compacted so the diff and extension remain 100% visible.
    /// </summary>
    private void DrawPreviewItemNewName(Graphics g, float s, string orig, string newN, Font font, float x, float y, float maxW, bool hasConflict, StringFormat sf)
    {
        if (orig == newN)
        {
            using var dimBrush = new SolidBrush(Color.FromArgb(115, 115, 115));
            string compact = ModernControls.CompactMiddle(newN, font, maxW, g, sf);
            g.DrawString(compact, font, dimBrush, x, y, sf);
            return;
        }

        if (hasConflict)
        {
            using var errBrush = new SolidBrush(Color.FromArgb(255, 110, 110));
            string compact = ModernControls.CompactMiddle(newN, font, maxW, g, sf);
            g.DrawString(compact, font, errBrush, x, y, sf);
            return;
        }

        // Compute longest common prefix and suffix to isolate the modified diff fragment
        int p = 0;
        while (p < orig.Length && p < newN.Length && orig[p] == newN[p]) p++;

        int sLen = 0;
        while (sLen < (orig.Length - p) && sLen < (newN.Length - p) && orig[orig.Length - 1 - sLen] == newN[newN.Length - 1 - sLen]) sLen++;

        string pre = newN.Substring(0, p);
        string diff = newN.Substring(p, newN.Length - p - sLen);
        string post = newN.Substring(newN.Length - sLen);

        using var normalBrush = new SolidBrush(Color.FromArgb(175, 175, 175));
        using var diffBrush = new SolidBrush(Color.FromArgb(255, 215, 64)); // Vivid Gold

        if (string.IsNullOrEmpty(diff))
        {
            // Deletion: draw new name in accent gold so the change is evident
            string compact = ModernControls.CompactMiddle(newN, font, maxW, g, sf);
            g.DrawString(compact, font, diffBrush, x, y, sf);
            return;
        }

        float wDiff = g.MeasureString(diff, font, 10000, sf).Width;
        float wPost = g.MeasureString(post, font, 10000, sf).Width;
        float wPre = g.MeasureString(pre, font, 10000, sf).Width;

        if (wPre + wDiff + wPost > maxW)
        {
            // Truncate unchanged portions while preserving diff and extension
            if (wPre > wPost && (maxW - wDiff - wPost) > (24 * s))
            {
                float availForPre = maxW - wDiff - wPost;
                pre = ModernControls.CompactMiddle(pre, font, availForPre, g, sf);
                wPre = g.MeasureString(pre, font, 10000, sf).Width;
            }
            else if ((maxW - wPre - wDiff) > (24 * s))
            {
                float availForPost = maxW - wPre - wDiff;
                post = ModernControls.CompactMiddle(post, font, availForPost, g, sf);
                wPost = g.MeasureString(post, font, 10000, sf).Width;
            }
            else
            {
                string compact = ModernControls.CompactMiddle(newN, font, maxW, g, sf);
                g.DrawString(compact, font, diffBrush, x, y, sf);
                return;
            }
        }

        float curX = x;
        if (!string.IsNullOrEmpty(pre))
        {
            g.DrawString(pre, font, normalBrush, curX, y, sf);
            curX += wPre;
        }

        if (!string.IsNullOrEmpty(diff))
        {
            var diffBgRect = new RectangleF(curX - (1 * s), y, wDiff + (2 * s), font.GetHeight(g));
            using var diffBg = new SolidBrush(Color.FromArgb(50, 255, 215, 64));
            using var diffPath = UIHelper.GetRoundedRectPath(diffBgRect, 2 * s);
            g.FillPath(diffBg, diffPath);

            g.DrawString(diff, font, diffBrush, curX, y, sf);
            curX += wDiff;
        }

        if (!string.IsNullOrEmpty(post))
        {
            g.DrawString(post, font, normalBrush, curX, y, sf);
        }
    }

    private bool HandleBatchRenameLButtonDown(IntPtr hwnd, int x, int y)
    {
        if (!_isPromptingBatchRename) return false;

        float s = _dpiScale;
        int mouseX = (int)(x / s);
        int mouseY = (int)(y / s);

        int clientW = _brClientWidth;
        int clientH = _brClientHeight;

        // Check Sequence toggle (box or label)
        if (BatchRenameLayout.SeqCheckRect.ContainsInclusive(mouseX, mouseY))
        {
            _brEnableSeq = !_brEnableSeq;
            UpdateSequenceControlsEnabled();
            UpdateBatchRenamePlan();
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return true;
        }

        // Sequence position segmented toggle (Left: Prefix, Right: Suffix) - only active when enabled
        if (_brEnableSeq && BatchRenameLayout.SeqPosToggleRect.ContainsInclusive(mouseX, mouseY))
        {
            int midX = BatchRenameLayout.SeqPosToggleRect.X + (BatchRenameLayout.SeqPosToggleRect.Width / 2);
            _brSeqPos = mouseX < midX ? InsertPosition.Prefix : InsertPosition.Suffix;
            UpdateBatchRenamePlan();
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return true;
        }

        // Match case toggle
        if (BatchRenameLayout.MatchCaseRect.ContainsInclusive(mouseX, mouseY))
        {
            _brMatchCase = !_brMatchCase;
            UpdateBatchRenamePlan();
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return true;
        }

        // Regex toggle
        if (BatchRenameLayout.RegexRect.ContainsInclusive(mouseX, mouseY))
        {
            _brUseRegex = !_brUseRegex;
            UpdateBatchRenamePlan();
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return true;
        }

        // Collapsible Advanced toggle
        if (BatchRenameLayout.AdvancedToggleRect.ContainsInclusive(mouseX, mouseY))
        {
            _brAdvancedExpanded = !_brAdvancedExpanded;
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return true;
        }

        if (_brAdvancedExpanded)
        {
            // Case mode segmented toggle
            if (BatchRenameLayout.CaseSegmentRect.ContainsInclusive(mouseX, mouseY))
            {
                float segW = BatchRenameLayout.LeftColW / 4f;
                int idx = (int)((mouseX - BatchRenameLayout.LeftColX) / segW);
                if (idx >= 0 && idx < 4)
                {
                    _brCaseMode = (CaseTransformMode)idx;
                    UpdateBatchRenamePlan();
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                    return true;
                }
            }

            // Extension mode segmented toggle
            if (BatchRenameLayout.ExtSegmentRect.ContainsInclusive(mouseX, mouseY))
            {
                float segW = BatchRenameLayout.LeftColW / 3f;
                int idx = (int)((mouseX - BatchRenameLayout.LeftColX) / segW);
                if (idx >= 0 && idx < 3)
                {
                    _brExtMode = (ExtensionTransformMode)idx;
                    UpdateBatchRenamePlan();
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                    return true;
                }
            }
        }

        // Add files
        if (BatchRenameLayout.AddFilesRect(clientH).ContainsInclusive(mouseX, mouseY))
        {
            PromptAddBatchRenameFiles(hwnd);
            return true;
        }

        // Clear files
        if (BatchRenameLayout.ClearRect(clientH).ContainsInclusive(mouseX, mouseY))
        {
            _brFiles.Clear();
            _brScrollOffset = 0;
            UpdateBatchRenamePlan();
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return true;
        }

        // Execute
        if (BatchRenameLayout.ExecuteRect(clientW, clientH).ContainsInclusive(mouseX, mouseY))
        {
            ExecuteBatchRenameFromPrompt(hwnd);
            return true;
        }

        // Cancel
        if (BatchRenameLayout.CancelRect(clientH).ContainsInclusive(mouseX, mouseY))
        {
            DestroyOrHideBatchRenameControls();
            _isPromptingBatchRename = false;
            DestroyWindow(hwnd);
            return true;
        }

        return false;
    }

    private void PromptAddBatchRenameFiles(IntPtr hwnd)
    {
        try
        {
            var ofn = new OPENFILENAME();
            ofn.lStructSize = Marshal.SizeOf(ofn);
            ofn.hwndOwner = hwnd;
            ofn.lpstrFilter = "All Files (*.*)\0*.*\0\0";

            int maxFile = 65536;
            IntPtr fileBuffer = Marshal.AllocHGlobal(maxFile * 2);
            byte[] zeros = new byte[maxFile * 2];
            Marshal.Copy(zeros, 0, fileBuffer, zeros.Length);

            ofn.lpstrFile = fileBuffer;
            ofn.nMaxFile = maxFile;
            ofn.lpstrTitle = Loc("batch_rename_add_files");
            ofn.Flags = 0x00080000 | 0x00000200 | 0x00001000 | 0x00000004;

            if (GetOpenFileName(ref ofn))
            {
                var paths = new List<string>();
                IntPtr currentPtr = fileBuffer;
                while (true)
                {
                    string? s = Marshal.PtrToStringUni(currentPtr);
                    if (string.IsNullOrEmpty(s)) break;
                    paths.Add(s);
                    currentPtr += (s.Length + 1) * 2;
                }

                if (paths.Count > 0)
                {
                    if (paths.Count == 1)
                    {
                        if (!_brFiles.Contains(paths[0], StringComparer.OrdinalIgnoreCase))
                            _brFiles.Add(paths[0]);
                    }
                    else
                    {
                        string dir = paths[0];
                        for (int i = 1; i < paths.Count; i++)
                        {
                            string full = Path.Combine(dir, paths[i]);
                            if (!_brFiles.Contains(full, StringComparer.OrdinalIgnoreCase))
                                _brFiles.Add(full);
                        }
                    }
                    UpdateBatchRenamePlan();
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                }
            }

            Marshal.FreeHGlobal(fileBuffer);
        }
        catch
        {
            // Ignore native dialog error
        }
    }

    private bool HandleBatchRenameMouseWheel(IntPtr hwnd, int delta)
    {
        if (!_isPromptingBatchRename) return false;

        _brScrollOffset -= (delta / 120) * 40;
        if (_brScrollOffset < 0) _brScrollOffset = 0;

        int totalH = (_brCurrentPlan?.Items.Count ?? 0) * 38;
        int listH = BatchRenameLayout.GetPreviewListH(_brClientHeight);
        int maxScroll = Math.Max(0, totalH - listH);
        if (_brScrollOffset > maxScroll) _brScrollOffset = maxScroll;

        InvalidateRect(hwnd, IntPtr.Zero, false);
        return true;
    }

    internal IntPtr HandleBatchRenameMouseMove(IntPtr hwnd, int mouseX, int mouseY)
    {
        if (!_isPromptingBatchRename) return IntPtr.Zero;

        int clientW = _brClientWidth;
        int clientH = _brClientHeight;

        var listLayout = BatchRenameLayout.PreviewListRect(clientW, clientH);
        int newHoverIndex = -1;

        if (listLayout.ContainsInclusive(mouseX, mouseY) && _brCurrentPlan != null && _brCurrentPlan.Items.Count > 0)
        {
            int relativeY = mouseY - BatchRenameLayout.PreviewListY + _brScrollOffset;
            int idx = relativeY / 38;
            if (idx >= 0 && idx < _brCurrentPlan.Items.Count)
            {
                int cardOffsetInRow = relativeY % 38;
                if (cardOffsetInRow <= 34)
                {
                    newHoverIndex = idx;
                }
            }
        }

        if (newHoverIndex != _brHoveredIndex)
        {
            _brHoveredIndex = newHoverIndex;
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        return IntPtr.Zero;
    }

    private void DrawBatchRenameHoverTooltip(Graphics g, float s, int clientW, int clientH)
    {
        if (_msgFont == null || _brHoveredIndex < 0 || _brCurrentPlan == null || _brHoveredIndex >= _brCurrentPlan.Items.Count)
            return;

        var item = _brCurrentPlan.Items[_brHoveredIndex];
        float rowH = 38 * s;
        var listLayout = BatchRenameLayout.PreviewListRect(clientW, clientH);
        var listRect = ScaleBatchRenameRect(listLayout, s);

        float rowY = listRect.Y - (_brScrollOffset * s) + (_brHoveredIndex * rowH);
        if (rowY + rowH < listRect.Y || rowY > listRect.Bottom)
            return;

        using var tipLabelFont = new Font(_msgFont.FontFamily, 11f * s, FontStyle.Bold, GraphicsUnit.Pixel);
        using var tipValFont = new Font(_msgFont.FontFamily, 11.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);

        string origLabel = Loc("batch_rename_tip_original");
        string newLabel = Loc("batch_rename_tip_new");
        string dirLabel = Loc("batch_rename_tip_dir");

        string origVal = item.OriginalName;
        string newVal = item.NewName;
        string dirVal = Path.GetDirectoryName(item.OriginalPath) ?? "";

        float lblW = Math.Max(g.MeasureString(origLabel, tipLabelFont).Width,
                     Math.Max(g.MeasureString(newLabel, tipLabelFont).Width,
                              g.MeasureString(dirLabel, tipLabelFont).Width));

        float valW = Math.Max(g.MeasureString(origVal, tipValFont).Width,
                     Math.Max(g.MeasureString(newVal, tipValFont).Width,
                              g.MeasureString(dirVal, tipValFont).Width));

        float padding = 12 * s;
        float tipW = Math.Min((clientW - 64) * s, Math.Max(320 * s, lblW + valW + (padding * 2) + (8 * s)));
        float lineHeight = 19 * s;
        float tipH = (lineHeight * 3) + (padding * 1.5f);

        float tipX = Math.Max(listRect.X, Math.Min(listRect.Right - tipW, listRect.X + (20 * s)));
        float tipY = rowY + (38 * s) + (4 * s);
        if (tipY + tipH > listRect.Bottom + (40 * s))
        {
            tipY = rowY - tipH - (4 * s);
        }

        var tipRect = new RectangleF(tipX, tipY, tipW, tipH);

        // Shadow
        var shadowRect = new RectangleF(tipX + (2 * s), tipY + (3 * s), tipW, tipH);
        using var shadowBrush = new SolidBrush(Color.FromArgb(90, 0, 0, 0));
        using var shadowPath = UIHelper.GetRoundedRectPath(shadowRect, 6 * s);
        g.FillPath(shadowBrush, shadowPath);

        // Background
        using var tipBg = new SolidBrush(Color.FromArgb(28, 28, 34));
        using var tipPath = UIHelper.GetRoundedRectPath(tipRect, 6 * s);
        g.FillPath(tipBg, tipPath);

        // Border & Accent top strip
        using var borderPen = new Pen(Color.FromArgb(75, 75, 88), 1.2f * s);
        g.DrawPath(borderPen, tipPath);

        Color accent = UIHelper.GetSystemColorizationColor();
        using var accentPen = new Pen(accent, 2f * s);
        g.DrawLine(accentPen, tipRect.X + (8 * s), tipRect.Y + (1f * s), tipRect.Right - (8 * s), tipRect.Y + (1f * s));

        // Draw content
        using var lblBrush = new SolidBrush(Color.FromArgb(160, 160, 160));
        using var whiteBrush = new SolidBrush(Color.White);
        bool willChange = item.OriginalName != item.NewName;
        using var newBrush = new SolidBrush(willChange ? Color.FromArgb(255, 215, 64) : Color.White);
        using var dirBrush = new SolidBrush(Color.FromArgb(170, 170, 170));

        float textX = tipRect.X + padding;
        float valueX = textX + lblW + (6 * s);
        float valueMaxW = tipRect.Right - valueX - padding;
        using var sfVal = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisPath };

        float curY = tipRect.Y + (padding * 0.8f);
        g.DrawString(origLabel, tipLabelFont, lblBrush, textX, curY);
        g.DrawString(origVal, tipValFont, whiteBrush, new RectangleF(valueX, curY, valueMaxW, lineHeight), sfVal);

        curY += lineHeight;
        g.DrawString(newLabel, tipLabelFont, lblBrush, textX, curY);
        g.DrawString(newVal, tipValFont, newBrush, new RectangleF(valueX, curY, valueMaxW, lineHeight), sfVal);

        curY += lineHeight;
        g.DrawString(dirLabel, tipLabelFont, lblBrush, textX, curY);
        g.DrawString(dirVal, tipValFont, dirBrush, new RectangleF(valueX, curY, valueMaxW, lineHeight), sfVal);
    }

    private void ExecuteBatchRenameFromPrompt(IntPtr hwnd)
    {
        UpdateBatchRenamePlan();
        if (_brCurrentPlan == null || _brCurrentPlan.Items.Count == 0) return;

        if (_brCurrentPlan.Items.All(i => i.OriginalName == i.NewName)) return;

        // Hide edit controls
        DestroyOrHideBatchRenameControls();

        // Pass plan into command options
        _commandOptions = new Dictionary<string, object>
        {
            ["plan"] = _brCurrentPlan
        };

        _isPromptingBatchRename = false;
        _progressPresentation.ResumeRunning();

        // Switch to progress UI mode seamlessly
        ResizeWindowForBatchRename(hwnd, false);
        StartProcessingThread(hwnd);
    }
}
