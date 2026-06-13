# PDF 翻譯與佈局保留引擎 - 完整技術設計與硬性規則規範 (Full Translation & Bypass Rules Specification)

本文件匯整並定義了 Clickra PDF 翻譯模組的所有核心規則與硬性規範。包含「版面分析」、「避讓邏輯」、「翻譯與術語修正」、「渲染排版」及「超連結校正」五大範疇，為後續維護與開發提供唯一標準。

---

## 📂 規則一覽與架構圖

```
                                 PDF 頁面輸入
                                      │
                                      ▼
                        ┌────────────────────────┐
                        │  1. 版面分析與合併規則  │ ────► 橫向/縱向合併與雙欄切分
                        └────────────────────────┘
                                      │
                                      ▼
                        ┌────────────────────────┐
                        │      2. 避讓規則       │ ────► 判別 IsBypassed
                        └────────────────────────┘
                          /                    \
                (IsBypassed = true)        (IsBypassed = false)
                        /                        \
                       ▼                          ▼
          ┌────────────────────────┐    ┌────────────────────────┐
          │     保留原始英文       │    │ 3. 翻譯與術語修正規則  │ ──► Batch/Sequential + 術語與 Email
          └────────────────────────┘    └────────────────────────┘
                       \                          /
                        \                        /
                         ▼                      ▼
                        ┌────────────────────────┐
                        │  4. 渲染與繪製排版規則  │ ──► CJK/數學字型、旋轉、字體縮放與遮罩
                        └────────────────────────┘
                                      │
                                      ▼
                        ┌────────────────────────┐
                        │  5. 超連結位置動態校正  │ ──► Link Annotation 重設包圍盒
                        └────────────────────────┘
                                      │
                                      ▼
                                翻譯後 PDF 輸出
```

---

## 1. 📐 版面分析與合併規則 (Layout Analysis & Merging Rules)

在提取 PDF 文字時，必須精準還原邏輯段落，避免跨欄或跨表格合併。

### A. 雙欄排版重疊修正 (Multi-Column Splitting)
*   **規則**：在雙欄（Double-Column）PDF 中，必須防止左右兩欄文字橫向合併。
*   **實作**：在 `GetMergedBlocks` 中以頁面中央線（Center = Width / 2）為界。
*   對於跨越中央線的文字行（TextLine），檢查相鄰單字之間是否存在寬度 $\ge 8.0\text{ pt}$ 的空白間距（Gutter Gap）。
*   若存在，則將該行精準切割為左、右兩半，並拆分至各自獨立的 Block 中。

### B. 表格欄位對齊 (Table Cell Grouping)
*   **規則**：表格內的數據與單元格文字必須保持欄位垂直對齊。
*   **實作**：
    *   在一般本文中，橫向合併的字距門檻為 $15.0\text{ pt}$。
    *   在表格頁面（`isTablePage = true`）或表格區塊中，此字距門檻必須縮小為 **$8.0\text{ pt}$**。若單字間距大於 $8.0\text{ pt}$，則嚴禁橫向合併。
    *   對於兩個橫跨中央線但分屬左右兩欄的 Block，其橫向合併的間距門檻硬性限制為 **$5.0\text{ pt}$**。

### C. 縱向段落合併與分界 (Vertical Paragraph Merging)
*   **規則**：在同一個欄位內，屬於同一個段落的相鄰行應合併為單一 `PdfParagraph` 以利翻譯，但必須在段落分界處精準斷開。
*   **條件**：縱向相鄰的兩行 `p1` 與 `p2`，只有在滿足以下所有條件時才能合併：
    1.  `p1` 與 `p2` 皆未被避讓（`IsBypassed = false`）。
    2.  `p1` 不是章節標題。
    3.  `p1` 結尾**不是**句號或結束標點（`.`, `?`, `!`, `:`, `。`, `」`, `"`）。
    4.  `p1` 與 `p2` 的水平重疊比例 $\ge 60\%$（`overlap / minWidth >= 0.6`）。
    5.  兩行之間的縱向間距（Gap）在 **$-10\text{ pt}$ 到 $6\text{ pt}$** 之間（硬性限制，防止跨段落合併）。
    6.  `p2` 開頭沒有觸發「新段落/章節起始標記」。

