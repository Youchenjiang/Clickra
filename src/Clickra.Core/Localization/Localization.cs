using System;
using System.Collections.Generic;

namespace Clickra.Core
{
    public static class Localization
    {
        private const string LangTw = "zh-TW";
        private const string LangCn = "zh-CN";
        private const string LangEn = "en-US";
        private const string LangJa = "ja-JP";
        private const string LangKo = "ko-KR";
        private const string AppExcel = "Excel";
        private const string KeyCmdCompressPdf = "cmd_compress_pdf";
        private const string KeyLibreOfficeExternalNote = "setting_libreoffice_external_note";
        private const string KeyLibreOfficeExternalHint = "setting_libreoffice_external_hint";
        private const string MicrosoftOfficeLabel = "Microsoft Office";
        private const string LibreOfficeLabel = "LibreOffice";
        private const string WordToPdfLabel = "Word → PDF";
        private const string ExcelToPdfLabel = "Excel → PDF";
        private const string PptToPdfLabel = "PPT → PDF";
        private const string KeyFluentTitle = "setting_fluent_title";
        private const string KeyFluentDescription = "setting_fluent_desc";
        private const string KeyFluentReady = "setting_fluent_ready";
        private const string KeyFluentNotInstalled = "setting_fluent_not_installed";
        private const string KeyFluentInstall = "setting_fluent_install";
        private const string RetentionDayZh = "{0} 天";
        private const string OfficeName = "Office";

        private static readonly Dictionary<string, Dictionary<string, string>> Translations = new(StringComparer.OrdinalIgnoreCase)
        {
            [LangTw] = new(StringComparer.OrdinalIgnoreCase),
            [LangCn] = new(StringComparer.OrdinalIgnoreCase),
            [LangEn] = new(StringComparer.OrdinalIgnoreCase),
            [LangJa] = new(StringComparer.OrdinalIgnoreCase),
            [LangKo] = new(StringComparer.OrdinalIgnoreCase)
        };

