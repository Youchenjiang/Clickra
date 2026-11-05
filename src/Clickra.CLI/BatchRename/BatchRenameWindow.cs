using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clickra.Core;
using Clickra.Core.Modules.BatchRename.Application;
using Clickra.Core.Modules.BatchRename.Domain;
using Clickra.Core.Modules.BatchRename.Processing;
using Clickra.UI.Native;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI;

/// <summary>
/// NativeAOT Win32 Interactive Visual Window for Batch File and Folder Renaming.
/// </summary>
public static class BatchRenameWindow
{
    private const string WindowClassName = "ClickraBatchRenameWnd";
    private static bool _classRegistered = false;
    private static float _dpiScale = 1.0f;

    private static IntPtr _hwnd = IntPtr.Zero;
    private static IntPtr _hFont = IntPtr.Zero;
    private static IntPtr _darkBrush = IntPtr.Zero;
    private static IntPtr _editBgBrush = IntPtr.Zero;

    // Edit controls
    private static IntPtr _hwndPrefix = IntPtr.Zero;
    private static IntPtr _hwndSuffix = IntPtr.Zero;
    private static IntPtr _hwndStart = IntPtr.Zero;
    private static IntPtr _hwndPad = IntPtr.Zero;
    private static IntPtr _hwndFind = IntPtr.Zero;
    private static IntPtr _hwndReplace = IntPtr.Zero;

    // State
    private static readonly List<string> _files = new();
    private static BatchRenamePlan? _currentPlan;
    private static bool _enableNumbering = true;
    private static InsertPosition _numPos = InsertPosition.Suffix;
    private static CaseTransformMode _caseMode = CaseTransformMode.None;
    private static ExtensionTransformMode _extMode = ExtensionTransformMode.KeepOriginal;
    private static bool _matchCase = false;
    private static bool _useRegex = false;

    private static int _scrollOffset = 0;
    private static int _hoveredButton = -1;
    private static bool _isExecuting = false;

    // Fonts
    private static Font? _titleFont;
    private static Font? _sectionFont;
    private static Font? _bodyFont;
    private static Font? _codeFont;
    private static Font? _badgeFont;

    private static WndProcDelegate? _wndProcDelegate;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Opens the interactive Batch Rename window with optional initial file list.
    /// </summary>
    public static void Show(IReadOnlyList<string>? initialFiles = null)
    {
        if (_hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(_hwnd);
            if (initialFiles != null && initialFiles.Count > 0)
            {
                AddFiles(initialFiles);
            }
            return;
        }

        _files.Clear();
        _scrollOffset = 0;
        if (initialFiles != null && initialFiles.Count > 0)
        {
            AddFiles(initialFiles);
        }

        uint dpi = 96;
        try { dpi = GetDpiForSystem(); } catch { }
        _dpiScale = dpi / 96.0f;

        EnsureWindowClass();
        CreateScaledFonts();

        int clientW = (int)(960 * _dpiScale);
        int clientH = (int)(640 * _dpiScale);
        var rect = new RECT { left = 0, top = 0, right = clientW, bottom = clientH };
        AdjustWindowRectEx(ref rect, WS_OVERLAPPEDWINDOW, false, 0);

        int winW = rect.right - rect.left;
        int winH = rect.bottom - rect.top;

        _darkBrush = CreateSolidBrush(0x001E1E1E);
        _editBgBrush = CreateSolidBrush(0x002D2D2D);

        _hwnd = CreateWindowEx(
            0,
            WindowClassName,
            "Clickra - 檔案與資料夾批次命名 (Batch Rename)",
            WS_OVERLAPPEDWINDOW | WS_VISIBLE,
            CW_USEDEFAULT, CW_USEDEFAULT, winW, winH,
            IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

        if (_hwnd == IntPtr.Zero) return;

        int dark = 1;
        DwmSetWindowAttribute(_hwnd, DWMWA_DARK_MODE, ref dark, sizeof(int));
        DragAcceptFiles(_hwnd, true);

        CreateControls(_hwnd);
        RecalculatePlan();

        // Run local modal message loop
        while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (_hwnd != IntPtr.Zero && IsDialogMessageW(_hwnd, ref msg))
            {
                continue;
            }
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        CleanupResources();
    }

    private static void EnsureWindowClass()
    {
        if (_classRegistered) return;

        _wndProcDelegate = WindowProc;
        IntPtr hClass = Marshal.StringToHGlobalUni(WindowClassName);

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 3, // CS_HREDRAW | CS_VREDRAW
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = GetModuleHandle(null),
            hCursor = LoadCursorW(IntPtr.Zero, 32512), // IDC_ARROW
            hbrBackground = IntPtr.Zero,
            lpszClassName = hClass
        };

        RegisterClassEx(ref wc);
        _classRegistered = true;
    }