### D. 新段落/章節起始標記 (Paragraph Start Detection)
*   以下特徵代表新段落或獨立區塊的起點，必須與前文斷開：
    *   段落開頭符合清單或編號格式，例如 `[1]`, `1.`, `1)`, `a.`, `a)`, `•`, `-`, `*`。
    *   段落開頭符合章節編號，例如 `3.4.1`, `10. `。
    *   長度 < 30 字元，且全為大寫字母（如 `REFERENCES`, `ABSTRACT`）。
    *   段落開頭為特殊圖表標題，如 `Table`, `Figure`, `Fig`, `表`, `圖`, `RQ1` 等。
    *   文字為 "Keywords"、"Keyword"、"關鍵字"、"关键字"。

---

## 2. 🛑 避讓規則：絕對不可翻譯與修改的內容 (Hard Bypass Rules)

避讓內容在 Pass 1 標記階段會將 `IsBypassed` 設為 `true`。這些區域**不得**對其底層 PDF 串流進行文字剝除（Strip），亦**不得**在其上覆蓋白色遮罩或翻譯。

### A. 向量圖表與點陣圖 (Diagrams & Images) - ❗️ 最優先保護
*   **點陣圖片**：任何寬度 > 80 且高度 > 80 的點陣圖（`Image XObjects`）。
*   **向量圖表**：由線條、路徑組成的向量繪圖（`page.ExperimentalAccess.Paths`）。
    *   **幾何判定**：任何單個路徑的包圍盒（Bounding Box）滿足 **(寬度 > 80 且 高度 > 30)** 或 **(寬度 > 30 且 高度 > 60)** 時，該包圍盒區域即視為圖表區。
    *   **避讓對象**：
        1. 任何與圖表區幾何交集的段落。
        2. **鄰近度傳播避讓**：長度 $\le 20$ 字元的零散文字標籤（如 "APK"、"Sinks"、數據標記），若與已被避讓的段落距離在 $30\text{ pt}$ 以內，自動設為 `IsBypassed = true`。
    *   **無損核心限制**：嚴禁解壓縮、修改或剝除 `/Form` XObjects 與 `/Image` XObjects 內部 content stream 的任何內容。

### B. 獨立與行內數學公式 (Math Formulas & Equations)
*   **特徵**：
    *   段落結尾帶有公式編號（如 `(1)`, `(2)`）。
    *   文字結構符合數學定義樣式（如 `x : A -> B` 並包含 `→`, `↦`, `⇒`, `⊆`, `∈` 等符號）。
    *   段落剔除 `{v0}` 等公式占位符後，剩餘的英文字母極少，且運算子與標點符號的比例 $> 40\%$。
    *   個別字元字型名稱包含 `Math`、`Symbol`、`MSAM`、`MSBM`、`CMSY`，或單字平均字型大小小於本文平均字型大小的 $79\%$。
*   **處理**：將公式段落以預留占位符（如 `{v0}`, `{v1}`）進行替代並標記為避讓，翻譯時略過，渲染時依原始字型與相對位置重繪。

### C. 程式碼區塊 (Code Blocks)
*   **特徵**：使用等寬字型（包含 `Courier`, `Console`, `Inconsolata`, `Typewriter`, `NimbusMon`, `MonL`, `cmtt`, `ectt`, `sftt`, `Teletype`, `Mono`, `Code` 或正則匹配 `tt\d+`）。
*   **處理**：直接標記避讓，保留原始程式碼結構。

### D. 第一頁作者與機構資訊 (First Page Author Block)
*   **特徵**：位於第一頁，且在 `ABSTRACT`（或 `摘要`）字樣上方，且平均字型大小 $< 15.0\text{ pt}$ 的所有段落。
*   **處理**：標記為 `IsBypassed = true`，避免將人名或學校機構名稱翻譯成中文造成排版錯亂。

