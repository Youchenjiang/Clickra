using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Clickra.Core;
using Clickra.Core.Processors;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI;

/// <summary>NativeAOT fallback for the one-shot Markdown-to-PDF options dialog.</summary>
internal static class MarkdownOptionsPrompt
{
    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    private static readonly WndProcDelegate WndProcRoot = WndProc;
    private static readonly ConcurrentDictionary<IntPtr, PromptState> States = new();

    private const int IdConvert = 2001;
    private const int IdCancel = 2002;
    private const uint WmCommand = 0x0111;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint CbAddString = 0x0143;
    private const uint CbGetCurSel = 0x0147;
    private const uint CbSetCurSel = 0x014E;
    private const uint WsTabStop = 0x00010000;
    private const uint WsVScroll = 0x00200000;
    private const uint CbsDropdownList = 0x0003;

    public static Dictionary<string, object>? Show()
    {
        var state = new PromptState();
        string className = $"ClickraMarkdownOptions_{Environment.ProcessId}";
        IntPtr classNamePtr = Marshal.StringToHGlobalUni(className);
        try
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcRoot),
                hInstance = GetModuleHandle(null),
                hCursor = LoadCursorW(IntPtr.Zero, 32512),
                hbrBackground = (IntPtr)(16 + 1),
                lpszClassName = classNamePtr
            };
            RegisterClassEx(ref wc);

            var rect = new RECT { left = 0, top = 0, right = 500, bottom = 410 };
            AdjustWindowRectEx(ref rect, WS_OVERLAPPED_FIXED, false, 0);
            IntPtr hwnd = CreateWindowEx(
                0, className, Localization.T("md_options_title"), WS_OVERLAPPED_FIXED,
                CW_USEDEFAULT, CW_USEDEFAULT, rect.right - rect.left, rect.bottom - rect.top,
                IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) return null;
            States[hwnd] = state;

            int dark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_DARK_MODE, ref dark, sizeof(int));
            CreateControls(hwnd, wc.hInstance, state);
            ShowWindow(hwnd, SW_SHOW);
            SetForegroundWindow(hwnd);

            while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (IsDialogMessageW(hwnd, ref msg)) continue;
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            return state.Result;
        }
        finally
        {
            Marshal.FreeHGlobal(classNamePtr);
        }
    }

    private static void CreateControls(IntPtr hwnd, IntPtr instance, PromptState state)
    {
        string lang = ClickraStorage.GetSetting(ClickraSettings.Language);
        CreateLabel(hwnd, instance, Localization.T("md_options_hint", lang), 24, 20, 450, 44);
        CreateLabel(hwnd, instance, Localization.T("md_options_style", lang), 24, 78, 140, 22);
        state.StyleCombo = CreateCombo(hwnd, instance, 170, 74, 300,
            Localization.T("md_options_style_default", lang),
            Localization.T("md_options_style_minimal", lang),
            Localization.T("md_options_style_academic", lang));

        CreateLabel(hwnd, instance, Localization.T("md_options_paper", lang), 24, 126, 140, 22);
        state.PaperCombo = CreateCombo(hwnd, instance, 170, 122, 300, "A4", "Letter");

        CreateLabel(hwnd, instance, Localization.T("md_options_text_size", lang), 24, 174, 140, 22);
        state.TextCombo = CreateCombo(hwnd, instance, 170, 170, 300,
            Localization.T("md_options_text_small", lang),
            Localization.T("md_options_text_standard", lang),
            Localization.T("md_options_text_large", lang));
        SendMessageW(state.TextCombo, CbSetCurSel, (IntPtr)1, IntPtr.Zero);

        CreateLabel(hwnd, instance, Localization.T("md_options_more", lang), 24, 226, 446, 22);
        CreateLabel(hwnd, instance, Localization.T("md_options_code_theme", lang), 24, 260, 140, 22);
        state.CodeCombo = CreateCombo(hwnd, instance, 170, 256, 300,
            Localization.T("md_options_code_dark", lang),
            Localization.T("md_options_code_light", lang));

        CreateWindowEx(0, "BUTTON", Localization.T("md_options_convert", lang),
            WS_CHILD | WS_VISIBLE | WsTabStop | 0x00000001,
            270, 340, 96, 32, hwnd, (IntPtr)IdConvert, instance, IntPtr.Zero);
        CreateWindowEx(0, "BUTTON", Localization.T("dialog_cancel", lang),
            WS_CHILD | WS_VISIBLE | WsTabStop,
            378, 340, 92, 32, hwnd, (IntPtr)IdCancel, instance, IntPtr.Zero);
    }

    private static void CreateLabel(IntPtr parent, IntPtr instance, string text, int x, int y, int width, int height) =>
        CreateWindowEx(0, "STATIC", text, WS_CHILD | WS_VISIBLE,
            x, y, width, height, parent, IntPtr.Zero, instance, IntPtr.Zero);

    private static IntPtr CreateCombo(IntPtr parent, IntPtr instance, int x, int y, int width, params string[] items)
    {
        IntPtr combo = CreateWindowEx(0, "COMBOBOX", "",
            WS_CHILD | WS_VISIBLE | WsTabStop | WsVScroll | CbsDropdownList,
            x, y, width, 180, parent, IntPtr.Zero, instance, IntPtr.Zero);
        foreach (string item in items) AddComboItem(combo, item);
        SendMessageW(combo, CbSetCurSel, IntPtr.Zero, IntPtr.Zero);
        return combo;
    }

    private static void AddComboItem(IntPtr combo, string text)
    {
        IntPtr value = Marshal.StringToHGlobalUni(text);
        try { SendMessageW(combo, CbAddString, IntPtr.Zero, value); }
        finally { Marshal.FreeHGlobal(value); }
    }

    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmCommand)
        {
            int id = (int)(wParam.ToInt64() & 0xFFFF);
            if (id == IdConvert && States.TryGetValue(hwnd, out PromptState? state))
            {
                state.Result = MarkdownPdfOptions.Create(
                    ComboValue(state.StyleCombo, MarkdownPdfOptions.ThemeDefault, MarkdownPdfOptions.ThemeMinimal, MarkdownPdfOptions.ThemeAcademic),
                    ComboValue(state.PaperCombo, MarkdownPdfOptions.PaperA4, MarkdownPdfOptions.PaperLetter),
                    ComboValue(state.TextCombo, MarkdownPdfOptions.TextSmall, MarkdownPdfOptions.TextStandard, MarkdownPdfOptions.TextLarge),
                    ComboValue(state.CodeCombo, MarkdownPdfOptions.CodeDark, MarkdownPdfOptions.CodeLight));
                DestroyWindow(hwnd);
                return IntPtr.Zero;
            }
            if (id == IdCancel)
            {
                DestroyWindow(hwnd);
                return IntPtr.Zero;
            }
        }
        else if (msg == WmClose)
        {
            DestroyWindow(hwnd);
            return IntPtr.Zero;
        }
        else if (msg == WmDestroy)
        {
            States.TryRemove(hwnd, out _);
            PostQuitMessage(0);
            return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static string ComboValue(IntPtr combo, params string[] values)
    {
        int index = (int)SendMessageW(combo, CbGetCurSel, IntPtr.Zero, IntPtr.Zero);
        return index >= 0 && index < values.Length ? values[index] : values[0];
    }

    private sealed class PromptState
    {
        public IntPtr StyleCombo { get; set; }
        public IntPtr PaperCombo { get; set; }
        public IntPtr TextCombo { get; set; }
        public IntPtr CodeCombo { get; set; }
        public Dictionary<string, object>? Result { get; set; }
    }
}
