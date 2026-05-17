using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Text;
using Clickra.Core;

namespace Clickra.UI
{
    public static class ProgressWindow
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public IntPtr lpszMenuName;
            public IntPtr lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public Point pt; }

        [StructLayout(LayoutKind.Sequential)]
        struct PAINTSTRUCT
        {
            public IntPtr hdc; public bool fErase;
            public int rcLeft, rcTop, rcRight, rcBottom;
            public bool fRestore, fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
        }

        [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode)] 
        static extern ushort RegisterClassEx(ref WNDCLASSEX c);
        
        [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)] 
        static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr p, IntPtr m, IntPtr inst, IntPtr par);
        
        [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode)] 
        static extern bool SetWindowText(IntPtr h, string text);

        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int n);
        [DllImport("user32.dll")] static extern bool GetMessage(out MSG m, IntPtr h, uint f, uint l);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG m);
        [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr h, out PAINTSTRUCT p);
        [DllImport("user32.dll")] static extern bool EndPaint(IntPtr h, ref PAINTSTRUCT p);
        [DllImport("user32.dll")] static extern void PostQuitMessage(int c);
        [DllImport("user32.dll")] static extern IntPtr LoadCursorW(IntPtr h, int n);
        [DllImport("user32.dll")] static extern IntPtr SendMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);
        [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        [DllImport("shell32.dll", EntryPoint = "ExtractIconW", CharSet = CharSet.Unicode)] 
        static extern IntPtr ExtractIcon(IntPtr h, string path, int idx);
        
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        const uint WS_OVERLAPPED_FIXED = 0x00CF0000 & ~0x00040000u & ~0x00020000u;
        const int DWMWA_DARK_MODE = 20;
        const int CW_USEDEFAULT = unchecked((int)0x80000000);

        delegate IntPtr WndProcDelegate(IntPtr h, uint msg, IntPtr w, IntPtr l);
        static WndProcDelegate _wndProc = WndProc;

        static string _command = "";
        static List<string> _files = new List<string>();
        static int _current = 0;
        static int _total = 0;
        static string _message = "";
        static bool _completed = false;
        static bool _hasError = false;
        static string _errorMessage = "";
        static IntPtr _hwnd = IntPtr.Zero;

        public static void Show(string command, List<string> files)
        {
            _command = command;
            _files = files;
            _current = 0;
            _total = files.Count;
            _message = "正在準備處理...";
            _completed = false;
            _hasError = false;
            _errorMessage = "";

            string className = "ClickraProgressWnd";
            IntPtr hClass = Marshal.StringToHGlobalUni(className);

            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = Marshal.GetHINSTANCE(typeof(ProgressWindow).Module),
                hCursor = LoadCursorW(IntPtr.Zero, 32512),
                hbrBackground = IntPtr.Zero,
                lpszClassName = hClass
            };

            RegisterClassEx(ref wc);

            _hwnd = CreateWindowEx(0, className, "Clickra",
                WS_OVERLAPPED_FIXED, CW_USEDEFAULT, CW_USEDEFAULT, 520, 280,
                IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

            int dark = 1;
            DwmSetWindowAttribute(_hwnd, DWMWA_DARK_MODE, ref dark, sizeof(int));
            SetWindowText(_hwnd, "Clickra");

            string exePath = System.Diagnostics.Process.GetCurrentProcess()?.MainModule?.FileName ?? "";
            if (!string.IsNullOrEmpty(exePath))
            {
                var hIcon = ExtractIcon(IntPtr.Zero, exePath, 0);
                if (hIcon != IntPtr.Zero)
                {
                    SendMessageW(_hwnd, 0x0080, (IntPtr)0, hIcon); // ICON_BIG
                    SendMessageW(_hwnd, 0x0080, (IntPtr)1, hIcon); // ICON_SMALL
                }
            }

            ShowWindow(_hwnd, 5);

            Thread bgThread = new Thread(() => RunProcessing(_hwnd));
            bgThread.IsBackground = true;
            bgThread.Start();

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
                DispatchMessage(ref msg);
            }

            Marshal.FreeHGlobal(hClass);
        }

        static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            switch (msg)
            {
                case 0x0014: return (IntPtr)1; // WM_ERASEBKGND
                case 0x000F: // WM_PAINT
                    var ps = new PAINTSTRUCT();
                    var hdc = BeginPaint(hwnd, out ps);
                    Paint(hdc);
                    EndPaint(hwnd, ref ps);
                    return IntPtr.Zero;
                case 0x0002: // WM_DESTROY
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, w, l);
        }

        static void RunProcessing(IntPtr hwnd)
        {
            try
            {
                Action<int, int, string> progressCallback = (curr, tot, msg) =>
                {
                    _current = curr;
                    if (tot > 0) _total = tot;
                    _message = msg;
                    InvalidateRect(hwnd, IntPtr.Zero, true);
                };

                string outputDir = Path.GetDirectoryName(_files[0]) ?? "";

                switch (_command)
                {
                    case "ppt2pdf":
                        FileProcessor.ConvertPptToPdf(_files, progressCallback);
                        break;
                    case "word2pdf":
                        FileProcessor.ConvertWordToPdf(_files, progressCallback);
                        break;
                    case "merge-pdf":
                        FileProcessor.MergePdfs(_files, Path.Combine(outputDir, "Merged_PDF.pdf"), progressCallback);
                        break;
                    case "img2pdf":
                        for (int i = 0; i < _files.Count; i++)
                        {
                            var f = _files[i];
                            string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + ".pdf");
                            progressCallback((i * 100) + 50, _files.Count * 100, $"正在轉換圖片: {Path.GetFileName(f)} ({i + 1}/{_files.Count})...");
                            FileProcessor.ImagesToPdf(new List<string> { f }, outName, null);
                        }
                        progressCallback(_files.Count * 100, _files.Count * 100, "轉換完成，正在儲存 PDF...");
                        break;
                    case "img-merge":
                        FileProcessor.ImagesToPdf(_files, Path.Combine(outputDir, "Merged_Images.pdf"), progressCallback);
                        break;
                    case "img-stitch":
                        FileProcessor.StitchImages(_files, Path.Combine(outputDir, "Stitched_Image.png"), progressCallback);
                        break;
                }

                _completed = true;
                _message = "所有作業已順利完成！";
                InvalidateRect(hwnd, IntPtr.Zero, true);

                ShowToastNotification(_command, _files.Count);

                Thread.Sleep(3000);
                PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
            }
            catch (Exception ex)
            {
                _hasError = true;
                _errorMessage = ex.Message;
                InvalidateRect(hwnd, IntPtr.Zero, true);
                
                MessageBox(hwnd, $"處理過程中發生錯誤：\n{ex.Message}", "Clickra — 錯誤", 0x10); // MB_ICONERROR
                PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
            }
        }

        static void ShowToastNotification(string command, int count)
        {
            try
            {
                string title = "Clickra 轉換成功";
                string body = $"已順利完成 {command} 作業 (共 {count} 個檔案)。";
                
                string psScript = $@"
$ErrorActionPreference = 'Stop'
try {{
    [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
    $template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
    $textNodes = $template.GetElementsByTagName('text')
    $textNodes.Item(0).AppendChild($template.CreateTextNode('{title.Replace("'", "''")}')) | Out-Null
    $textNodes.Item(1).AppendChild($template.CreateTextNode('{body.Replace("'", "''")}')) | Out-Null
    $toast = [Windows.UI.Notifications.ToastNotification]::new($template)
    $notifier = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Clickra')
    $notifier.Show($toast)
}} catch {{
    # 忽略 Toast 失敗
}}";

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = System.Diagnostics.Process.Start(startInfo);
            }
            catch { }
        }

        static void Paint(IntPtr hdc)
        {
            using var bmp = new Bitmap(520, 280);
            using var g = Graphics.FromImage(bmp);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(32, 32, 32));

            using var titleFont = new Font("Segoe UI Variable Display", 24, FontStyle.Bold);
            g.DrawString("Clickra", titleFont, Brushes.White, 36, 28);

            using var subFont = new Font("Segoe UI Variable Display", 11);
            string subText = _hasError ? "作業失敗" : (_completed ? "作業完成" : "正在執行作業...");
            Color subColor = _hasError ? Color.FromArgb(255, 90, 70) : (_completed ? Color.FromArgb(100, 220, 100) : Color.FromArgb(160, 160, 160));
            g.DrawString(subText, subFont, new SolidBrush(subColor), 36, 72);

            using var pen = new Pen(Color.FromArgb(60, 60, 60));
            g.DrawLine(pen, 36, 110, 484, 110);

            if (_hasError)
            {
                using var errHeaderFont = new Font("Segoe UI Variable Display", 16, FontStyle.Bold);
                g.DrawString("❌ 處理失敗", errHeaderFont, new SolidBrush(Color.FromArgb(255, 90, 70)), 36, 130);

                using var errMsgFont = new Font("Segoe UI Variable Display", 10);
                g.DrawString(_errorMessage, errMsgFont, new SolidBrush(Color.FromArgb(200, 200, 200)), new RectangleF(36, 170, 448, 60));
            }
            else if (_completed)
            {
                using var successHeaderFont = new Font("Segoe UI Variable Display", 16, FontStyle.Bold);
                g.DrawString("✔ 轉換成功！", successHeaderFont, new SolidBrush(Color.FromArgb(100, 220, 100)), 36, 130);

                using var msgFont = new Font("Segoe UI Variable Display", 11);
                g.DrawString(_message, msgFont, new SolidBrush(Color.FromArgb(220, 220, 220)), 36, 170);

                using var tipFont = new Font("Segoe UI Variable Display", 9);
                g.DrawString("視窗將於數秒後自動關閉...", tipFont, new SolidBrush(Color.FromArgb(120, 120, 120)), 36, 220);
            }
            else
            {
                using var msgFont = new Font("Segoe UI Variable Display", 11);
                g.DrawString(_message, msgFont, Brushes.White, 36, 130);

                int barX = 36, barY = 170, barW = 448, barH = 16;
                using var bgBrush = new SolidBrush(Color.FromArgb(45, 45, 45));
                g.FillRectangle(bgBrush, barX, barY, barW, barH);
                using var borderPen = new Pen(Color.FromArgb(70, 70, 70));
                g.DrawRectangle(borderPen, barX, barY, barW, barH);

                if (_total > 0 && _current > 0)
                {
                    int fillW = (int)(barW * (double)_current / _total);
                    if (fillW > barW) fillW = barW;
                    using var fillBrush = new SolidBrush(Color.FromArgb(100, 220, 100));
                    g.FillRectangle(fillBrush, barX, barY, fillW, barH);
                }

                string pct = _total > 0 ? $"{(_current * 100 / _total)}%" : "";
                using var pctFont = new Font("Segoe UI Variable Display", 10, FontStyle.Bold);
                var size = g.MeasureString(pct, pctFont);
                g.DrawString(pct, pctFont, new SolidBrush(Color.FromArgb(180, 180, 180)), 484 - size.Width, 145);

                using var tipFont = new Font("Segoe UI Variable Display", 9);
                g.DrawString("請稍候，正在背景高速處理中...", tipFont, new SolidBrush(Color.FromArgb(100, 100, 100)), 36, 220);
            }

            using var targetG = Graphics.FromHdc(hdc);
            targetG.DrawImage(bmp, 0, 0);
        }
    }
}
