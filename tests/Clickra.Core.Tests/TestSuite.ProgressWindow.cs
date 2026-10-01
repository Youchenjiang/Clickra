using System;
using System.IO;

namespace Clickra.Core.Tests;

/// <summary>
/// 進度視窗「縮到系統匣」的守門。標題列的最小化按鈕是作業系統畫的，我們只接得到
/// WM_SYSCOMMAND／SC_MINIMIZE，所以這條規則只能在原始碼層釘住。釘的是「兩條入口是否走
/// 同一條路」與「有沒有變成單程票」，不是排版。
///
/// 背景：先前只有自繪的 ↘ 按鈕會建立系統匣圖示，按標題列的「—」則單純縮到工作列——
/// 視窗還在跑，但系統匣的進度 tooltip 完全不見了。
/// </summary>
static partial class TestSuite
{
    public static void RegisterProgressWindowTests(TestRunner runner)
    {
        runner.Run("Progress window: every minimize goes through the tray path", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

            string dir = Path.Combine(root, "src", "Clickra.CLI", "Progress");
            string controls = StripComments(File.ReadAllText(Path.Combine(dir, "ProgressWindow.Controls.cs")));
            string tray = StripComments(File.ReadAllText(Path.Combine(dir, "ProgressWindow.Tray.cs")));

            // The title bar's minimize button reaches the app only as WM_SYSCOMMAND; without this
            // dispatch the window drops to the taskbar and the tray indicator disappears.
            Assert.True(controls.Contains("0x0112", StringComparison.Ordinal),
                "ProgressWindow must dispatch WM_SYSCOMMAND (0x0112) for the title-bar minimize button.");

            // One implementation of "add the tray icon, then hide": a second call site spelling
            // both steps out again is exactly how one entry point drifts from the other.
            string minimize = MethodBody(tray, "private void MinimizeToTray(IntPtr hwnd)");
            Assert.True(minimize.Contains("SetupTrayIcon(hwnd)", StringComparison.Ordinal),
                "MinimizeToTray must add the tray icon — it is the only place the progress tooltip lives.");
            Assert.True(minimize.Contains("ShowWindow(hwnd, 0)", StringComparison.Ordinal),
                "MinimizeToTray must hide the window (SW_HIDE).");

            // Both entry points must delegate rather than re-implement the two steps.
            Assert.True(MethodBody(controls, "private void HandleProgressClick").Contains("MinimizeToTray(hwnd)", StringComparison.Ordinal),
                "The self-drawn minimize button must send the window to the tray through MinimizeToTray.");
            Assert.True(MethodBody(controls, "private IntPtr? HandleSysCommand").Contains("MinimizeToTray(hwnd)", StringComparison.Ordinal),
                "The title bar's minimize button must send the window to the tray through MinimizeToTray.");
            Assert.False(controls.Contains("SetupTrayIcon(", StringComparison.Ordinal),
                "Message handlers must not add the tray icon themselves; that belongs to MinimizeToTray alone.");

            // The handler owns only the minimize command (SC_MINIMIZE = 0xF020) and defers every
            // other system command, so move/close/menu behaviour stays with Windows.
            string sysCommand = MethodBody(controls, "private IntPtr? HandleSysCommand");
            Assert.True(sysCommand.Contains("0xF020", StringComparison.Ordinal),
                "HandleSysCommand must compare against SC_MINIMIZE (0xF020).");
            Assert.True(sysCommand.Contains("return null", StringComparison.Ordinal),
                "HandleSysCommand must defer non-minimize system commands instead of swallowing them.");

            // A finished or failed job stops reporting progress and auto-closes; pinning a tray
            // icon for those states would leave an icon behind for a window that is going away.
            Assert.True(sysCommand.Contains("_completed", StringComparison.Ordinal) &&
                        sysCommand.Contains("_hasError", StringComparison.Ordinal),
                "HandleSysCommand must keep the ordinary minimize once the job is completed or failed.");
        });

