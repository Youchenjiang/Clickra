using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Clickra.Core;
using Clickra.UI.Layout;
using static Clickra.UI.Native.Win32;

namespace Clickra.UI;

/// <summary>
/// Specifies the display style and interactive controls for a native modal dialog.
/// </summary>
public enum NativeDialogKind
{
    Text,
    Confirm,
    Alert,
    Warning,
    Error
}

/// <summary>
/// Provides unified, modal prompt dialog services for Clickra with identical visual layout,
/// typography, and branding to Clickra's standard prompt window design.
/// </summary>
public static class NativeDialogs
{
    private const string PropOldWndProc = "ClickraOldWndProc";
    private const string PropOldBtnProc = "ClickraOldBtnProc";
    private const string ButtonClassName = "BUTTON";
    private const string EditClassName = "EDIT";

    private static bool _classRegistered;
    private static string? _resultText;
    private static bool _confirmed;
    private static NativeDialogKind _currentKind = NativeDialogKind.Text;

    private static IntPtr _hEdit;
    private static IntPtr _hBtnOk;
    private static IntPtr _hBtnCancel;
    private static IntPtr _hFontChild;
    private static IntPtr _editBgBrush;
    private static IntPtr _hIcon;

    private static IntPtr _parentHwnd;
    private static bool _parentWasEnabled;

    private static string _currentSubtitle = "";
    private static string _currentHeader = "";
    private static string _currentPrompt = "";
    private static string _currentLanguage = "zh-Hant";
    private static float _currentDpiScale = 1.0f;
    private static string _currentFontName = "Microsoft JhengHei UI";

    private const int IdEdit = 101;
    private const int IdOk = 1001;
    private const int IdCancel = 1002;

