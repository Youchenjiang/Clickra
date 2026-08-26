using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Clickra.Core;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {
        public static void Show()
        {
            bool createdNew;
            _mutex = new System.Threading.Mutex(true, "Global\\Clickra_Dashboard_Mutex", out createdNew);
            if (!createdNew)
            {
                IntPtr existingHwnd = FindWindow("ClickraWnd", null);
                if (existingHwnd != IntPtr.Zero)
                {
                    ShowWindow(existingHwnd, 5); // SW_SHOW
                    ShowWindow(existingHwnd, 9); // SW_RESTORE
                    SetForegroundWindow(existingHwnd);
                }
                _mutex.Dispose();
                _mutex = null;
                return;
            }

            RefreshHistoryData();

            // 清除先前右鍵操作殘留的 ClickraShell surrogate（dllhost，帶套件身分），
            // 避免解除安裝時被「應用程式仍在執行」擋住。
            ClickraShellProcess.KillSurrogateHosts();

            try { SetProcessDpiAwarenessContext((IntPtr)(-4)); } catch {}
            uint dpi = 96;
            try { dpi = GetDpiForSystem(); } catch {}
            _dpiScale = dpi / 96.0f;

            string className = "ClickraWnd";
            IntPtr hClass = Marshal.StringToHGlobalUni(className);

            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = GetModuleHandle(null),
                hCursor = LoadCursorW(IntPtr.Zero, 32512),
                hbrBackground = IntPtr.Zero,
                lpszClassName = hClass
            };

            ushort regResult = RegisterClassEx(ref wc);

            int clientW = (int)(760 * _dpiScale);
            int clientH = (int)(460 * _dpiScale);
            var rect = new RECT { left = 0, top = 0, right = clientW, bottom = clientH };
            AdjustWindowRectEx(ref rect, WS_OVERLAPPEDWINDOW, false, 0);
            int winW = rect.right - rect.left;
            int winH = rect.bottom - rect.top;

            var hwnd = CreateWindowEx(0, className, "Clickra",
                WS_OVERLAPPEDWINDOW, CW_USEDEFAULT, CW_USEDEFAULT, winW, winH,
                IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

            if (hwnd == IntPtr.Zero)
            {
                MessageBox(IntPtr.Zero, $"CreateWindowEx failed!\nhInstance: {wc.hInstance}\nregResult: {regResult}\nclientW: {clientW}\nclientH: {clientH}\nwinW: {winW}\nwinH: {winH}", "Clickra Diagnostics", 0x10);
                _mutex.Dispose();
                _mutex = null;
                return;
            }

            DragAcceptFiles(hwnd, true);

            int dark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_DARK_MODE, ref dark, sizeof(int));

            SetWindowText(hwnd, "Clickra");

            string exePath = Environment.ProcessPath ?? "";
            if (!string.IsNullOrEmpty(exePath))
            {
                _hIcon = ExtractIcon(IntPtr.Zero, exePath, 0);
                if (_hIcon != IntPtr.Zero)
                {
                    SendMessageW(hwnd, 0x0080, (IntPtr)0, _hIcon); // ICON_BIG
                    SendMessageW(hwnd, 0x0080, (IntPtr)1, _hIcon); // ICON_SMALL
                }
            }

            RecreateScaledFonts();
            RecreateBuffer(clientW, clientH);

            ShowWindow(hwnd, 5);

            SetTimer(hwnd, TIMER_ID_REFRESH, 250, IntPtr.Zero);

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            
            Marshal.FreeHGlobal(hClass);
        }

        static void RefreshHistoryData()
        {
            try
            {
                _historyEntries = ClickraStorage.GetHistory(50);
                _statTotal = _historyEntries.Count;
                _statSuccess = _historyEntries.Count(h => h.IsSuccess);
                _statFailed = _historyEntries.Count(h => !h.IsSuccess);
            }
            catch
            {
                _historyEntries = new List<ClickraStorage.HistoryEntry>();
                _statTotal = 0;
                _statSuccess = 0;
                _statFailed = 0;
            }
        }

        static void CleanupResources()
        {
            try { _titleFont?.Dispose(); _titleFont = null; } catch { }
            try { _subFont?.Dispose(); _subFont = null; } catch { }
            try { _tabFont?.Dispose(); _tabFont = null; } catch { }
            try { _contentTitleFont?.Dispose(); _contentTitleFont = null; } catch { }
            try { _sectionFont?.Dispose(); _sectionFont = null; } catch { }
            try { _bodyFont?.Dispose(); _bodyFont = null; } catch { }
            try { _tagFont?.Dispose(); _tagFont = null; } catch { }
            try { _iconFont?.Dispose(); _iconFont = null; } catch { }

            try { _bufferGraphics?.Dispose(); _bufferGraphics = null; } catch { }
            try { _bufferBmp?.Dispose(); _bufferBmp = null; } catch { }

            if (_hIcon != IntPtr.Zero)
            {
                DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }
        }
    }
}
