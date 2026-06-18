using System;
using System.Runtime.InteropServices;
using Clickra.Core;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public partial class ProgressWindow
    {
        private void SetupTrayIcon(IntPtr hwnd)
        {
            if (_trayIconAdded) return;

            _nid = new NOTIFYICONDATAW();
            _nid.cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>();
            _nid.hWnd = hwnd;
            _nid.uID = 2;
            _nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            _nid.uCallbackMessage = WM_TRAYICON;
            _nid.hIcon = _hIcon;

            int pct = 0;
            lock (_stateLock)
            {
                if (_total > 0)
                {
                    pct = _current * 100 / _total;
                }
            }
            _nid.szTip = $"Clickra - 正在轉換... {pct}%";

            Shell_NotifyIcon(NIM_ADD, ref _nid);
            _trayIconAdded = true;
        }

        private void UpdateTrayIconProgress()
        {
            if (!_trayIconAdded) return;

            int pct = 0;
            lock (_stateLock)
            {
                if (_total > 0)
                {
                    pct = _current * 100 / _total;
                }
            }
            _nid.szTip = $"Clickra - 正在轉換... {pct}%";
            _nid.uFlags = NIF_TIP;
            Shell_NotifyIcon(NIM_MODIFY, ref _nid);
        }

        private void RemoveTrayIcon()
        {
            if (_trayIconAdded)
            {
                Shell_NotifyIcon(NIM_DELETE, ref _nid);
                _trayIconAdded = false;
            }
        }
    }
}
