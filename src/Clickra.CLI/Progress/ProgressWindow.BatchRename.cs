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

        _hwndBrPrefix = CreateSingleEdit(hwnd, BatchRenameLayout.PrefixRect, "", font, s);
        _hwndBrSuffix = CreateSingleEdit(hwnd, BatchRenameLayout.SuffixRect, "", font, s);
        _hwndBrSeqStart = CreateSingleEdit(hwnd, BatchRenameLayout.SeqStartRect, "1", font, s);
        int suggestedPad = Math.Max(2, (int)Math.Ceiling(Math.Log10(Math.Max(1, _brFiles.Count + 1))));
        _hwndBrSeqPad = CreateSingleEdit(hwnd, BatchRenameLayout.SeqPadRect, suggestedPad.ToString(), font, s);
        UpdateSequenceControlsEnabled();
        _hwndBrFind = CreateSingleEdit(hwnd, BatchRenameLayout.FindRect, "", font, s);
        _hwndBrReplace = CreateSingleEdit(hwnd, BatchRenameLayout.ReplaceRect, "", font, s);

        UpdateBatchRenamePlan();
    }

    private IntPtr CreateSingleEdit(IntPtr parent, LayoutRect rect, string defaultText, IntPtr font, float s)
    {
        var r = ScaleBatchRenameRect(rect, s);
        int editX = (int)(r.X + (3 * s));
        int editY = (int)(r.Y + (3 * s));
        int editW = (int)(r.Width - (6 * s));
        int editH = (int)(r.Height - (6 * s));

        IntPtr edit = CreateWindowEx(
            0, // No WS_EX_CLIENTEDGE (flat modern container)
            "EDIT",
            defaultText,
            0x50000080 | 0x0080, // WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL
            editX, editY, editW, editH,
            parent, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

        if (font != IntPtr.Zero)
        {
            SendMessageW(edit, 0x0030, font, (IntPtr)1); // WM_SETFONT
        }

        // EM_SETMARGINS (0x00D3): set left/right padding so text is not pressed against edge
        int margin = (int)(4 * s);
        SendMessageW(edit, 0x00D3, (IntPtr)(0x0001 | 0x0002), (IntPtr)((margin << 16) | margin));

        return edit;
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
    }

    private static RectangleF ScaleBatchRenameRect(LayoutRect rect, float scale) =>
        new(rect.X * scale, rect.Y * scale, rect.Width * scale, rect.Height * scale);

    private void PaintBatchRenamePrompt(Graphics g, float s)
    {
        if (_titleFont is null || _msgFont is null || _tipFont is null) return;

        int clientW = _brClientWidth;
        int clientH = _brClientHeight;

        using var bodyBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
        using var mutedBrush = new SolidBrush(Color.FromArgb(145, 145, 145));
        using var sectionFont = new Font(_msgFont.FontFamily, 16f * s, FontStyle.Bold, GraphicsUnit.Pixel);
        using var smallFont = new Font(_msgFont.FontFamily, 12f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        using var badgeFont = new Font("Segoe UI", 11f * s, FontStyle.Bold, GraphicsUnit.Pixel);

        Color accentColor = UIHelper.GetSystemColorizationColor();

        // --- Left Column: Rules ---
        g.DrawString(Loc("batch_rename_rules"), sectionFont, Brushes.White, 32 * s, BatchRenameLayout.RulesTitleY * s);

        // 1. Prefix & Suffix (Paired side-by-side)
        g.DrawString(Loc("batch_rename_prefix"), smallFont, mutedBrush, 32 * s, BatchRenameLayout.PrefixLabelY * s);
        DrawModernTextBoxContainer(g, s, BatchRenameLayout.PrefixRect, _hwndBrPrefix, accentColor);

        g.DrawString(Loc("batch_rename_suffix"), smallFont, mutedBrush, BatchRenameLayout.RightInputX * s, BatchRenameLayout.PrefixLabelY * s);
        DrawModernTextBoxContainer(g, s, BatchRenameLayout.SuffixRect, _hwndBrSuffix, accentColor);

        // 2. Find & Replace (Paired side-by-side)
        g.DrawString(Loc("batch_rename_find"), smallFont, mutedBrush, 32 * s, BatchRenameLayout.FindLabelY * s);
        DrawModernTextBoxContainer(g, s, BatchRenameLayout.FindRect, _hwndBrFind, accentColor);

        g.DrawString(Loc("batch_rename_replace"), smallFont, mutedBrush, BatchRenameLayout.RightInputX * s, BatchRenameLayout.FindLabelY * s);
        DrawModernTextBoxContainer(g, s, BatchRenameLayout.ReplaceRect, _hwndBrReplace, accentColor);

        DrawModernCheckBox(g, s, BatchRenameLayout.MatchCaseRect, _brMatchCase, Loc("batch_rename_match_case"), smallFont, bodyBrush, accentColor);
        DrawModernCheckBox(g, s, BatchRenameLayout.RegexRect, _brUseRegex, Loc("batch_rename_regex"), smallFont, bodyBrush, accentColor);

        // 3. Sequence Numbering (Paired & Compact with disabled state)
        DrawModernCheckBox(g, s, BatchRenameLayout.SeqCheckRect, _brEnableSeq, Loc("batch_rename_numbering"), smallFont, bodyBrush, accentColor);
        DrawSegmentedToggle(g, s, BatchRenameLayout.SeqPosToggleRect, Loc("batch_rename_pos_prefix"), Loc("batch_rename_pos_suffix"), _brSeqPos == InsertPosition.Prefix, smallFont, accentColor, _brEnableSeq);

        using var seqLabelBrush = new SolidBrush(_brEnableSeq ? Color.FromArgb(145, 145, 145) : Color.FromArgb(85, 85, 85));
        g.DrawString(Loc("batch_rename_start_index"), smallFont, seqLabelBrush, 32 * s, BatchRenameLayout.SeqParamLabelY * s);
        DrawModernTextBoxContainer(g, s, BatchRenameLayout.SeqStartRect, _hwndBrSeqStart, accentColor, _brEnableSeq);

        g.DrawString(Loc("batch_rename_padding"), smallFont, seqLabelBrush, BatchRenameLayout.RightInputX * s, BatchRenameLayout.SeqParamLabelY * s);
        DrawModernTextBoxContainer(g, s, BatchRenameLayout.SeqPadRect, _hwndBrSeqPad, accentColor, _brEnableSeq);

        // 4. Case & Ext transforms
        g.DrawString(Loc("batch_rename_case_mode"), smallFont, mutedBrush, 32 * s, BatchRenameLayout.CaseLabelY * s);
        string[] caseLabels = { Loc("batch_rename_case_none"), Loc("batch_rename_case_lower"), Loc("batch_rename_case_upper"), Loc("batch_rename_case_title") };
        for (int i = 0; i < 4; i++)
        {
            DrawBatchRenameButton(g, s, BatchRenameLayout.CaseBtnRect(i), caseLabels[i], (int)_brCaseMode == i, smallFont);
        }

        g.DrawString(Loc("batch_rename_ext_mode"), smallFont, mutedBrush, 32 * s, BatchRenameLayout.ExtLabelY * s);
        string[] extLabels = { Loc("batch_rename_ext_keep"), Loc("batch_rename_ext_lower"), Loc("batch_rename_ext_upper") };
        for (int i = 0; i < 3; i++)
        {
            int modeVal = i == 0 ? 0 : (i == 1 ? 1 : 2);
            DrawBatchRenameButton(g, s, BatchRenameLayout.ExtBtnRect(i), extLabels[i], (int)_brExtMode == modeVal, smallFont);
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
        g.DrawString(summaryText, smallFont, mutedBrush, BatchRenameLayout.RightColX * s, BatchRenameLayout.PreviewSummaryY * s);

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
                string origComp = CompactMiddle(item.OriginalName, smallFont, maxNameW, g, sfTypo);
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
            g.DrawString(Loc("cli_progress_no_files"), _msgFont, mutedBrush, listRect, sf);
        }

        g.Clip = oldClip;

        // Bottom separator
        int sepY = BatchRenameLayout.GetSeparatorY(clientH);
        g.DrawLine(divPen, 32 * s, sepY * s, (clientW - 32) * s, sepY * s);

        // Bottom Action buttons (Responsive positions)
        DrawBatchRenameButton(g, s, BatchRenameLayout.AddFilesRect(clientH), Loc("batch_rename_add_files"), false, _msgFont);
        DrawBatchRenameButton(g, s, BatchRenameLayout.ClearRect(clientH), Loc("batch_rename_clear"), false, _msgFont);

        bool canExecute = _brFiles.Count > 0 && (_brCurrentPlan?.Items.Count(i => i.OriginalName != i.NewName) ?? 0) > 0;
        DrawBatchRenameButton(g, s, BatchRenameLayout.ExecuteRect(clientW, clientH), Loc("batch_rename_execute"), true, _msgFont, canExecute);
        DrawBatchRenameButton(g, s, BatchRenameLayout.CancelRect(clientW, clientH), Loc("batch_rename_cancel"), false, _msgFont);
    }

    private void DrawModernTextBoxContainer(Graphics g, float s, LayoutRect logicalRect, IntPtr editHwnd, Color accent, bool enabled = true)
    {
        var r = ScaleBatchRenameRect(logicalRect, s);
        using var path = UIHelper.GetRoundedRectPath(r, 4 * s);

        bool isFocused = enabled && editHwnd != IntPtr.Zero && _hwndBrFocused == editHwnd;

        // Container background matching EDIT background (darker when disabled)
        using var bgBrush = new SolidBrush(enabled ? Color.FromArgb(45, 45, 45) : Color.FromArgb(30, 30, 30));
        g.FillPath(bgBrush, path);

        // Border: accent when focused, neutral dark grey when unfocused, subtle border when disabled
        Color borderCol = !enabled
            ? Color.FromArgb(48, 48, 52)
            : (isFocused ? accent : Color.FromArgb(68, 68, 76));
        float penW = isFocused ? 1.5f * s : 1.0f * s;
        using var pen = new Pen(borderCol, penW);
        g.DrawPath(pen, path);

        // Fluent accent underline on focus
        if (isFocused)
        {
            using var linePen = new Pen(accent, 2.0f * s);
            g.DrawLine(linePen, r.X + (3 * s), r.Bottom - (1f * s), r.Right - (3 * s), r.Bottom - (1f * s));
        }
    }

    private void DrawModernCheckBox(Graphics g, float s, LayoutRect logicalRect, bool isChecked, string text, Font font, Brush textBrush, Color accent)
    {
        var r = ScaleBatchRenameRect(logicalRect, s);
        float boxSize = 16f * s;
        var boxRect = new RectangleF(r.X, r.Y + ((r.Height - boxSize) / 2f), boxSize, boxSize);

        using var path = UIHelper.GetRoundedRectPath(boxRect, 3.5f * s);

        if (isChecked)
        {
            using var fill = new SolidBrush(accent);
            using var borderPen = new Pen(UIHelper.Lighten(accent, 0.2f), 1f * s);
            g.FillPath(fill, path);
            g.DrawPath(borderPen, path);

            var prevSmoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var checkPen = new Pen(Color.White, 2f * s)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            PointF p1 = new(boxRect.X + (3.5f * s), boxRect.Y + (8f * s));
            PointF p2 = new(boxRect.X + (6.5f * s), boxRect.Y + (11.5f * s));
            PointF p3 = new(boxRect.X + (12.5f * s), boxRect.Y + (4.5f * s));
            g.DrawLines(checkPen, new[] { p1, p2, p3 });
            g.SmoothingMode = prevSmoothing;
        }
        else
        {
            using var fill = new SolidBrush(Color.FromArgb(36, 36, 40));
            using var borderPen = new Pen(Color.FromArgb(88, 88, 96), 1.2f * s);
            g.FillPath(fill, path);
            g.DrawPath(borderPen, path);
        }

        float textX = boxRect.Right + (8f * s);
        float textY = r.Y + ((r.Height - font.GetHeight(g)) / 2f);
        g.DrawString(text, font, textBrush, textX, textY);
    }

    private void DrawSegmentedToggle(Graphics g, float s, LayoutRect logicalRect, string leftText, string rightText, bool isLeftSelected, Font font, Color accent, bool enabled = true)
    {
        var r = ScaleBatchRenameRect(logicalRect, s);
        using var containerPath = UIHelper.GetRoundedRectPath(r, 4 * s);
        using var containerFill = new SolidBrush(enabled ? Color.FromArgb(32, 32, 36) : Color.FromArgb(24, 24, 28));
        using var containerPen = new Pen(enabled ? Color.FromArgb(65, 65, 72) : Color.FromArgb(44, 44, 48), 1f * s);
        g.FillPath(containerFill, containerPath);
        g.DrawPath(containerPen, containerPath);

        float halfW = r.Width / 2f;
        var selectedRect = isLeftSelected
            ? new RectangleF(r.X + (1 * s), r.Y + (1 * s), halfW - (1 * s), r.Height - (2 * s))
            : new RectangleF(r.X + halfW, r.Y + (1 * s), halfW - (1 * s), r.Height - (2 * s));

        using var selPath = UIHelper.GetRoundedRectPath(selectedRect, 3 * s);
        Color selColor = enabled ? accent : Color.FromArgb(48, 48, 54);
        using var selFill = new SolidBrush(selColor);
        g.FillPath(selFill, selPath);

        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        Color leftCol = !enabled
            ? Color.FromArgb(95, 95, 95)
            : (isLeftSelected ? Color.White : Color.FromArgb(170, 170, 170));
        Color rightCol = !enabled
            ? Color.FromArgb(95, 95, 95)
            : (!isLeftSelected ? Color.White : Color.FromArgb(170, 170, 170));

        using var leftBrush = new SolidBrush(leftCol);
        using var rightBrush = new SolidBrush(rightCol);

        var leftR = new RectangleF(r.X, r.Y, halfW, r.Height);
        var rightR = new RectangleF(r.X + halfW, r.Y, halfW, r.Height);
        g.DrawString(leftText, font, leftBrush, leftR, sf);
        g.DrawString(rightText, font, rightBrush, rightR, sf);
    }

    /// <summary>
    /// Compacts a filename or path by truncating in the middle, keeping prefix and suffix/extension visible.
    /// E.g. "bacagent-ccs26_translated_page_01.png" -> "bacagent-ccs…page_01.png"
    /// </summary>
    private static string CompactMiddle(string text, Font font, float maxW, Graphics g, StringFormat sf)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (g.MeasureString(text, font, 10000, sf).Width <= maxW) return text;

        string ext = Path.GetExtension(text) ?? "";
        string stem = Path.GetFileNameWithoutExtension(text) ?? text;
        if (stem.Length <= 4) return text;

        int low = 2;
        int high = stem.Length - 1;
        string best = text;

        while (low <= high)
        {
            int midLen = (low + high) / 2;
            int leftChars = Math.Max(1, (midLen * 5) / 8);
            int rightChars = Math.Max(1, midLen - leftChars);

            if (leftChars + rightChars >= stem.Length)
            {
                rightChars = Math.Max(1, stem.Length - leftChars - 1);
            }

            if (leftChars > 0 && rightChars > 0 && (leftChars + rightChars) < stem.Length)
            {
                string candidate = stem.Substring(0, leftChars) + "…" + stem.Substring(stem.Length - rightChars) + ext;
                if (g.MeasureString(candidate, font, 10000, sf).Width <= maxW)
                {
                    best = candidate;
                    low = midLen + 1; // Try keeping more characters
                    continue;
                }
            }
            high = midLen - 1; // Need fewer characters
        }

        return best;
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
            string compact = CompactMiddle(newN, font, maxW, g, sf);
            g.DrawString(compact, font, dimBrush, x, y, sf);
            return;
        }

        if (hasConflict)
        {
            using var errBrush = new SolidBrush(Color.FromArgb(255, 110, 110));
            string compact = CompactMiddle(newN, font, maxW, g, sf);
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
            string compact = CompactMiddle(newN, font, maxW, g, sf);
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
                pre = CompactMiddle(pre, font, availForPre, g, sf);
                wPre = g.MeasureString(pre, font, 10000, sf).Width;
            }
            else if ((maxW - wPre - wDiff) > (24 * s))
            {
                float availForPost = maxW - wPre - wDiff;
                post = CompactMiddle(post, font, availForPost, g, sf);
                wPost = g.MeasureString(post, font, 10000, sf).Width;
            }
            else
            {
                string compact = CompactMiddle(newN, font, maxW, g, sf);
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

    private void DrawBatchRenameButton(Graphics g, float s, LayoutRect logicalRect, string text, bool isSelectedOrAccent, Font font, bool enabled = true)
    {
        var rect = ScaleBatchRenameRect(logicalRect, s);
        using var path = UIHelper.GetRoundedRectPath(rect, 4 * s);

        Color accent = UIHelper.GetSystemColorizationColor();
        Color bg = !enabled
            ? Color.FromArgb(34, 34, 38)
            : (isSelectedOrAccent ? accent : Color.FromArgb(46, 46, 52));
        Color border = !enabled
            ? Color.FromArgb(46, 46, 50)
            : (isSelectedOrAccent ? UIHelper.Lighten(accent, 0.2f) : Color.FromArgb(70, 70, 78));

        using var fill = new SolidBrush(bg);
        using var pen = new Pen(border, 1f * s);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);

        Color textCol = !enabled ? Color.FromArgb(100, 100, 100) : Color.White;
        using var textBrush = new SolidBrush(textCol);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, textBrush, rect, sf);
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

        // Case mode buttons
        for (int i = 0; i < 4; i++)
        {
            if (BatchRenameLayout.CaseBtnRect(i).ContainsInclusive(mouseX, mouseY))
            {
                _brCaseMode = (CaseTransformMode)i;
                UpdateBatchRenamePlan();
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return true;
            }
        }

        // Extension mode buttons
        for (int i = 0; i < 3; i++)
        {
            if (BatchRenameLayout.ExtBtnRect(i).ContainsInclusive(mouseX, mouseY))
            {
                _brExtMode = (ExtensionTransformMode)i;
                UpdateBatchRenamePlan();
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return true;
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
        if (BatchRenameLayout.CancelRect(clientW, clientH).ContainsInclusive(mouseX, mouseY))
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
