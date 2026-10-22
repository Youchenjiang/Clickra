# PDF 壓縮技術深度評估與未來優化規劃 (PDF Compression Technical Review & Future Roadmap)

本文件針對 Clickra 現有的 **Native PDFsharp + GDI+** 壓縮實作進行技術剖析，並對比 **PDF 規格書 (PDF Reference)** 及業界一流工具（如 `pdfsizeopt`、`qpdf`、`Ghostscript`）的設計，提出 Clickra 在 PDF 壓縮領域的後續深度優化方向。

---

## 1. Clickra 目前實作現狀評估

Clickra 目前已實作了相當實用的「輕量級」PDF 壓縮核心，主要特點如下：
1. **圖片降樣式 (Downsampling)**：透過 `PdfPig` 獲取圖片的版面大小（Point），計算出實際 DPI，並利用 GDI+ 進行高品質雙立方（Bicubic）縮放，並重寫為有損 JPEG。
2. **圖片智慧跳過**：設定了解析度閾值（少於 30 萬像素）與大小閾值（小於 100 KB）保護機制，避免流程圖、簽名、圖表被壓縮至模糊。
3. **字型重複刪除**：透過 SHA-256 計算，合併內容完全一致的嵌入字型檔。
4. **文字流精簡 (Minification)**：去除文字流中的註解（`%` 開頭）及冗餘的空白字元。

> [!NOTE]
> 這些技術在處理「包含大量大圖的掃描檔或簡報」時效果顯著，且 AOT 啟動速度在 10 毫秒內，非常適合右鍵選單的一鍵即完成。然而，相較於 Adobe Acrobat 的「最佳化 PDF」或 Ghostscript，我們在 **PDF 規格支援度** 與 **深度無損精簡** 上仍有很大的升級空間。

---

## 2. 深度優化方向規劃 (Technical Roadmap)

以下依據技術難度、壓縮收益以及對 PDF 相容性的影響，將未來的優化方向分為三個階段：

```text
  [現有 Clickra C#/GDI+ 壓縮]
               │
               ▼
[第一階段: 結構與垃圾回收 (低難度/高收益)]
               │
               ▼
[第二階段: 進階圖片編碼 (中難度/中收益)]
               │
               ▼
[第三階段: 字型子集重建 (高難度/高風險)]
```

---

### 2.1 第一階段：結構精簡與流壓縮 (Object & Stream Level Optimization)

這是技術難度最低，但對**電子書與公文類 PDF** 體積縮減最為顯著的優化手段。

#### 1. 物件壓縮流 (Object Streams) 與 交叉引用流 (Cross-Reference Streams) [PDF 1.5+]
* **原理**：在傳統 PDF 1.4 中，每個 PDF 物件（如 Dictionary、Array）都是純文字，且在 `xref` 交叉引用表中佔用一個固定 20-byte 的行。而在 PDF 1.5 中，引入了物件流 (`ObjStm`)，允許將多個非 Stream 物件（例如字型字典、頁面配置物件）封裝至一個單一的 Stream 中，再以 `/FlateDecode`（ZIP 壓縮）進行整體壓縮。
* **做法**：重寫輸出儲存模組，將零散的物件合併至 `Object Streams` 中，並改用二進位 `Cross-Reference Streams` 替代純文字 `xref` 表。
* **預期收益**：**結構性開銷減少 10% ~ 20%**，特別是物件極多（如 CAD 轉出的 PDF）的檔案。

#### 2. 圖可達性垃圾回收 (Unreachable Objects Garbage Collection)
* **原理**：許多 PDF 經過軟體多次編輯、編輯器不當存檔後，內部存在大量沒有被任何頁面或 Catalog 引用到的「孤立對象」（如已被刪除的舊圖、舊字型、舊頁面殘留）。
* **做法**：實現一個從 Catalog（根節點）開始的 **深度優先搜尋 (DFS)** 可達性演算法。遍歷整棵物件樹，將所有未標記為可達的物件徹底從文件結構中移除。
* **預期收益**：對於經過頻繁修改的 PDF，**可減少 5% ~ 50% 不等的冗餘體積**。

---

### 2.2 第二階段：進階影像編碼與無損重壓 (Advanced Image Encoding)

影像通常佔據 PDF 70% 以上的體積。除了現有的 JPEG 重壓縮外，我們需要針對不同類型的影像套用最合適的編碼。

| 影像類型 | 最佳編碼 | Clickra 目前做法 | 未來優化做法 |
| :--- | :--- | :--- | :--- |
| **黑白掃描文字** | `/JBIG2Decode` | 轉換為 RGB JPEG (極大且模糊) | 套用 JBIG2 編碼 (體積縮減為 1/10) |
| **PNG/螢幕截圖** | `/FlateDecode` + Zopfli / 7-Zip | 轉換為 JPEG (邊緣產生雜訊) | 採用強力預測器無損重壓縮 |
| **高品質相片** | `/JPXDecode` (JPEG 2000) | 轉換為 `/DCTDecode` (JPEG) | 轉換為 JPEG 2000，消除網格雜訊 |