        runner.Run("Progress window: a window sent to the tray can always come back", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

            string dir = Path.Combine(root, "src", "Clickra.CLI", "Progress");
            string controls = StripComments(File.ReadAllText(Path.Combine(dir, "ProgressWindow.Controls.cs")));
            string window = StripComments(File.ReadAllText(Path.Combine(dir, "ProgressWindow.cs")));
            string win32 = StripComments(File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Native", "Win32.cs")));

            // Minimizing to the tray is only acceptable because the tray icon restores the window;
            // if the round trip breaks, hiding the window becomes a one-way trip.
            Assert.True(controls.Contains("WM_TRAYICON", StringComparison.Ordinal),
                "ProgressWindow must receive tray icon callbacks to restore the window.");

            string restore = MethodBody(controls, "private IntPtr HandleTrayIcon");
            Assert.True(restore.Contains("RemoveTrayIcon()", StringComparison.Ordinal),
                "Restoring the window must drop the tray icon, otherwise a stale icon outlives the affordance.");
            Assert.True(restore.Contains("ShowWindow(hwnd, 5)", StringComparison.Ordinal) &&
                        restore.Contains("ShowWindow(hwnd, 9)", StringComparison.Ordinal),
                "Restoring from the tray must show and restore the window, not just re-show it.");

            (string Token, string Failure)[] trayContracts =
            {
                ("0x0202", "HandleTrayIcon must handle WM_LBUTTONUP (0x0202) for single-click restore."),
                ("0x0203", "HandleTrayIcon must handle WM_LBUTTONDBLCLK (0x0203) for double-click restore."),
                ("0x0205", "HandleTrayIcon must handle WM_RBUTTONUP (0x0205) for context menu popup."),
                ("ShowTrayActionMenu(", "HandleTrayIcon must delegate native popup-menu lifetime to the Win32 tray menu helper."),
                ("command != TrayPopupCommand.Unavailable", "WM_NULL must only be posted after the native popup menu was successfully created."),
                ("cli_tray_restore", "Tray context menu must contain localized restore item."),
                ("cli_tray_cancel", "Tray context menu must contain localized cancel item.")
            };
            foreach ((string token, string failure) in trayContracts)
            {
                Assert.True(restore.Contains(token, StringComparison.Ordinal), failure);
            }

            int cancelBranch = restore.IndexOf("else if (command == TrayPopupCommand.Cancel)", StringComparison.Ordinal);
            Assert.True(cancelBranch >= 0,
                "Tray cancel selection must have its own managed command branch.");
            string cancelBody = restore[cancelBranch..];
            Assert.True(cancelBody.Contains("SendMessageW(hwnd, 0x0010", StringComparison.Ordinal),
                "Tray cancel item must forward WM_CLOSE to the existing HandleClose cancellation workflow.");

            string trayMenu = MethodBody(win32, "public static TrayPopupCommand ShowTrayActionMenu");
            int finallyBlock = trayMenu.IndexOf("finally", StringComparison.Ordinal);
            Assert.True(finallyBlock >= 0 &&
                        trayMenu[finallyBlock..].Contains("DestroyMenuNative(menu)", StringComparison.Ordinal),
                "The Win32 tray popup helper must destroy the native menu from its finally block.");

            // The icon must never outlive the window.
            Assert.True(MethodBody(window, "private void CleanupResources").Contains("RemoveTrayIcon()", StringComparison.Ordinal),
                "CleanupResources must remove the tray icon so a closed progress window leaves nothing behind.");
        });
    }

    /// <summary>Body of the member whose declaration starts at <paramref name="signature"/>. It ends
    /// at that member's own closing brace, the only line indented exactly eight spaces, so it works
    /// for the last member in a file too. The source must already have its comments stripped.</summary>
    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected to find '{signature}' in the Win32 progress window sources.");

        const string memberClose = "\n        }";
        int end = source.IndexOf(memberClose, start + signature.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Expected '{signature}' to end with a member-level brace.");
        return source[start..(end + memberClose.Length)];
    }
}
