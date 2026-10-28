using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Clickra.Core;
using static Clickra.UI.Native.Win32;

namespace Clickra.UI;

/// <summary>
/// Specifies the display style and interactive controls for a native modal dialog.
/// </summary>
public enum NativeDialogKind
{
    Text,
    Confirm,
    Alert
}

/// <summary>
/// Provides unified, modal prompt dialog services for Clickra with identical visual layout,
/// typography, and branding to Clickra's standard prompt window design.
/// </summary>
public static class NativeDialogs
{
    private static bool _classRegistered;
    private static string? _resultText;
    private static bool _confirmed;
    private static NativeDialogKind _currentKind = NativeDialogKind.Text;

    private static IntPtr _hDialog;
    private static IntPtr _hEdit;
    private static IntPtr _hBtnOk;
    private static IntPtr _hBtnCancel;
    private static IntPtr _hFontChild;
    private static IntPtr _editBgBrush;
    private static IntPtr _hIcon;

    private static IntPtr _parentHwnd;
    private static bool _parentWasEnabled;

    private static string _currentSubtitle = "";
    private static string _currentPrompt = "";
    private static float _currentDpiScale = 1.0f;
    private static string _currentFontName = "Microsoft JhengHei UI";

    private const int IdEdit = 101;
    private const int IdOk = 1001;
    private const int IdCancel = 1002;

