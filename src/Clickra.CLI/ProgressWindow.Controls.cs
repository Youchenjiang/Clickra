using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Text;
using System.Drawing.Drawing2D;
using Clickra.Core;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    /// <summary>
    /// 提供 CLI 執行階段專專用之 Win32 進度視窗。
    /// </summary>
    public partial class ProgressWindow
    {
        static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            ProgressWindow? window = null;
            if (msg == 0x0081) // WM_NCCREATE
            {
                IntPtr lpCreateParams = Marshal.ReadIntPtr(l);
                SetWindowLongPtr(hwnd, -21, lpCreateParams); // GWLP_USERDATA = -21
                if (lpCreateParams != IntPtr.Zero)
                {
                    GCHandle gcHandle = GCHandle.FromIntPtr(lpCreateParams);
                    window = gcHandle.Target as ProgressWindow;
                    if (window != null) window._hwnd = hwnd;
                }
            }
            else
            {
                IntPtr userData = GetWindowLongPtr(hwnd, -21);
                if (userData != IntPtr.Zero)
                {
                    GCHandle gcHandle = GCHandle.FromIntPtr(userData);
                    if (gcHandle.IsAllocated)
                    {
                        window = gcHandle.Target as ProgressWindow;
                    }
                }
            }

            IntPtr result = IntPtr.Zero;
            if (window != null)
            {
                result = window.InstanceWndProc(hwnd, msg, w, l);
            }
            else
            {
                result = DefWindowProcW(hwnd, msg, w, l);
            }

            if (msg == 0x0082) // WM_NCDESTROY
            {
                IntPtr userData = GetWindowLongPtr(hwnd, -21);
                if (userData != IntPtr.Zero)
                {
                    GCHandle gcHandle = GCHandle.FromIntPtr(userData);
                    if (gcHandle.IsAllocated)
                    {
                        gcHandle.Free();
                    }
                    SetWindowLongPtr(hwnd, -21, IntPtr.Zero);
                }
            }

            return result;
        }

        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
        private static unsafe IntPtr EditSubclassProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            IntPtr oldProc = GetProp(hwnd, "ClickraOldWndProc");
            if (msg == 0x0100) // WM_KEYDOWN
            {
                int key = w.ToInt32();
                if (key == 0x0D) // VK_RETURN
                {
                    IntPtr parent = GetParent(hwnd);
                    PostMessageW(parent, 0x0111, (IntPtr)1001, IntPtr.Zero); // WM_COMMAND, ID = 1001 (OK)
                    return IntPtr.Zero;
                }
                if (key == 0x1B) // VK_ESCAPE
                {
                    IntPtr parent = GetParent(hwnd);
                    PostMessageW(parent, 0x0111, (IntPtr)1002, IntPtr.Zero); // WM_COMMAND, ID = 1002 (Cancel)
                    return IntPtr.Zero;
                }
            }
            if (msg == 0x0002) // WM_DESTROY
            {
                RemoveProp(hwnd, "ClickraOldWndProc");
            }
            return oldProc != IntPtr.Zero ? CallWindowProc(oldProc, hwnd, msg, w, l) : DefWindowProcW(hwnd, msg, w, l);
        }

        private unsafe IntPtr InstanceWndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            switch (msg)
            {
                case WM_USER_SHOW_PASSWORD_INPUT:
                    {
                        if (_hwndEdit != IntPtr.Zero) return IntPtr.Zero;

                        float scale = _dpiScale;
                        string lang = ClickraStorage.GetSetting("Language");
                        string normLang = Localization.NormalizeLanguageCode(lang);
                        string fontName = "Segoe UI";
                        if (normLang.StartsWith("zh-TW")) fontName = "Microsoft JhengHei UI";
                        else if (normLang.StartsWith("zh-CN")) fontName = "Microsoft YaHei UI";
                        else if (normLang.StartsWith("ja")) fontName = "Yu Gothic UI";
                        else if (normLang.StartsWith("ko")) fontName = "Malgun Gothic";

                        if (_hFont == IntPtr.Zero)
                        {
                            _hFont = CreateFontW((int)(14.5 * scale), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, fontName);
                        }

                        IntPtr hInstance = GetModuleHandle(null);
                        _hwndEdit = CreateWindowEx(0, "EDIT", "", WS_CHILD | WS_VISIBLE | WS_BORDER | WS_TABSTOP | 0x0020 | 0x0080, (int)(36 * scale), (int)(165 * scale), (int)(448 * scale), (int)(28 * scale), hwnd, (IntPtr)101, hInstance, IntPtr.Zero);
                        _hwndBtnOk = CreateWindowEx(0, "BUTTON", Localization.T("dialog_ok", lang), WS_CHILD | WS_VISIBLE | WS_TABSTOP | 0x00000001, (int)(280 * scale), (int)(210 * scale), (int)(90 * scale), (int)(30 * scale), hwnd, (IntPtr)1001, hInstance, IntPtr.Zero);
                        _hwndBtnCancel = CreateWindowEx(0, "BUTTON", Localization.T("dialog_cancel", lang), WS_CHILD | WS_VISIBLE | WS_TABSTOP, (int)(394 * scale), (int)(210 * scale), (int)(90 * scale), (int)(30 * scale), hwnd, (IntPtr)1002, hInstance, IntPtr.Zero);

                        SendMessageW(_hwndEdit, 0x0030, _hFont, (IntPtr)1); // WM_SETFONT = 0x0030
                        SendMessageW(_hwndBtnOk, 0x0030, _hFont, (IntPtr)1);
                        SendMessageW(_hwndBtnCancel, 0x0030, _hFont, (IntPtr)1);

                        // Subclass EDIT control for Enter/Esc VKs
                        IntPtr originalEditProc = GetWindowLongPtr(_hwndEdit, -4); // GWL_WNDPROC = -4
                        SetProp(_hwndEdit, "ClickraOldWndProc", originalEditProc);
                        SetWindowLongPtr(_hwndEdit, -4, (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&EditSubclassProc);

                        SetFocus(_hwndEdit);
                        InvalidateRect(hwnd, IntPtr.Zero, true);
                        InvalidateRect(_hwndEdit, IntPtr.Zero, true);
                        InvalidateRect(_hwndBtnOk, IntPtr.Zero, true);
                        InvalidateRect(_hwndBtnCancel, IntPtr.Zero, true);
                    }
                    return IntPtr.Zero;

                case WM_USER_HIDE_PASSWORD_INPUT:
                    {
                        if (_hwndEdit != IntPtr.Zero)
                        {
                            IntPtr oldProc = GetProp(_hwndEdit, "ClickraOldWndProc");
                            if (oldProc != IntPtr.Zero)
                            {
                                SetWindowLongPtr(_hwndEdit, -4, oldProc);
                                RemoveProp(_hwndEdit, "ClickraOldWndProc");
                            }
                            DestroyWindow(_hwndEdit);
                            _hwndEdit = IntPtr.Zero;
                        }
                        if (_hwndBtnOk != IntPtr.Zero)
                        {
                            DestroyWindow(_hwndBtnOk);
                            _hwndBtnOk = IntPtr.Zero;
                        }
                        if (_hwndBtnCancel != IntPtr.Zero)
                        {
                            DestroyWindow(_hwndBtnCancel);
                            _hwndBtnCancel = IntPtr.Zero;
                        }
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    return IntPtr.Zero;

                case 0x0133: // WM_CTLCOLOREDIT
                    {
                        IntPtr editHdc = w;
                        SetTextColor(editHdc, 0x00FFFFFF); // White
                        SetBkColor(editHdc, 0x002D2D2D); // Edit bg (45, 45, 45)
                        return _editBgBrush;
                    }

                case 0x0111: // WM_COMMAND
                    {
                        int id = (int)w.ToInt64() & 0xFFFF;
                        if (id == 1001) // OK button
                        {
                            string? pwd = null;
                            if (_hwndEdit != IntPtr.Zero)
                            {
                                var sb = new System.Text.StringBuilder(260);
                                GetWindowTextW(_hwndEdit, sb, 260);
                                pwd = sb.ToString();
                            }
                            lock (_stateLock)
                            {
                                _inputPassword = pwd;
                                _passwordCancelled = false;
                            }
                            PostMessageW(hwnd, WM_USER_HIDE_PASSWORD_INPUT, IntPtr.Zero, IntPtr.Zero);
                            _passwordEvent.Set();
                        }
                        else if (id == 1002 || id == 2) // Cancel button
                        {
                            lock (_stateLock)
                            {
                                _inputPassword = null;
                                _passwordCancelled = true;
                            }
                            PostMessageW(hwnd, WM_USER_HIDE_PASSWORD_INPUT, IntPtr.Zero, IntPtr.Zero);
                            _passwordEvent.Set();
                        }
                    }
                    return IntPtr.Zero;
                case 0x020A: // WM_MOUSEWHEEL
                    {
                        int delta = (short)((w.ToInt64() >> 16) & 0xFFFF);
                        int scrollDir = delta > 0 ? -1 : 1;
                        lock (_stateLock)
                        {
                            if (!_completed && !_hasError)
                            {
                                _scrollOffset += scrollDir * 30;
                                if (_scrollOffset < 0) _scrollOffset = 0;
                            }
                        }
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    return IntPtr.Zero;
                case WM_USER_INVALIDATE:
                    InvalidateRect(hwnd, IntPtr.Zero, w != IntPtr.Zero);
                    return IntPtr.Zero;
                case 0x0200: // WM_MOUSEMOVE
                    {
                        int rawX = (short)(l.ToInt64() & 0xFFFF);
                        int rawY = (short)((l.ToInt64() >> 16) & 0xFFFF);
                        int mouseX = (int)(rawX / _dpiScale);
                        int mouseY = (int)(rawY / _dpiScale);

                        lock (_stateLock)
                        {
                            if (!_completed && !_hasError)
                            {
                                if (_isDraggingScroll)
                                {
                                    string statusMsg = _message;
                                    float logicalPctW = 0;
                                    string drawPctStr = _total > 0 ? $"{(_current * 100 / _total)}%" : "";
                                    if (_pctFont != null && _total > 0)
                                    {
                                        using var tempBmp = new Bitmap(1, 1);
                                        using var tempG = Graphics.FromImage(tempBmp);
                                        logicalPctW = tempG.MeasureString(drawPctStr, _pctFont).Width / _dpiScale;
                                    }
                                    float logicalMaxMsgW = 448f;
                                    if (logicalPctW > 0)
                                    {
                                        logicalMaxMsgW = 448f - logicalPctW - 10f;
                                    }

                                    float fullMsgW = 0f;
                                    using (var tempBmp = new Bitmap(1, 1))
                                    using (var tempG = Graphics.FromImage(tempBmp))
                                    {
                                        if (_msgFont != null)
                                        {
                                            fullMsgW = tempG.MeasureString(statusMsg, _msgFont).Width / _dpiScale;
                                        }
                                    }

                                    float maxLogicalScroll = Math.Max(0f, fullMsgW - logicalMaxMsgW);
                                    if (maxLogicalScroll > 0)
                                    {
                                        float thumbW = Math.Max(15f, (logicalMaxMsgW / fullMsgW) * logicalMaxMsgW);
                                        float travelRange = logicalMaxMsgW - thumbW;
                                        if (travelRange > 0)
                                        {
                                            float deltaX = mouseX - _dragStartMouseX;
                                            float deltaOffset = (deltaX / travelRange) * maxLogicalScroll;
                                            _scrollOffset = Math.Max(0f, Math.Min(_dragStartOffset + deltaOffset, maxLogicalScroll));
                                            InvalidateRect(hwnd, IntPtr.Zero, false);
                                        }
                                    }
                                }
                                else
                                {
                                    bool hovered = (mouseX >= 456 && mouseX <= 484 && mouseY >= 36 && mouseY <= 64);
                                    if (hovered != _isTrayBtnHovered)
                                    {
                                        _isTrayBtnHovered = hovered;
                                        InvalidateRect(hwnd, IntPtr.Zero, false);
                                    }
                                }
                            }
                        }
                    }
                    return IntPtr.Zero;
                case 0x0201: // WM_LBUTTONDOWN
                    {
                        int rawX = (short)(l.ToInt64() & 0xFFFF);
                        int rawY = (short)((l.ToInt64() >> 16) & 0xFFFF);
                        int mouseX = (int)(rawX / _dpiScale);
                        int mouseY = (int)(rawY / _dpiScale);

                        lock (_stateLock)
                        {
                            if (!_completed && !_hasError)
                            {
                                if (mouseX >= 456 && mouseX <= 484 && mouseY >= 36 && mouseY <= 64)
                                {
                                    SetupTrayIcon(hwnd);
                                    ShowWindow(hwnd, 0); // SW_HIDE
                                    _isTrayBtnHovered = false;
                                    return IntPtr.Zero;
                                }

                                float logicalPctW = 0;
                                string drawPctStr = _total > 0 ? $"{(_current * 100 / _total)}%" : "";
                                if (_pctFont != null && _total > 0)
                                {
                                    using var tempBmp = new Bitmap(1, 1);
                                    using var tempG = Graphics.FromImage(tempBmp);
                                    logicalPctW = tempG.MeasureString(drawPctStr, _pctFont).Width / _dpiScale;
                                }
                                float logicalMaxMsgW = 448f;
                                if (logicalPctW > 0)
                                {
                                    logicalMaxMsgW = 448f - logicalPctW - 10f;
                                }

                                if (mouseX >= 36 && mouseX <= 36 + logicalMaxMsgW && mouseY >= 148 && mouseY <= 158)
                                {
                                    string statusMsg = _message;
                                    float fullMsgW = 0f;
                                    using (var tempBmp = new Bitmap(1, 1))
                                    using (var tempG = Graphics.FromImage(tempBmp))
                                    {
                                        if (_msgFont != null)
                                        {
                                            fullMsgW = tempG.MeasureString(statusMsg, _msgFont).Width / _dpiScale;
                                        }
                                    }

                                    float maxLogicalScroll = Math.Max(0f, fullMsgW - logicalMaxMsgW);
                                    if (maxLogicalScroll > 0)
                                    {
                                        float clickX = mouseX - 36f;
                                        float thumbW = Math.Max(15f, (logicalMaxMsgW / fullMsgW) * logicalMaxMsgW);
                                        float thumbX = (_scrollOffset / fullMsgW) * logicalMaxMsgW;
                                        if (thumbX + thumbW > logicalMaxMsgW) thumbX = logicalMaxMsgW - thumbW;

                                        float travelRange = logicalMaxMsgW - thumbW;
                                        float relativePos = travelRange > 0 ? (clickX - thumbW / 2f) / travelRange : 0f;
                                        float newOffset = Math.Max(0f, Math.Min(relativePos * maxLogicalScroll, maxLogicalScroll));

                                        _scrollOffset = newOffset;
                                        _isDraggingScroll = true;
                                        _dragStartMouseX = mouseX;
                                        _dragStartOffset = newOffset;
                                        SetCapture(hwnd);
                                        InvalidateRect(hwnd, IntPtr.Zero, false);
                                    }
                                }
                            }
                        }
                    }
                    return IntPtr.Zero;
                case 0x0202: // WM_LBUTTONUP
                    {
                        lock (_stateLock)
                        {
                            if (_isDraggingScroll)
                            {
                                _isDraggingScroll = false;
                                ReleaseCapture();
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                        }
                    }
                    return IntPtr.Zero;
                case WM_TRAYICON:
                    if (l.ToInt64() == 0x0203) // WM_LBUTTONDBLCLK
                    {
                        ShowWindow(hwnd, 5); // SW_SHOW
                        ShowWindow(hwnd, 9); // SW_RESTORE
                        SetForegroundWindow(hwnd);
                        RemoveTrayIcon();
                    }
                    return IntPtr.Zero;
                case 0x0010: // WM_CLOSE
                    {
                        bool needsConfirmation = false;
                        lock (_stateLock)
                        {
                            if (!_completed && !_hasError)
                            {
                                needsConfirmation = true;
                            }
                        }

                        if (needsConfirmation)
                        {
                            string text = Localization.T("progress_cancel_confirm", ClickraStorage.GetSetting("Language"));
                            string caption = "Clickra";
                            int btn = MessageBox(hwnd, text, caption, 0x24 | 0x30); // MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2
                            if (btn != 6) // 6 is IDYES
                            {
                                return IntPtr.Zero; // Ignore close
                            }

                            // User confirmed cancellation
                            try { _cts.Cancel(); } catch { }
                            lock (_stateLock)
                            {
                                _passwordCancelled = true;
                            }
                            _passwordEvent.Set(); // Wake up background thread if blocked on password prompt
                            return IntPtr.Zero; // Wait for background thread to handle cancellation and close the window
                        }

                        DestroyWindow(hwnd);
                    }
                    return IntPtr.Zero;
                case 0x02E0: // WM_DPICHANGED
                    {
                        uint newDpi = (uint)(w.ToInt64() & 0xFFFF);
                        _dpiScale = newDpi / 96.0f;
                        RecreateScaledFonts();
                        
                        int clientW = (int)(520 * _dpiScale);
                        int clientH = (int)(280 * _dpiScale);
                        
                        if (_bufferBmp != null)
                        {
                            _bufferGraphics?.Dispose();
                            _bufferBmp?.Dispose();
                            _bufferBmp = new Bitmap(clientW, clientH);
                            _bufferGraphics = Graphics.FromImage(_bufferBmp);
                            _bufferGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                            _bufferGraphics.SmoothingMode = SmoothingMode.AntiAlias;
                        }

                        var rect = Marshal.PtrToStructure<RECT>(l);
                        SetWindowPos(hwnd, IntPtr.Zero, rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top, 0x0010 | 0x0004);
                    }
                    return IntPtr.Zero;
                case 0x0014: return (IntPtr)1; // WM_ERASEBKGND
                case 0x0113: // WM_TIMER
                    lock (_stateLock)
                    {
                        if (!_completed && !_hasError && !_isPromptingPassword)
                        {
                            if (_currentDispWidth < _targetWidth)
                            {
                                double diff = _targetWidth - _currentDispWidth;
                                double step = diff * 0.15;
                                if (step < 1.0) step = 1.0;
                                
                                _currentDispWidth += step;
                                if (_currentDispWidth >= _targetWidth) _currentDispWidth = _targetWidth;
                            }
                            
                            _shimmerOffset += 5.0f;
                            if (_shimmerOffset > 448) _shimmerOffset = -120;
                            
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                    }
                    return IntPtr.Zero;
                case 0x000F: // WM_PAINT
                    var ps = new PAINTSTRUCT();
                    var hdc = BeginPaint(hwnd, out ps);
                    Paint(hdc);
                    EndPaint(hwnd, ref ps);
                    return IntPtr.Zero;
                case 0x0002: // WM_DESTROY
                    KillTimer(hwnd, (IntPtr)1);
                    CleanupResources();
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, w, l);
        }

    }
}
