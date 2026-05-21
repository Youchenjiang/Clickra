using System;
using System.Collections.Generic;

namespace Clickra.Core
{
    public static class Localization
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Translations = new(StringComparer.OrdinalIgnoreCase)
        {
            ["zh-TW"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["tab_status"] = "首頁狀態",
                ["tab_history"] = "轉換歷史",
                ["tab_settings"] = "偏好設定",
                ["setting_silent_title"] = "背景靜默轉檔",
                ["setting_silent_desc"] = "在右鍵選單點擊時直接於背景處理，不顯示進度視窗",
                ["setting_notify_title"] = "顯示轉換通知",
                ["setting_notify_desc"] = "作業完成或失敗後，於系統右下角彈出 Windows Toast 通知",
                ["setting_output_title"] = "預設輸出路徑",
                ["setting_output_desc"] = "選擇轉換後 PDF 與圖片預設的儲存位置",
                ["setting_output_same_as_source"] = "與來源相同",
                ["setting_output_desktop"] = "桌面",
                ["setting_output_downloads"] = "下載",
                ["overview_engine_status"] = "轉換引擎狀態",
                ["overview_stats"] = "轉換統計",
                ["overview_stat_total"] = "總轉換次數",
                ["overview_stat_success"] = "成功次數",
                ["overview_stat_failed"] = "失敗次數",
                ["history_clear"] = "清除紀錄",
                ["history_clear_confirm"] = "您確定要清除所有的轉換歷史紀錄嗎？",
                ["history_empty"] = "尚無任何轉換紀錄。",
                ["status_pending"] = "等待中",
                ["status_converting"] = "轉換中",
                ["status_success"] = "成功",
                ["status_failed"] = "失敗",
                ["label_files"] = "個檔案",
                ["setting_lang_title"] = "介面語言",
                ["setting_lang_desc"] = "選擇 Dashboard 的顯示語言",
                ["search_lang_placeholder"] = "搜尋語言 / Search...",
                ["engine_pdf"] = "PDF 處理核心 (PDF Engine)",
                ["engine_ppt"] = "PowerPoint 轉換器 (PowerPoint)",
                ["engine_word"] = "Word 轉換器 (Word)",
                ["engine_ready"] = "已就緒",
                ["engine_office_not_installed"] = "Office 未安裝",
                ["overview_tip"] = "提示：直接在檔案總管選取檔案，右鍵即可呼叫 Clickra 選單進行轉換。",
                ["cmd_word_to_pdf"] = "Word → PDF",
                ["cmd_ppt_to_pdf"] = "PPT → PDF",
                ["cmd_merge_pdf"] = "合併 PDF",
                ["cmd_img_to_pdf"] = "圖片 → PDF",
                ["cmd_merge_img"] = "圖片合併",
                ["cmd_stitch_img"] = "圖片拼接",
                ["tab_convert"] = "快速轉檔",
                ["convert_drag_drop_hint"] = "拖曳檔案至此，或點擊此處選取檔案",
                ["convert_drag_drop_sub"] = "支援 Word, PPT, PDF 及多種圖片格式",
                ["convert_selected_count"] = "已選取 {0} 個檔案",
                ["convert_clear"] = "清除",
                ["convert_start"] = "開始轉檔",
                ["convert_err_min_files"] = "此功能至少需要 {0} 個檔案！",
                ["convert_err_invalid_ext"] = "檔案格式不符，請重新選取！"
            },
            ["zh-CN"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["tab_status"] = "首页状态",
                ["tab_history"] = "转换历史",
                ["tab_settings"] = "偏好设置",
                ["setting_silent_title"] = "背景静默转档",
                ["setting_silent_desc"] = "在右键菜单点击时直接于背景处理，不显示进度窗口",
                ["setting_notify_title"] = "显示转换通知",
                ["setting_notify_desc"] = "作业完成或失败后，于系统右下角弹出 Windows Toast 通知",
                ["setting_output_title"] = "默认输出路径",
                ["setting_output_desc"] = "选择转换后 PDF 与图片默认的存储位置",
                ["setting_output_same_as_source"] = "与来源相同",
                ["setting_output_desktop"] = "桌面",
                ["setting_output_downloads"] = "下载",
                ["overview_engine_status"] = "转换引擎状态",
                ["overview_stats"] = "转换统计",
                ["overview_stat_total"] = "总转换次数",
                ["overview_stat_success"] = "成功次数",
                ["overview_stat_failed"] = "失败次数",
                ["history_clear"] = "清除记录",
                ["history_clear_confirm"] = "您确定要清除所有的转换历史记录吗？",
                ["history_empty"] = "尚无任何转换记录。",
                ["status_pending"] = "等待中",
                ["status_converting"] = "转换中",
                ["status_success"] = "成功",
                ["status_failed"] = "失败",
                ["label_files"] = "个文件",
                ["setting_lang_title"] = "界面语言",
                ["setting_lang_desc"] = "选择 Dashboard 的显示语言",
                ["search_lang_placeholder"] = "搜索语言...",
                ["engine_pdf"] = "PDF 处理核心 (PDF Engine)",
                ["engine_ppt"] = "PowerPoint 转换器 (PowerPoint)",
                ["engine_word"] = "Word 转换器 (Word)",
                ["engine_ready"] = "已就绪",
                ["engine_office_not_installed"] = "Office 未安装",
                ["overview_tip"] = "提示：直接在文件资源管理器中选择文件，右键即可呼叫 Clickra 菜单进行转换。",
                ["cmd_word_to_pdf"] = "Word → PDF",
                ["cmd_ppt_to_pdf"] = "PPT → PDF",
                ["cmd_merge_pdf"] = "合并 PDF",
                ["cmd_img_to_pdf"] = "图片 → PDF",
                ["cmd_merge_img"] = "图片合并",
                ["cmd_stitch_img"] = "图片拼接",
                ["tab_convert"] = "快速转档",
                ["convert_drag_drop_hint"] = "拖拽文件至此，或点击此处选择文件",
                ["convert_drag_drop_sub"] = "支持 Word, PPT, PDF 及多种图片格式",
                ["convert_selected_count"] = "已选择 {0} 个文件",
                ["convert_clear"] = "清除",
                ["convert_start"] = "开始转档",
                ["convert_err_min_files"] = "此功能至少需要 {0} 个文件！",
                ["convert_err_invalid_ext"] = "文件格式不符，请重新选择！"
            },
            ["en-US"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["tab_status"] = "Status",
                ["tab_history"] = "History",
                ["tab_settings"] = "Settings",
                ["setting_silent_title"] = "Silent Mode",
                ["setting_silent_desc"] = "Process in background without showing progress window",
                ["setting_notify_title"] = "Show Notifications",
                ["setting_notify_desc"] = "Show Toast notification when conversion completes/fails",
                ["setting_output_title"] = "Default Output Path",
                ["setting_output_desc"] = "Select the default save location for converted files",
                ["setting_output_same_as_source"] = "Same as source",
                ["setting_output_desktop"] = "Desktop",
                ["setting_output_downloads"] = "Downloads",
                ["overview_engine_status"] = "Conversion Engine Status",
                ["overview_stats"] = "Conversion Stats",
                ["overview_stat_total"] = "Total Conversions",
                ["overview_stat_success"] = "Successful",
                ["overview_stat_failed"] = "Failed",
                ["history_clear"] = "Clear History",
                ["history_clear_confirm"] = "Are you sure you want to clear all history?",
                ["history_empty"] = "No conversion history.",
                ["status_pending"] = "Pending",
                ["status_converting"] = "Converting",
                ["status_success"] = "Success",
                ["status_failed"] = "Failed",
                ["label_files"] = "files",
                ["setting_lang_title"] = "Interface Language",
                ["setting_lang_desc"] = "Select the display language for the Dashboard",
                ["search_lang_placeholder"] = "Search language...",
                ["engine_pdf"] = "PDF Processing Engine",
                ["engine_ppt"] = "PowerPoint Converter",
                ["engine_word"] = "Word Converter",
                ["engine_ready"] = "Ready",
                ["engine_office_not_installed"] = "Office Not Installed",
                ["overview_tip"] = "Tip: Select files in File Explorer, right-click, and select Clickra to convert.",
                ["cmd_word_to_pdf"] = "Word → PDF",
                ["cmd_ppt_to_pdf"] = "PPT → PDF",
                ["cmd_merge_pdf"] = "Merge PDF",
                ["cmd_img_to_pdf"] = "Image → PDF",
                ["cmd_merge_img"] = "Merge Images",
                ["cmd_stitch_img"] = "Stitch Images",
                ["tab_convert"] = "Convert",
                ["convert_drag_drop_hint"] = "Drag files here, or click to browse",
                ["convert_drag_drop_sub"] = "Supports Word, PPT, PDF, and image files",
                ["convert_selected_count"] = "{0} files selected",
                ["convert_clear"] = "Clear",
                ["convert_start"] = "Start Conversion",
                ["convert_err_min_files"] = "This action requires at least {0} files!",
                ["convert_err_invalid_ext"] = "Invalid file extensions detected!"
            },
            ["ja-JP"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["tab_status"] = "ステータス",
                ["tab_history"] = "変換履歴",
                ["tab_settings"] = "環境設定",
                ["setting_silent_title"] = "サイレントモード",
                ["setting_silent_desc"] = "進行状況ウィンドウを表示せず、バックグラウンドで処理します",
                ["setting_notify_title"] = "変換通知を表示",
                ["setting_notify_desc"] = "完了または失敗時に、Windows トースト通知を表示します",
                ["setting_output_title"] = "既定の出力先",
                ["setting_output_desc"] = "変換後のファイルの保存先を選択します",
                ["setting_output_same_as_source"] = "ソースと同じ",
                ["setting_output_desktop"] = "デスクトップ",
                ["setting_output_downloads"] = "ダウンロード",
                ["overview_engine_status"] = "変換エンジンの状態",
                ["overview_stats"] = "統計情報",
                ["overview_stat_total"] = "総変換回数",
                ["overview_stat_success"] = "成功回数",
                ["overview_stat_failed"] = "失敗回数",
                ["history_clear"] = "履歴をクリア",
                ["history_clear_confirm"] = "すべての変換履歴をクリアしてもよろしいですか？",
                ["history_empty"] = "変換履歴はありません。",
                ["status_pending"] = "待機中",
                ["status_converting"] = "変換中",
                ["status_success"] = "成功",
                ["status_failed"] = "失敗",
                ["label_files"] = "個のファイル",
                ["setting_lang_title"] = "表示言語",
                ["setting_lang_desc"] = "Dashboard の表示言語を選択します",
                ["search_lang_placeholder"] = "言語を検索...",
                ["engine_pdf"] = "PDF 処理エンジン",
                ["engine_ppt"] = "PowerPoint 変換器",
                ["engine_word"] = "Word 変換器",
                ["engine_ready"] = "準備完了",
                ["engine_office_not_installed"] = "Office 未インストール",
                ["overview_tip"] = "ヒント：エクスプローラーでファイルを選択し、右クリックして Clickra から変換します。",
                ["cmd_word_to_pdf"] = "Word → PDF",
                ["cmd_ppt_to_pdf"] = "PPT → PDF",
                ["cmd_merge_pdf"] = "PDF 結合",
                ["cmd_img_to_pdf"] = "画像 → PDF",
                ["cmd_merge_img"] = "画像結合",
                ["cmd_stitch_img"] = "画像結合 (縦/横)",
                ["tab_convert"] = "クイック変換",
                ["convert_drag_drop_hint"] = "ここにファイルをドラッグするか、クリックして選択",
                ["convert_drag_drop_sub"] = "Word、PPT、PDF、および画像ファイルをサポート",
                ["convert_selected_count"] = "{0} 個のファイルが選択されました",
                ["convert_clear"] = "クリア",
                ["convert_start"] = "変換開始",
                ["convert_err_min_files"] = "この機能には少なくとも {0} 個のファイルが必要です！",
                ["convert_err_invalid_ext"] = "無効なファイル形式が含まれています！"
            },
            ["ko-KR"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["tab_status"] = "홈 상태",
                ["tab_history"] = "변환 기록",
                ["tab_settings"] = "환경 설정",
                ["setting_silent_title"] = "자동 변환 모드",
                ["setting_silent_desc"] = "진행 창을 표시하지 않고 백그라운드에서 바로 처리합니다",
                ["setting_notify_title"] = "변환 알림 표시",
                ["setting_notify_desc"] = "완료 또는 실패 시 Windows 토스트 알림을 표시합니다",
                ["setting_output_title"] = "기본 출력 경로",
                ["setting_output_desc"] = "변환된 파일의 기본 저장 위치를 선택합니다",
                ["setting_output_same_as_source"] = "원본과 동일",
                ["setting_output_desktop"] = "바탕 화면",
                ["setting_output_downloads"] = "다운로드",
                ["overview_engine_status"] = "변환 엔진 상태",
                ["overview_stats"] = "오늘의 통계",
                ["overview_stat_total"] = "총 변환 횟수",
                ["overview_stat_success"] = "성공 횟수",
                ["overview_stat_failed"] = "실패 횟수",
                ["history_clear"] = "기록 삭제",
                ["history_clear_confirm"] = "모든 변환 기록을 삭제하시겠습니까?",
                ["history_empty"] = "변환 기록이 없습니다.",
                ["status_pending"] = "대기 중",
                ["status_converting"] = "변환 중",
                ["status_success"] = "성공",
                ["status_failed"] = "실패",
                ["label_files"] = "개의 파일",
                ["setting_lang_title"] = "표시 언어",
                ["setting_lang_desc"] = "Dashboard 표시 언어를 선택합니다",
                ["search_lang_placeholder"] = "언어 검색...",
                ["engine_pdf"] = "PDF 처리 엔진",
                ["engine_ppt"] = "PowerPoint 변환기",
                ["engine_word"] = "Word 변환기",
                ["engine_ready"] = "준비 완료",
                ["engine_office_not_installed"] = "Office 미설치",
                ["overview_tip"] = "팁: 파일 탐색기에서 파일을 선택하고 마우스 오른쪽 버튼을 클릭하여 Clickra로 변환하세요.",
                ["cmd_word_to_pdf"] = "Word → PDF",
                ["cmd_ppt_to_pdf"] = "PPT → PDF",
                ["cmd_merge_pdf"] = "PDF 병합",
                ["cmd_img_to_pdf"] = "이미지 → PDF",
                ["cmd_merge_img"] = "이미지 병합",
                ["cmd_stitch_img"] = "이미지 이어붙이기",
                ["tab_convert"] = "빠른 변환",
                ["convert_drag_drop_hint"] = "여기에 파일을 끌어다 놓거나 클릭하여 선택",
                ["convert_drag_drop_sub"] = "Word, PPT, PDF 및 이미지 파일 지원",
                ["convert_selected_count"] = "{0}개의 파일이 선택됨",
                ["convert_clear"] = "지우기",
                ["convert_start"] = "변환 시작",
                ["convert_err_min_files"] = "이 작업은 최소 {0}개의 파일이 필요합니다!",
                ["convert_err_invalid_ext"] = "잘못된 파일 확장자가 감지되었습니다!"
            }
        };

        public static string NormalizeLanguageCode(string langCode)
        {
            if (string.IsNullOrEmpty(langCode))
            {
                langCode = System.Globalization.CultureInfo.CurrentUICulture.Name;
            }

            if (langCode.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en-US";
            if (langCode.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
            if (langCode.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja-JP";
            if (langCode.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return "ko-KR";
            if (langCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-TW";

            return "zh-TW";
        }

        public static string T(string key, string langCode)
        {
            string targetKey = NormalizeLanguageCode(langCode);

            // Try looking up the logical ID in the target language
            if (Translations.TryGetValue(targetKey, out var dict) && dict.TryGetValue(key, out var translated))
            {
                return translated;
            }

            // Fallback: If not found in target language, try Traditional Chinese (zh-TW)
            if (targetKey != "zh-TW" && Translations.TryGetValue("zh-TW", out var twDict) && twDict.TryGetValue(key, out var twTranslated))
            {
                return twTranslated;
            }

            // Fallback: If not found anywhere, return the key as-is
            return key;
        }
    }
}
