# PDF 壓縮與最佳化功能規劃

## 目標

讓使用者可以在右鍵選單、CLI 與 Dashboard 中一鍵最佳化 PDF。第一版採 Clickra 內建引擎，避免要求使用者安裝 Ghostscript、iText、pdftk 或其他授權/安裝體驗不確定的外部工具。

## 使用者體驗

- 右鍵選單新增「壓縮 PDF」。
- Dashboard 轉檔頁的 PDF 工具群組新增「壓縮 PDF」。
- CLI 新增 `compress-pdf` 指令，支援單檔與多檔。
- 預設使用「平衡」壓縮等級，讓第一次使用不需要先理解 DPI、影像品質與字型嵌入。
- 輸出檔名預設為 `_compressed.pdf`，避免覆蓋原檔。
- 若 PDF 已接近最佳化，仍輸出可用檔案；後續再於歷史紀錄補上壓縮前後大小與是否有效縮小。

## 壓縮等級

| 等級 | 用途 | 第一版行為 |
| --- | --- | --- |
| 小檔 | 郵件、上傳平台、預覽 | 使用最積極的內建清理策略，移除可選文件資訊並重新封裝 PDF。 |
| 平衡 | 一般分享與保存 | 預設選項，重新封裝 PDF 並保留基本文件資訊。 |
| 高品質 | 列印、正式文件 | 保守重新封裝，避免破壞文件中可保留的描述資訊。 |

第一版不是 Ghostscript 等級的完整重生成引擎，因此不承諾所有 PDF 都能大幅縮小。功能定位是「免安裝、授權乾淨、可預期的 PDF 最佳化」。

## 引擎策略

第一版採內建 native optimizer：

1. 使用既有 Apache-2.0 相容依賴 `PDFsharp` 重新讀取與儲存 PDF。
2. 不新增商業、AGPL、GPL 或需要外部安裝的預設依賴。
3. 不自動下載或安裝 Ghostscript，避免重複 LibreOffice 安裝體驗問題。
4. 將壓縮能力包在 `IPdfCompressionEngine`，後續可在不改 UI/CLI 的情況下新增可選引擎。

後續可評估：

- Ghostscript 作為進階可選引擎，僅在使用者已自行安裝或明確同意時使用。
- 若取得商業授權，再評估 Docotic.Pdf、Syncfusion、IronPDF 或 iText commercial engine。
- 壓縮前後大小比較、壓縮率顯示與未縮小提示。
- 圖片重新取樣、JPEG quality、灰階化等進階參數。

## 技術設計

- 新增 `PdfCompressionProcessor`，沿用現有檔案處理 processor 模式。
- 新增 `IPdfCompressionEngine`，讓 CLI、Dashboard 與 shell extension 不直接依賴某個底層實作。
- 新增 `NativePdfCompressionEngine`，負責：
    - 讀取輸入 PDF。
    - 依壓縮等級清理可選文件資訊。
    - 重新匯入頁面並儲存為新的 PDF。
    - 使用暫存檔完成後再替換輸出，避免失敗時留下半成品。
- 新增 `PdfCompressionOptions`，集中處理 `small`、`balanced`、`high` 等使用者輸入別名。

## 測試範圍

- 壓縮等級與別名解析。
- 不支援的等級會被拒絕。
- 無外部工具時仍可產出 PDF。
- Processor 會把解析後的等級交給 compression engine。
- 輸出檔名 suffix 與 CLI/Dashboard/shell extension 接線。

## 暫不做

- Ghostscript 自動下載/安裝。
- 商業 PDF SDK 整合。
- PDF/A、PDF/X 或色彩管理進階選項。
- 自訂 DPI、JPEG quality、灰階化等細部參數。
- 壓縮前後的視覺 diff。