    private readonly record struct ModalDialogOptions(
        IntPtr ParentHwnd,
        string Subtitle,
        string Prompt,
        NativeDialogKind Kind,
        float DpiScale,
        string Language,
        string? Header = null,
        RECT? CenterRect = null,
        string? InitialValue = null,
        bool IsPassword = false,
        string? OkText = null,
        string? CancelText = null);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S6640", Justification = "NativeAOT Win32 window procedure callbacks require unmanaged function pointers.")]
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
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Background (#202020)
            using (var bgBrush = new SolidBrush(Color.FromArgb(32, 32, 32)))
            {
                g.FillRectangle(bgBrush, 0, 0, clientW, clientH);
            }

            // Outer 1px Border (#464646)
            using (var borderPen = new Pen(Color.FromArgb(70, 70, 70), 1f * s))
            {
                g.DrawRectangle(borderPen, 0.5f, 0.5f, clientW - 1f, clientH - 1f);
            }

            // Title 'Clickra' (Color 255, 255, 255, 20px Bold)
            using (var titleFont = new Font(_currentFontName, 20f * s, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var titleBrush = new SolidBrush(Color.FromArgb(255, 255, 255)))
            {
                g.DrawString("Clickra", titleFont, titleBrush, 36 * s, 36 * s);
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

            if (_currentKind == NativeDialogKind.Error)
            {
                var errColor = Color.FromArgb(239, 68, 68);
                using var errBrush = new SolidBrush(errColor);
                using var headerFont = new Font(_currentFontName, 15f * s, FontStyle.Bold, GraphicsUnit.Pixel);

                VectorIcons.DrawFailureCross(g, 36 * s, 134 * s, 16 * s, errColor, 2f * s);
                string headerText = !string.IsNullOrWhiteSpace(_currentHeader)
                    ? _currentHeader
                    : _currentSubtitle;
                g.DrawString(headerText, headerFont, errBrush, 58 * s, 131 * s);

                using var msgFont = new Font(_currentFontName, 14f * s, GraphicsUnit.Pixel);
                using var promptBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
                var promptRect = new RectangleF(36 * s, 160 * s, 448 * s, 46 * s);
                g.DrawString(_currentPrompt, msgFont, promptBrush, promptRect);
            }
            else if (_currentKind == NativeDialogKind.Warning)
            {
                var warnColor = Color.FromArgb(234, 179, 8);
                using var warnBrush = new SolidBrush(warnColor);
                using var headerFont = new Font(_currentFontName, 15f * s, FontStyle.Bold, GraphicsUnit.Pixel);

                VectorIcons.DrawWarningTriangle(g, 36 * s, 134 * s, 16 * s, warnColor, 2f * s);
                string headerText = !string.IsNullOrWhiteSpace(_currentHeader)
                    ? _currentHeader
                    : _currentSubtitle;
                g.DrawString(headerText, headerFont, warnBrush, 58 * s, 131 * s);

                using var msgFont = new Font(_currentFontName, 14f * s, GraphicsUnit.Pixel);
                using var promptBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
                var promptRect = new RectangleF(36 * s, 160 * s, 448 * s, 46 * s);
                g.DrawString(_currentPrompt, msgFont, promptBrush, promptRect);
            }
            else
            {
                // Instruction / Message Prompt (Color 220, 220, 220, 14.67px)
                using (var msgFont = new Font(_currentFontName, 14.67f * s, GraphicsUnit.Pixel))
                using (var promptBrush = new SolidBrush(Color.FromArgb(220, 220, 220)))
                {
                    float promptH = (_currentKind == NativeDialogKind.Text) ? (32 * s) : (72 * s);
                    var promptRect = new RectangleF(36 * s, 130 * s, 448 * s, promptH);
                    g.DrawString(_currentPrompt, msgFont, promptBrush, promptRect);
                }
            }
        }

        using var screenG = Graphics.FromHdc(hdc);
        screenG.DrawImage(bmp, 0, 0);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static IntPtr EditSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        IntPtr oldProc = GetProp(hwnd, PropOldWndProc);
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S6640", Justification = "NativeAOT Win32 window procedure callbacks require unmanaged function pointers.")]
    private static unsafe void SubclassButton(IntPtr btn)
    {
        if (btn == IntPtr.Zero) return;
        IntPtr oldProc = GetWindowLongPtr(btn, -4);
        SetProp(btn, PropOldBtnProc, oldProc);
        SetWindowLongPtr(btn, -4, (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&ButtonSubclassProc);
    }

    private static void UnsubclassButton(IntPtr btn)
    {
        if (btn == IntPtr.Zero) return;
        IntPtr oldProc = GetProp(btn, PropOldBtnProc);
        if (oldProc != IntPtr.Zero)
        {
            SetWindowLongPtr(btn, -4, oldProc);
            RemoveProp(btn, PropOldBtnProc);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S6640", Justification = "NativeAOT Win32 window procedure callbacks require unmanaged function pointers.")]
    private static unsafe void SubclassEdit(IntPtr hEdit)
    {
        IntPtr oldProc = GetWindowLongPtr(hEdit, -4);
        SetProp(hEdit, PropOldWndProc, oldProc);
        SetWindowLongPtr(hEdit, -4, (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&EditSubclassProc);
    }

    private static void UnsubclassEdit(IntPtr hEdit)
    {
        if (hEdit == IntPtr.Zero) return;
        IntPtr currentProc = GetProp(hEdit, PropOldWndProc);
        if (currentProc != IntPtr.Zero)
        {
            SetWindowLongPtr(hEdit, -4, currentProc);
            RemoveProp(hEdit, PropOldWndProc);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static IntPtr ButtonSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        IntPtr oldProc = GetProp(hwnd, PropOldBtnProc);
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

    private static void ShowModal(in ModalDialogOptions options)
    {
        EnsureClassRegistered();
        _resultText = null;
        _confirmed = false;
        _currentKind = options.Kind;

        float s = options.DpiScale > 0 ? options.DpiScale : 1.0f;
        _currentDpiScale = s;
        _currentSubtitle = options.Subtitle ?? "";
        _currentHeader = options.Header ?? "";
        _currentPrompt = options.Prompt ?? "";
        _currentLanguage = options.Language ?? "zh-Hant";
        _currentFontName = LocalizedUiFontSelector.GetTextFontName(_currentLanguage);

        _parentHwnd = options.ParentHwnd;
        _parentWasEnabled = options.ParentHwnd != IntPtr.Zero && IsWindowEnabled(options.ParentHwnd);

        IntPtr dlg = CreateDialogWindow(options, s);
        ApplyDialogThemeAndIcon(dlg);
        CreateDialogControls(dlg, options, s);

        ShowWindow(dlg, 5); // SW_SHOW
        FocusDefaultControl();

        if (_parentHwnd != IntPtr.Zero && _parentWasEnabled)
        {
            EnableWindow(_parentHwnd, false);
        }

        RunModalLoop(dlg);

        UnsubclassEdit(_hEdit);
        UnsubclassButton(_hBtnOk);
        UnsubclassButton(_hBtnCancel);

        if (_parentHwnd != IntPtr.Zero && _parentWasEnabled && !IsWindowEnabled(_parentHwnd))
        {
            EnableWindow(_parentHwnd, true);
            SetForegroundWindow(_parentHwnd);
        }
    }

    private static IntPtr CreateDialogWindow(in ModalDialogOptions options, float s)
    {
        int clientW = (int)(520 * s);
        int clientH = (int)(265 * s);

        var rect = new RECT { left = 0, top = 0, right = clientW, bottom = clientH };
        AdjustWindowRectEx(ref rect, WS_OVERLAPPED_FIXED, false, 0);
        int winW = rect.right - rect.left;
        int winH = rect.bottom - rect.top;

        int x = CW_USEDEFAULT;
        int y = CW_USEDEFAULT;
        if (options.ParentHwnd != IntPtr.Zero && GetWindowRect(options.ParentHwnd, out RECT parentRect))
        {
            x = parentRect.left + ((parentRect.right - parentRect.left) - winW) / 2;
            y = parentRect.top + ((parentRect.bottom - parentRect.top) - winH) / 2;
        }
        else if (options.CenterRect.HasValue && options.CenterRect.Value.right > options.CenterRect.Value.left)
        {
            RECT cr = options.CenterRect.Value;
            x = cr.left + ((cr.right - cr.left) - winW) / 2;
            y = cr.top + ((cr.bottom - cr.top) - winH) / 2;
        }

        IntPtr hInstance = GetModuleHandle(null);
        return CreateWindowEx(
            0,
            "ClickraPromptModalWnd",
            "Clickra",
            WS_OVERLAPPED_FIXED,
            x,
            y,
            winW,
            winH,
            options.ParentHwnd,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);
    }

    private static void ApplyDialogThemeAndIcon(IntPtr dlg)
    {
        int darkMode = 1;
        DwmSetWindowAttribute(dlg, DWMWA_DARK_MODE, ref darkMode, sizeof(int));

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
            SendMessageW(dlg, 0x0080, (IntPtr)0, _hIcon);
            SendMessageW(dlg, 0x0080, (IntPtr)1, _hIcon);
        }
    }

    private static void CreateDialogControls(IntPtr dlg, in ModalDialogOptions options, float s)
    {
        if (_hFontChild == IntPtr.Zero)
        {
            _hFontChild = CreateFontW((int)(14.5 * s), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, _currentFontName);
        }

        string resolvedOkText = options.OkText ?? Localization.T("dialog_ok", _currentLanguage);
        string resolvedCancelText = options.CancelText ?? Localization.T("dialog_cancel", _currentLanguage);
        IntPtr hInstance = GetModuleHandle(null);

        if (options.Kind == NativeDialogKind.Text)
        {
            uint editStyle = 0x40000000 | 0x10000000 | 0x00800000 | 0x00010000 | 0x0080;
            if (options.IsPassword) editStyle |= 0x0020;
            _hEdit = CreateWindowEx(0, EditClassName, options.InitialValue ?? "", editStyle, (int)(36 * s), (int)(165 * s), (int)(448 * s), (int)(28 * s), dlg, (IntPtr)IdEdit, hInstance, IntPtr.Zero);
            SendMessageW(_hEdit, 0x0030, _hFontChild, (IntPtr)1);
            SubclassEdit(_hEdit);

            _hBtnOk = CreateWindowEx(0, ButtonClassName, resolvedOkText, 0x40000000 | 0x10000000 | 0x00010000 | 0x0001, (int)(280 * s), (int)(210 * s), (int)(90 * s), (int)(30 * s), dlg, (IntPtr)IdOk, hInstance, IntPtr.Zero);
            _hBtnCancel = CreateWindowEx(0, ButtonClassName, resolvedCancelText, 0x40000000 | 0x10000000 | 0x00010000, (int)(394 * s), (int)(210 * s), (int)(90 * s), (int)(30 * s), dlg, (IntPtr)IdCancel, hInstance, IntPtr.Zero);
            SendMessageW(_hBtnOk, 0x0030, _hFontChild, (IntPtr)1);
            SendMessageW(_hBtnCancel, 0x0030, _hFontChild, (IntPtr)1);
        }
        else if (options.Kind == NativeDialogKind.Confirm)
        {
            _hEdit = IntPtr.Zero;
            _hBtnOk = CreateWindowEx(0, ButtonClassName, resolvedOkText, 0x40000000 | 0x10000000 | 0x00010000 | 0x0001, (int)(280 * s), (int)(210 * s), (int)(90 * s), (int)(30 * s), dlg, (IntPtr)IdOk, hInstance, IntPtr.Zero);
            _hBtnCancel = CreateWindowEx(0, ButtonClassName, resolvedCancelText, 0x40000000 | 0x10000000 | 0x00010000, (int)(394 * s), (int)(210 * s), (int)(90 * s), (int)(30 * s), dlg, (IntPtr)IdCancel, hInstance, IntPtr.Zero);
            SendMessageW(_hBtnOk, 0x0030, _hFontChild, (IntPtr)1);
            SendMessageW(_hBtnCancel, 0x0030, _hFontChild, (IntPtr)1);
        }
        else
        {
            _hEdit = IntPtr.Zero;
            _hBtnCancel = IntPtr.Zero;
            _hBtnOk = CreateWindowEx(0, ButtonClassName, resolvedOkText, 0x40000000 | 0x10000000 | 0x00010000 | 0x0001, (int)(394 * s), (int)(210 * s), (int)(90 * s), (int)(30 * s), dlg, (IntPtr)IdOk, hInstance, IntPtr.Zero);
            SendMessageW(_hBtnOk, 0x0030, _hFontChild, (IntPtr)1);
        }

        SubclassButton(_hBtnOk);
        SubclassButton(_hBtnCancel);
    }

    private static void FocusDefaultControl()
    {
        if (_hEdit != IntPtr.Zero)
        {
            SetFocus(_hEdit);
            SendMessageW(_hEdit, 0x00B1, IntPtr.Zero, (IntPtr)(-1));
        }
        else if (_hBtnOk != IntPtr.Zero)
        {
            SetFocus(_hBtnOk);
        }
    }

    private static void RunModalLoop(IntPtr dlg)
    {
        while (IsWindow(dlg) && GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (!IsDialogMessageW(dlg, ref msg))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
    }

    /// <summary>
    /// Displays a modal text prompt dialog matching Clickra's password input window layout and branding.
    /// </summary>
    public static string? PromptText(
        IntPtr parentHwnd,
        string subtitle,
        string prompt,
        string? initialValue,
        bool isPassword,
        float dpiScale,
        string language)
    {
        ShowModal(new ModalDialogOptions(
            ParentHwnd: parentHwnd,
            Subtitle: subtitle,
            Prompt: prompt,
            Kind: NativeDialogKind.Text,
            DpiScale: dpiScale,
            Language: language,
            InitialValue: initialValue,
            IsPassword: isPassword));
        return _resultText;
    }

    /// <summary>
    /// Displays a modal confirmation dialog with OK and Cancel buttons.
    /// </summary>
    public static bool Confirm(
        IntPtr parentHwnd,
        string subtitle,
        string prompt,
        float dpiScale,
        string language,
        string? okText = null,
        string? cancelText = null)
    {
        ShowModal(new ModalDialogOptions(
            ParentHwnd: parentHwnd,
            Subtitle: subtitle,
            Prompt: prompt,
            Kind: NativeDialogKind.Confirm,
            DpiScale: dpiScale,
            Language: language,
            OkText: okText,
            CancelText: cancelText));
        return _confirmed;
    }

    /// <summary>
    /// Displays a modal information/warning/error alert dialog with a single OK button.
    /// </summary>
    public static void Alert(
        IntPtr parentHwnd,
        string subtitle,
        string prompt,
        float dpiScale,
        string language,
        string? okText = null)
    {
        ShowModal(new ModalDialogOptions(
            ParentHwnd: parentHwnd,
            Subtitle: subtitle,
            Prompt: prompt,
            Kind: NativeDialogKind.Alert,
            DpiScale: dpiScale,
            Language: language,
            OkText: okText));
    }

    /// <summary>
    /// Displays a modal warning alert dialog with vector warning indicator and a single OK button.
    /// </summary>
    public static void Warning(
        IntPtr parentHwnd,
        string subtitle,
        string prompt,
        float dpiScale,
        string language,
        string? header = null,
        RECT? centerRect = null)
    {
        ShowModal(new ModalDialogOptions(
            ParentHwnd: parentHwnd,
            Subtitle: subtitle,
            Prompt: prompt,
            Kind: NativeDialogKind.Warning,
            DpiScale: dpiScale,
            Language: language,
            Header: header,
            CenterRect: centerRect));
    }

    /// <summary>
    /// Displays a modal failure error dialog with vector cross icon, red header and a single OK button.
    /// </summary>
    public static void Error(
        IntPtr parentHwnd,
        string subtitle,
        string prompt,
        float dpiScale,
        string language,
        string? header = null,
        RECT? centerRect = null)
    {
        string displayPrompt = prompt ?? "";
        if (displayPrompt.Equals("User Aborted", StringComparison.OrdinalIgnoreCase))
        {
            displayPrompt = Localization.T("error_user_aborted", language);
        }
        ShowModal(new ModalDialogOptions(
            ParentHwnd: parentHwnd,
            Subtitle: string.IsNullOrWhiteSpace(subtitle) ? Localization.T("progress_sub_failed", language) : subtitle,
            Prompt: displayPrompt,
            Kind: NativeDialogKind.Error,
            DpiScale: dpiScale,
            Language: language,
            Header: string.IsNullOrWhiteSpace(header) ? Localization.T("progress_header_failed", language) : header,
            CenterRect: centerRect));
    }
}