    private static unsafe void EnsureClassRegistered()
    {
        if (_classRegistered) return;

        _editBgBrush = CreateSolidBrush(0x002D2D2D); // Edit box background #2D2D2D

        IntPtr hClass = Marshal.StringToHGlobalUni("ClickraPromptModalWnd");
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)sizeof(WNDCLASSEX),
            style = 3, // CS_HREDRAW | CS_VREDRAW
            lpfnWndProc = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&DialogWndProc,
            hInstance = GetModuleHandle(null),
            hCursor = LoadCursorW(IntPtr.Zero, 32512), // IDC_ARROW
            hbrBackground = IntPtr.Zero, // Handled in WM_PAINT
            lpszClassName = hClass
        };
        RegisterClassEx(ref wc);
        _classRegistered = true;
    }

    private static void DismissDialog(IntPtr hwnd, string? result)
    {
        _resultText = result;
        if (_parentHwnd != IntPtr.Zero && _parentWasEnabled)
        {
            EnableWindow(_parentHwnd, true);
            SetForegroundWindow(_parentHwnd);
            SetFocus(_parentHwnd);
        }
        DestroyWindow(hwnd);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static IntPtr DialogWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case 0x000F: // WM_PAINT
            {
                IntPtr hdc = BeginPaint(hwnd, out PAINTSTRUCT ps);
                PaintDialog(hdc);
                EndPaint(hwnd, ref ps);
                return IntPtr.Zero;
            }
            case 0x0133: // WM_CTLCOLOREDIT
            {
                IntPtr hdc = wParam;
                SetTextColor(hdc, 0x00FFFFFF); // Pure white
                SetBkColor(hdc, 0x002D2D2D);   // Dark edit background
                return _editBgBrush;
            }
            case 0x0100: // WM_KEYDOWN
            {
                int key = (int)wParam.ToInt64() & 0xFFFF;
                if (key == 0x0D) // VK_RETURN
                {
                    SendMessageW(hwnd, 0x0111, (IntPtr)IdOk, IntPtr.Zero);
                    return IntPtr.Zero;
                }
                if (key == 0x1B) // VK_ESCAPE
                {
                    SendMessageW(hwnd, 0x0111, (IntPtr)IdCancel, IntPtr.Zero);
                    return IntPtr.Zero;
                }
                break;
            }
            case 0x0111: // WM_COMMAND
            {
                int id = (int)(wParam.ToInt64() & 0xFFFF);
                if (id == IdOk || id == 1 /* IDOK */)
                {
                    _confirmed = true;
                    string text = "";
                    if (_hEdit != IntPtr.Zero)
                    {
                        var sb = new StringBuilder(2048);
                        GetWindowTextW(_hEdit, sb, 2048);
                        text = sb.ToString();
                    }
                    DismissDialog(hwnd, text);
                    return IntPtr.Zero;
                }
                if (id == IdCancel || id == 2 /* IDCANCEL */)
                {
                    _confirmed = false;
                    DismissDialog(hwnd, null);
                    return IntPtr.Zero;
                }
                break;
            }
            case 0x0010: // WM_CLOSE
            {
                _confirmed = false;
                DismissDialog(hwnd, null);
                return IntPtr.Zero;
            }
            case 0x0002: // WM_DESTROY
            {
                _hDialog = IntPtr.Zero;
                _hEdit = IntPtr.Zero;
                _hBtnOk = IntPtr.Zero;
                _hBtnCancel = IntPtr.Zero;
                return IntPtr.Zero;
            }
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static void PaintDialog(IntPtr hdc)
    {
        float s = _currentDpiScale;
        int clientW = (int)(520 * s);
        int clientH = (int)(265 * s);

        using var bmp = new Bitmap(clientW, clientH);
        using (var g = Graphics.FromImage(bmp))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Background #202020
            using (var bgBrush = new SolidBrush(Color.FromArgb(32, 32, 32)))
            {
                g.FillRectangle(bgBrush, 0, 0, clientW, clientH);
            }

            // Big "Clickra" Title (White, 32px Bold)
            using (var titleFont = new Font("Segoe UI Variable Display", 32f * s, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.DrawString("Clickra", titleFont, Brushes.White, 36 * s, 28 * s);
            }

            // Subtitle (Color 160, 160, 160, 14.67px)
            if (!string.IsNullOrWhiteSpace(_currentSubtitle) && _currentSubtitle != "Clickra")
            {
                using (var subFont = new Font(_currentFontName, 14.67f * s, GraphicsUnit.Pixel))
                using (var subBrush = new SolidBrush(Color.FromArgb(160, 160, 160)))
                {
                    g.DrawString(_currentSubtitle, subFont, subBrush, 36 * s, 72 * s);
                }
            }

            // Divider Line (Color 60, 60, 60)
            using (var linePen = new Pen(Color.FromArgb(60, 60, 60), 1f * s))
            {
                g.DrawLine(linePen, 36 * s, 110 * s, 484 * s, 110 * s);
            }

            // Instruction / Message Prompt (Color 220, 220, 220, 14.67px)
            using (var msgFont = new Font(_currentFontName, 14.67f * s, GraphicsUnit.Pixel))
            using (var promptBrush = new SolidBrush(Color.FromArgb(220, 220, 220)))
            {
                float promptH = (_currentKind == NativeDialogKind.Text) ? (32 * s) : (72 * s);
                var promptRect = new RectangleF(36 * s, 130 * s, 448 * s, promptH);
                g.DrawString(_currentPrompt, msgFont, promptBrush, promptRect);
            }
        }

        using var screenG = Graphics.FromHdc(hdc);
        screenG.DrawImage(bmp, 0, 0);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static IntPtr EditSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        IntPtr oldProc = GetProp(hwnd, "ClickraOldWndProc");
        if (msg == 0x0100) // WM_KEYDOWN
        {
            int key = (int)wParam.ToInt64() & 0xFFFF;
            if (key == 0x0D) // VK_RETURN
            {
                IntPtr parent = GetParent(hwnd);
                if (parent != IntPtr.Zero)
                {
                    SendMessageW(parent, 0x0111, (IntPtr)IdOk, IntPtr.Zero);
                }
                return IntPtr.Zero;
            }
            if (key == 0x1B) // VK_ESCAPE
            {
                IntPtr parent = GetParent(hwnd);
                if (parent != IntPtr.Zero)
                {
                    SendMessageW(parent, 0x0111, (IntPtr)IdCancel, IntPtr.Zero);
                }
                return IntPtr.Zero;
            }
        }
        return CallWindowProc(oldProc, hwnd, msg, wParam, lParam);
    }

    private static unsafe void SubclassButton(IntPtr btn)
    {
        if (btn == IntPtr.Zero) return;
        IntPtr oldProc = GetWindowLongPtr(btn, -4);
        SetProp(btn, "ClickraOldBtnProc", oldProc);
        SetWindowLongPtr(btn, -4, (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&ButtonSubclassProc);
    }

    private static void UnsubclassButton(IntPtr btn)
    {
        if (btn == IntPtr.Zero) return;
        IntPtr oldProc = GetProp(btn, "ClickraOldBtnProc");
        if (oldProc != IntPtr.Zero)
        {
            SetWindowLongPtr(btn, -4, oldProc);
            RemoveProp(btn, "ClickraOldBtnProc");
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static IntPtr ButtonSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        IntPtr oldProc = GetProp(hwnd, "ClickraOldBtnProc");
        if (msg == 0x0100) // WM_KEYDOWN
        {
            int key = (int)wParam.ToInt64() & 0xFFFF;
            if (key == 0x0D) // VK_RETURN
            {
                IntPtr parent = GetParent(hwnd);
                if (parent != IntPtr.Zero)
                {
                    int id = (hwnd == _hBtnCancel) ? IdCancel : IdOk;
                    SendMessageW(parent, 0x0111, (IntPtr)id, IntPtr.Zero);
                }
                return IntPtr.Zero;
            }
            if (key == 0x1B) // VK_ESCAPE
            {
                IntPtr parent = GetParent(hwnd);
                if (parent != IntPtr.Zero)
                {
                    SendMessageW(parent, 0x0111, (IntPtr)IdCancel, IntPtr.Zero);
                }
                return IntPtr.Zero;
            }
        }
        return CallWindowProc(oldProc, hwnd, msg, wParam, lParam);
    }

    private static unsafe void ShowModal(
        IntPtr parentHwnd,
        string subtitle,
        string prompt,
        NativeDialogKind kind,
        float dpiScale,
        string language,
        string? initialValue = null,
        bool isPassword = false,
        string? okText = null,
        string? cancelText = null)
    {
        EnsureClassRegistered();
        _resultText = null;
        _confirmed = false;
        _currentKind = kind;

        float s = dpiScale > 0 ? dpiScale : 1.0f;
        _currentDpiScale = s;
        _currentSubtitle = subtitle ?? "";
        _currentPrompt = prompt ?? "";
        _currentFontName = LocalizedUiFontSelector.GetTextFontName(language);

        _parentHwnd = parentHwnd;
        _parentWasEnabled = parentHwnd != IntPtr.Zero && IsWindowEnabled(parentHwnd);

        int clientW = (int)(520 * s);
        int clientH = (int)(265 * s);

        var rect = new RECT { left = 0, top = 0, right = clientW, bottom = clientH };
        AdjustWindowRectEx(ref rect, WS_OVERLAPPED_FIXED, false, 0);
        int winW = rect.right - rect.left;
        int winH = rect.bottom - rect.top;

        int x = CW_USEDEFAULT;
        int y = CW_USEDEFAULT;
        if (parentHwnd != IntPtr.Zero && GetWindowRect(parentHwnd, out RECT parentRect))
        {
            x = parentRect.left + ((parentRect.right - parentRect.left) - winW) / 2;
            y = parentRect.top + ((parentRect.bottom - parentRect.top) - winH) / 2;
        }

        IntPtr hInstance = GetModuleHandle(null);
        IntPtr dlg = CreateWindowEx(
            0,
            "ClickraPromptModalWnd",
            "Clickra",
            WS_OVERLAPPED_FIXED,
            x,
            y,
            winW,
            winH,
            parentHwnd,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        _hDialog = dlg;

        // DWM Dark Mode
        int darkMode = 1;
        DwmSetWindowAttribute(dlg, DWMWA_DARK_MODE, ref darkMode, sizeof(int));

        // Clickra App Icon
        if (_hIcon == IntPtr.Zero)
        {
            string exePath = Environment.ProcessPath ?? "";
            if (!string.IsNullOrEmpty(exePath))
            {
                _hIcon = ExtractIcon(IntPtr.Zero, exePath, 0);
            }
        }
        if (_hIcon != IntPtr.Zero)
        {
            SendMessageW(dlg, 0x0080, (IntPtr)0, _hIcon); // ICON_BIG
            SendMessageW(dlg, 0x0080, (IntPtr)1, _hIcon); // ICON_SMALL
        }

        if (_hFontChild == IntPtr.Zero)
        {
            _hFontChild = CreateFontW((int)(14.5 * s), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, _currentFontName);
        }

        string resolvedOkText = okText ?? Localization.T("dialog_ok", language);
        string resolvedCancelText = cancelText ?? Localization.T("dialog_cancel", language);

        if (kind == NativeDialogKind.Text)
        {
            uint editStyle = 0x40000000 | 0x10000000 | 0x00800000 | 0x00010000 | 0x0080; // WS_CHILD | WS_VISIBLE | WS_BORDER | WS_TABSTOP | ES_AUTOHSCROLL
            if (isPassword) editStyle |= 0x0020; // ES_PASSWORD
            _hEdit = CreateWindowEx(0, "EDIT", initialValue ?? "", editStyle, (int)(36 * s), (int)(165 * s), (int)(448 * s), (int)(28 * s), dlg, (IntPtr)IdEdit, hInstance, IntPtr.Zero);
            SendMessageW(_hEdit, 0x0030, _hFontChild, (IntPtr)1);

            IntPtr oldEditProc = GetWindowLongPtr(_hEdit, -4);
            SetProp(_hEdit, "ClickraOldWndProc", oldEditProc);
            SetWindowLongPtr(_hEdit, -4, (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&EditSubclassProc);

            _hBtnOk = CreateWindowEx(0, "BUTTON", resolvedOkText, 0x40000000 | 0x10000000 | 0x00010000 | 0x0001, (int)(280 * s), (int)(210 * s), (int)(90 * s), (int)(30 * s), dlg, (IntPtr)IdOk, hInstance, IntPtr.Zero);
            _hBtnCancel = CreateWindowEx(0, "BUTTON", resolvedCancelText, 0x40000000 | 0x10000000 | 0x00010000, (int)(394 * s), (int)(210 * s), (int)(90 * s), (int)(30 * s), dlg, (IntPtr)IdCancel, hInstance, IntPtr.Zero);
            SendMessageW(_hBtnOk, 0x0030, _hFontChild, (IntPtr)1);
            SendMessageW(_hBtnCancel, 0x0030, _hFontChild, (IntPtr)1);
        }
        else if (kind == NativeDialogKind.Confirm)
        {
            _hEdit = IntPtr.Zero;
            _hBtnOk = CreateWindowEx(0, "BUTTON", resolvedOkText, 0x40000000 | 0x10000000 | 0x00010000 | 0x0001, (int)(280 * s), (int)(210 * s), (int)(90 * s), (int)(30 * s), dlg, (IntPtr)IdOk, hInstance, IntPtr.Zero);
            _hBtnCancel = CreateWindowEx(0, "BUTTON", resolvedCancelText, 0x40000000 | 0x10000000 | 0x00010000, (int)(394 * s), (int)(210 * s), (int)(90 * s), (int)(30 * s), dlg, (IntPtr)IdCancel, hInstance, IntPtr.Zero);
            SendMessageW(_hBtnOk, 0x0030, _hFontChild, (IntPtr)1);
            SendMessageW(_hBtnCancel, 0x0030, _hFontChild, (IntPtr)1);
        }
        else // Alert
        {
            _hEdit = IntPtr.Zero;
            _hBtnCancel = IntPtr.Zero;
            _hBtnOk = CreateWindowEx(0, "BUTTON", resolvedOkText, 0x40000000 | 0x10000000 | 0x00010000 | 0x0001, (int)(394 * s), (int)(210 * s), (int)(90 * s), (int)(30 * s), dlg, (IntPtr)IdOk, hInstance, IntPtr.Zero);
            SendMessageW(_hBtnOk, 0x0030, _hFontChild, (IntPtr)1);
        }

        SubclassButton(_hBtnOk);
        SubclassButton(_hBtnCancel);

        ShowWindow(dlg, 5); // SW_SHOW

        if (_hEdit != IntPtr.Zero)
        {
            SetFocus(_hEdit);
            SendMessageW(_hEdit, 0x00B1, IntPtr.Zero, (IntPtr)(-1));
        }
        else if (_hBtnOk != IntPtr.Zero)
        {
            SetFocus(_hBtnOk);
        }

        if (_parentHwnd != IntPtr.Zero && _parentWasEnabled)
        {
            EnableWindow(_parentHwnd, false);
        }

        while (_hDialog != IntPtr.Zero && GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (!IsDialogMessageW(_hDialog, ref msg))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        if (_hEdit != IntPtr.Zero)
        {
            IntPtr currentProc = GetProp(_hEdit, "ClickraOldWndProc");
            if (currentProc != IntPtr.Zero)
            {
                SetWindowLongPtr(_hEdit, -4, currentProc);
                RemoveProp(_hEdit, "ClickraOldWndProc");
            }
        }
        UnsubclassButton(_hBtnOk);
        UnsubclassButton(_hBtnCancel);

        if (_parentHwnd != IntPtr.Zero && _parentWasEnabled && !IsWindowEnabled(_parentHwnd))
        {
            EnableWindow(_parentHwnd, true);
            SetForegroundWindow(_parentHwnd);
        }
    }

    /// <summary>
    /// Displays a modal text prompt dialog matching Clickra's password input window layout and branding.
    /// </summary>
    /// <param name="parentHwnd">Owner window handle.</param>
    /// <param name="subtitle">Secondary title line below 'Clickra' (e.g. setting name or '輸入 PDF 密碼').</param>
    /// <param name="prompt">Instruction text above the input box.</param>
    /// <param name="initialValue">Initial text content inside the input field.</param>
    /// <param name="isPassword">Whether input should be masked with password dots.</param>
    /// <param name="dpiScale">DPI scaling factor.</param>
    /// <param name="language">Language code for localized font selection.</param>
    /// <returns>The confirmed text if OK was clicked; null if cancelled or closed.</returns>
    public static string? PromptText(
        IntPtr parentHwnd,
        string subtitle,
        string prompt,
        string? initialValue,
        bool isPassword,
        float dpiScale,
        string language)
    {
        ShowModal(
            parentHwnd: parentHwnd,
            subtitle: subtitle,
            prompt: prompt,
            kind: NativeDialogKind.Text,
            dpiScale: dpiScale,
            language: language,
            initialValue: initialValue,
            isPassword: isPassword);
        return _resultText;
    }

    /// <summary>
    /// Displays a modal confirmation dialog with OK and Cancel buttons.
    /// </summary>
    /// <param name="parentHwnd">Owner window handle.</param>
    /// <param name="subtitle">Secondary title line below 'Clickra' (optional).</param>
    /// <param name="prompt">Confirmation message text.</param>
    /// <param name="dpiScale">DPI scaling factor.</param>
    /// <param name="language">Language code for localized font and button labels.</param>
    /// <param name="okText">Custom OK button label (optional).</param>
    /// <param name="cancelText">Custom Cancel button label (optional).</param>
    /// <returns>True if confirmed (OK), false if cancelled or closed.</returns>
    public static bool Confirm(
        IntPtr parentHwnd,
        string subtitle,
        string prompt,
        float dpiScale,
        string language,
        string? okText = null,
        string? cancelText = null)
    {
        ShowModal(
            parentHwnd: parentHwnd,
            subtitle: subtitle,
            prompt: prompt,
            kind: NativeDialogKind.Confirm,
            dpiScale: dpiScale,
            language: language,
            okText: okText,
            cancelText: cancelText);
        return _confirmed;
    }

    /// <summary>
    /// Displays a modal information/warning/error alert dialog with a single OK button.
    /// </summary>
    /// <param name="parentHwnd">Owner window handle.</param>
    /// <param name="subtitle">Secondary title line below 'Clickra' (optional).</param>
    /// <param name="prompt">Alert message text.</param>
    /// <param name="dpiScale">DPI scaling factor.</param>
    /// <param name="language">Language code for localized font and button labels.</param>
    /// <param name="okText">Custom OK button label (optional).</param>
    public static void Alert(
        IntPtr parentHwnd,
        string subtitle,
        string prompt,
        float dpiScale,
        string language,
        string? okText = null)
    {
        ShowModal(
            parentHwnd: parentHwnd,
            subtitle: subtitle,
            prompt: prompt,
            kind: NativeDialogKind.Alert,
            dpiScale: dpiScale,
            language: language,
            okText: okText);
    }
}
