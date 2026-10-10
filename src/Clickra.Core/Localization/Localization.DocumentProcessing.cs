using System;

namespace Clickra.Core;

    public static partial class Localization
    {
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
                ("cmd_img_compress", "壓縮圖片", "压缩图片", "Compress Images", "画像圧縮", "이미지 압축"),
                ("md_pdf_progress_parsing", MarkdownParsingZh, MarkdownParsingZh, "Parsing Markdown: {0}", "Markdown を解析中: {0}", "Markdown 분석 중: {0}"),
                ("md_pdf_progress_rendering", "正在排版 PDF：{0}", "正在排版 PDF：{0}", "Rendering PDF layout: {0}", "PDF レイアウトを生成中: {0}", "PDF 레이아웃 렌더링 중: {0}"),
                ("md_pdf_progress_saving", "正在儲存 PDF：{0}", "正在保存 PDF：{0}", "Saving PDF: {0}", "PDF を保存中: {0}", "PDF 저장 중: {0}"),
                ("md_pdf_progress_done", "Markdown 轉 PDF 完成：{0}", "Markdown 转 PDF 完成：{0}", "Markdown to PDF complete: {0}", "Markdown から PDF への変換が完了: {0}", "Markdown PDF 변환 완료: {0}"),
                ("md_word_progress_parsing", MarkdownParsingZh, MarkdownParsingZh, "Parsing Markdown: {0}", "Markdown を解析中: {0}", "Markdown 분석 중: {0}"),
                ("md_word_progress_rendering", "正在建立 Word 文件：{0}", "正在创建 Word 文档：{0}", "Building Word document: {0}", "Word 文書を生成中: {0}", "Word 문서 생성 중: {0}"),
                ("md_word_progress_saving", "正在儲存 Word：{0}", "正在保存 Word：{0}", "Saving Word document: {0}", "Word 文書を保存中: {0}", "Word 문서 저장 중: {0}"),
                ("md_word_progress_done", "Markdown 轉 Word 完成：{0}", "Markdown 转 Word 完成：{0}", "Markdown to Word complete: {0}", "Markdown から Word への変換が完了: {0}", "Markdown Word 변환 완료: {0}"),
                ("img_compress_progress_compressing", "正在壓縮圖片: {0} ({1}/{2})...", "正在压缩图片: {0} ({1}/{2})...", "Compressing image: {0} ({1}/{2})...", "画像を圧縮中: {0} ({1}/{2})...", "이미지 압축 중: {0} ({1}/{2})..."),
                ("img_compress_progress_saving", "壓縮完成，正在儲存圖片: {0}", "压缩完成，正在保存图片: {0}", "Compression complete, saving image: {0}", "圧縮が完了、画像を保存中: {0}", "압축 완료, 이미지 저장 중: {0}"),
                ("img_compress_progress_preserving", "無需重新編碼，保留原始圖片資料: {0}", "无需重新编码，保留原始图片数据: {0}", "No re-encode needed; preserving original image bytes: {0}", "再エンコード不要のため、元の画像データを保持: {0}", "재인코딩이 필요 없어 원본 이미지 데이터를 유지합니다: {0}"),
                ("img_compress_progress_not_smaller", "壓縮後不會更小，改保留原始圖片資料: {0}", "压缩后不会更小，改为保留原始图片数据: {0}", "Compressed result was not smaller; preserving original image bytes: {0}", "圧縮後に小さくならないため、元の画像データを保持: {0}", "압축 결과가 더 작지 않아 원본 이미지 데이터를 유지합니다: {0}"),
                ("error_img_compress_unsupported", "不支援的圖片格式，無法壓縮: {0}", "不支持的图片格式，无法压缩: {0}", "Unsupported image format, cannot compress: {0}", "未対応の画像形式のため圧縮できません: {0}", "지원하지 않는 이미지 형식이라 압축할 수 없습니다: {0}"),
                ("error_img_compress_multiframe_resize", "多影格／多頁圖片目前無法安全調整尺寸: {0}", "多帧／多页图片目前无法安全调整尺寸: {0}", "Multi-frame or multi-page images cannot be safely resized yet: {0}", "複数フレーム／複数ページ画像は現在安全にリサイズできません: {0}", "다중 프레임/다중 페이지 이미지는 현재 안전하게 크기를 조정할 수 없습니다: {0}"),
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
                ("error_image_output_overwrites_input", "輸出路徑會覆寫另一個已選取的輸入檔案，已中止以避免資料遺失：{0}", "输出路径会覆盖另一个已选择的输入文件，已中止以避免数据丢失：{0}", "An output path would overwrite another selected input; conversion stopped to prevent data loss: {0}", "出力先が選択済みの別の入力ファイルを上書きするため、データ損失を防ぐため処理を中止しました: {0}", "출력 경로가 선택된 다른 입력 파일을 덮어쓰므로 데이터 손실을 방지하기 위해 변환을 중단했습니다: {0}"),
                ("codec_missing_store_prompt", "{0}\n\n是否立即開啟 Microsoft Store 免費安裝「{1}」？", "{0}\n\n是否立即打开 Microsoft Store 免费安装「{1}」？", "{0}\n\nOpen the Microsoft Store now to install the free \"{1}\"?", "{0}\n\n今すぐ Microsoft Store を開いて無料の「{1}」をインストールしますか？", "{0}\n\n지금 Microsoft Store를 열어 무료 \"{1}\"을(를) 설치하시겠습니까?"),
                ("codec_heif_extension_name", "HEIF 影像延伸", "HEIF 图像扩展", "HEIF Image Extensions", "HEIF 画像拡張機能", "HEIF 이미지 확장"),
                ("codec_missing_install_action", "安裝編碼器", "安装编码器", "Install codec", "コーデックをインストール", "코덱 설치")
            };

            RegisterTranslations(data);
        }
    }
