using System;
using System.Runtime.CompilerServices;
using System.Text;
using Clickra.Core;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public partial class ProgressWindow
    {
        /// <summary>Creates the password EDIT / OK / Cancel child windows (skipped while the
        /// visual splitter renders its own controls) and subclasses the EDIT control.</summary>
        private unsafe void ShowPasswordInputControls(IntPtr hwnd)
        {
            if (_hwndEdit != IntPtr.Zero) return;

            if (_isPromptingVisualSplitter)
            {
                // The visual splitter renders its own controls; do not create the
                // password EDIT / OK / Cancel child windows over the splitter UI.
                return;
            }

            float scale = _dpiScale;
            string lang = ClickraStorage.GetSetting("Language");
            string fontName = LocalizedUiFontSelector.GetTextFontName(lang);

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

            IntPtr originalEditProc = GetWindowLongPtr(_hwndEdit, -4); // GWL_WNDPROC = -4
            SetProp(_hwndEdit, "ClickraOldWndProc", originalEditProc);
            SetWindowLongPtr(_hwndEdit, -4, (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&EditSubclassProc);

            SetFocus(_hwndEdit);
            InvalidateRect(hwnd, IntPtr.Zero, true);
            InvalidateRect(_hwndEdit, IntPtr.Zero, true);
            InvalidateRect(_hwndBtnOk, IntPtr.Zero, true);
            InvalidateRect(_hwndBtnCancel, IntPtr.Zero, true);
        }

        /// <summary>Restores the EDIT window procedure, destroys the child controls and
        /// invalidates the window.</summary>
        private void HidePasswordInputControls(IntPtr hwnd)
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

        /// <summary>Handles OK/Cancel button commands from the password prompt, storing the
        /// result and releasing the waiting processing thread.</summary>
        private void HandlePasswordInputCommand(IntPtr hwnd, IntPtr w)
        {
            int id = (int)w.ToInt64() & 0xFFFF;
            if (id == 1001) // OK button
            {
                string? pwd = null;
                if (_hwndEdit != IntPtr.Zero)
                {
                    var sb = new StringBuilder(260);
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
    }
}