    private static void CreateScaledFonts()
    {
        float s = _dpiScale;
        string fontName = "Segoe UI";

        _titleFont = new Font(fontName, 18f * s, FontStyle.Bold, GraphicsUnit.Pixel);
        _sectionFont = new Font(fontName, 13f * s, FontStyle.Bold, GraphicsUnit.Pixel);
        _bodyFont = new Font(fontName, 12f * s, GraphicsUnit.Pixel);
        _codeFont = new Font("Consolas", 11.5f * s, GraphicsUnit.Pixel);
        _badgeFont = new Font(fontName, 10.5f * s, FontStyle.Bold, GraphicsUnit.Pixel);

        _hFont = _bodyFont.ToHfont();
    }

    private static void CreateControls(IntPtr hwnd)
    {
        float s = _dpiScale;
        IntPtr hInst = GetModuleHandle(null);
        uint editStyle = 0x50010080; // WS_CHILD | WS_VISIBLE | WS_BORDER | ES_AUTOHSCROLL

        int leftX = (int)(24 * s);
        int editW = (int)(280 * s);
        int editH = (int)(24 * s);

        // Prefix
        _hwndPrefix = CreateWindowEx(0, "EDIT", "", editStyle, leftX, (int)(110 * s), editW, editH, hwnd, (IntPtr)201, hInst, IntPtr.Zero);
        SendMessageW(_hwndPrefix, 0x0030, _hFont, (IntPtr)1); // WM_SETFONT

        // Suffix
        _hwndSuffix = CreateWindowEx(0, "EDIT", "", editStyle, leftX, (int)(160 * s), editW, editH, hwnd, (IntPtr)202, hInst, IntPtr.Zero);
        SendMessageW(_hwndSuffix, 0x0030, _hFont, (IntPtr)1);

        // Numbering Start
        _hwndStart = CreateWindowEx(0, "EDIT", "1", editStyle, leftX, (int)(262 * s), (int)(135 * s), editH, hwnd, (IntPtr)203, hInst, IntPtr.Zero);
        SendMessageW(_hwndStart, 0x0030, _hFont, (IntPtr)1);

        // Numbering Pad Digits
        _hwndPad = CreateWindowEx(0, "EDIT", "2", editStyle, leftX + (int)(145 * s), (int)(262 * s), (int)(135 * s), editH, hwnd, (IntPtr)204, hInst, IntPtr.Zero);
        SendMessageW(_hwndPad, 0x0030, _hFont, (IntPtr)1);

        // Find
        _hwndFind = CreateWindowEx(0, "EDIT", "", editStyle, leftX, (int)(380 * s), editW, editH, hwnd, (IntPtr)205, hInst, IntPtr.Zero);
        SendMessageW(_hwndFind, 0x0030, _hFont, (IntPtr)1);

        // Replace
        _hwndReplace = CreateWindowEx(0, "EDIT", "", editStyle, leftX, (int)(430 * s), editW, editH, hwnd, (IntPtr)206, hInst, IntPtr.Zero);
        SendMessageW(_hwndReplace, 0x0030, _hFont, (IntPtr)1);
    }

