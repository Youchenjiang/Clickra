using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Clickra_Fluent;

/// <summary>
/// 全 app 單一系統匣圖示（IDM 模型，見 docs/development/fluent_aot_parity.md G1）。
/// 取代各頁面各自管理匣圖示的做法：
/// <list type="bullet">
/// <item>雙擊 → 還原所有背景轉換視窗</item>
/// <item>右鍵 → 彈出選單：「還原所有轉換視窗」＋ 每個背景任務一列（點選還原該視窗）</item>
/// </list>
/// 圖示只在有背景任務時存在（閒置即無匣圖示，維持解除安裝零殘留約束）。
/// </summary>
internal sealed class TrayService
{
    public static TrayService Instance { get; } = new();

    private readonly List<TrayEntry> _entries = new();
    private TrayIcon? _icon;

    private sealed class TrayEntry
    {
        public required Window Window;
        public required string Label;
        public int Percent;
    }

    private TrayService() { }

    /// <summary>登記一個縮到匣的背景轉換視窗（重複登記無效）。</summary>
    public void AddBackgroundWindow(Window window, string label)
    {
        if (window == null || _entries.Exists(e => e.Window == window)) return;
        _entries.Add(new TrayEntry { Window = window, Label = label });
        EnsureIcon();
        RefreshTooltip();
    }

    /// <summary>移除背景轉換視窗（還原、完成或關閉時）；全部移除後匣圖示消失。</summary>
    public void RemoveBackgroundWindow(Window? window)
    {
        if (window == null) return;
        if (_entries.RemoveAll(e => e.Window == window) == 0) return;
        if (_entries.Count == 0)
        {
            RemoveIcon();
        }
        else
        {
            RefreshTooltip();
        }
    }

    /// <summary>更新某個背景任務的進度 %（tooltip 與選單顯示）。</summary>
    public void UpdateProgress(Window window, string label, int percent)
    {
        var entry = _entries.Find(e => e.Window == window);
        if (entry == null) return;
        entry.Label = label;
        entry.Percent = Math.Clamp(percent, 0, 100);
        RefreshTooltip();
    }

    /// <summary>雙擊匣圖示：還原所有背景視窗。</summary>
    public void RestoreAll()
    {
        foreach (var entry in _entries.ToArray())
        {
            RestoreWindow(entry.Window);
        }
    }

    private void RestoreWindow(Window window)
    {
        RemoveBackgroundWindow(window);
        App.GetTaskProgressPage(window)?.MarkRestored();
        window.AppWindow.Show();
        window.Activate();
    }

    private void EnsureIcon()
    {
        if (_icon != null) return;
        _icon = new TrayIcon("Clickra");
        _icon.DoubleClick += RestoreAll;
        _icon.RightClick += ShowMenu;
    }

    private void RemoveIcon()
    {
        if (_icon == null) return;
        _icon.DoubleClick -= RestoreAll;
        _icon.RightClick -= ShowMenu;
        _icon.Dispose();
        _icon = null;
    }

    private void RefreshTooltip()
    {
        if (_icon == null) return;
        if (_entries.Count == 1)
        {
            _icon.SetTooltip($"Clickra - {EntryText(_entries[0])}");
        }
        else if (_entries.Count > 1)
        {
            _icon.SetTooltip($"Clickra - {_entries.Count} 個轉換背景執行中");
        }
        else
        {
            _icon.SetTooltip("Clickra");
        }
    }

    private static string EntryText(TrayEntry entry)
        => entry.Percent > 0 ? $"{entry.Label} - {entry.Percent}%" : entry.Label;

    private void ShowMenu()
    {
        if (_entries.Count == 0 || _icon == null) return;

        const uint MF_STRING = 0x00000000;
        const uint MF_SEPARATOR = 0x00000800;
        const uint TPM_RETURNCMD = 0x00000100;
        const uint TPM_NONOTIFY = 0x00000080;
        const uint TPM_BOTTOMALIGN = 0x00000020;

        uint idRestoreAll = 1;
        const uint idFirstTask = 100;

        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenuW(menu, MF_STRING, idRestoreAll, "還原所有轉換視窗");
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            uint id = idFirstTask;
            foreach (var entry in _entries)
            {
                AppendMenuW(menu, MF_STRING, id++, EntryText(entry));
            }

            GetCursorPos(out POINT pt);
            // TPM_RETURNCMD：由回傳值取得選中的命令，避免對 message-only 視窗 SendMessage。
            uint cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_NONOTIFY | TPM_BOTTOMALIGN,
                pt.X, pt.Y, IntPtr.Zero, IntPtr.Zero);

            if (cmd == idRestoreAll)
            {
                RestoreAll();
            }
            else if (cmd >= idFirstTask)
            {
                int index = (int)(cmd - idFirstTask);
                if (index >= 0 && index < _entries.Count)
                {
                    RestoreWindow(_entries[index].Window);
                }
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr hMenu);
}
