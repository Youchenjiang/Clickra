using System;

namespace Clickra.Core
{
    public static partial class Localization
    {
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
    }
}