    private static void AddFiles(IEnumerable<string> paths)
    {
        var resolved = BatchRenameUseCase.ResolveTargetFiles(paths, recursive: false);
        foreach (string file in resolved)
        {
            if (!_files.Contains(file, StringComparer.OrdinalIgnoreCase))
            {
                _files.Add(file);
            }
        }
        RecalculatePlan();
    }

    private static void RecalculatePlan()
    {
        string prefix = GetEditText(_hwndPrefix);
        string suffix = GetEditText(_hwndSuffix);
        string find = GetEditText(_hwndFind);
        string replace = GetEditText(_hwndReplace);
        string startStr = GetEditText(_hwndStart);
        string padStr = GetEditText(_hwndPad);

        int start = int.TryParse(startStr, out int sVal) ? sVal : 1;
        int pad = int.TryParse(padStr, out int pVal) ? pVal : 2;

        NumberingOptions? numbering = _enableNumbering
            ? new NumberingOptions(StartIndex: start, Step: 1, PaddingDigits: pad, Position: _numPos)
            : null;

        ReplaceOptions? replaceOpts = !string.IsNullOrEmpty(find)
            ? new ReplaceOptions(FindText: find, ReplaceWith: replace, IgnoreCase: !_matchCase, UseRegex: _useRegex)
            : null;

        var options = new BatchRenameOptions(
            Prefix: string.IsNullOrEmpty(prefix) ? null : prefix,
            Suffix: string.IsNullOrEmpty(suffix) ? null : suffix,
            Numbering: numbering,
            Replace: replaceOpts,
            CaseTransform: _caseMode,
            ExtensionMode: _extMode);

        _currentPlan = BatchRenameEngine.GeneratePlan(_files, options);

        if (_hwnd != IntPtr.Zero)
        {
            InvalidateRect(_hwnd, IntPtr.Zero, false);
        }
    }