        /// <summary>Maps a language code (or the current UI culture when empty) to one of the
        /// supported language keys, defaulting to Traditional Chinese.</summary>
        public static string NormalizeLanguageCode(string langCode)
        {
            if (string.IsNullOrEmpty(langCode))
            {
                langCode = System.Globalization.CultureInfo.CurrentUICulture.Name;
            }

            if (langCode.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return LangEn;
            if (langCode.Equals(LangCn, StringComparison.OrdinalIgnoreCase)) return LangCn;
            if (langCode.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return LangJa;
            if (langCode.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return LangKo;
            if (langCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return LangTw;

            return LangTw;
        }

        /// <summary>Translates a resource key into the target language, falling back to
        /// Traditional Chinese and finally to the key itself.</summary>
        public static string T(string key, string langCode)
        {
            string targetKey = NormalizeLanguageCode(langCode);

            // Try looking up the logical ID in the target language
            if (Translations.TryGetValue(targetKey, out var dict) && dict.TryGetValue(key, out var translated))
            {
                return translated;
            }

            // Fallback: If not found in target language, try Traditional Chinese (zh-TW)
            if (targetKey != LangTw && Translations.TryGetValue(LangTw, out var twDict) && twDict.TryGetValue(key, out var twTranslated))
            {
                return twTranslated;
            }

            // Fallback: If not found anywhere, return the key as-is
            return key;
        }

        /// <summary>
        /// Translates a resource key using the currently configured language setting.
        /// </summary>
        public static string T(string key) =>
            T(key, ClickraStorage.GetSetting(ClickraSettings.Language));

        /// <summary>
        /// Translates a resource key using the currently configured language setting and formats it with arguments.
        /// </summary>
        public static string T(string key, params object[] args) =>
            args is { Length: > 0 } ? string.Format(T(key), args) : T(key);

        /// <summary>Canonical list of the 5 supported language codes.</summary>
        public static IReadOnlyList<string> SupportedLanguages => new[] { LangTw, LangCn, LangEn, LangJa, LangKo };

        /// <summary>
        /// Checks if an exact, non-empty translation exists for the given key in the specified language (bypassing fallback to zh-TW).
        /// </summary>
        public static bool HasExactTranslation(string key, string langCode)
        {
            string targetKey = NormalizeLanguageCode(langCode);
            return Translations.TryGetValue(targetKey, out var dict) &&
                   dict.TryGetValue(key, out var val) &&
                   !string.IsNullOrWhiteSpace(val);
        }

        /// <summary>
        /// Returns all unique translation keys registered in any language dictionary.
        /// </summary>
        public static IReadOnlyCollection<string> GetAllKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dict in Translations.Values)
            {
                foreach (var k in dict.Keys)
                {
                    keys.Add(k);
                }
            }
            return keys;
        }

        /// <summary>
        /// Analyzes translations for the specified keys (or all registered keys if null) across
        /// the given languages (or all supported languages if null), and returns a dictionary
        /// mapping each language to its list of missing keys.
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> FindMissingTranslations(
            IEnumerable<string>? keysToCheck = null,
            IEnumerable<string>? languagesToCheck = null)
        {
            var langs = (languagesToCheck ?? SupportedLanguages).ToArray();
            var keys = (keysToCheck ?? GetAllKeys()).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();

            var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (string lang in langs)
            {
                List<string> missing = keys
                    .Where(key => !HasExactTranslation(key, lang))
                    .ToList();

                if (missing.Count > 0)
                {
                    result[lang] = missing;
                }
            }

            return result;
        }

        /// <summary>
        /// Formats a human-readable diagnosis report grouping missing keys by language.
        /// </summary>
        public static string FormatMissingReport(IReadOnlyDictionary<string, IReadOnlyList<string>> missing)
        {
            if (missing.Count == 0) return string.Empty;

            int total = 0;
            foreach (var list in missing.Values) total += list.Count;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Missing translations detected across languages (total missing: {total} in {missing.Count} language(s)):");

            foreach (var kvp in missing.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine();
                sb.AppendLine($"[{kvp.Key}] ({kvp.Value.Count} missing):");
                foreach (string key in kvp.Value)
                {
                    sb.AppendLine($"  - {key}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        static Localization()
        {
            RegisterGeneralTranslations();
            RegisterCompressionTranslations();
            RegisterProcessingTranslations();
            RegisterLibreOfficeManagementTranslations();
            RegisterFluentDashboardTranslations();
            RegisterCliTranslations();
            RegisterDiagnosticsEmailTranslations();
        }


        private static void RegisterGeneralTranslations()
        {
            var data = new (string Key, string Tw, string Cn, string En, string Ja, string Ko)[]
            {
                ("tab_status", "狀態", "状态", "Status", "状態", "상태"),
                ("tab_history", "歷史", "历史", "History", "履歴", "기록"),
                ("tab_settings", "設定", "设置", "Settings", "設定", "설정"),
                ("setting_silent_title", "背景靜默轉檔", "背景静默转档", "Silent Mode", "サイレントモード", "자동 변환 모드"),
                ("setting_silent_desc", "在右鍵選單點擊時直接於背景處理，不顯示進度視窗", "在右键菜单点击时直接于背景处理，不显示进度窗口", "Process in background without showing progress window", "進行状況ウィンドウを表示せず、バックグラウンドで処理します", "진행 창을 표시하지 않고 백그라운드에서 바로 처리합니다"),
                ("setting_notify_title", "顯示轉換通知", "显示转换通知", "Show Notifications", "変換通知を表示", "변환 알림 표시"),
                ("setting_notify_desc", "作業完成或失敗後，於系統右下角彈出 Windows Toast 通知", "作业完成或失败后，于系统右下角弹出 Windows Toast 通知", "Show Toast notification when conversion completes/fails", "完了または失敗時に、Windows トースト通知を表示します", "완료 또는 실패 시 Windows 토스트 알림을 표시합니다"),
                ("setting_output_title", "預設輸出路徑", "默认输出路径", "Default Output Path", "既定の出力先", "기본 출력 경로"),
                ("setting_output_desc", "選擇轉換後 PDF 與圖片預設的儲存位置", "选择转换后 PDF 与图片默认的存储位置", "Select the default save location for converted files", "変換後のファイルの保存先を選択します", "변환된 파일의 기본 저장 위치를 선택합니다"),
                ("setting_output_same_as_source", "與來源相同", "与来源相同", "Same as source", "ソースと同じ", "원본과 동일"),
                ("setting_output_desktop", "桌面", "桌面", "Desktop", "デスクトップ", "바탕 화면"),
                ("setting_output_downloads", "下載", "下载", "Downloads", "ダウンロード", "다운로드"),
                ("setting_output_custom", "自訂...", "自定义...", "Custom...", "カスタム...", "사용자 정의..."),
                ("setting_output_selected_path", "已選取路徑", "已选择路径", "Selected path", "選択されたパス", "선택한 경로"),
                ("setting_output_browse_title", "選擇預設輸出資料夾", "选择默认输出文件夹", "Select Default Output Folder", "既定の出力フォルダを選択", "기본 출력 폴더 선택"),
                (KeyFluentTitle, "Fluent 介面", "Fluent 界面", "Fluent Interface", "Fluent インターフェース", "Fluent 인터페이스"),
                (KeyFluentDescription, "安裝 Fluent 介面附加元件以使用現代化 WinUI 3 介面", "安装 Fluent 界面附加组件以使用现代 WinUI 3 界面", "Install Fluent add-on to enable modern WinUI 3 interface", "Fluent アドオンをインストールして最新の WinUI 3 インターフェースを使用します", "Fluent 추가 기능을 설치하여 최신 WinUI 3 인터페이스를 사용합니다"),
                (KeyFluentReady, "Fluent 介面已安裝，下次啟動將自動使用", "Fluent 界面已安装，下次启动将自动使用", "Fluent installed. Will be used on next launch.", "Fluent インターフェースはインストール済みです。次回起動時に自動的に使用されます", "Fluent 인터페이스가 설치되었습니다. 다음 시작 시 자동으로 사용됩니다"),
                (KeyFluentNotInstalled, "Fluent 介面未安裝，目前使用經典介面", "Fluent 界面未安装，当前使用经典界面", "Fluent not installed. Using classic interface.", "Fluent インターフェースは未インストールです。現在クラシックを使用中", "Fluent 인터페이스가 설치되지 않았습니다. 현재 클래식 인터페이스를 사용 중입니다"),
                (KeyFluentInstall, "從 Store 安裝 Fluent 介面", "从 Store 安装 Fluent 界面", "Install Fluent from Store", "Store から Fluent インターフェースをインストール", "Store에서 Fluent 인터페이스 설치"),
                ("overview_engine_status", "轉換引擎狀態", "转换引擎状态", "Conversion Engine Status", "変換エンジンの状態", "변환 엔진 상태"),
                ("overview_stats", "轉換統計", "转换统计", "Conversion Stats", "統計情報", "오늘의 통계"),
                ("overview_stat_total", "總轉換次數", "总转换次数", "Total Conversions", "総変換回数", "총 변환 횟수"),
                ("overview_stat_success", "成功次數", "成功次数", "Successful", "成功回数", "성공 횟수"),
                ("overview_stat_failed", "失敗次數", "失败次数", "Failed", "失敗回数", "실패 횟수"),
                ("overview_office_conversion", "Office 轉檔", "Office 转换", "Office conversion", "Office 変換", "Office 변환"),
                ("overview_engine_active", "使用 {0}", "使用 {0}", "Using {0}", "{0} を使用", "{0} 사용"),
                ("overview_engine_unavailable", "{0} 尚未就緒", "{0} 尚未就绪", "{0} not ready", "{0} は未準備です", "{0} 준비되지 않음"),
                ("history_clear", "清除紀錄", "清除记录", "Clear History", "履歴をクリア", "기록 삭제"),
                ("history_clear_confirm", "您確定要清除所有的轉換歷史紀錄嗎？", "您确定要清除所有的转换历史记录吗？", "Are you sure you want to clear all history?", "すべての変換履歴をクリアしてもよろしいですか？", "모든 변환 기록을 삭제하시겠습니까?"),
                ("history_empty", "尚無任何轉換紀錄。", "尚无任何转换记录。", "No conversion history.", "変換履歴はありません。", "변환 기록이 없습니다."),
                ("status_pending", "等待中", "等待中", "Pending", "待機中", "대기 중"),
                ("status_converting", "轉換中", "转换中", "Converting", "変換中", "변환 중"),
                ("status_success", "成功", "成功", "Success", "成功", "성공"),
                ("status_failed", "失敗", "失败", "Failed", "失敗", "실패"),
                ("status_error", "錯誤", "错误", "Error", "エラー", "오류"),
                ("label_files", "個檔案", "个文件", "files", "個のファイル", "개의 파일"),
                ("setting_lang_title", "介面語言", "界面语言", "Interface Language", "表示言語", "표시 언어"),
                ("setting_lang_desc", "選擇 Dashboard 的顯示語言", "选择 Dashboard 的显示语言", "Select the display language for the Dashboard", "Dashboard の表示言語を選択します", "Dashboard 표시 언어를 선택합니다"),
                ("search_lang_placeholder", "搜尋語言 / Search...", "搜索语言...", "Search language...", "言語を検索...", "언어 검색..."),
                ("engine_pdf", "PDF 處理核心 (PDF Engine)", "PDF 处理核心 (PDF Engine)", "PDF Processing Engine", "PDF 処理エンジン", "PDF 처리 엔진"),
                ("engine_ppt", "PowerPoint 轉換器 (PowerPoint)", "PowerPoint 转换器 (PowerPoint)", "PowerPoint Converter", "PowerPoint 変換器", "PowerPoint 변환기"),
                ("engine_word", "Word 轉換器 (Word)", "Word 转换器 (Word)", "Word Converter", "Word 変換器", "Word 변환기"),
                ("engine_excel", "Excel 轉換器 (Excel)", "Excel 转换器 (Excel)", "Excel Converter", "Excel 変換器", "Excel 변환기"),
                ("engine_libreoffice", "LibreOffice 離線引擎", "LibreOffice 离线引擎", "LibreOffice Offline Engine", "LibreOffice オフラインエンジン", "LibreOffice 오프라인 엔진"),
                ("engine_ready", "已就緒", "已就绪", "Ready", "準備完了", "준비 완료"),
                ("engine_office_not_installed", "Office 未安裝", "Office 未安装", "Office Not Installed", "Office 未インストール", "Office 미설치"),
                ("setting_engine_title", "Office 轉檔引擎", "Office 转档引擎", "Office Conversion Engine", "Office 変換エンジン", "Office 변환 엔진"),
                ("setting_engine_desc", "選擇 Word、Excel、PowerPoint 轉 PDF 時使用的引擎", "选择 Word、Excel、PowerPoint 转 PDF 时使用的引擎", "Choose the engine used for Word, Excel, and PowerPoint to PDF conversion", "Word、Excel、PowerPoint から PDF への変換に使うエンジンを選択します", "Word, Excel, PowerPoint를 PDF로 변환할 때 사용할 엔진을 선택합니다"),
                ("setting_engine_auto", "自動", "自动", "Auto", "自動", "자동"),
                ("setting_engine_microsoft", MicrosoftOfficeLabel, MicrosoftOfficeLabel, MicrosoftOfficeLabel, MicrosoftOfficeLabel, MicrosoftOfficeLabel),
                ("setting_engine_libreoffice", LibreOfficeLabel, LibreOfficeLabel, LibreOfficeLabel, LibreOfficeLabel, LibreOfficeLabel),
                ("setting_libreoffice_ready", "LibreOffice 已就緒", "LibreOffice 已就绪", "LibreOffice ready", "LibreOffice 準備完了", "LibreOffice 준비 완료"),
                ("setting_libreoffice_missing", "LibreOffice 尚未設定", "LibreOffice 尚未设置", "LibreOffice is not configured", "LibreOffice は未設定です", "LibreOffice가 설정되지 않았습니다"),
                ("setting_libreoffice_optional", "Microsoft Office 已就緒；LibreOffice 可作為免費備援引擎。", "Microsoft Office 已就绪；LibreOffice 可作为免费备用引擎。", "Microsoft Office is ready; LibreOffice can be used as a free fallback engine.", "Microsoft Office は準備完了です。LibreOffice は無料の予備エンジンとして使用できます。", "Microsoft Office가 준비되었습니다. LibreOffice는 무료 예비 엔진으로 사용할 수 있습니다."),
                ("setting_libreoffice_browse", "手動指定位置", "手动指定位置", "Choose location", "場所を手動指定", "위치 수동 지정"),
                ("setting_libreoffice_browse_title", "選擇 LibreOffice soffice.exe", "选择 LibreOffice soffice.exe", "Select LibreOffice soffice.exe", "LibreOffice soffice.exe を選択", "LibreOffice soffice.exe 선택"),
                ("setting_libreoffice_invalid", "請選擇 LibreOffice 的 soffice.exe。", "请选择 LibreOffice 的 soffice.exe。", "Please select LibreOffice soffice.exe.", "LibreOffice の soffice.exe を選択してください。", "LibreOffice의 soffice.exe를 선택하세요."),
                ("setting_libreoffice_download", "取得 LibreOffice", "获取 LibreOffice", "Get LibreOffice", "LibreOffice を取得", "LibreOffice 받기"),
                ("setting_libreoffice_download_ready", "LibreOffice 已安裝並啟用：\n{0}", "LibreOffice 已安装并启用：\n{0}", "LibreOffice is installed and enabled:\n{0}", "LibreOffice をインストールして有効にしました：\n{0}", "LibreOffice 설치 및 사용 설정 완료:\n{0}"),
                ("setting_libreoffice_install_restart_required", "LibreOffice 已安裝，但 Windows 需要重新啟動才會完全完成設定。\n{0}", "LibreOffice 已安装，但 Windows 需要重新启动才会完全完成设置。\n{0}", "LibreOffice is installed, but Windows needs a restart to finish configuration.\n{0}", "LibreOffice をインストールしましたが、設定を完了するには Windows の再起動が必要です。\n{0}", "LibreOffice가 설치되었지만 설정을 완료하려면 Windows를 다시 시작해야 합니다.\n{0}"),
                ("setting_libreoffice_download_failed", "LibreOffice 連線、下載、安裝或驗證失敗：\n{0}", "LibreOffice 连接、下载、安装或验证失败：\n{0}", "LibreOffice connection, download, installation, or verification failed:\n{0}", "LibreOffice への接続、ダウンロード、インストール、または検証に失敗しました：\n{0}", "LibreOffice 연결, 다운로드, 설치 또는 검증 실패:\n{0}"),
                ("setting_libreoffice_download_starting", "正在準備下載 LibreOffice...", "正在准备下载 LibreOffice...", "Preparing LibreOffice download...", "LibreOffice のダウンロードを準備しています...", "LibreOffice 다운로드를 준비 중..."),
                ("setting_libreoffice_reinstall_starting", "正在準備重新安裝 LibreOffice...", "正在准备重新安装 LibreOffice...", "Preparing LibreOffice reinstall...", "LibreOffice の再インストールを準備しています...", "LibreOffice 재설치를 준비 중..."),
                ("setting_libreoffice_download_progress", "正在下載 LibreOffice... {0}%", "正在下载 LibreOffice... {0}%", "Downloading LibreOffice... {0}%", "LibreOffice をダウンロード中... {0}%", "LibreOffice 다운로드 중... {0}%"),
                ("setting_libreoffice_verifying", "正在驗證 LibreOffice 安裝檔...", "正在验证 LibreOffice 安装文件...", "Verifying LibreOffice installer...", "LibreOffice インストーラーを検証しています...", "LibreOffice 설치 파일을 검증 중..."),
                ("setting_libreoffice_installing", "正在安裝 LibreOffice...", "正在安装 LibreOffice...", "Installing LibreOffice...", "LibreOffice をインストール中...", "LibreOffice 설치 중..."),
                ("setting_libreoffice_download_in_progress", "LibreOffice 正在下載、安裝或移除中，請稍候。", "LibreOffice 正在下载、安装或移除中，请稍候。", "LibreOffice is already downloading, installing, or being removed. Please wait.", "LibreOffice のダウンロード、インストール、または削除を実行中です。お待ちください。", "LibreOffice 다운로드, 설치 또는 제거가 진행 중입니다. 잠시 기다려 주세요."),
                ("setting_libreoffice_validated", "LibreOffice 驗證成功：\n{0}", "LibreOffice 验证成功：\n{0}", "LibreOffice validated:\n{0}", "LibreOffice の検証に成功しました：\n{0}", "LibreOffice 검증 성공:\n{0}"),
                ("setting_libreoffice_validation_failed", "LibreOffice 安裝不完整。請重新安裝 LibreOffice，或選擇完整安裝目錄中的 soffice.exe。", "LibreOffice 安装不完整。请重新安装 LibreOffice，或选择完整安装目录中的 soffice.exe。", "LibreOffice installation is incomplete. Reinstall LibreOffice, or choose soffice.exe from a complete installation folder.", "LibreOffice のインストールが不完全です。再インストールするか、完全なインストール先の soffice.exe を選択してください。", "LibreOffice 설치가 완전하지 않습니다. LibreOffice를 다시 설치하거나 전체 설치 폴더의 soffice.exe를 선택하세요."),
                ("setting_libreoffice_update", "更新 LibreOffice", "更新 LibreOffice", "Update LibreOffice", "LibreOffice を更新", "LibreOffice 업데이트"),
                ("setting_libreoffice_reinstall", "重新安裝 LibreOffice", "重新安装 LibreOffice", "Reinstall LibreOffice", "LibreOffice を再インストール", "LibreOffice 재설치"),
                ("setting_libreoffice_uninstall", "移除 LibreOffice", "移除 LibreOffice", "Remove LibreOffice", "LibreOffice を削除", "LibreOffice 제거"),
                ("setting_libreoffice_uninstall_confirm", "Clickra 會在背景移除系統中的 LibreOffice，並清除 Clickra 的 LibreOffice 設定。移除期間可能會要求系統權限。是否繼續？", "Clickra 会在后台移除系统中的 LibreOffice，并清除 Clickra 的 LibreOffice 设置。移除期间可能会要求系统权限。是否继续？", "Clickra will remove LibreOffice in the background and clear Clickra's LibreOffice settings. Windows may ask for permission. Continue?", "Clickra がバックグラウンドで LibreOffice を削除し、Clickra の LibreOffice 設定を消去します。Windows が権限を要求する場合があります。続行しますか？", "Clickra가 백그라운드에서 LibreOffice를 제거하고 Clickra의 LibreOffice 설정을 지웁니다. Windows 권한 요청이 표시될 수 있습니다. 계속하시겠습니까?"),
                (KeyLibreOfficeExternalNote, "這台電腦上的 LibreOffice 不是由 Clickra 安裝，Clickra 不會移除它。如需移除，請使用 Windows 的「應用程式與功能」。", "这台电脑上的 LibreOffice 不是由 Clickra 安装，Clickra 不会移除它。如需移除，请使用 Windows 的“应用和功能”。", "This LibreOffice was not installed by Clickra, so Clickra will not remove it. Use Windows Settings > Apps to remove it yourself.", "このコンピューターの LibreOffice は Clickra がインストールしたものではありません。Clickra は削除しません。削除する場合は Windows の「アプリと機能」をご利用ください。", "이 컴퓨터의 LibreOffice는 Clickra가 설치한 것이 아니므로 Clickra가 제거하지 않습니다. 제거하려면 Windows의 '앱 및 기능'을 사용하세요."),
                (KeyLibreOfficeExternalHint, "未由 Clickra 安裝：請改用 Windows 的「應用程式與功能」移除。", "非 Clickra 安装：请改用 Windows 的“应用和功能”移除。", "Not installed by Clickra: remove it from Windows Settings > Apps.", "Clickra 未インストール：Windows の「アプリと機能」から削除してください。", "Clickra가 설치하지 않음: Windows의 '앱 및 기능'에서 제거하세요."),
                ("setting_libreoffice_uninstalling", "正在背景移除 LibreOffice...", "正在后台移除 LibreOffice...", "Removing LibreOffice in the background...", "バックグラウンドで LibreOffice を削除しています...", "백그라운드에서 LibreOffice 제거 중..."),
                ("setting_libreoffice_uninstall_ready", "LibreOffice 已移除，Clickra 已改回自動引擎。", "LibreOffice 已移除，Clickra 已切回自动引擎。", "LibreOffice has been removed. Clickra switched back to Auto.", "LibreOffice を削除しました。Clickra は自動エンジンに戻りました。", "LibreOffice가 제거되었습니다. Clickra가 자동 엔진으로 돌아갔습니다."),
                ("setting_libreoffice_uninstall_restart_required", "LibreOffice 已排程移除，Clickra 已改回自動引擎。請重新啟動 Windows 以完成移除。", "LibreOffice 已安排移除，Clickra 已切回自动引擎。请重新启动 Windows 以完成移除。", "LibreOffice removal is scheduled. Clickra switched back to Auto. Restart Windows to finish removal.", "LibreOffice の削除を予約しました。Clickra は自動エンジンに戻りました。削除を完了するには Windows を再起動してください。", "LibreOffice 제거가 예약되었습니다. Clickra가 자동 엔진으로 돌아갔습니다. 제거를 완료하려면 Windows를 다시 시작하세요."),
                ("setting_libreoffice_removal_pending", "LibreOffice 正在等待 Windows 重新啟動以完成移除。", "LibreOffice 正在等待 Windows 重新启动以完成移除。", "LibreOffice is waiting for Windows to restart and finish removal.", "LibreOffice は Windows の再起動後に削除が完了します。", "LibreOffice가 Windows 재시작 후 제거를 완료하도록 대기 중입니다."),
                ("setting_libreoffice_uninstall_failed", "LibreOffice 移除失敗：\n{0}", "LibreOffice 移除失败：\n{0}", "LibreOffice removal failed:\n{0}", "LibreOffice の削除に失敗しました：\n{0}", "LibreOffice 제거 실패:\n{0}"),
                ("setting_libreoffice_already_current", "目前已安裝的 LibreOffice 已是 Clickra 建議版本（{0}），不需要更新。", "当前已安装的 LibreOffice 已是 Clickra 建议版本（{0}），不需要更新。", "The installed LibreOffice is already Clickra's recommended version ({0}). No update is needed.", "インストール済みの LibreOffice は Clickra 推奨バージョン（{0}）です。更新は不要です。", "설치된 LibreOffice가 이미 Clickra 권장 버전({0})입니다. 업데이트가 필요 없습니다."),
                ("setting_microsoft_ready", "Microsoft Office 已就緒", "Microsoft Office 已就绪", "Microsoft Office ready", "Microsoft Office 準備完了", "Microsoft Office 준비 완료"),
                ("setting_microsoft_missing", "Microsoft Office 尚未安裝或不完整", "Microsoft Office 尚未安装或不完整", "Microsoft Office is not installed or is incomplete", "Microsoft Office が未インストール、または不完全です", "Microsoft Office가 설치되지 않았거나 완전하지 않습니다"),
                ("setting_engine_auto_using", "自動：使用 {0}", "自动：使用 {0}", "Auto: using {0}", "自動：{0} を使用", "자동: {0} 사용"),
                ("setting_engine_none_available", "沒有可用的 Office 轉檔引擎。請安裝 Microsoft Office 或取得 LibreOffice。", "没有可用的 Office 转换引擎。请安装 Microsoft Office 或获取 LibreOffice。", "No Office conversion engine is available. Install Microsoft Office or get LibreOffice.", "利用可能な Office 変換エンジンがありません。Microsoft Office をインストールするか、LibreOffice を取得してください。", "사용 가능한 Office 변환 엔진이 없습니다. Microsoft Office를 설치하거나 LibreOffice를 받으세요."),
                ("overview_tip", "提示：直接在檔案總管選取檔案，右鍵即可呼叫 Clickra 選單進行轉換。", "提示：直接在文件资源管理器中选择文件，右键即可呼叫 Clickra 菜单进行转换。", "Tip: Select files in File Explorer, right-click, and select Clickra to convert.", "ヒント：エクスプローラーでファイルを選択し、右クリックして Clickra から変換します。", "팁: 파일 탐색기에서 파일을 선택하고 마우스 오른쪽 버튼을 클릭하여 Clickra로 변환하세요."),
                ("cmd_word_to_pdf", WordToPdfLabel, WordToPdfLabel, WordToPdfLabel, WordToPdfLabel, WordToPdfLabel),
                ("cmd_excel_to_pdf", ExcelToPdfLabel, ExcelToPdfLabel, ExcelToPdfLabel, ExcelToPdfLabel, ExcelToPdfLabel),
                ("cmd_ppt_to_pdf", PptToPdfLabel, PptToPdfLabel, PptToPdfLabel, PptToPdfLabel, PptToPdfLabel),
                ("cmd_merge_pdf", "合併 PDF", "合并 PDF", "Merge PDF", "PDF 結合", "PDF 병합"),
                ("cmd_img_to_pdf", "圖片 → PDF", "图片 → PDF", "Image → PDF", "画像 → PDF", "이미지 → PDF"),
                ("cmd_merge_img", "圖片合併", "图片合并", "Merge Images", "画像結合", "이미지 병합"),
                ("cmd_stitch_img", "圖片拼接", "图片拼接", "Stitch Images", "画像結合 (縦/横)", "이미지 이어붙이기"),
                ("tab_convert", "轉檔", "转档", "Convert", "変換", "변환"),
                ("convert_drag_drop_hint", "拖曳檔案至此，或點擊此處選取檔案", "拖拽文件至此，或点击此处选择文件", "Drag files here, or click to browse", "ここにファイルをドラッグするか、クリックして選択", "여기에 파일을 끌어다 놓거나 클릭하여 선택"),
                ("convert_drag_drop_sub", "支援 Word, PPT, PDF 及多種圖片格式", "支持 Word, PPT, PDF 及多种图片格式", "Supports Word, PPT, PDF, and image files", "Word、PPT、PDF、および画像ファイルをサポート", "Word, PPT, PDF 및 이미지 파일 지원"),
                ("convert_selected_count", "已選取 {0} 個檔案", "已选择 {0} 个文件", "{0} files selected", "{0} 個のファイルが選択されました", "{0}개의 파일이 선택됨"),
                ("convert_clear", "清除", "清除", "Clear", "クリア", "지우기"),
                ("convert_start", "開始轉檔", "开始转档", "Start Conversion", "変換開始", "변환 시작"),
                ("convert_group_office", "Office 轉 PDF", "Office 转 PDF", "Office to PDF", "Office から PDF", "Office to PDF"),
                ("convert_group_pdf", "PDF 工具", "PDF 工具", "PDF Tools", "PDF ツール", "PDF 도구"),
                ("convert_group_image", "圖片工具", "图片工具", "Image Tools", "画像ツール", "이미지 도구"),
                ("convert_err_min_files", "此功能至少需要 {0} 個檔案！", "此功能至少需要 {0} 个文件！", "This action requires at least {0} files!", "この機能には少なくとも {0} 個のファイルが必要です！", "이 작업은 최소 {0}개의 파일이 필요합니다!"),
                ("convert_err_invalid_ext", "檔案格式不符，請重新選取！", "文件格式不符，请重新选择！", "Invalid file extensions detected!", "無効なファイル形式が含まれています！", "잘못된 파일 확장자가 감지되었습니다!"),
                ("tab_about", "關於", "关于", "About", "紹介", "정보"),
                ("about_desc_title", "專案說明", "项目说明", "About Project", "プロジェクトについて", "프로젝트 정보"),
                ("about_desc_body", "Clickra 是一款專為 Windows 11 量身打造的極致輕量、現代化右鍵轉檔工具。\n底層採用先進的 C# NativeAOT 技術，擺脫笨重的執行環境，實現小於 0.01 秒的瞬時啟動與免安裝極致體驗，與系統完美融為一體。", "Clickra 是一款专为 Windows 11 量身打造的极致轻量、现代化右键转档工具。\n底层采用先进的 C# NativeAOT 技术，摆脱臃肿的运行环境，实现小于 0.01 秒的瞬时启动与免安装极致体验，与系统完美融为一体。", "Clickra is a premium, ultra-lightweight modern context menu utility designed for Windows 11.\nBuilt with C# NativeAOT, it skips all runtime overhead to deliver sub-10ms instantaneous startup and a zero-dependency user experience that feels like a native OS feature.", "Clickra は、Windows 11 のために設計された極めて軽量でモダンな右鍵コンテキストメニュー変換ツールです。\nC# NativeAOT 技術を採用し、不要なランタイムを排除することで、0.01秒未満の瞬間起動と完全なスタンドアロン動作を実現しました。", "Clickra는 Windows 11을 위해 특별히 설계된 초경량 현대식 마우스 오른쪽 버튼 변환 도구입니다.\n최첨단 C# NativeAOT 기술을 적용하여 불필요한 런타임을 배제하고 0.01초 미만의 즉각적인 실행 속도와 무설치 작동을 제공합니다."),
                ("about_collab_title", "協作開發與原始碼", "协作开发与源码", "Collaboration & Source", "コラボレーションとソースコード", "협업 및 소스 코드"),
                ("about_collab_body", "本專案以 Apache 2.0 協議完全開源！我們熱烈歡迎各位開發者加入，共同改善與擴充功能。\n您可以透過 GitHub 提交 Pull Request、回報 Issue，一起打造更實用的 Windows 生態系工具。", "本项目以 Apache 2.0 协议完全开源！我们热烈欢迎各位开发者加入，共同改善与扩充功能。\n您可以通过 GitHub 提交 Pull Request、反馈 Issue，一起打造更实用的 Windows 生态系工具。", "This project is fully open-source under the Apache 2.0 license! We love community contributions.\nVisit our GitHub repository to submit Pull Requests, report issues, or collaborate on building better tools for Windows.", "本プロジェクトは Apache 2.0 ライセンスの下で完全にオープンソース化されています！\nGitHub での Pull Request や Issue の報告を通じて、Windows ユーザーのための優れたツール開発にぜひご協力ください。", "이 프로젝트는 Apache 2.0 라이선스 하에 완전한 오픈 소스로 제공됩니다!\nGitHub 리포지토리에서 Pull Request 제출, Issue 등록을 통해 더 편리한 Windows 환경을 만드는 데 동참해 주세요."),
                ("about_diag_title", "診斷與回報說明", "诊断与反馈说明", "Diagnostics & Support", "診断とサポート", "진단 및 지원"),
                ("about_diag_body", "遇到轉檔失敗或異常嗎？我們為您簡化了回報流程！\n點擊下方按鈕將一鍵開啟 Gmail 線上郵件撰寫，並同步在檔案總管為您選取好日誌檔「history.log」，您只需將該檔案拖曳至郵件中即可快速傳送診斷資訊。", "遇到转档失败或异常吗？我们为您简化了反馈流程！\n点击下方按钮将一键开启 Gmail 网页版邮件撰写，并同步在文件资源管理器中为您选好日志「history.log」，您只需将该文件拖拽至邮件中即可快速发送诊断信息。", "Encountered a conversion issue? We've streamlined the feedback process!\nClick below to open Gmail Web Composer and highlight 'history.log' in File Explorer. Simply drag the highlighted file into Gmail to send us the diagnostic details.", "変換エラーや不具合が発生しましたか？フィードバックの手順を劇的に簡略化しました！\nボタンをクリックすると、Gmail 作成画面が開き、エクスプローラーでログファイル「history.log」が自動選択されます。ファイルをそのままドラッグ＆ドロップして送信してください。", "변환 중 문제가 발생하면 아래 버튼을 누르면 Gmail 작성 창이 열리고 탐색기에서 로그 파일「history.log」가 자동 선택됩니다. 파일을 메일에 드래그 앤 드롭하여 손쉽게 진단 정보를 보내주세요."),
                ("about_btn_open_data_dir", "檢視日誌資料夾", "查看日志文件夹", "Open Log Folder", "ログフォルダを開く", "로그 폴더 열기"),
                ("about_btn_github", "GitHub 專案", "GitHub 项目", "View on GitHub", "GitHub で表示", "GitHub 리포지토리"),
                ("about_btn_gmail", "Gmail 回報", "Gmail 反馈", "Gmail Diagnostics", "Gmail でフィードバック", "Gmail 피드백"),
                ("history_detail_inputs", "輸入路徑", "输入路径", "Input Paths", "入力パス", "입력 경로"),
                ("history_detail_outputs", "輸出路徑", "输出路径", "Output Path", "出力パス", "출력 경로"),
                ("history_detail_time", "轉換時間", "转换时间", "Conversion Time", "変換日時", "변환 시간"),
                ("history_detail_elapsed", "執行耗時", "执行耗时", "Elapsed Time", "処理時間", "소요 시간"),
                ("progress_cancel_confirm", "您確定要取消目前的轉換作業嗎？", "您确定要取消当前的转换作业吗？", "Are you sure you want to cancel the current conversion?", "現在の変換作業をキャンセルしてもよろしいですか？", "현재 변환 작업을 취소하시겠습니까?"),
                ("history_detail_error", "錯誤訊息", "错误信息", "Error Message", "エラーメッセージ", "오류 메시지"),
                ("error_user_aborted", "取消", "取消", "Canceled", "キャンセル", "취소됨"),
                ("progress_tray_hint", "縮至系統匣後仍會在背景繼續執行", "最小化到系统托盘后仍会在后台继续运行", "Keeps running in the background after minimizing to the tray", "トレイに最小化してもバックグラウンドで実行を続けます", "트레이로 최소화해도 백그라운드에서 계속 실행됩니다"),
                ("progress_background", "縮小至系統匣", "最小化到系统托盘", "Minimize to System Tray", "システムトレイに最小化", "시스템 트레이로 최소화"),
                ("error_processing_failed", "處理過程中發生錯誤：\n{0}", "处理过程中发生错误：\n{0}", "An error occurred while processing:\n{0}", "処理中にエラーが発生しました：\n{0}", "처리 중 오류가 발생했습니다:\n{0}"),
                ("error_libreoffice_not_ready", "LibreOffice 引擎尚未就緒。請到「設定」重新取得 LibreOffice，或將 Office 轉檔引擎改回「自動」或「Microsoft Office」。", "LibreOffice 引擎尚未就绪。请到“设置”重新获取 LibreOffice，或将 Office 转换引擎改回“自动”或“Microsoft Office”。", "The LibreOffice engine is not ready. Open Settings and get LibreOffice again, or switch the Office engine back to Auto or Microsoft Office.", "LibreOffice エンジンはまだ準備できていません。設定で LibreOffice を再取得するか、Office 変換エンジンを「自動」または「Microsoft Office」に戻してください。", "LibreOffice 엔진이 아직 준비되지 않았습니다. 설정에서 LibreOffice를 다시 받거나 Office 변환 엔진을 자동 또는 Microsoft Office로 변경하세요."),
                ("error_libreoffice_unusable", "目前的 LibreOffice 無法正常啟動。請到「設定」重新取得 LibreOffice，或手動指定一個可正常執行的 LibreOffice。", "当前的 LibreOffice 无法正常启动。请到“设置”重新获取 LibreOffice，或手动指定一个可正常运行的 LibreOffice。", "The current LibreOffice engine cannot start correctly. Open Settings and get LibreOffice again, or choose a working LibreOffice installation manually.", "現在の LibreOffice は正常に起動できません。設定で LibreOffice を再取得するか、正常に動作する LibreOffice を手動で指定してください。", "현재 LibreOffice를 정상적으로 시작할 수 없습니다. 설정에서 LibreOffice를 다시 받거나 정상 실행되는 LibreOffice를 직접 지정하세요."),
                ("error_microsoftoffice_not_ready", "Microsoft Office 尚未就緒。請安裝對應的 Office 應用程式，或將 Office 轉檔引擎改為「自動」或「LibreOffice」。", "Microsoft Office 尚未就绪。请安装对应的 Office 应用程序，或将 Office 转换引擎改为“自动”或“LibreOffice”。", "Microsoft Office is not ready. Install the required Office app, or switch the Office engine to Auto or LibreOffice.", "Microsoft Office は準備できていません。必要な Office アプリをインストールするか、Office 変換エンジンを「自動」または「LibreOffice」に変更してください。", "Microsoft Office가 준비되지 않았습니다. 필요한 Office 앱을 설치하거나 Office 변환 엔진을 자동 또는 LibreOffice로 변경하세요."),
                ("status_office_fallback_to_libreoffice", "Microsoft {0} 轉換失敗，正在改用 LibreOffice：{1}...", "Microsoft {0} 转换失败，正在改用 LibreOffice：{1}...", "Microsoft {0} conversion failed; switching to LibreOffice: {1}...", "Microsoft {0} の変換に失敗しました。LibreOffice に切り替えています：{1}...", "Microsoft {0} 변환에 실패했습니다. LibreOffice로 전환 중: {1}..."),
                ("status_libreoffice_starting", "正在啟動 LibreOffice 引擎 ({0}/{1})...", "正在启动 LibreOffice 引擎 ({0}/{1})...", "Starting LibreOffice engine ({0}/{1})...", "LibreOffice エンジンを起動しています ({0}/{1})...", "LibreOffice 엔진 시작 중 ({0}/{1})..."),
                ("status_libreoffice_exporting", "正在使用 LibreOffice 匯出 PDF：{0}...", "正在使用 LibreOffice 导出 PDF：{0}...", "Exporting PDF with LibreOffice: {0}...", "LibreOffice で PDF を書き出しています：{0}...", "LibreOffice로 PDF 내보내는 중: {0}..."),
                ("status_libreoffice_completed", "已完成 LibreOffice 轉換：{0}", "已完成 LibreOffice 转换：{0}", "LibreOffice conversion completed: {0}", "LibreOffice 変換が完了しました：{0}", "LibreOffice 변환 완료: {0}"),
                ("setting_pdf_title", "PDF 一鍵翻譯", "PDF 翻译", "PDF One-Click Translation", "PDF 一括翻訳", "PDF 일괄 번역"),
                ("setting_pdf_desc", "設定 PDF 翻譯目標語言", "设置 PDF 翻译语言", "Configure target language for translation", "翻訳の対象言語を設定します", "번역 대상 언어를 설정합니다"),
                ("setting_pdf_lang", "目標語言 (Target Language)", "目标语言", "Target Language", "対象言語 (Target Language)", "대상 언어 (Target Language)"),
                ("cmd_translate_pdf", "PDF 一鍵翻譯", "PDF 翻译", "PDF Translation", "PDF 翻訳", "PDF 번역"),
                ("cmd_decrypt_pdf", "去除 PDF 密碼", "去除 PDF 密码", "Remove PDF Password", "PDF パスワード解除", "PDF 비밀번호 제거"),
                ("pdf_password_title", "輸入 PDF 密碼", "输入 PDF 密码", "Enter PDF Password", "PDF パスワード入力", "PDF 비밀번호 입력"),
                ("pdf_password_prompt", "「{0}」受密碼保護，請輸入開啟密碼：", "「{0}」受密码保护，请输入打开密码：", "\"{0}\" is password protected. Enter open password:", "「{0}」はパスワードで保護されています。パスワードを入力してください：", "「{0}」 파일이 비밀번호로 보호되어 있습니다. 비밀번호를 입력하십시오:"),
                ("pdf_password_retry", "密碼錯誤。請重新輸入「{0}」的密碼：", "密码错误。请重新输入「{0}」的密码：", "Incorrect password. Re-enter password for \"{0}\":", "パスワードが正しくありません。もう一度入力してください：「{0}」", "비밀번호가 잘못되었습니다. 다시 입력하십시오:「{0}」"),
                ("error_pdf_password_quiet", "密碼錯誤或未提供密碼（靜默模式下無法手動輸入密碼）。", "密码错误或未提供密码（静默模式下无法手动输入密码）。", "Incorrect or missing password (cannot prompt for password in quiet mode).", "パスワードが正しくないか入力されていません（サイレントモードではパスワードを入力できません）。", "비밀번호가 잘못되었거나 누락되었습니다 (조용한 모드에서는 비밀번호를 입력할 수 없습니다)."),
                ("pdf_not_encrypted", "此檔案未加密，無須解除密碼。", "此文件未加密，无需解除密码。", "This file is not encrypted; no decryption needed.", "このファイルは暗号化されていません。パスワード解除は不要です。", "이 파일은 암호화되어 있지 않아 비밀번호를 제거할 필요가 없습니다."),
                ("dialog_ok", "確定", "确定", "OK", "確定", "확인"),
                ("dialog_cancel", "取消", "取消", "Cancel", "キャンセル", "취소"),
            };

            RegisterTranslations(data);
        }

        private static void RegisterTranslations((string Key, string Tw, string Cn, string En, string Ja, string Ko)[] data)
        {
            foreach (var item in data)
            {
                Translations[LangTw][item.Key] = item.Tw;
                Translations[LangCn][item.Key] = item.Cn;
                Translations[LangEn][item.Key] = item.En;
                Translations[LangJa][item.Key] = item.Ja;
                Translations[LangKo][item.Key] = item.Ko;
            }
        }

        private static void RegisterLibreOfficeManagementTranslations()
        {
            var data = new (string Key, string Tw, string Cn, string En, string Ja, string Ko)[]
            {
                ( "setting_libreoffice_download_prompt", "Clickra 將下載並驗證 LibreOffice 官方 MSI 安裝程式。\n\n版本：{0}\n版本類型：{1}\n下載大小：約 {2}\n安裝位置：{3}\nSHA256：{4}\n\n接下來 Windows 可能會要求系統安裝權限。安裝或更新完成後，Clickra 會在能驗證唯一 MSI 身分時管理這套系統 LibreOffice，並可從 Clickra 設定中解除安裝；LibreOffice 仍可在 Clickra 以外正常使用。是否繼續？", "Clickra 将下载并验证 LibreOffice 官方 MSI 安装程序。\n\n版本：{0}\n版本类型：{1}\n下载大小：约 {2}\n安装位置：{3}\nSHA256：{4}\n\n接下来 Windows 可能会要求系统安装权限。安装或更新完成后，Clickra 会在能够验证唯一 MSI 身份时管理这套系统 LibreOffice，并可从 Clickra 设置中卸载；LibreOffice 仍可在 Clickra 以外正常使用。是否继续？", "Clickra will download and verify the official LibreOffice MSI installer.\n\nVersion: {0}\nEdition: {1}\nDownload size: about {2}\nInstall path: {3}\nSHA256: {4}\n\nWindows may ask for permission to install system software. After installation or update, Clickra will manage this system LibreOffice only when Windows can verify one unique MSI identity; a verified installation can then be uninstalled from Clickra settings. LibreOffice can still be used outside Clickra. Continue?", "Clickra は LibreOffice 公式 MSI インストーラーをダウンロードして検証します。\n\nバージョン：{0}\nエディション：{1}\nダウンロードサイズ：約 {2}\nインストール先：{3}\nSHA256：{4}\n\nWindows がシステムへのインストール許可を求める場合があります。インストールまたは更新後、Windows が一意の MSI 識別情報を確認できた場合にのみ、このシステム LibreOffice は Clickra の管理対象となり、Clickra の設定から削除できます。LibreOffice は引き続き Clickra 以外でも使用できます。続行しますか？", "Clickra가 공식 LibreOffice MSI 설치 파일을 다운로드하고 검증합니다.\n\n버전: {0}\n에디션: {1}\n다운로드 크기: 약 {2}\n설치 위치: {3}\nSHA256: {4}\n\nWindows가 시스템 소프트웨어 설치 권한을 요청할 수 있습니다. 설치 또는 업데이트 후 Windows에서 하나의 고유한 MSI ID를 확인할 수 있을 때만 이 시스템 LibreOffice를 Clickra가 관리하며, Clickra 설정에서 제거할 수 있습니다. LibreOffice는 계속 Clickra 밖에서도 사용할 수 있습니다. 계속하시겠습니까?" ),
                ( "setting_libreoffice_management_unverified", "LibreOffice 已完成安裝或更新，但 Windows 無法唯一驗證其 MSI 身分，因此 Clickra 未取得解除安裝管理權。這套 LibreOffice 仍可正常使用。", "LibreOffice 已完成安装或更新，但 Windows 无法唯一验证其 MSI 身份，因此 Clickra 未取得卸载管理权限。这套 LibreOffice 仍可正常使用。", "LibreOffice was installed or updated successfully, but Windows could not verify one unique MSI identity. Clickra therefore did not take uninstall management of this installation. LibreOffice remains available for normal use.", "LibreOffice のインストールまたは更新は完了しましたが、Windows で一意の MSI 識別情報を確認できませんでした。そのため Clickra はアンインストール管理を取得していません。LibreOffice は通常どおり使用できます。", "LibreOffice 설치 또는 업데이트는 완료되었지만 Windows에서 하나의 고유한 MSI ID를 확인할 수 없었습니다. 따라서 Clickra는 이 설치의 제거 관리 권한을 갖지 않습니다. LibreOffice는 정상적으로 사용할 수 있습니다." ),
                ( "setting_libreoffice_adopt", "交由 Clickra 管理", "交给 Clickra 管理", "Manage with Clickra", "Clickra で管理", "Clickra에서 관리" ),
                ( "setting_libreoffice_adopt_confirm", "確定要將此既有 LibreOffice 交由 Clickra 管理嗎？\n納管後您將能直接在 Clickra 設定中更新或解除安裝此 LibreOffice。", "确定要将此现有 LibreOffice 交给 Clickra 管理吗？\n接管后您将能直接在 Clickra 设置中更新或卸载此 LibreOffice。", "Adopt this existing LibreOffice installation into Clickra management?\nOnce adopted, Clickra will be able to update or uninstall this LibreOffice directly from settings.", "この既存の LibreOffice を Clickra の管理下に移行しますか？\n移行後は Clickra の設定から直接更新や削除ができるようになります。", "기존 LibreOffice를 Clickra 관리 대상으로 전환하시겠습니까?\n전환 후에는 Clickra 설정에서 직접 업데이트하거나 제거할 수 있습니다." ),
                ( "setting_libreoffice_adopt_success", "此 LibreOffice 現已交由 Clickra 管理。", "此 LibreOffice 现已交给 Clickra 管理。", "This LibreOffice is now managed by Clickra.", "この LibreOffice は Clickra の管理下に移行されました。", "이 LibreOffice는 이제 Clickra에서 관리됩니다." )
            };

            RegisterTranslations(data);
        }

        private static void RegisterCompressionTranslations()
        {
            var data = new (string Key, string Tw, string Cn, string En, string Ja, string Ko)[]
            {
                (KeyCmdCompressPdf, "壓縮 PDF", "压缩 PDF", "Compress PDF", "PDF 圧縮", "PDF 압축"),
                ("setting_pdf_compress_title", "PDF 壓縮設定", "PDF 压缩设置", "PDF Compression Settings", "PDF 圧縮設定", "PDF 압축 설정"),
                ("setting_pdf_compress_desc", "自訂 PDF 壓縮的字型、圖片解析度與向量結構簡化方式", "自定义 PDF 压缩的字体、图片分辨率与矢量结构简化方式", "Customize font embedding, image resolution, and vector structure options", "フォントの埋め込み、画像の解像度、ベクター構造の最適化をカスタマイズします", "글꼴 포함, 이미지 해상도 및 벡터 구조 최적화 옵션을 사용자 정의합니다"),
                ("setting_pdf_compress_group_image", "圖片壓縮", "图片压缩", "Image Compression", "画像圧縮", "이미지 압축"),
                ("setting_pdf_compress_group_other", "其他優化", "其他优化", "Other Optimization", "その他の最適化", "기타 최적화"),
                ("setting_pdf_compress_strip_fonts", "剝離嵌入字型以極致壓縮", "剥离嵌入字体以极致压缩", "Strip Embedded Fonts for Maximum Compression", "フォントの埋め込みを解除して極限圧縮", "글꼴 포함을 해제하여 극대 압축"),
                ("setting_pdf_compress_minify_content", "簡化向量圖形與排版結構", "简化矢量图形与排版结构", "Minify Vector Graphics & Content Streams", "ベクターグラフィックスとコンテンツストリームの簡素化", "벡터 그래픽 및 콘텐츠 스트림 단순화"),
                ("setting_pdf_compress_smaller", "← 體積最小", "← 体积最小", "← Smaller", "← 最小サイズ", "← 최소 크기"),
                ("setting_pdf_compress_higher", "質量最高 →", "质量最高 →", "Higher Quality →", "最高品質 →", "최고 품질 →"),
                ("setting_pdf_compress_level_min", "極小", "极小", "Min", "最小", "최소"),
                ("setting_pdf_compress_level_small", "小檔", "小档", "Small", "小", "소형"),
                ("setting_pdf_compress_level_std", "標準", "标准", "Std", "標準", "표준"),
                ("setting_pdf_compress_level_high", "高品質", "高质量", "High", "高品質", "고품질")
            };

            RegisterTranslations(data);
        }

        private static void RegisterProcessingTranslations()
        {
            var data = new (string Key, string Tw, string Cn, string En, string Ja, string Ko)[]
            {
                ("status_office_starting", "正在啟動 {0} 引擎 ({1}/{2})...", "正在启动 {0} 引擎 ({1}/{2})...", "Starting {0} engine ({1}/{2})...", "{0} エンジンを起動中 ({1}/{2})...", "{0} 엔진 시작 중 ({1}/{2})..."),
                ("status_office_reading", "正在讀取文件：{0}...", "正在读取文档：{0}...", "Reading document: {0}...", "文書を読み込み中: {0}...", "문서 읽는 중: {0}..."),
                ("status_office_exporting", "正在匯出 PDF：{0}...", "正在导出 PDF：{0}...", "Exporting PDF: {0}...", "PDF を書き出し中: {0}...", "PDF 내보내는 중: {0}..."),
                ("status_office_completed", "已完成轉換：{0}", "已完成转换：{0}", "Conversion completed: {0}", "変換完了: {0}", "변환 완료: {0}"),
                ("status_office_converting", "正在轉換 {0}：{1}...", "正在转换 {0}：{1}...", "Converting {0}: {1}...", "{0} を変換中: {1}...", "{0} 변환 중: {1}..."),
                ("error_office_unsupported", "不支援的 Office 應用程式：{0}。", "不支持的 Office 应用程序：{0}。", "Office application {0} is not supported.", "Office アプリ {0} はサポートされていません。", "Office 앱 {0}은(는) 지원되지 않습니다."),
                ("error_office_output_missing", "{0} 轉換失敗：未建立輸出 PDF。", "{0} 转换失败：未创建输出 PDF。", "{0} conversion failed: output PDF file was not created.", "{0} 変換失敗: 出力 PDF が作成されませんでした。", "{0} 변환 실패: 출력 PDF 파일이 생성되지 않았습니다."),
                ("error_office_powershell_start", "Microsoft {0} 轉換失敗：無法啟動 PowerShell。", "Microsoft {0} 转换失败：无法启动 PowerShell。", "Microsoft {0} conversion failed: unable to start PowerShell.", "Microsoft {0} 変換失敗: PowerShell を起動できません。", "Microsoft {0} 변환 실패: PowerShell을 시작할 수 없습니다."),
                ("error_office_timeout", "Microsoft {0} 轉換超過 2 分鐘。請改用其他 Office 引擎，或手動匯出此檔案。", "Microsoft {0} 转换超过 2 分钟。请改用其他 Office 引擎，或手动导出此文件。", "Microsoft {0} conversion timed out after 2 minutes. Try another Office engine or export this file manually.", "Microsoft {0} 変換が 2 分でタイムアウトしました。別の Office エンジンを使うか手動で書き出してください。", "Microsoft {0} 변환이 2분 후 시간 초과되었습니다. 다른 Office 엔진을 사용하거나 수동으로 내보내세요."),
                ("error_office_not_installed", "尚未安裝 Microsoft {0}。此功能需要系統中已安裝 Microsoft {0}。", "尚未安装 Microsoft {0}。此功能需要系统中已安装 Microsoft {0}。", "Microsoft {0} is not installed. This feature requires Microsoft {0} to be installed on your system.", "Microsoft {0} がインストールされていません。この機能には Microsoft {0} が必要です。", "Microsoft {0}이(가) 설치되어 있지 않습니다. 이 기능에는 Microsoft {0}이(가) 필요합니다."),
                ("error_office_failed", "{0} 轉換失敗：{1}", "{0} 转换失败：{1}", "{0} conversion failed: {1}", "{0} 変換失敗: {1}", "{0} 변환 실패: {1}"),
                ("error_office_exit_code", "{0} 轉換失敗，結束碼 {1}。", "{0} 转换失败，退出码 {1}。", "{0} conversion failed with exit code {1}.", "{0} 変換失敗、終了コード {1}。", "{0} 변환 실패, 종료 코드 {1}."),
                ("error_libreoffice_unsupported", "LibreOffice 不支援轉換 {0}。", "LibreOffice 不支持转换 {0}。", "LibreOffice conversion does not support {0}.", "LibreOffice は {0} 変換をサポートしていません。", "LibreOffice는 {0} 변환을 지원하지 않습니다."),
                ("error_libreoffice_start", "LibreOffice 轉換失敗：無法啟動程序。", "LibreOffice 转换失败：无法启动进程。", "LibreOffice conversion failed: unable to start process.", "LibreOffice 変換失敗: プロセスを起動できません。", "LibreOffice 변환 실패: 프로세스를 시작할 수 없습니다."),
                ("error_libreoffice_timeout", "LibreOffice 轉換超過 2 分鐘。請改用 Microsoft Office 或自動引擎處理此檔案。", "LibreOffice 转换超过 2 分钟。请改用 Microsoft Office 或自动引擎处理此文件。", "LibreOffice conversion timed out after 2 minutes. Try Microsoft Office or Auto engine for this file.", "LibreOffice 変換が 2 分でタイムアウトしました。Microsoft Office または自動エンジンを試してください。", "LibreOffice 변환이 2분 후 시간 초과되었습니다. Microsoft Office 또는 자동 엔진을 사용해 보세요."),
                ("error_libreoffice_exit_code", "LibreOffice 轉換失敗，結束碼 {0} ({1})：{2}", "LibreOffice 转换失败，退出码 {0} ({1})：{2}", "LibreOffice conversion failed with exit code {0} ({1}): {2}", "LibreOffice 変換失敗、終了コード {0} ({1}): {2}", "LibreOffice 변환 실패, 종료 코드 {0} ({1}): {2}"),
                ("error_libreoffice_output_missing", "LibreOffice 轉換失敗：未建立輸出 PDF。{0}", "LibreOffice 转换失败：未创建输出 PDF。{0}", "LibreOffice conversion failed: output PDF file was not created. {0}", "LibreOffice 変換失敗: 出力 PDF が作成されませんでした。{0}", "LibreOffice 변환 실패: 출력 PDF 파일이 생성되지 않았습니다. {0}"),
                ("pdf_progress_analyzing", "正在分析 PDF 版面結構與公式...", "正在分析 PDF 版面结构与公式...", "Analyzing PDF layout and formulas...", "PDF レイアウトと数式を解析中...", "PDF 레이아웃과 수식 분석 중..."),
                ("pdf_progress_translating", "正在翻譯文本內容...", "正在翻译文本内容...", "Translating text content...", "テキストを翻訳中...", "텍스트 내용 번역 중..."),
                ("pdf_progress_translating_page", "正在翻譯第 {0}/{1} 頁...", "正在翻译第 {0}/{1} 页...", "Translating page {0}/{1}...", "{0}/{1} ページを翻訳中...", "{0}/{1} 페이지 번역 중..."),
                ("pdf_progress_translating_batch", "正在翻譯第 {0}/{1} 頁，批次 {2}/{3}（段落 {4}-{5}/{6}）...", "正在翻译第 {0}/{1} 页，批次 {2}/{3}（段落 {4}-{5}/{6}）...", "Translating page {0}/{1}, batch {2}/{3} (paragraphs {4}-{5}/{6})...", "{0}/{1} ページ、バッチ {2}/{3} を翻訳中（段落 {4}-{5}/{6}）...", "{0}/{1} 페이지, 배치 {2}/{3} 번역 중(문단 {4}-{5}/{6})..."),
                ("pdf_progress_rebuilding", "正在重建 PDF 版面與公式...", "正在重建 PDF 版面与公式...", "Rebuilding PDF layout and formulas...", "PDF レイアウトと数式を再構築中...", "PDF 레이아웃과 수식 재구성 중..."),
                ("pdf_progress_saving", "正在儲存翻譯後的檔案...", "正在保存翻译后的文件...", "Saving translated file...", "翻訳済みファイルを保存中...", "번역된 파일 저장 중..."),
                ("pdf_error_deadline", "PDF 翻譯超過 10 分鐘文件時限。", "PDF 翻译超过 10 分钟文档时限。", "PDF translation exceeded the 10-minute document deadline.", "PDF 翻訳が 10 分の文書制限を超えました。", "PDF 번역이 10분 문서 제한 시간을 초과했습니다."),
                ("pdf_error_translation_failed_page", "PDF 第 {0} 頁翻譯失敗；自動批次拆分與供應商備援已用盡。", "PDF 第 {0} 页翻译失败；自动批次拆分与供应商备用已用尽。", "PDF translation failed on page {0}; automatic batch splitting and provider fallback were exhausted.", "PDF {0} ページの翻訳に失敗しました。自動バッチ分割とプロバイダー代替を使い切りました。", "PDF {0}페이지 번역 실패: 자동 배치 분할과 공급자 대체를 모두 사용했습니다."),
                ("pdf_error_mismatched_batch", "批次翻譯結果數量不一致。", "批次翻译结果数量不一致。", "Mismatched batch translation results count.", "バッチ翻訳結果数が一致しません。", "배치 번역 결과 수가 일치하지 않습니다."),
                ("pdf_error_provider_empty", "翻譯供應商回傳空白結果。", "翻译供应商返回空白结果。", "Translator returned an empty result.", "翻訳プロバイダーが空の結果を返しました。", "번역 공급자가 빈 결과를 반환했습니다."),
                ("pdf_error_unable_paragraph", "第 {0} 頁段落在批次拆分與供應商備援後仍無法翻譯。", "第 {0} 页段落在批次拆分与供应商备用后仍无法翻译。", "Unable to translate page {0} paragraph after batch splitting and provider fallback.", "{0} ページの段落をバッチ分割と代替後も翻訳できません。", "{0}페이지 문단을 배치 분할과 공급자 대체 후에도 번역할 수 없습니다."),
                ("pdf_error_provider_timeout", "翻譯供應商鏈呼叫超過 {0} 秒。", "翻译供应商链调用超过 {0} 秒。", "Translation provider chain call exceeded {0}s.", "翻訳プロバイダー呼び出しが {0} 秒を超えました。", "번역 공급자 호출이 {0}초를 초과했습니다."),
                ("cmd_split_pdf", "分割 PDF", "分割 PDF", "Split PDF", "PDF 分割", "PDF 분할"),
                ("pdf_split_title", "PDF 視覺化分割", "PDF 可视化分割", "Split PDF Visually", "PDF 視覚分割", "PDF 시각적 분할"),
                ("pdf_split_prompt", "請輸入分頁範圍（例如 1-5, 8 或 all 拆分為單頁檔）：", "请输入分页范围（例如 1-5, 8 或 all 拆分为单页档）：", "Enter page range (e.g. 1-5, 8 or all to split into single pages):", "ページ範囲を入力してください（例：1-5, 8 または全ページ分割の all）：", "페이지 범위를 입력하십시오 (예: 1-5, 8 또는 전체 분할 all):"),
                ("pdf_split_mode_custom", "自訂分段", "自訂分段", "Custom segments", "カスタム分割", "사용자 정의 분할"),
                ("pdf_split_mode_each", "全拆單頁", "全拆单页", "Split each page", "全ページ分割", "전체 페이지 분할"),
                ("pdf_split_mode_fixed", "固定頁數", "固定页数", "Fixed pages per file", "固定ページ数", "고정 페이지 수"),
                ("pdf_split_pages_per_segment", "每檔頁數", "每档页数", "Pages per file", "1ファイルあたりのページ数", "파일당 페이지 수"),
                ("pdf_split_decrease_pages", "減少每檔頁數", "减少每档页数", "Decrease pages per file", "1ファイルあたりのページ数を減らす", "파일당 페이지 수 줄이기"),
                ("pdf_split_increase_pages", "增加每檔頁數", "增加每档页数", "Increase pages per file", "1ファイルあたりのページ数を増やす", "파일당 페이지 수 늘리기"),
                ("pdf_split_previous_page", "上一頁", "上一页", "Previous page", "前のページ", "이전 페이지"),
                ("pdf_split_next_page", "下一頁", "下一页", "Next page", "次のページ", "다음 페이지"),
                ("pdf_split_zoom_out", "縮小", "缩小", "Zoom out", "縮小", "축소"),
                ("pdf_split_zoom_in", "放大", "放大", "Zoom in", "拡大", "확대"),
                ("pdf_split_zoom_tag", "放大", "放大", "Zoom", "拡大", "확대"),
                ("pdf_split_zoom_title", "頁面放大預覽", "页面放大预览", "Page zoom preview", "ページ拡大プレビュー", "페이지 확대 미리보기"),
                ("pdf_split_zoom_hint", "滾輪縮放 · 拖曳平移 · 空白鍵/Esc 關閉", "滚轮缩放 · 拖拽平移 · 空格/Esc 关闭", "Wheel to zoom · drag to pan · Space/Esc to close", "ホイールで拡大 · ドラッグで移動 · Space/Esc で閉じる", "휠 확대 · 드래그 이동 · Space/Esc 닫기"),
                ("pdf_split_zoom_close", "X 關閉", "X 关闭", "X Close", "X 閉じる", "X 닫기"),
                ("pdf_split_zoom_fit", "適配", "适配", "Fit", "フィット", "맞춤"),
                ("cmd_img_to_png", "轉成 PNG", "转换为 PNG", "Convert to PNG", "PNG に変換", "PNG로 변환"),
                ("cmd_img_to_jpg", "轉成 JPG", "转换为 JPG", "Convert to JPG", "JPG に変換", "JPG로 변환"),
                ("cmd_img_to_webp", "轉成 WEBP", "转换为 WEBP", "Convert to WEBP", "WEBP に変換", "WEBP로 변환"),
                ("cmd_img_to_heic", "轉成 HEIC", "转换为 HEIC", "Convert to HEIC", "HEIC に変換", "HEIC로 변환"),
                ("cmd_img_to_gif", "轉成 GIF", "转换为 GIF", "Convert to GIF", "GIF に変換", "GIF로 변환"),
                ("error_heic_decoder_missing", "此系統沒有 HEIF/HEIC 解碼器，無法讀取 HEIC 輸入檔案。", "此系统没有 HEIF/HEIC 解码器，无法读取 HEIC 输入文件。", "This system has no HEIF/HEIC decoder, so HEIC input files cannot be read.", "このシステムには HEIF/HEIC デコーダーがなく、HEIC 入力ファイルを読み取れません。", "이 시스템에는 HEIF/HEIC 디코더가 없어 HEIC 입력 파일을 읽을 수 없습니다."),
                ("tray_background_running", "Clickra - {0} 個轉換背景執行中", "Clickra - {0} 个转换后台运行中", "Clickra - {0} active conversion(s) in background", "Clickra - {0} 件の変換がバックグラウンドで実行中", "Clickra - {0}개의 변환이 백그라운드에서 실행 중"),
                ("tray_restore_all", "還原所有轉換視窗", "还原所有转换窗口", "Restore All Conversion Windows", "すべての変換ウィンドウを復元", "모든 변환 창 복원"),
                ("pdf_split_btn_add", "＋ 新增", "＋ 新增", "+ Add", "＋ 追加", "＋ 추가"),
                ("pdf_split_btn_delete", "刪除", "删除", "Delete", "削除", "삭제"),
                ("pdf_split_btn_clear", "清空", "清空", "Clear", "クリア", "비우기"),
                ("pdf_split_btn_split_at", "切開", "切开", "Split", "分割", "분할"),
                ("pdf_split_mode_fixed_n", "{0}: {1} 頁", "{0}: {1} 页", "{0}: {1} pages", "{0}: {1} ページ", "{0}: {1} 페이지"),
                ("pdf_split_segment_header", "分段列表", "分段列表", "Segments", "セグメント一覧", "세그먼트 목록"),
                ("pdf_split_segment_item", "區段 {0}: {1} ({2} 頁)", "区段 {0}: {1} ({2} 页)", "Segment {0}: {1} ({2} pages)", "区間 {0}: {1} ({2} ページ)", "구간 {0}: {1} ({2} 페이지)"),
                ("pdf_split_page_preview_format", "P.{0} (第 {1}/{2} 頁)", "P.{0} (第 {1}/{2} 页)", "P.{0} (Page {1}/{2})", "P.{0} ({1}/{2} ページ)", "P.{0} ({1}/{2} 페이지)"),
                ("pdf_split_badge_format", "[PDF] {0} ({1} 頁)", "[PDF] {0} ({1} 页)", "[PDF] {0} ({1} pages)", "[PDF] {0} ({1} ページ)", "[PDF] {0} ({1} 페이지)"),
                ("pdf_split_pages_n", "每 {0} 頁", "每 {0} 页", "{0} pages", "{0} ページごと", "{0}페이지마다"),
                ("error_heic_codec_missing", "此系統沒有 HEIF/HEIC 編碼器，無法輸出 HEIC 檔案。", "此系统没有 HEIF/HEIC 编码器，无法输出 HEIC 文件。", "This system has no HEIF/HEIC encoder, so HEIC files cannot be written.", "このシステムには HEIF/HEIC エンコーダーがなく、HEIC ファイルを出力できません。", "이 시스템에는 HEIF/HEIC 인코더가 없어 HEIC 파일을 출력할 수 없습니다."),
                ("error_image_output_collision", "多個輸入檔案會寫入相同的輸出路徑，已中止以避免覆寫：{0}", "多个输入文件会写入相同的输出路径，已中止以避免覆盖：{0}", "Multiple inputs would write to the same output path; conversion stopped to prevent overwriting: {0}", "複数の入力ファイルが同じ出力先に書き込まれるため、上書きを防ぐため処理を中止しました: {0}", "여러 입력 파일이 동일한 출력 경로에 기록되므로 덮어쓰기를 방지하기 위해 변환을 중단했습니다: {0}"),
                ("codec_missing_store_prompt", "{0}\n\n是否立即開啟 Microsoft Store 免費安裝「{1}」？", "{0}\n\n是否立即打开 Microsoft Store 免费安装「{1}」？", "{0}\n\nOpen the Microsoft Store now to install the free \"{1}\"?", "{0}\n\n今すぐ Microsoft Store を開いて無料の「{1}」をインストールしますか？", "{0}\n\n지금 Microsoft Store를 열어 무료 \"{1}\"을(를) 설치하시겠습니까?"),
                ("codec_heif_extension_name", "HEIF 影像延伸", "HEIF 图像扩展", "HEIF Image Extensions", "HEIF 画像拡張機能", "HEIF 이미지 확장"),
                ("codec_missing_install_action", "安裝編碼器", "安装编码器", "Install codec", "コーデックをインストール", "코덱 설치")
            };

            RegisterTranslations(data);
        }

        private static void RegisterFluentDashboardTranslations()
        {
            var data = new (string Key, string Tw, string Cn, string En, string Ja, string Ko)[]
            {
                ("fluent_nav_overview", "總覽", "总览", "Overview", "概要", "개요"),
                ("fluent_nav_convert", "轉檔", "转换", "Convert", "変換", "변환"),
                ("fluent_nav_history", "歷史", "历史", "History", "履歴", "기록"),
                ("fluent_nav_settings", "設定", "设置", "Settings", "設定", "설정"),
                ("fluent_nav_about", "關於", "关于", "About", "情報", "정보"),
                ("fluent_overview_title", "轉換檔案", "转换文件", "Convert files", "ファイルを変換", "파일 변환"),
                ("fluent_overview_subtitle", "PDF、Office 與圖片工具集中在同一個工作區。", "PDF、Office 与图片工具集中在同一个工作区。", "PDF, Office, and image tools in one workspace.", "PDF、Office、画像ツールを 1 つの作業領域に集約。", "PDF, Office, 이미지 도구를 한 작업 공간에서 사용합니다."),
                ("fluent_choose_files", "選擇檔案", "选择文件", "Choose files", "ファイルを選択", "파일 선택"),
                ("fluent_recent_jobs", "近期作業", "近期作业", "Recent jobs", "最近のジョブ", "최근 작업"),
                ("fluent_recent_activity", "近期活動", "近期活动", "Recent activity", "最近の操作", "최근 활동"),
                ("fluent_no_history", "尚無轉換紀錄。", "暂无转换记录。", "No conversion history yet.", "変換履歴はまだありません。", "변환 기록이 없습니다."),
                ("fluent_activity_summary", "活動摘要", "活动摘要", "Activity summary", "操作サマリー", "활동 요약"),
                ("fluent_total", "總計", "总计", "Total", "合計", "전체"),
                ("fluent_success", "成功", "成功", "OK", "成功", "성공"),
                ("fluent_failed", "失敗", "失败", "Fail", "失敗", "실패"),
                ("fluent_tools", "PDF 與 Office 工具", "PDF 与 Office 工具", "PDF and Office tools", "PDF と Office ツール", "PDF 및 Office 도구"),
                ("fluent_explorer_menu", "檔案總管選單", "文件资源管理器菜单", "Explorer menu", "Explorer メニュー", "Explorer 메뉴"),
                ("fluent_available", "可用", "可用", "Available", "利用可能", "사용 가능"),
                ("fluent_pdf_desc", "合併、壓縮、翻譯、解密。", "合并、压缩、翻译、解密。", "Merge, compress, translate, decrypt.", "結合、圧縮、翻訳、復号。", "병합, 압축, 번역, 암호 해제."),
                ("fluent_office_desc", "Word、Excel、PowerPoint 轉 PDF。", "Word、Excel、PowerPoint 转 PDF。", "Word, Excel, PowerPoint to PDF.", "Word、Excel、PowerPoint を PDF へ。", "Word, Excel, PowerPoint를 PDF로 변환."),
                ("fluent_images_desc", "建立 PDF、合併、拼接。", "创建 PDF、合并、拼接。", "Create PDFs, merge, stitch.", "PDF 作成、結合、連結。", "PDF 생성, 병합, 이어붙이기."),
                ("fluent_drop_title", "拖放檔案到這裡", "拖放文件到这里", "Drop files here", "ここにファイルをドロップ", "여기에 파일 놓기"),
                ("fluent_drop_browse", "點擊此區域選取檔案", "点击此区域选择文件", "Click this area to browse", "この領域をクリックして選択", "이 영역을 클릭해 선택"),
                ("fluent_drop_types", "PDF、Office 文件與圖片", "PDF、Office 文档与图片", "PDF, Office documents, and images", "PDF、Office 文書、画像", "PDF, Office 문서, 이미지"),
                ("fluent_selected_files", "已選取檔案", "已选择文件", "Selected files", "選択したファイル", "선택한 파일"),
                ("fluent_selected_files_desc", "可個別移除；轉換後只清除用到的檔案。", "可单独移除；转换后只清除用到的文件。", "Files can be removed individually; only used files are cleared after conversion.", "個別に削除できます。変換後は使用したファイルだけがクリアされます。", "개별로 제거할 수 있으며, 변환 후 사용된 파일만 지워집니다."),
                ("fluent_no_files", "未選取檔案", "未选择文件", "No files selected", "ファイル未選択", "선택한 파일 없음"),
                ("fluent_remove_file", "移除這個檔案", "移除这个文件", "Remove this file", "このファイルを削除", "이 파일 제거"),
                ("fluent_file_type", "檔案類型", "文件类型", "File type", "ファイル種類", "파일 유형"),
                ("fluent_file_type_pdf", "PDF", "PDF", "PDF", "PDF", "PDF"),
                ("fluent_file_type_word", "Word", "Word", "Word", "Word", "Word"),
                ("fluent_file_type_excel", AppExcel, AppExcel, AppExcel, AppExcel, AppExcel),
                ("fluent_file_type_ppt", "PPT", "PPT", "PPT", "PPT", "PPT"),
                ("fluent_file_type_image", "圖片", "图片", "Images", "画像", "이미지"),
                ("fluent_drop_title_for_type", "拖放 {0} 檔案到這裡", "拖放 {0} 文件到这里", "Drop {0} files here", "{0} ファイルをここにドロップ", "{0} 파일을 여기에 놓기"),
                ("fluent_drop_types_only", "僅接受 {0} 檔案", "仅接受 {0} 文件", "Only {0} files are accepted", "{0} ファイルのみ受け付けます", "{0} 파일만 허용됩니다"),
                ("fluent_files_skipped_type", "已略過 {0} 個不屬於「{1}」類型的檔案。", "已跳过 {0} 个不属于“{1}”类型的文件。", "Skipped {0} file(s) that are not {1} files.", "{1} ファイルではない {0} 件をスキップしました。", "{1} 파일이 아닌 {0}개를 건너뛰었습니다."),
                ("fluent_files_removed_type", "已移除 {0} 個不屬於「{1}」類型的檔案。", "已移除 {0} 个不属于“{1}”类型的文件。", "Removed {0} file(s) that are not {1} files.", "{1} ファイルではない {0} 件を削除しました。", "{1} 파일이 아닌 {0}개를 제거했습니다."),
                ("fluent_command", "命令", "命令", "Command", "コマンド", "명령"),
                ("fluent_choose_command", "選擇命令", "选择命令", "Choose a command", "コマンドを選択", "명령 선택"),
                ("fluent_selected_command", "已選擇：{0}", "已选择：{0}", "Selected: {0}", "選択中: {0}", "선택됨: {0}"),
                ("fluent_run", "執行", "运行", "Run", "実行", "실행"),
                ("fluent_start", "開始轉換", "开始转换", "Start conversion", "変換開始", "변환 시작"),
                ("fluent_ready", "準備就緒", "准备就绪", "Ready", "準備完了", "준비됨"),
                ("fluent_file_count", "個檔案", "个文件", "file(s)", "件", "개 파일"),
                ("fluent_select_history", "選取一筆歷史紀錄", "选择一条历史记录", "Select a history item", "履歴項目を選択", "기록 항목 선택"),
                ("fluent_files", "檔案", "文件", "Files", "ファイル", "파일"),
                ("fluent_elapsed", "耗時", "耗时", "Elapsed", "経過時間", "소요 시간"),
                ("fluent_result", "結果", "结果", "Result", "結果", "결과"),
                ("fluent_input_paths", "輸入路徑", "输入路径", "Input paths", "入力パス", "입력 경로"),
                ("fluent_output_paths", "輸出路徑", "输出路径", "Output paths", "出力パス", "출력 경로"),
                ("fluent_error_message", "錯誤訊息", "错误消息", "Error message", "エラーメッセージ", "오류 메시지"),
                ("fluent_settings_subtitle", "使用者介面與檔案總管命令的預設值", "界面与文件资源管理器命令的默认值", "Defaults used by the dashboard and Explorer commands", "ダッシュボードと Explorer コマンドの既定値", "대시보드와 Explorer 명령의 기본값"),
                ("fluent_output_dir", "輸出資料夾", "输出文件夹", "Output directory", "出力フォルダー", "출력 폴더"),
                ("fluent_output_dir_desc", "轉換後檔案的儲存位置", "转换后文件的保存位置", "Where converted files are saved", "変換後ファイルの保存先", "변환된 파일 저장 위치"),
                ("fluent_output_source", "與來源相同", "与来源相同", "Same as source", "元と同じ", "원본과 같음"),
                ("fluent_office_engine", "Office 引擎", "Office 引擎", "Office engine", "Office エンジン", "Office 엔진"),
                ("fluent_office_engine_desc", "Office 轉檔使用的引擎", "Office 转换使用的引擎", "Engine used for Office conversion", "Office 変換に使うエンジン", "Office 변환에 사용할 엔진"),
                ("fluent_custom", "自訂...", "自定义...", "Custom...", "カスタム...", "사용자 지정..."),
                ("fluent_default_language", "預設語言", "默认语言", "Default language", "既定の言語", "기본 언어"),
                ("fluent_default_language_desc", "用於介面、檔案總管命令與處理訊息。", "用于界面、文件资源管理器命令与处理消息。", "Used by the interface, Explorer commands, and processor messages.", "UI、Explorer コマンド、処理メッセージに使用します。", "UI, Explorer 명령, 처리 메시지에 사용합니다."),
                ("fluent_pdf_target", "PDF 翻譯目標", "PDF 翻译目标", "PDF translation target", "PDF 翻訳先", "PDF 번역 대상"),
                ("fluent_behavior", "行為", "行为", "Behavior", "動作", "동작"),
                ("fluent_quiet_mode", "靜默模式", "静默模式", "Quiet mode", "静音モード", "무음 모드"),
                ("fluent_quiet_mode_desc", "檔案總管命令預設使用；此視窗仍會顯示。", "文件资源管理器命令默认使用；此窗口仍会显示。", "Default for Explorer commands; this dashboard still stays visible.", "Explorer コマンドの既定。この画面は表示されます。", "Explorer 명령의 기본값입니다. 이 창은 계속 표시됩니다."),
                ("fluent_notifications", "通知", "通知", "Notifications", "通知", "알림"),
                ("fluent_notifications_desc", "轉換完成時顯示通知", "转换完成时显示通知", "Show toast when conversion completes", "変換完了時に通知を表示", "변환 완료 시 알림 표시"),
                ("fluent_pdf_compression", "PDF 壓縮", "PDF 压缩", "PDF compression", "PDF 圧縮", "PDF 압축"),
                ("fluent_strip_fonts", "移除嵌入字型", "移除嵌入字体", "Strip embedded fonts", "埋め込みフォントを削除", "포함 글꼴 제거"),
                ("fluent_minify_content", "最佳化 PDF 結構", "优化 PDF 结构", "Optimize PDF structure", "PDF 構造を最適化", "PDF 구조 최적화"),
                ("fluent_about_desc", "Clickra 是 Windows 上快速、輕量的檔案轉換工作區。", "Clickra 是 Windows 上快速、轻量的文件转换工作区。", "A fast, lightweight file conversion workspace for Windows.", "Windows 向けの高速で軽量なファイル変換ワークスペース。", "Windows용 빠르고 가벼운 파일 변환 작업 공간입니다."),
                ("fluent_github", "在 GitHub 檢視", "在 GitHub 查看", "View on GitHub", "GitHub で表示", "GitHub에서 보기"),
                ("fluent_platform", "平台", "平台", "Platform", "プラットフォーム", "플랫폼"),
                ("fluent_app_model", "應用程式模型", "应用模型", "App model", "アプリモデル", "앱 모델"),
                ("fluent_toast_done_title", "Clickra 轉換完成", "Clickra 转换完成", "Clickra conversion completed", "Clickra 変換完了", "Clickra 변환 완료"),
                ("fluent_toast_done_body", "{0} 已完成，共 {1} 個檔案。", "{0} 已完成，共 {1} 个文件。", "{0} finished for {1} file(s).", "{0} が完了しました。{1} 件。", "{0} 완료, {1}개 파일."),
                ("fluent_toast_canceled_title", "Clickra 轉換已取消", "Clickra 转换已取消", "Clickra conversion canceled", "Clickra 変換キャンセル", "Clickra 변환 취소"),
                ("fluent_toast_canceled_body", "{0} 已取消。", "{0} 已取消。", "{0} was canceled.", "{0} はキャンセルされました。", "{0} 취소됨."),
                ("fluent_toast_failed_title", "Clickra 轉換失敗", "Clickra 转换失败", "Clickra conversion failed", "Clickra 変換失敗", "Clickra 변환 실패"),
                ("fluent_validate_min_files", "{0} 至少需要 {1} 個檔案。", "{0} 至少需要 {1} 个文件。", "{0} needs at least {1} file(s).", "{0} には少なくとも {1} 件のファイルが必要です。", "{0}에는 최소 {1}개 파일이 필요합니다."),
                ("fluent_validate_bad_ext", "{0} 不適用於 {1}。", "{0} 不适用于 {1}。", "{0} is not valid for {1}.", "{0} は {1} に使用できません。", "{0}은(는) {1}에 사용할 수 없습니다."),
                ("fluent_progress_starting", "正在開始...", "正在开始...", "Starting...", "開始中...", "시작 중..."),
                ("fluent_progress_completed", "已完成。", "已完成。", "Completed.", "完了しました。", "완료됨."),
                ("fluent_progress_canceled", "已取消。", "已取消。", "Canceled.", "キャンセルしました。", "취소됨."),
                ("fluent_progress_failed", "失敗：{0}", "失败：{0}", "Failed: {0}", "失敗: {0}", "실패: {0}"),
                ("fluent_progress_running_title", "正在{0}", "正在{0}", "{0} in progress", "{0} を実行中", "{0} 진행 중"),
                ("fluent_progress_preparing", "準備中", "准备中", "Preparing", "準備中", "준비 중"),
                ("fluent_progress_output", "輸出：", "输出：", "Output: ", "出力: ", "출력: "),
                ("fluent_progress_waiting", "請稍候，正在背景處理中...", "请稍候，正在后台处理中...", "Please wait. Processing in the background...", "お待ちください。バックグラウンドで処理しています...", "잠시만 기다려 주세요. 백그라운드에서 처리 중입니다..."),
                ("fluent_progress_processing", "正在處理...", "正在处理...", "Processing...", "処理中...", "처리 중..."),
                ("fluent_progress_done_title", "處理完成", "处理完成", "Completed", "処理完了", "처리 완료"),
                ("fluent_progress_failed_title", "處理未完成", "处理未完成", "Not completed", "未完了", "완료되지 않음"),
                ("fluent_progress_done_footer", "處理完成，可以開啟輸出資料夾。", "处理完成，可以打开输出文件夹。", "Completed. You can open the output folder.", "完了しました。出力フォルダーを開けます。", "완료되었습니다. 출력 폴더를 열 수 있습니다."),
                ("fluent_progress_failed_footer", "請檢查訊息後關閉視窗。", "请检查消息后关闭窗口。", "Check the message, then close this window.", "メッセージを確認してから閉じてください。", "메시지를 확인한 후 창을 닫아 주세요."),
                ("fluent_progress_open_folder", "開啟資料夾", "打开文件夹", "Open folder", "フォルダーを開く", "폴더 열기"),
                ("fluent_progress_close", "關閉", "关闭", "Close", "閉じる", "닫기"),
                ("fluent_progress_done_summary", "{0} 個檔案 · 耗時 {1} 秒", "{0} 个文件 · 耗时 {1} 秒", "{0} file(s) · {1} s", "{0} 件 · {1} 秒", "{0}개 파일 · {1}초"),
                ("fluent_progress_failed_generic", "轉換過程中發生錯誤。", "转换过程中发生错误。", "An error occurred during conversion.", "変換中にエラーが発生しました。", "변환 중 오류가 발생했습니다."),
                ("fluent_progress_invalid_command", "無效的轉換命令。", "无效的转换命令。", "Invalid conversion command.", "無効な変換コマンドです。", "잘못된 변환 명령입니다."),
                ("fluent_progress_file_not_found", "找不到可轉換的檔案。", "找不到可转换的文件。", "No convertible files were found.", "変換できるファイルが見つかりません。", "변환할 파일을 찾을 수 없습니다."),
                ("fluent_progress_multiple_files", "{0} 等 {1} 個檔案", "{0} 等 {1} 个文件", "{0} and {1} files", "{0} ほか {1} 件", "{0} 외 {1}개 파일"),
                ("fluent_task_queue_title", "進行中任務", "进行中任务", "Active tasks", "実行中のタスク", "실행 중인 작업"),
                ("fluent_task_queue_running", "{0} 個任務進行中", "{0} 个任务进行中", "{0} task(s) in progress", "{0} 件のタスク実行中", "{0}개 작업 실행 중"),
                ("fluent_task_ledger_title", "進行中任務", "进行中任务", "Active tasks", "実行中のタスク", "실행 중인 작업"),
                ("fluent_task_ledger_empty", "目前沒有進行中的任務。", "当前没有进行中的任务。", "No active tasks.", "実行中のタスクはありません。", "실행 중인 작업이 없습니다."),
                ("fluent_task_view", "查看", "查看", "View", "表示", "보기"),
                ("fluent_task_resume", "繼續", "继续", "Resume", "再開", "재개"),
                ("fluent_task_file_index", "第 {0}/{1} 檔", "第 {0}/{1} 个文件", "File {0}/{1}", "{0}/{1} ファイル", "{0}/{1} 파일"),
                ("setting_parked_ttl_title", "暫存保留", "暂存保留", "Parked task retention", "一時停止の保持期間", "보류 작업 보관"),
                ("setting_parked_ttl_desc", "已暫存轉換的保留天數（0 = 無限期）", "已暂存转换的保留天数（0 = 无期限）", "Days to keep parked conversions (0 = unlimited)", "一時停止した変換の保持日数（0 = 無期限）", "보류된 변환 보관 일수(0 = 무제한)"),
                ("setting_parked_ttl_days", RetentionDayZh, RetentionDayZh, "{0} days", "{0} 日", "{0} 일"),
                ("setting_parked_ttl_day_single", RetentionDayZh, RetentionDayZh, "{0} day", "{0} 日", "{0} 일"),
                ("setting_parked_ttl_unlimited", "0（無限期）", "0（无期限）", "0 (unlimited)", "0（無期限）", "0 (무제한)"),
                ("setting_parked_ttl_default", "{0} 天（預設）", "{0} 天（默认）", "{0} days (default)", "{0} 日（デフォルト）", "{0} 일 (기본값)"),
                ("setting_parked_ttl_current", "目前保留：{0}", "当前保留：{0}", "Current retention: {0}", "現在の保持期間: {0}", "현재 보관 기간: {0}"),
                ("fluent_park_toast_title", "Clickra 轉換已暫存", "Clickra 转换已暂存", "Clickra conversion parked", "Clickra 変換を一時停止", "Clickra 변환이 보류됨"),
                ("fluent_park_toast_body", "可在「歷史」頁繼續或取消。", "可在“历史”页继续或取消。", "Resume or cancel it from the History page.", "「履歴」ページで再開またはキャンセルできます。", "「기록」페이지에서 재개하거나 취소할 수 있습니다."),
                ("fluent_pdf_password", "PDF 密碼", "PDF 密码", "PDF password", "PDF パスワード", "PDF 암호"),
                ("fluent_pdf_password_placeholder", "輸入 PDF 密碼", "输入 PDF 密码", "Enter PDF password", "PDF パスワードを入力", "PDF 암호 입력"),
                ("fluent_ok", "確定", "确定", "OK", "OK", "확인"),
                ("fluent_office", OfficeName, OfficeName, OfficeName, OfficeName, OfficeName),
                ("fluent_images", "圖片", "图片", "Images", "画像", "이미지"),
                ("fluent_history_subtitle", "近期轉換結果", "近期转换结果", "Recent conversion results", "最近の変換結果", "최근 변환 결과"),
                ("task_parked_waiting", "等待輸入", "等待输入", "Waiting for input", "入力待ち", "입력 대기"),
                ("task_parked_title", "待繼續任務", "待继续任务", "Tasks awaiting continuation", "再開待ちタスク", "재개 대기 작업"),
                ("task_parked_desc", "已暫存的轉換作業，可在過期前隨時繼續或取消。", "已暂存的转换任务，可在过期前随时继续或取消。", "Parked conversions that can be resumed or cancelled before expiring.", "一時停止した変換作業は、有効期限が切れる前にいつでも再開またはキャンセルできます。", "보류된 변환 작업은 만료되기 전에 언제든지 재개하거나 취소할 수 있습니다."),
                ("task_parked_cancel_confirm", "確定要取消此暫存任務嗎？", "确定要取消此暂存任务吗？", "Are you sure you want to cancel this parked task?", "この一時停止タスクをキャンセルしてもよろしいですか？", "이 보류된 작업을 취소하시겠습니까?"),
                ("task_parked_ttl_days", "剩餘 {0} 天過期", "剩余 {0} 天过期", "Expires in {0} days", "残り {0} 日で期限切れ", "{0}일 후 만료"),
                ("task_parked_ttl_days_one", "剩餘 {0} 天過期", "剩余 {0} 天过期", "Expires in {0} day", "残り {0} 日で期限切れ", "{0}일 후 만료"),
                ("task_parked_ttl_expiring_soon", "即將過期（剩餘不到 1 天）", "即将过期（剩余不到 1 天）", "Expiring soon (less than 1 day left)", "まもなく期限切れ（残り1日未満）", "곧 만료됨 (1일 미만 남음)"),
                ("task_parked_ttl_unlimited", "永久保留", "永久保留", "Never expires", "無期限に保持", "영구 보관"),
                ("task_parked_ttl_expired", "已過期（即將清理）", "已过期（即将清理）", "Expired (pending cleanup)", "期限切れ（まもなく削除）", "만료됨 (곧 정리됨)"),
                ("task_parked_expiring_warning", "⚠️ 有 {0} 個待繼續任務即將過期，請儘速處理。", "⚠️ 有 {0} 个待继续任务即将过期，请尽快处理。", "⚠️ {0} parked task(s) expiring soon. Please resume or cancel promptly.", "⚠️ {0} 件の再開待ちタスクがまもなく期限切れになります。速やかに処理してください。", "⚠️ {0}개의 재개 대기 작업이 곧 만료됩니다. 신속히 처리해 주세요."),
                ("task_parked_badge_expiring", "即將過期", "即将过期", "Expiring soon", "まもなく期限切れ", "곧 만료됨"),
                ("setting_image_compress_title", "圖片壓縮品質", "图片压缩质量", "Image Compression Quality", "画像圧縮品質", "이미지 압축 품질"),
                ("setting_image_compress_desc", "選擇圖片壓縮品質等級（等級越高，壓縮比越大）", "选择图片压缩质量等级（等级越高，压缩比越大）", "Select image compression quality level (higher levels compress more)", "画像圧縮品質レベルを選択（高いほど高圧縮）", "이미지 압축 품질 수준 선택 (높을수록 압축률 증가)"),
                ("setting_image_level_min", "最低", "最低", "Minimum", "最小", "최소"),
                ("setting_image_level_small", "較小", "较小", "Small", "小", "작게"),
                ("setting_image_level_std", "標準", "标准", "Standard", "標準", "표준"),
                ("setting_image_level_high", "高壓縮", "高压缩", "High", "高圧縮", "고압축"),
                ("setting_image_max_dimension_title", "圖片最大長邊", "图片最大长边", "Image Max Dimension", "画像最大長辺", "이미지 최대 긴 변"),
                ("setting_image_max_dimension_desc", "限制壓縮後圖片的長邊像素（0 = 保持原始尺寸）", "限制压缩后图片的长边像素（0 = 保持原始尺寸）", "Max long-edge pixels for compressed images (0 = keep original size)", "圧縮後画像の長辺ピクセルを制限（0 = 元のサイズを維持）", "압축 후 이미지의 긴 쪽 픽셀 제한 (0 = 원본 크기 유지)"),
                ("fluent_status_canceled", "已取消", "已取消", "Canceled", "キャンセル済み", "취소됨"),
            };

            RegisterTranslations(data);
        }

        private static void RegisterCliTranslations()
        {
            var data = new (string Key, string Tw, string Cn, string En, string Ja, string Ko)[]
            {
                ( "cli_err_prefix", "[錯誤] ", "[错误] ", "[Error] ", "[エラー] ", "[오류] " ),
                ( "cli_err_unknown_command", "未知指令: {0}", "未知指令: {0}", "Unknown command: {0}", "不明なコマンド: {0}", "알 수 없는 명령: {0}" ),
                ( "cli_err_no_files_found", "指令「{0}」找不到可處理的檔案。", "指令“{0}”找不到可处理的文件。", "No convertible files found for command '{0}'.", "コマンド「{0}」で処理できるファイルが見つかりません。", "명령 '{0}'에서 처리할 수 있는 파일을 찾을 수 없습니다." ),
                ( "cli_err_invalid_format", "指令「{0}」只接受以下格式：{1}\n\n以下檔案格式不符，已中止執行：\n  {2}", "指令“{0}”只接受以下格式：{1}\n\n以下文件格式不符，已中止执行：\n  {2}", "Command '{0}' only accepts the following formats: {1}\n\nThe following files have invalid formats and execution was aborted:\n  {2}", "コマンド「{0}」は次の形式のみ受け付けます: {1}\n\n次のファイル形式が一致しないため、実行を中止しました:\n  {2}", "명령 '{0}'은(는) 다음 형식만 지원합니다: {1}\n\n다음 파일 형식이 일치하지 않아 실행을 중단했습니다:\n  {2}" ),
                ( "cli_err_invalid_format_title", "Clickra — 格式錯誤", "Clickra — 格式错误", "Clickra — Invalid Format", "Clickra — 形式エラー", "Clickra — 형식 오류" ),
                ( "cli_err_min_files", "指令「{0}」至少需要 {1} 個檔案，但您只傳入了 {2} 個。\n\n請多選幾個檔案後，再透過「傳送到」執行。", "指令“{0}”至少需要 {1} 个文件，但您只传入了 {2} 个。\n\n请多选几个文件后，再通过“发送到”执行。", "Command '{0}' requires at least {1} file(s), but only {2} were provided.\n\nPlease select more files and try again via 'Send to'.", "コマンド「{0}」には少なくとも {1} 個のファイルが必要ですが、{2} 個しか指定されていません。\n\n複数のファイルを選択してから「送る」で再実行してください。", "명령 '{0}'은(는) 최소 {1}개의 파일이 필요하지만 {2}개만 전달되었습니다.\n\n파일을 더 선택한 후 '보내기'를 통해 다시 실행하세요." ),
                ( "cli_err_min_files_title", "Clickra — 檔案數量不足", "Clickra — 文件数量不足", "Clickra — Insufficient Files", "Clickra — ファイル数が不足", "Clickra — 파일 수 부족" ),
                ( "cli_err_option_requires_dir", "參數「{0}」需要指定資料夾。", "参数“{0}”需要指定文件夹。", "Option '{0}' requires a target directory.", "オプション「{0}」にはフォルダーの指定が必要です。", "옵션 '{0}'은(는) 폴더를 지정해야 합니다." ),
                ( "cli_progress_compressing_pdf", "正在壓縮 PDF: {0} ({1}/{2})...", "正在压缩 PDF: {0} ({1}/{2})...", "Compressing PDF: {0} ({1}/{2})...", "PDF を圧縮中: {0} ({1}/{2})...", "PDF 압축 중: {0} ({1}/{2})..." ),
                ( "cli_progress_splitting_pdf", "正在分割 PDF: {0} ({1}/{2})...", "正在分割 PDF: {0} ({1}/{2})...", "Splitting PDF: {0} ({1}/{2})...", "PDF を分割中: {0} ({1}/{2})...", "PDF 분할 중: {0} ({1}/{2})..." ),
                ( "cli_progress_converting_image", "正在轉換圖片: {0} ({1}/{2})...", "正在转换图片: {0} ({1}/{2})...", "Converting image: {0} ({1}/{2})...", "画像を変換中: {0} ({1}/{2})...", "이미지 변환 중: {0} ({1}/{2})..." ),
                ( "cli_progress_converting_image_saving", "轉換完成，正在儲存 PDF...", "转换完成，正在保存 PDF...", "Conversion complete, saving PDF...", "変換完了、PDF を保存中...", "변환 완료, PDF 저장 중..." ),
                ( "cli_progress_decrypting_pdf", "正在移除密碼: {0} ({1}/{2})...", "正在移除密码: {0} ({1}/{2})...", "Removing password: {0} ({1}/{2})...", "パスワードを解除中: {0} ({1}/{2})...", "암호 제거 중: {0} ({1}/{2})..." ),
                ( "cli_progress_translating_pdf_start", "開始翻譯 PDF: {0} ({1}/{2})", "开始翻译 PDF: {0} ({1}/{2})", "Starting PDF translation: {0} ({1}/{2})", "PDF 翻訳を開始: {0} ({1}/{2})", "PDF 번역 시작: {0} ({1}/{2})" ),
                ( "cli_progress_translating_pdf", "正在翻譯 PDF: {0} ({1}/{2})...", "正在翻译 PDF: {0} ({1}/{2})...", "Translating PDF: {0} ({1}/{2})...", "PDF を翻訳中: {0} ({1}/{2})...", "PDF 번역 중: {0} ({1}/{2})..." ),
                ( "cli_progress_translating_pdf_done", "完成翻譯 PDF: {0} ({1}/{2})", "完成翻译 PDF: {0} ({1}/{2})", "Finished translating PDF: {0} ({1}/{2})", "PDF 翻訳が完了: {0} ({1}/{2})", "PDF 번역 완료: {0} ({1}/{2})" ),
                ( "cli_warn_file_missing_skip", "跳過已不存在的 PDF: {0} ({1}/{2})", "跳过已不存在的 PDF: {0} ({1}/{2})", "Skipping missing PDF: {0} ({1}/{2})", "存在しない PDF をスキップ: {0} ({1}/{2})", "존재하지 않는 PDF 건너뜀: {0} ({1}/{2})" ),
                ( "cli_warn_translate_file_vanished", "翻譯期間檔案消失，已跳過: {0}", "翻译期间文件消失，已跳过: {0}", "File disappeared during translation, skipped: {0}", "翻訳中にファイルが消失したためスキップしました: {0}", "번역 중 파일이 사라져 건너뛰었습니다: {0}" ),
                ( "cli_warn_translate_dir_vanished", "翻譯期間資料夾消失，已跳過: {0}", "翻译期间文件夹消失，已跳过: {0}", "Directory disappeared during translation, skipped: {0}", "翻訳中にフォルダーが消失したためスキップしました: {0}", "번역 중 폴더가 사라져 건너뛰었습니다: {0}" ),
                ( "cli_err_translate_failed", "翻譯檔案未完成: {0}. 錯誤訊息: {1}", "翻译文件未完成: {0}. 错误信息: {1}", "Translation failed: {0}. Error: {1}", "ファイルの翻訳が未完了です: {0}. エラー: {1}", "파일 번역 미완료: {0}. 오류: {1}" ),
                ( "progress_sub_failed", "作業失敗", "作业失败", "Operation failed", "処理失敗", "작업 실패" ),
                ( "progress_sub_completed", "作業完成", "作业完成", "Operation completed", "処理完了", "작업 완료" ),
                ( "progress_sub_visual_splitter", "PDF 視覺化分割標記", "PDF 可视化分割标记", "PDF visual split marking", "PDF 視覚的分割マーキング", "PDF 시각적 분할 표시" ),
                ( "progress_sub_running", "正在執行作業...", "正在执行作业...", "Working...", "処理を実行中...", "작업 실행 중..." ),
                ( "progress_header_failed", "處理失敗", "处理失败", "Processing failed", "処理失敗", "처리 실패" ),
                ( "progress_header_success", "轉換成功！", "转换成功！", "Conversion successful!", "変換に成功しました！", "변환 성공!" ),
                ( "progress_auto_close_hint", "視窗將於數秒後自動關閉...", "窗口将于数秒后自动关闭...", "Window will close automatically in a few seconds...", "ウィンドウは数秒後に自動で閉じます...", "창이 몇 초 후 자동으로 닫힙니다..." ),
                ( "progress_tip_processing", "請稍候，正在背景高速處理中...", "请稍候，正在后台高速处理中...", "Please wait, processing in background...", "しばらくお待ちください。バックグラウンドで処理中です...", "잠시 기다려 주십시오. 백그라운드에서 처리 중입니다..." ),
                ("cli_err_no_input", "未傳入任何檔案進行處理。", "未传入任何文件进行处理。", "No files were passed in for processing.", "処理するファイルが渡されていません。", "처리할 파일이 전달되지 않았습니다."),
                ("cli_err_no_input_title", "Clickra — 警告", "Clickra — 警告", "Clickra — Warning", "Clickra — 警告", "Clickra — 경고"),
                ("cli_progress_all_done", "所有作業已順利完成！", "所有作业已顺利完成！", "All operations completed successfully!", "すべての処理が完了しました！", "모든 작업이 완료되었습니다!"),
                ("cli_progress_compressing_pdf_done", "PDF 壓縮完成。", "PDF 压缩完成。", "PDF compression complete.", "PDF 圧縮が完了しました。", "PDF 압축 완료."),
                ("cli_progress_compressing_pdf_stage", "[PDF 壓縮] {0} ({1}/{2})", "[PDF 压缩] {0} ({1}/{2})", "[PDF compress] {0} ({1}/{2})", "[PDF 圧縮] {0} ({1}/{2})", "[PDF 압축] {0} ({1}/{2})"),
                ("cli_progress_decrypting_pdf_saving", "密碼去除完成，正在儲存 PDF...", "密码去除完成，正在保存 PDF...", "Password removal complete, saving PDF...", "パスワード解除完了、PDF を保存中...", "비밀번호 제거 완료, PDF 저장 중..."),
                ("cli_progress_decrypting_pdf_stage", "[去除密碼] {0} ({1}/{2})", "[去除密码] {0} ({1}/{2})", "[Remove password] {0} ({1}/{2})", "[パスワード解除] {0} ({1}/{2})", "[비밀번호 제거] {0} ({1}/{2})"),
                ("cli_progress_no_files", "無檔案可處理。", "无文件可处理。", "No files to process.", "処理できるファイルがありません。", "처리할 파일이 없습니다."),
                ("cli_progress_pages_input_canceled", "使用者已取消頁碼範圍輸入。", "用户已取消页码范围输入。", "Page range input was canceled by the user.", "ユーザーがページ範囲の入力をキャンセルしました。", "사용자가 페이지 범위 입력을 취소했습니다."),
                ("cli_progress_preparing", "正在準備處理...", "正在准备处理...", "Preparing...", "準備しています...", "준비 중..."),
                ("cli_progress_splitting_pdf_done", "PDF 分割完成。", "PDF 分割完成。", "PDF split complete.", "PDF 分割が完了しました。", "PDF 분할 완료."),
                ("cli_progress_splitting_pdf_stage", "[PDF 分割] {0} ({1}/{2})", "[PDF 分割] {0} ({1}/{2})", "[PDF split] {0} ({1}/{2})", "[PDF 分割] {0} ({1}/{2})", "[PDF 분할] {0} ({1}/{2})"),
                ("cli_progress_toast_body", "已順利完成「{0}」作業（共 {1} 個檔案）。", "已顺利完成“{0}”作业（共 {1} 个文件）。", "Completed {0} successfully ({1} file(s)).", "「{0}」を {1} 件すべて完了しました。", "'{0}' 작업을 {1}개 파일 모두 완료했습니다."),
                ("cli_progress_toast_title", "Clickra 轉換成功", "Clickra 转换成功", "Clickra conversion complete", "Clickra 変換完了", "Clickra 변환 완료"),
                ("cli_progress_translating_pdf_saving", "翻譯完成，正在儲存 PDF...", "翻译完成，正在保存 PDF...", "Translation complete, saving PDF...", "翻訳完了、PDF を保存中...", "번역 완료, PDF 저장 중..."),
                ("cli_progress_translating_pdf_stage", "[PDF 翻譯] {0} ({1}/{2})", "[PDF 翻译] {0} ({1}/{2})", "[PDF translate] {0} ({1}/{2})", "[PDF 翻訳] {0} ({1}/{2})", "[PDF 번역] {0} ({1}/{2})"),
                ("cli_tray_converting", "Clickra - 正在轉換... {0}%", "Clickra - 正在转换... {0}%", "Clickra - Converting... {0}%", "Clickra - 変換中... {0}%", "Clickra - 변환 중... {0}%"),
                ("cli_tray_restore", "還原", "还原", "Restore", "復元", "복원"),
                ("cli_tray_cancel", "取消作業", "取消作业", "Cancel Operation", "処理を中止", "작업 취소"),
            };

            RegisterTranslations(data);
        }

        private static void RegisterDiagnosticsEmailTranslations()
        {
            var data = new (string Key, string Tw, string Cn, string En, string Ja, string Ko)[]
            {
                ( "diag_email_subject", "Clickra 診斷回報", "Clickra 诊断报告", "Clickra Diagnostics Report", "Clickra 診断レポート", "Clickra 진단 보고서" ),
                ( "diag_email_thanks", "感謝您提交 Clickra 診斷回報！", "感谢您提交 Clickra 诊断报告！", "Thank you for submitting a Clickra diagnostics report!", "Clickra 診断レポートの送信にご協力いただきありがとうございます。", "Clickra 진단 보고서를 제출해 주셔서 감사합니다!" ),
                ( "diag_email_attachment_hint", "請直接將已為您選取好的「history.log」拖曳到此郵件中作為附件。", "请直接将已为您选中的“history.log”拖拽到此邮件中作为附件。", "Please drag and drop the selected \"history.log\" file into this email as an attachment.", "選択されている「history.log」をこのメールにドラッグ＆ドロップして添付してください。", "선택된 'history.log' 파일을 이 메일에 첨부 파일로 끌어다 놓으세요." ),
                ( "diag_email_system_info", "[系統資訊]", "[系统信息]", "[System Information]", "[システム情報]", "[시스템 정보]" ),
                ( "diag_email_os", "作業系統: Windows", "操作系统: Windows", "Operating System: Windows", "OS: Windows", "운영 체제: Windows" ),
                ( "diag_email_version", "Clickra 版本: {0}", "Clickra 版本: {0}", "Clickra Version: {0}", "Clickra バージョン: {0}", "Clickra 버전: {0}" ),
                ( "diag_email_time", "時間: {0}", "时间: {0}", "Time: {0}", "日時: {0}", "시간: {0}" ),
                ( "diag_email_problem_desc", "[問題描述]", "[问题描述]", "[Problem Description]", "[問題の説明]", "[문제 설명]" ),
                ( "diag_email_problem_placeholder", "（請在此處填寫您遇到的問題...）", "（请在此处填写您遇到的问题...）", "(Please describe the issue you encountered here...)", "（ここに発生した問題の詳細をご記入ください...）", "(여기에 발생한 문제를 작성해 주세요...)" )
            };

            RegisterTranslations(data);
        }

        /// <summary>
        /// 組裝本地化的診斷回報郵件主旨與內文範本。
        /// </summary>
        public static (string Subject, string Body) BuildDiagnosticsEmail(string version, string time, string? langCode = null)
        {
            string lang = langCode ?? ClickraStorage.GetSetting(ClickraSettings.Language);
            string subject = T("diag_email_subject", lang);
            string body =
                T("diag_email_thanks", lang) + "\r\n\r\n" +
                T("diag_email_attachment_hint", lang) + "\r\n\r\n" +
                T("diag_email_system_info", lang) + "\r\n" +
                T("diag_email_os", lang) + "\r\n" +
                string.Format(T("diag_email_version", lang), version) + "\r\n" +
                string.Format(T("diag_email_time", lang), time) + "\r\n\r\n" +
                T("diag_email_problem_desc", lang) + "\r\n" +
                T("diag_email_problem_placeholder", lang);

            return (subject, body);
        }
    }
}