### E. 表格與數據 (Tables & Data)
*   **特徵**：經 `MarkTableParagraphs` 幾何分群判定為表格單元格。
*   **處理**：所有被識別為表格單元格的段落，在 Pass 1 階段皆會直接標記為 `IsBypassed = true`。這意味著**所有表格文字皆保留原始英文/原文，不進行任何翻譯**。此外，在 Pass 1 遮罩繪製階段會防止在其上繪製白色背景，在 Pass 2 時以原始語言重繪，以避免抹除表格的框線與網格線。


---

## 3. 📝 翻譯與術語修正規則 (Translation & Terminology Rules)

在傳送與接收翻譯時，必須保證高併發下的防 Ban 限流，並對專業術語進行後處理替換。

### A. 限流與雙軌翻譯機制 (Rate Limiting & Fallback)
*   **併發控制**：使用 `SemaphoreSlim(1, 1)` 強制單一執行緒進行翻譯請求，並在鎖定內加入 $150\text{ms}$ 至 $400\text{ms}$ 的隨機延遲。
*   **批次翻譯 (Batch Translate)**：優先調用 `TranslateBatchAsync` 進行整頁批次翻譯，減少 HTTP 請求往返。
*   **單條降級 (Sequential Fallback)**：若批次翻譯因網絡或 API 限流失敗，記錄錯誤日誌並自動降級為 `TranslateAsync` 逐句翻譯，並採用指數型退避延遲（初始 $1500\text{ms}$，每次失敗乘以 2，最多重試 5 次）。

### B. 術語後處理修正 (Terminology replacements) - `PostProcessTranslation`
翻譯完成後，必須對譯文進行以下字串比對替換（僅適用於 `zh-TW` / `zh-CN`）：
1.  **電子郵件保護**：使用正則表達式提取原始英文中的 Email 地址，翻譯後若 Email 被分割或翻譯，強制將其還原為原始 ASCII Email 字串。
2.  **術語翻譯糾正**：
    *   原始英文包含 `LLM` $\rightarrow$ 強制將「法學碩士」替換為**大型語言模型**（繁體）/ **大型语言模型**（簡體）。
    *   原始英文包含 `sink` $\rightarrow$ 強制將「水槽」替換為**接收端**（繁體）/ **接收器**（簡體）。
    *   原始英文包含 `character` $\rightarrow$ 將「字元/字符」替換為**角色**（在非代碼本文語境中）。
    *   原始英文包含 `title`（且不含 `entitle`） $\rightarrow$ 將「標題/标题」替換為**作品**。
    *   原始英文包含 `features` 或 `feature` $\rightarrow$ 將「功能/特性」替換為**特徵**（繁體）/ **特征**（簡體）。
    *   獨立的 "ABSTRACT" $\rightarrow$ 翻譯為 "摘要"。
3.  **公式殘留符號清除**：
    *   清除公式提取器誤讀導致的 dangling 符號（如結尾或開頭殘留的 `):(Equation (1))` 或 `):`）。

---

## 4. 🎨 渲染與繪製排版規則 (Rendering & Typography Rules)

翻譯後的中文文字必須使用 PDFsharp 的 `XGraphics` 重新繪製，並嚴格遵循字型與佈局對齊規範。

### A. 系統字型 TTC 硬性映射 (Font Resolution)
*   **限制**：PDFsharp 不支援 TrueType Collection (`.ttc`) 字型。
*   **硬性映射表**：
    *   微軟正黑體 (`msjh`, `jhenghei`) $\rightarrow$ 載入實體檔 `kaiu.ttf` (標楷體)。
    *   微軟雅黑體 (`msyh`, `yahei`) $\rightarrow$ 載入實體檔 `simsunb.ttf` (宋體粗體)。
    *   日文字型 (`msgothic`) $\rightarrow$ 載入實體檔 `kaiu.ttf` (以標楷體替代日文漢字)。
    *   韓文字型 (`malgun`) $\rightarrow$ 載入實體檔 `malgun.ttf` (若無則使用標楷體)。
    *   Cambria / Math 字型 $\rightarrow$ `times.ttf` (Times New Roman，避開 Cambria 預設 TTC 崩潰)。
    *   等寬字型 $\rightarrow$ `cour.ttf` (Courier New)。