    private static string GetEditText(IntPtr hEdit)
    {
        if (hEdit == IntPtr.Zero) return "";
        int len = (int)SendMessageW(hEdit, 0x000E, IntPtr.Zero, IntPtr.Zero); // WM_GETTEXTLENGTH
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowTextW(hEdit, sb, len + 1);
        return sb.ToString();
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case 0x000F: // WM_PAINT
                return HandlePaint(hwnd);

            case 0x0133: // WM_CTLCOLOREDIT
            {
                IntPtr hdc = wParam;
                SetTextColor(hdc, 0x00FFFFFF);
                SetBkColor(hdc, 0x002D2D2D);
                return _editBgBrush;
            }

            case 0x0111: // WM_COMMAND
            {
                int notifyCode = (int)((wParam.ToInt64() >> 16) & 0xFFFF);
                int ctrlId = (int)(wParam.ToInt64() & 0xFFFF);
                if (notifyCode == 0x0300) // EN_CHANGE
                {
                    RecalculatePlan();
                }
                return IntPtr.Zero;
            }

            case 0x0233: // WM_DROPFILES
            {
                IntPtr hDrop = wParam;
                uint count = DragQueryFileW(hDrop, 0xFFFFFFFF, IntPtr.Zero, 0);
                var dropped = new List<string>();
                for (uint i = 0; i < count; i++)
                {
                    IntPtr buffer = Marshal.AllocHGlobal(512 * sizeof(char));
                    if (DragQueryFileW(hDrop, i, buffer, 512) > 0)
                    {
                        string? path = Marshal.PtrToStringUni(buffer);
                        if (!string.IsNullOrEmpty(path)) dropped.Add(path);
                    }
                    Marshal.FreeHGlobal(buffer);
                }
                DragFinish(hDrop);
                if (dropped.Count > 0)
                {
                    AddFiles(dropped);
                }
                return IntPtr.Zero;
            }

            case 0x020A: // WM_MOUSEWHEEL
            {
                short delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                if (delta > 0 && _scrollOffset > 0)
                {
                    _scrollOffset = Math.Max(0, _scrollOffset - 2);
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                }
                else if (delta < 0 && _currentPlan != null && _scrollOffset < _currentPlan.Items.Count - 1)
                {
                    _scrollOffset++;
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                }
                return IntPtr.Zero;
            }

            case 0x0200: // WM_MOUSEMOVE
            {
                int x = (int)(lParam.ToInt64() & 0xFFFF);
                int y = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                int newHover = HitTestButton(x, y);
                if (newHover != _hoveredButton)
                {
                    _hoveredButton = newHover;
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                }
                return IntPtr.Zero;
            }

            case 0x0201: // WM_LBUTTONDOWN
            {
                int x = (int)(lParam.ToInt64() & 0xFFFF);
                int y = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                HandleClick(hwnd, x, y);
                return IntPtr.Zero;
            }

            case 0x0100: // WM_KEYDOWN
            {
                int key = wParam.ToInt32();
                if (key == 0x1B) // ESC
                {
                    PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
                    return IntPtr.Zero;
                }
                break;
            }

            case 0x0010: // WM_CLOSE
                DestroyWindow(hwnd);
                return IntPtr.Zero;

            case 0x0002: // WM_DESTROY
                _hwnd = IntPtr.Zero;
                PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static int HitTestButton(int px, int py)
    {
        float s = _dpiScale;
        RECT client;
        GetClientRect(_hwnd, out client);
        int winW = client.right - client.left;
        int winH = client.bottom - client.top;

        // Button 1: Add Files (Bottom Left)
        if (new Rectangle((int)(24 * s), winH - (int)(45 * s), (int)(130 * s), (int)(32 * s)).Contains(px, py))
            return 1;

        // Button 2: Clear Files (Bottom Left 2)
        if (new Rectangle((int)(162 * s), winH - (int)(45 * s), (int)(110 * s), (int)(32 * s)).Contains(px, py))
            return 2;

        // Button 3: Rename (Bottom Right 1)
        if (new Rectangle(winW - (int)(240 * s), winH - (int)(45 * s), (int)(130 * s), (int)(32 * s)).Contains(px, py))
            return 3;

        // Button 4: Close (Bottom Right 2)
        if (new Rectangle(winW - (int)(98 * s), winH - (int)(45 * s), (int)(78 * s), (int)(32 * s)).Contains(px, py))
            return 4;

        // Button 5: Toggle Numbering
        if (new Rectangle((int)(24 * s), (int)(208 * s), (int)(160 * s), (int)(20 * s)).Contains(px, py))
            return 5;

        // Button 6: Toggle Numbering Position
        if (new Rectangle((int)(200 * s), (int)(208 * s), (int)(100 * s), (int)(20 * s)).Contains(px, py))
            return 6;

        // Button 7: Toggle Case Mode
        if (new Rectangle((int)(24 * s), (int)(492 * s), (int)(140 * s), (int)(24 * s)).Contains(px, py))
            return 7;

        // Button 8: Toggle Extension Mode
        if (new Rectangle((int)(174 * s), (int)(492 * s), (int)(130 * s), (int)(24 * s)).Contains(px, py))
            return 8;

        return -1;
    }

    private static void HandleClick(IntPtr hwnd, int px, int py)
    {
        int hit = HitTestButton(px, py);
        switch (hit)
        {
            case 1: // Add Files
                PickFiles(hwnd);
                break;
            case 2: // Clear List
                _files.Clear();
                RecalculatePlan();
                break;
            case 3: // Execute Rename
                if (_currentPlan != null && _currentPlan.CanExecute && !_isExecuting)
                {
                    ExecuteRename(hwnd);
                }
                break;
            case 4: // Close
                PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
                break;
            case 5: // Toggle Numbering
                _enableNumbering = !_enableNumbering;
                RecalculatePlan();
                break;
            case 6: // Numbering Position
                _numPos = _numPos switch
                {
                    InsertPosition.Suffix => InsertPosition.Prefix,
                    InsertPosition.Prefix => InsertPosition.ReplaceName,
                    _ => InsertPosition.Suffix
                };
                RecalculatePlan();
                break;
            case 7: // Case Mode
                _caseMode = _caseMode switch
                {
                    CaseTransformMode.None => CaseTransformMode.LowerCase,
                    CaseTransformMode.LowerCase => CaseTransformMode.UpperCase,
                    CaseTransformMode.UpperCase => CaseTransformMode.TitleCase,
                    _ => CaseTransformMode.None
                };
                RecalculatePlan();
                break;
            case 8: // Extension Mode
                _extMode = _extMode switch
                {
                    ExtensionTransformMode.KeepOriginal => ExtensionTransformMode.LowerCase,
                    ExtensionTransformMode.LowerCase => ExtensionTransformMode.UpperCase,
                    _ => ExtensionTransformMode.KeepOriginal
                };
                RecalculatePlan();
                break;
        }
    }

    private static void PickFiles(IntPtr hwnd)
    {
        var ofn = new OPENFILENAME();
        ofn.lStructSize = Marshal.SizeOf(ofn);
        ofn.hwndOwner = hwnd;
        ofn.lpstrFilter = "所有檔案 (*.*)\0*.*\0\0";
        ofn.nFilterIndex = 1;

        IntPtr fileBuffer = Marshal.AllocHGlobal(65536 * sizeof(char));
        Marshal.WriteInt16(fileBuffer, 0);
        ofn.lpstrFile = fileBuffer;
        ofn.nMaxFile = 65536;
        ofn.lpstrTitle = "選擇欲批次命名的檔案";
        ofn.Flags = 0x00080000 | 0x00000200 | 0x00001000 | 0x00000800; // OFN_EXPLORER | OFN_ALLOWMULTISELECT | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST

        if (GetOpenFileName(ref ofn))
        {
            var paths = new List<string>();
            IntPtr ptr = fileBuffer;
            string? first = Marshal.PtrToStringUni(ptr);
            ptr += (first?.Length ?? 0) * 2 + 2;
            string? next = Marshal.PtrToStringUni(ptr);

            if (string.IsNullOrEmpty(next))
            {
                if (!string.IsNullOrEmpty(first)) paths.Add(first);
            }
            else
            {
                string dir = first ?? "";
                while (!string.IsNullOrEmpty(next))
                {
                    paths.Add(Path.Combine(dir, next));
                    ptr += next.Length * 2 + 2;
                    next = Marshal.PtrToStringUni(ptr);
                }
            }
            AddFiles(paths);
        }

        Marshal.FreeHGlobal(fileBuffer);
    }

    private static void ExecuteRename(IntPtr hwnd)
    {
        if (_currentPlan == null || !_currentPlan.CanExecute) return;
        _isExecuting = true;
        InvalidateRect(hwnd, IntPtr.Zero, false);

        var plan = _currentPlan;
        var useCase = new BatchRenameUseCase();

        Task.Run(async () =>
        {
            try
            {
                var result = await useCase.ExecuteAsync(plan, null, CancellationToken.None);
                _files.Clear();
                foreach (var pair in result.RenamedFiles)
                {
                    _files.Add(pair.NewPath);
                }

                // Show success modal
                MessageBox(hwnd, $"批次更名完成！\n成功更名：{result.RenamedFiles.Count} 個檔案", "Clickra 批次命名", 0x40); // MB_ICONINFORMATION
            }
            catch (Exception ex)
            {
                MessageBox(hwnd, $"更名過程發生錯誤：\n{ex.Message}", "錯誤", 0x10);
            }
            finally
            {
                _isExecuting = false;
                RecalculatePlan();
            }
        });
    }

    private static IntPtr HandlePaint(IntPtr hwnd)
    {
        IntPtr hdc = BeginPaint(hwnd, out PAINTSTRUCT ps);
        RECT client;
        GetClientRect(hwnd, out client);
        int w = client.right - client.left;
        int h = client.bottom - client.top;

        using (var bmp = new Bitmap(w, h))
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(24, 24, 24));

            float s = _dpiScale;

            // Draw Left Panel (Rules Background)
            int leftW = (int)(320 * s);
            using (var leftBrush = new SolidBrush(Color.FromArgb(32, 32, 32)))
            {
                g.FillRectangle(leftBrush, 0, 0, leftW, h);
            }
            using (var borderPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(borderPen, leftW, 0, leftW, h);
            }

            // Paint Left Controls
            PaintLeftPanel(g, s);

            // Paint Right Preview Panel
            PaintRightPanel(g, s, leftW, w, h);

            // Paint Bottom Action Bar
            PaintBottomBar(g, s, w, h);

            // Blit buffer to screen
            using var screenG = Graphics.FromHdc(hdc);
            screenG.DrawImage(bmp, 0, 0);
        }