#### 1. JBIG2 雙值影像壓縮 (JBIG2 Bi-level Compression)
* **原理**：專門為 1-bit 黑白掃描文件設計的壓縮法。JBIG2 會分析頁面上出現的字形，將相同的字形（例如所有的英文字母 'e'）對齊並共用同一個點陣圖，只需儲存字形庫與座標，因此壓縮率極為驚人。
* **做法**：引進開源的 JBIG2 編碼核心（例如 `jbig2enc` 封裝，或基於 C# 的二進位壓縮實作），將 1-bit 的點陣圖轉換為 JBIG2 流。
* **預期收益**：**黑白掃描文檔體積縮減 80% 以上**，且文字邊緣依舊銳利，無 JPEG 雜訊。

#### 2. Flate 影像的 Zopfli / 7-zip 級別無損重壓縮
* **原理**：PDF 內的 PNG 圖片採用 `/FlateDecode`。預設的 Zip 壓縮演算法為了速度，壓縮率並非極限。
* **做法**：使用 **Zopfli 壓縮演算法**（Google 開源的超高壓縮率 Deflate 實作）或 7-Zip 的 Deflate 演算法，在背景對這些流進行重複壓縮。雖然計算耗時較長，但因 100% 無損，非常適合高品質要求的壓縮。
* **預期收益**：**無損影像體積縮減 10% ~ 30%**。

---

### 2.3 第三階段：字型子集深度去重與重構 (Font Subset Reconstruction)

字型是 PDF 檔案中最難處理、但對非圖片型 PDF 影響最大的部分。

#### 1. 字型子集合併 (Font Subset Merging)
* **面臨問題**：Clickra 目前是比對字型檔的 SHA-256 來去重。但很多 PDF 在合併（Merge）後，每頁都嵌入了相同字型的不同「子集」（例如第一頁含有 Arial 字符集 A，第二頁含有 Arial 字符集 B）。因為子集不同，SHA-256 不同，目前的去重無法生效，導致檔案內塞了數十個同名但字元不全的 Arial 檔案。
* **做法**：解構 TrueType/OpenType 二進位字型結構，將同名且設定相同的字型子集進行「字元聯集（Union）合併」，重構出單一字型子集並更新頁面中的 `/Widths` 與 `/ToUnicode` 字典。
* **預期收益**：**大幅解決合併 PDF 後，檔案異常膨脹的問題**（可縮減數 MB 空間）。

#### 2. 安全字型剝離與標準字型替換 (Standard 14 Font Replacement)
* **面臨問題**：Clickra 現有的 `UnembedLargeFonts` 只是直接將大於 100 KB 的字型檔刪除。這會使沒有安裝該字型的系統（特別是手機端、Mac 或 Linux）打開時變為亂碼或排版破裂。
* **做法**：
  1. 檢查該字型是否能被 PDF **標準 14 字型 (Standard 14 Fonts)** 替代（如 Times, Helvetica, Courier）。
  2. 若不能，在移除嵌入時，必須在 `FontDescriptor` 字典中完整保留 `/FontName`、`/Flags`、`/FontBBox`、`/ItalicAngle`、`/Ascent`、`/Descent`、`/CapHeight`、`/StemV` 等度量屬性（Metrics）。這樣即使字型被剝離，PDF 閱讀器也能根據這些數據，在本地系統中尋找最接近的字型進行無缝縮放與替代渲染，確保排版不跑位。
* **預期收益**：移除中文字型等龐大資源時，**檔案體積減少數 MB 至數十 MB**，同時保持排版不跑位。

---

## 3. 建議 Clickra 後續最佳實作路徑

考量到開發成本與 Clickra 的「輕量、原生、極速」定位，建議採用以下漸進式實作路徑：

1. **短期 (Milestone 1 - v3.7.0)**:
   * 實作 **「圖可達性垃圾回收 (DFS GC)」**，這在現有的 PDFsharp 結構下極易開發，且對雜亂的 PDF 去重效果立竿見影。
   * 優化 `UnembedLargeFonts`，在刪除字型時強制補齊字型度量字典 (Font Metrics Descriptor)，防止跑版。

2. **中期 (Milestone 2 - v3.8.0)**:
   * 導入 **Object Streams (ObjStm)**，將文字與結構物件打包壓縮，使 Clickra 輸出的 PDF 達到 PDF 1.5 現代化標準。
   * 對 `/FlateDecode` 影像引進更進階的壓縮器，以無損方式進一步減小體積。

3. **長期 (Milestone 3 - v3.9.0/v4.0.0)**:
   * 引入 JBIG2 支援，徹底解決「黑白掃描公文檔」壓縮後模糊且檔案龐大的痛點。