*   **字型缺字 Fallback**：若渲染特定 Unicode 字元或數學運算子遇到缺字，`ClickraFontResolver` 必須能回退至 `Segoe UI Symbol` (`seguisym.ttf`)。

### B. 動態字體縮放與行高 (Font Scaling & Spacing)
*   **行高乘數 (Line Spacing)**：
    *   預設中文字型為 **$1.35$** 倍字型大小。
    *   Arial 字型為 **$1.2$** 倍。
    *   參考文獻段落（References）為 **$1.15$** 倍。
*   **字體與佈局收縮**：
    *   若翻譯後中文字數增加，導致總渲染高度大於原始段落包圍盒高度：
        1. 優先壓縮行高倍數（計算 `limitHeight / (rows * fontSize)`），最低可壓縮至 **$1.0$** 倍。
        2. 若壓縮行高後高度依然超出，則對字型大小（FontSize）進行縮放，縮放比例最低限制為 **$0.8$**。

### C. 遮罩與擦除規範 (Masking Rules)
*   對於一般需要翻譯的段落：在 Pass 1 渲染前，必須先在原始段落坐標周圍外擴 $1.5\text{ pt}$ 繪製白色實心矩形（`gfx.DrawRectangle(XBrushes.White, ...)`）以擦除英文本文。
*   **表格防遮罩例外**：若段落被標記為表格單元格（`IsTable = true`），則**絕對不可**繪製白色遮罩，以防止抹除表格的邊框線或網格線。
*   **死碼清理**：嚴禁呼叫 `StripFormXObjects` 進行向量圖表文字抹除，以避免圖表結構損壞。

### D. 旋轉文字渲染 (Rotated Text Rendering)
*   支持 PDF 中的旋轉段落，透過 GDI+ 的矩陣變換進行坐標轉換：
    *   `Rotate270`：以段落左下角為基準，旋轉 $-90$ 度，佈局寬度改為原高度。
    *   `Rotate90`：以段落右上角為基準，旋轉 $90$ 度，佈局寬度改為原高度。
    *   `Rotate180`：以段落右上角為基準，旋轉 $180$ 度，佈局寬度為原寬度。

---

## 5. 🔗 超連結位置動態校正 (Link Annotation Realignment)

翻譯後本文寬度與長度會發生變化，原始 PDF 上的點擊超連結（Link Annotations）必須動態校正位置。

### A. 關聯對齊機制
1.  **Pass 0 (分析階段)**：遍歷頁面中的 `page.Annotations`。
2.  若超連結包圍盒與某個段落 `para` 內部的字母（`AllLetters`）有幾何重疊（誤差 $\pm 2.5\text{ pt}$），則將該超連結與該段落關聯。
3.  利用字母序列比對，找出超連結文字在段落中的出現索引（`OccurrenceIndex`）。
4.  **Pass 2 (繪製階段)**：在 `RenderParagraph` 繪製中文或還原英文時，程式會精準記錄每一個字元被繪製在畫布上的實際座標包圍盒（儲存於 `RenderedChar` 列表中）。
5.  **Pass 3 (校正階段)**：完成繪製後，調用 `FindAnnotationCharacters` 在重繪後的字元列表中重新定位超連結文字，並將該 `PdfAnnotation.Rectangle` 的包圍盒重設為重繪後字元的外包矩形（X 軸左右外擴 $1.0\text{ pt}$，Y 軸上下外擴 $1.5\text{ pt}$ 的 Padding），確保超連結的可點擊區域與翻譯後的新位置完全吻合。