        EndPaint(hwnd, ref ps);
        return IntPtr.Zero;
    }

    private static void PaintLeftPanel(Graphics g, float s)
    {
        if (_titleFont == null || _sectionFont == null || _bodyFont == null) return;

        using var whiteBrush = new SolidBrush(Color.White);
        using var textSecBrush = new SolidBrush(Color.FromArgb(180, 180, 180));
        using var accentBrush = new SolidBrush(Color.FromArgb(0, 120, 212));

        int x = (int)(24 * s);

        // Section: Title
        g.DrawString("命名規則設定", _titleFont, whiteBrush, x, (int)(20 * s));

        // Group 1: 前綴與後綴
        g.DrawString("前綴 (Prefix)", _sectionFont, whiteBrush, x, (int)(90 * s));
        g.DrawString("後綴 (Suffix)", _sectionFont, whiteBrush, x, (int)(140 * s));

        // Group 2: 自動編號
        string numPosLabel = _numPos switch
        {
            InsertPosition.Prefix => "位置: 前綴",
            InsertPosition.ReplaceName => "位置: 取代",
            _ => "位置: 後綴"
        };
        string numToggle = _enableNumbering ? "[✔] 自動流水號" : "[  ] 自動流水號";
        g.DrawString(numToggle, _sectionFont, _enableNumbering ? accentBrush : textSecBrush, x, (int)(206 * s));
        g.DrawString($"[{numPosLabel}]", _bodyFont, textSecBrush, (int)(200 * s), (int)(208 * s));

        g.DrawString("起始值", _bodyFont, textSecBrush, x, (int)(242 * s));
        g.DrawString("位數補零 (位)", _bodyFont, textSecBrush, x + (int)(145 * s), (int)(242 * s));

        // Group 3: 尋找與取代
        g.DrawString("尋找文字 (Find)", _sectionFont, whiteBrush, x, (int)(360 * s));
        g.DrawString("取代為 (Replace)", _sectionFont, whiteBrush, x, (int)(410 * s));

        // Group 4 & 5: 大小寫與副檔名
        string caseLabel = _caseMode switch
        {
            CaseTransformMode.LowerCase => "小寫",
            CaseTransformMode.UpperCase => "大寫",
            CaseTransformMode.TitleCase => "字首大寫",
            _ => "原樣"
        };
        string extLabel = _extMode switch
        {
            ExtensionTransformMode.LowerCase => ".小寫",
            ExtensionTransformMode.UpperCase => ".大寫",
            _ => ".保留"
        };
        g.DrawString($"大小寫: [{caseLabel}]", _sectionFont, textSecBrush, x, (int)(494 * s));
        g.DrawString($"副檔名: [{extLabel}]", _sectionFont, textSecBrush, x + (int)(150 * s), (int)(494 * s));
    }

    private static void PaintRightPanel(Graphics g, float s, int leftW, int winW, int winH)
    {
        if (_titleFont == null || _bodyFont == null || _codeFont == null || _badgeFont == null) return;

        using var whiteBrush = new SolidBrush(Color.White);
        using var textSecBrush = new SolidBrush(Color.FromArgb(160, 160, 160));
        using var arrowBrush = new SolidBrush(Color.FromArgb(0, 160, 240));
        using var linePen = new Pen(Color.FromArgb(42, 42, 42));

        int startX = leftW + (int)(24 * s);
        int rightW = winW - startX - (int)(24 * s);

        // Header Summary
        int total = _currentPlan?.TotalCount ?? 0;
        int valid = _currentPlan?.ValidCount ?? 0;
        int conflicts = (_currentPlan?.ConflictCount ?? 0) + (_currentPlan?.InvalidCount ?? 0);

        g.DrawString("檔案預覽 (Live Preview)", _titleFont, whiteBrush, startX, (int)(20 * s));

        string summary = $"清單總計 {total} 個檔案  |  即將更名 {valid} 個  |  衝突 {conflicts} 個";
        g.DrawString(summary, _bodyFont, conflicts > 0 ? Brushes.OrangeRed : textSecBrush, startX, (int)(54 * s));

        // 2-Phase Swap Warning Note
        if (_currentPlan?.RequiresTwoPhaseSwap == true)
        {
            using var swapBrush = new SolidBrush(Color.FromArgb(40, 130, 220));
            g.DrawString("ℹ 偵測到循環相依 (A ⇄ B)，將自動啟用安全兩階段原子交換防覆蓋", _badgeFont, swapBrush, startX, (int)(74 * s));
        }

        // Table Header Line
        int tableTopY = (int)(98 * s);
        g.DrawLine(linePen, startX, tableTopY, startX + rightW, tableTopY);

        // Render File Rows
        int rowH = (int)(32 * s);
        int tableBottomY = winH - (int)(65 * s);
        int maxRows = Math.Max(1, (tableBottomY - tableTopY) / rowH);

        if (_currentPlan == null || _currentPlan.Items.Count == 0)
        {
            using var tipBrush = new SolidBrush(Color.FromArgb(100, 100, 100));
            g.DrawString("請將檔案或資料夾拖曳至此視窗，或點擊下方「加入檔案」", _bodyFont, tipBrush, startX + (int)(30 * s), tableTopY + (int)(60 * s));
            return;
        }

        for (int i = 0; i < maxRows && (i + _scrollOffset) < _currentPlan.Items.Count; i++)
        {
            int idx = i + _scrollOffset;
            var item = _currentPlan.Items[idx];
            int rowY = tableTopY + i * rowH + (int)(4 * s);

            // Alternating row background
            if (i % 2 == 1)
            {
                using var rowBg = new SolidBrush(Color.FromArgb(28, 28, 28));
                g.FillRectangle(rowBg, startX, rowY - (int)(2 * s), rightW, rowH);
            }

            // Old Name
            string oldName = TruncateMiddle(item.OriginalName, 26);
            g.DrawString(oldName, _codeFont, whiteBrush, startX + (int)(6 * s), rowY + (int)(4 * s));

            // Arrow
            g.DrawString("➔", _bodyFont, arrowBrush, startX + (int)(200 * s), rowY + (int)(3 * s));

            // New Name
            string newName = TruncateMiddle(item.NewName, 26);
            Brush nameBrush = item.Status == RenameItemStatus.Valid
                ? Brushes.LightGreen
                : (item.Status == RenameItemStatus.Unchanged ? textSecBrush : Brushes.OrangeRed);
            g.DrawString(newName, _codeFont, nameBrush, startX + (int)(230 * s), rowY + (int)(4 * s));

            // Status Badge
            int badgeX = startX + rightW - (int)(75 * s);
            PaintStatusBadge(g, s, badgeX, rowY + (int)(3 * s), item.Status);
        }
    }

    private static void PaintStatusBadge(Graphics g, float s, int x, int y, RenameItemStatus status)
    {
        if (_badgeFont == null) return;

        Color bg = status switch
        {
            RenameItemStatus.Valid => Color.FromArgb(16, 124, 65),
            RenameItemStatus.Unchanged => Color.FromArgb(60, 60, 60),
            _ => Color.FromArgb(200, 40, 40)
        };
        string text = status switch
        {
            RenameItemStatus.Valid => "OK",
            RenameItemStatus.Unchanged => "未變更",
            RenameItemStatus.ConflictExistingFile => "已存在",
            RenameItemStatus.ConflictDuplicateTarget => "撞名",
            _ => "無效"
        };

        using var badgeBg = new SolidBrush(bg);
        g.FillRectangle(badgeBg, x, y, (int)(65 * s), (int)(18 * s));
        g.DrawString(text, _badgeFont, Brushes.White, x + (int)(8 * s), y + (int)(2 * s));
    }

    private static void PaintBottomBar(Graphics g, float s, int winW, int winH)
    {
        if (_sectionFont == null) return;

        using var barBg = new SolidBrush(Color.FromArgb(28, 28, 28));
        using var linePen = new Pen(Color.FromArgb(48, 48, 48));
        int barY = winH - (int)(55 * s);

        g.FillRectangle(barBg, 0, barY, winW, (int)(55 * s));
        g.DrawLine(linePen, 0, barY, winW, barY);

        // Button 1: Add Files
        DrawFlatButton(g, s, (int)(24 * s), barY + (int)(10 * s), (int)(130 * s), (int)(32 * s), "➕ 加入檔案", _hoveredButton == 1, false);

        // Button 2: Clear
        DrawFlatButton(g, s, (int)(162 * s), barY + (int)(10 * s), (int)(110 * s), (int)(32 * s), "🗑 清空", _hoveredButton == 2, false);

        // Button 3: Rename (Accent)
        bool canRename = _currentPlan != null && _currentPlan.CanExecute && !_isExecuting;
        Color accent = canRename ? Color.FromArgb(0, 120, 212) : Color.FromArgb(60, 60, 60);
        DrawFlatButton(g, s, winW - (int)(240 * s), barY + (int)(10 * s), (int)(130 * s), (int)(32 * s), _isExecuting ? "更名中..." : "✔ 執行更名", _hoveredButton == 3, true, accent);

        // Button 4: Close
        DrawFlatButton(g, s, winW - (int)(98 * s), barY + (int)(10 * s), (int)(78 * s), (int)(32 * s), "關閉", _hoveredButton == 4, false);
    }

    private static void DrawFlatButton(Graphics g, float s, int x, int y, int w, int h, string text, bool hovered, bool isAccent, Color? customBg = null)
    {
        if (_sectionFont == null) return;

        Color bg = customBg ?? (isAccent
            ? (hovered ? Color.FromArgb(16, 140, 230) : Color.FromArgb(0, 120, 212))
            : (hovered ? Color.FromArgb(55, 55, 55) : Color.FromArgb(42, 42, 42)));

        using var brush = new SolidBrush(bg);
        using var pen = new Pen(Color.FromArgb(70, 70, 70));
        g.FillRectangle(brush, x, y, w, h);
        g.DrawRectangle(pen, x, y, w, h);

        var sz = g.MeasureString(text, _sectionFont);
        g.DrawString(text, _sectionFont, Brushes.White, x + (w - sz.Width) / 2f, y + (h - sz.Height) / 2f);
    }

    private static string TruncateMiddle(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        int half = (maxChars - 3) / 2;
        return text[..half] + "..." + text[^half..];
    }

    private static void CleanupResources()
    {
        try { _titleFont?.Dispose(); } catch { }
        try { _sectionFont?.Dispose(); } catch { }
        try { _bodyFont?.Dispose(); } catch { }
        try { _codeFont?.Dispose(); } catch { }
        try { _badgeFont?.Dispose(); } catch { }

        if (_darkBrush != IntPtr.Zero) { DeleteObject(_darkBrush); _darkBrush = IntPtr.Zero; }
        if (_editBgBrush != IntPtr.Zero) { DeleteObject(_editBgBrush); _editBgBrush = IntPtr.Zero; }
        if (_hFont != IntPtr.Zero) { DeleteObject(_hFont); _hFont = IntPtr.Zero; }
    }
}
