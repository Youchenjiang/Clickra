using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Drawing;
using Clickra.Core;
using Clickra.Core.Layout;
using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {
        // UI State Variables
        static int _activeTab = 0; // 0: Overview, 1: Convert, 2: History, 3: Settings
        static int _hoveredElement = -1; // IDs of hovered elements
        
        // Convert tab state
        static int _convertCommandIndex = 0; // Default: Word to PDF
        static List<string> _selectedFiles = new List<string>();
        // ConvertCommands / ConvertCommandGroupSizes are derived from the
        // ConvertCommandDefs registry (see DashboardWindow.ConvertRegistry.cs).
        
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

        static int _pdfLangDropdownX = 0;
        static int _pdfLangDropdownY = 0;
        static int _pdfLangDropdownWidth = DashboardLayout.DropdownWidth;
        
        // History & Statistics Cache
        //
        // 一份清單三個切片（進行中、待繼續、已完成），與 Fluent 的 History 頁共用 Core 的
        // HistoryFeed：狀態文字、剩餘期限、檔案描述都已經在模型裡算好一次，這裡只排版。
        // 一次 250ms 的更新讀取一次，而不是每張畫格重新掃任務目錄、逐列查檔案時間。
        static HistoryFeed _historyFeed = HistoryFeed.Empty;

        static IReadOnlyList<HistoryItem> ActiveItems => _historyFeed.OfKind(HistoryItemKind.Active);
        static IReadOnlyList<HistoryItem> ParkedItems => _historyFeed.OfKind(HistoryItemKind.Parked);

        /// <summary>已完成紀錄：歷史列表可展開明細，也是統計卡的來源。</summary>
        static IReadOnlyList<HistoryItem> CompletedItems => _historyFeed.OfKind(HistoryItemKind.Completed);

        // 待繼續任務列右端的動作按鈕：每列四個（縮短、延長、繼續、取消）。元素 id 由此推導，
        // 幾何則來自 Core 的版面表（DashboardLayout.ParkedRowActionRect）。
        const int ParkedActionElementBase = 200;
        const int ParkedActionShorten = DashboardLayout.ParkedActionShorten;
        const int ParkedActionExtend = DashboardLayout.ParkedActionExtend;
        const int ParkedActionResume = DashboardLayout.ParkedActionResume;
        const int ParkedActionCancel = DashboardLayout.ParkedActionCancel;
        const int ParkedActionCount = DashboardLayout.ParkedActionCount;

        static int ParkedActionElement(int rowIndex, int action) =>
            ParkedActionElementBase + rowIndex * ParkedActionCount + action;

        /// <summary>這個元素 id 是不是待繼續列的動作按鈕；列數與動作數由同一份定義決定。</summary>
        static bool IsParkedActionElement(int element) =>
            element >= ParkedActionElementBase && element < ParkedActionElementBase + ParkedItems.Count * ParkedActionCount;

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
        static int _langDropdownX = 0;
        static int _langDropdownY = 390;
        static int _langDropdownWidth = DashboardLayout.DropdownWidth;
        static float _sidebarWidth = 170f;
        static IntPtr _hIcon = IntPtr.Zero;
        static float _wSource = 110f;
        static float _wDesktop = 65f;
        static float _wDownloads = 80f;
        static float _wCustom = 100f;
        static float _wEngineAuto = 80f;
        static float _wEngineMicrosoft = 125f;
        static float _wEngineLibreOffice = 110f;
        static float _wLibreOfficeBrowse = 120f;
        static float _wLibreOfficeDownload = 125f;
        static float _wLibreOfficeUninstall = 125f;
        static float _wLibreOfficeAdopt = 140f;
        static float _wGit = 160f;
        static float _wGmail = 160f;
        static float _overviewContentHeight = 430f;
        static readonly Dictionary<int, RectangleF> _settingsHitRects = new();
        static float _settingsContentHeight = 740f;
        static readonly object _libreOfficeDownloadLock = new();
        static bool _libreOfficeDownloadInProgress = false;
        static int _libreOfficeDownloadProgress = 0;
        static string _libreOfficeDownloadStatus = "";
        static readonly ConcurrentQueue<Action> _uiActions = new();

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

        // PDF Compress Slider state
        static float _pdfSliderTrackX = 0;
        static float _pdfSliderTrackW = 300;
        static bool _isDraggingPdfSlider = false;

        // Dynamic settings sliders state
        static float _dynamicSliderTrackX = 0;
        static float _dynamicSliderTrackW = 300;
        static bool _isDraggingDynamicSlider = false;
        static int _dynamicSliderDescriptorIndex = -1;


    }
}
