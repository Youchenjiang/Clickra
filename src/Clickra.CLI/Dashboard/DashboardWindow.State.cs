using System;
using System.Collections.Generic;
using System.Drawing;
using Clickra.Core;
using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {
        // UI State Variables
        static int _activeTab = 0; // 0: Overview, 1: Convert, 2: History, 3: Settings
        static int _hoveredElement = -1; // IDs of hovered elements
        
        // Convert tab state
        static int _convertCommandIndex = 1; // Default: 1 (word2pdf)
        static List<string> _selectedFiles = new List<string>();
        private static readonly string[] ConvertCommands = { "ppt2pdf", "word2pdf", "excel2pdf", "merge-pdf", "img2pdf", "img-merge", "img-stitch", "translate-pdf", "decrypt-pdf" };
        
        // Language Dropdown state
        static bool _langDropdownOpen = false;
        static string _langSearchQuery = "";
        static int _langHoveredIndex = 0;
        private static readonly List<(string Code, string NativeName, string EnglishName)> SupportedLanguages = new()
        {
            ("zh-TW", "繁體中文", "Traditional Chinese"),
            ("zh-CN", "简体中文", "Simplified Chinese"),
            ("en-US", "English", "English"),
            ("ja-JP", "日本語", "Japanese"),
            ("ko-KR", "한국어", "Korean")
        };

        // PDF Translation settings state

        static bool _pdfLangDropdownOpen = false;
        static int _pdfLangHoveredIndex = 0;
        private static readonly (string Code, string Name)[] PdfLangs =
        {
            ("zh-TW", "繁體中文 (Traditional Chinese)")
        };

        static int _pdfLangDropdownY = 0;
        
        // History & Statistics Cache
        static List<ClickraStorage.HistoryEntry> _historyEntries = new List<ClickraStorage.HistoryEntry>();

        static int _langScrollOffset = 0;
        static int _statTotal = 0;
        static int _statSuccess = 0;
        static int _statFailed = 0;

        // Double Buffering & Colors
        static Bitmap? _bufferBmp;
        static Graphics? _bufferGraphics;

        // Fonts
        static Font? _titleFont;
        static Font? _subFont;
        static Font? _tabFont;
        static Font? _contentTitleFont;
        static Font? _sectionFont;
        static Font? _bodyFont;
        static Font? _tagFont;
        static Font? _iconFont;


        // Extra UI State Variables for v3.0.9
        static float _dpiScale = 1.0f;
        static int _expandedHistoryIndex = -1;
        public static readonly Dictionary<(int, int), float> DetailScrollOffsets = new();
        static System.Threading.Mutex? _mutex;
        static float _aboutBtnY = 365;
        static float _githubBtnY = 240;
        static int _langDropdownY = 390;
        static float _sidebarWidth = 170f;
        static IntPtr _hIcon = IntPtr.Zero;
        static float _wSource = 110f;
        static float _wDesktop = 65f;
        static float _wDownloads = 80f;
        static float _wCustom = 100f;
        static float _wGit = 160f;
        static float _wGmail = 160f;

        // Content Area Scroll State
        static float _contentScrollX = 0;
        static float _contentScrollY = 0;
        static bool _isDraggingScrollX = false;
        static bool _isDraggingScrollY = false;
        static float _dragStartMouseX = 0;
        static float _dragStartMouseY = 0;
        static float _dragStartScrollX = 0;
        static float _dragStartScrollY = 0;

        // Detail scrollbar dragging state
        static bool _isDraggingDetailScroll = false;
        static int _draggingDetailRowIndex = -1;
        static int _draggingDetailFieldIndex = -1;
        static float _dragDetailStartMouseX = 0;
        static float _dragDetailStartOffset = 0;


    }
}
