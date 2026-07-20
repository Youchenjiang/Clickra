# PDF 翻譯與佈局保留引擎 - 完整技術設計與硬性規則規範 (Full Translation & Bypass Rules Specification)

本文件匯整並定義了 Clickra PDF 翻譯模組的所有核心規則與硬性規範。包含「版面分析」、「避讓邏輯」、「翻譯與術語修正」、「渲染排版」及「超連結校正」五大範疇，為後續維護與開發提供唯一標準。

## 0.1 翻譯復原與輸出完整性（v3.6.4）

*   批次翻譯失敗時，必須依序執行批次拆分、逐段請求與另一個 provider fallback；不得直接把原文標記為成功譯文。
*   每個 provider 請求各自最多 30 秒；fallback chain 預設有獨立 75 秒上限（可由 `CLICKRA_TRANSLATION_*` timeout 設定覆寫），不能讓 primary 的重試吃掉 fallback 的時間。整份文件最多 10 分鐘。呼叫端取消必須立即停止，逾時則記錄 provider、頁碼與段落資訊。
*   只有所有可用復原路徑都失敗時才回報翻譯失敗；失敗執行不得留下可被誤認為完成的 PDF。輸出先寫入 `.partial`，完成頁數與 layout health gate 後才改名為正式檔案。
*   每次執行在輸出目錄產生 `*_health.json`，至少包含頁數、成功/避讓段落數、provider、guard clip 數、實際 overflow 數與失敗原因。

### 0.2 審核檔案規則

*   交給使用者審核的檔案，必須是實際翻譯 provider 產出的目標語言 PDF。
*   `identity`、`synthetic-cjk`、英文原文、版面診斷重建檔與其他測試引擎只能用於內部測試，不得作為翻譯成果或審核檔案。
*   若實際 provider 尚未成功完成，必須明確回報「尚無可審核翻譯檔」，不得以測試引擎輸出代替。

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
*   **表格頁無例外**：`isTablePage = true` 時**仍必須**在 `GetMergedBlocks` 執行中央線切分；若跳過，左欄表格會與右欄正文被 Docstrum 合併為全頁寬段落，導致遮罩覆蓋整張表格。
*   對於跨越中央線的文字行（TextLine），檢查相鄰單字之間是否存在寬度 $\ge 8.0\text{ pt}$ 的空白間距（Gutter Gap）。
*   若存在，則將該行精準切割為左、右兩半，並拆分至各自獨立的 Block 中。
*   區塊內逐行分組時，若相鄰兩行分屬左右欄（`crossColumnSplit`），亦強制拆段。

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
    7.  **僅限文獻/清單合併**：`MergeVerticallyAdjacentParagraphs` 中，只有當 `p1` 或 `p2` 至少一方為參考文獻（`IsReferenceParagraph`）或清單/章節起始（`StartsNewParagraphOrSection`）時才允許合併；一般正文段落永不合併。

### C.1 大垂直空隙強制拆段 (Large Vertical Gap Splitting)
*   **規則**：在 Docstrum 區塊內逐行分組時，若前一行底部與當前行頂部之間的垂直間距 **> 15.0 pt**（`isVerticalGapLarge = true`），則強制拆成獨立 `PdfParagraph`。
*   **同規則亦適用於**：同一 Block 的 `TextLines` 序列中，相鄰兩行 `L1`（上一行）與 `L2`（下一行）若垂直淨空隙（`L1.Bottom - L2.Top`）大於 **$15.0\text{ pt}$**，亦必須在該處強制拆分。
*   **目的**：防止 Docstrum 將跨區塊（如標題與正文、段落與圖表說明）的遠距行誤合併為同一段落。

### C.2 視覺換行片段合併 (Wrapped-line Fragment Merge)
*   `MergeWrappedLineFragments` 是上述「一般正文段落永不合併」的窄例外：它只合併同一視覺欄位中、同一左錨點附近、來源字級相近且垂直相鄰的碎片。
*   兩段必須都是可翻譯、非標題、非圖說/表格/程式碼/公式/灰框/參考文獻，垂直間距須落在 **$-1\text{ pt}$ 到 $8\text{ pt}$**，且後段不得觸發新段落或章節起始標記。
*   這不是跨段落的通用合併；句末標點、清單編號、章節標題、欄位切換或保護區域都會停止合併。目的只是把 PDF 擷取器拆開的同一行/同一段視覺片段還原，避免續行被獨立翻譯或縮成小字。

### D. 新段落/章節起始標記 (Paragraph Start Detection)
*   以下特徵代表新段落或獨立區塊的起點，必須與前文斷開：
    *   段落開頭符合清單或編號格式，例如 `[1]`, `1.`, `1)`, `a.`, `a)`, `•`, `-`, `*`。
    *   段落開頭符合章節編號，例如 `3.4.1`, `10. `。
    *   長度 < 30 字元，且全為大寫字母（如 `REFERENCES`, `ABSTRACT`）。
    *   段落開頭為特殊圖表標題，如 `Table`, `Figure`, `Fig`, `表`, `圖`, `RQ1` 等。
    *   文字為 "Keywords"、"Keyword"、"關鍵字"、"关键字"。

### E. 表格頁標題與列式表格 (Table Page Caption & Row-Style Tables)
*   **`IsTableCaptionWord`**：僅當 `Table` / `表` 為**行首表格標題**時才將頁面標記為 `isTablePage = true`。
    *   同一行左側已有其他文字（幾何或閱讀順序）→ 非表格標題（如 "shown in Table"、"in Table 1"）。
    *   `Table` 前一個詞為 `in`, `see`, `shown`, `of`, `and`, `or`, `from`, `on`, `with`, `below`, `above`, `shows`, `depicts`, `illustrates`, `to`, `for`, `at`, `using`, `the` 等介系詞/動詞 → 非表格標題。
*   **列式表格（Row-Style Tables）**：`MarkTableRegionByCaption` 自 `TABLE N` / `Table N` 標題向下，於同欄、間距 $\le 28\text{ pt}$ 的連續整列段落標記 `IsTable = true`（`isTablePage` 上高度 $< 35\text{ pt}$、寬度 $> 80\text{ pt}$ 的整列文字列）。
*   遇 `Listing` / `Figure` / `Fig` 開頭，或羅馬數字章節行（`^[IVXLC]+\.\s`），或過長正文列時停止向下延伸。

---

## 2. 🛑 避讓規則：絕對不可翻譯與修改的內容 (Hard Bypass Rules)

避讓內容在標記階段會將 `IsBypassed` 設為 `true`。**圖區略過 = 不 strip、不 overlay、不重畫**：圖表、workflow 圖例標籤、數學公式、程式碼等區域不得對其底層 PDF 串流剝除字型、不得繪製白色遮罩、不得以 `RenderBypassedParagraph` 重繪。**表格頁為部分例外**：見 §2.E（僅 strip 可譯正文所用字型；表格儲存格若共用字型則仍須重繪）。

### A. 向量圖表與點陣圖 (Diagrams & Images) - ❗️ 最優先保護
*   **點陣圖片**：任何寬度 > 80 且高度 > 80 的點陣圖（`Image XObjects`）。
*   **向量圖表**：由線條、路徑組成的向量繪圖（`page.ExperimentalAccess.Paths`）。
    *   **幾何判定**：先收集包圍盒大於 **$4\times4\text{ pt}$** 的向量路徑，再以叢集規則保留真正的圖形區（單一路徑須達 **$80\times30$** 或 **$30\times60$**；多路徑叢集須達最少 3 條且合併區域達相同尺度）。過小、全頁背景與細線框不形成 `DiagramMaskRegions`。
    *   **避讓對象**：
        1. 任何與圖表區幾何交集的段落。
        2. **可選取圖表文字層（Figure labels）**：柱狀圖/折線圖的軸刻度、圖例（`GPT-4`、`Models`、`Completion Level (%)`）、子圖標題 `(a)…` 等常為 PDF **文字層**（可選取），與向量路徑重疊。`MarkDiagramFigureLabels` + `FinalizeDiagramFigureLabels` 依字元/段落與 `DiagramMaskRegions` 重疊標記 `IsDiagram = true`；`ReclassifyChartLabelsMisclassifiedAsTable` 將誤標為 `IsTable` 的圖例改回圖表。
        3. **Workflow 短標籤優先**：段落幾何與 `DiagramMaskRegions` 相交、字高 ≤ 22 pt 且 ≤ 6 詞的短標籤，即使一般 callout heuristic 將其視為可翻譯，也必須標記 `IsDiagram = true`、`IsBypassed = true`。最後的頁面分類 invariant 會再次套用此規則，防止後續 cleanup 把標籤改回可翻譯而造成白色遮罩。長段落仍維持翻譯；明確 gray Prompt 標題／子標題優先走 gray Prompt 規則。這裡使用段落與圖形區的幾何相交，不依賴不穩定的字元重疊比例。
        4. **鄰近度傳播避讓**：頁面含圖表時，**僅** `IsLikelyChartLabel` 短標籤（≤4 詞、高度 ≤22 pt，或 `(a)` / 軸刻度等明確型態），若與已避讓圖表段落距離在 $30\text{ pt}$ 以內，自動設為 `IsBypassed = true` 且 `IsDiagram = true`。
        5. **頁首頁尾排除**：`ClearDiagramFlagOnRunningHeaders` 清除頂部/底部 running header/footer 的 `IsDiagram`，避免誤判。
    *   **選擇性 Strip（Selective Font Strip）**：`StripTextFromPage` **僅**剝除可譯段落（`IsBypassed = false`）所使用的 PDF 字型資源；圖表/workflow 標籤、表格儲存格（若未共用可譯字型）、程式碼、灰色 Prompt 框、數學公式、參考文獻、作者欄所用字型**保留**於原始 PDF 串流。Pass 2 中 `RenderBypassedParagraph` **僅**在該段落字型已被 strip 時才重繪；`IsDiagram` 標籤預設不重畫，維持原始英文單層清晰文字。
    *   **圖表與灰色 Prompt 必須分離**：`DiagramMaskRegions` 只代表圖形幾何，不能直接推論為灰色 Prompt。只有同一幾何區內存在明確 Prompt 標題，或頁面實際存在淺灰填色向量框時，才可建立 `EffectiveGrayMaskRegions`。圖表區不得因尺寸相似而套用白色 Prompt 遮罩。
    *   **內容串流狀態不可重設**：PDF page 的 `/Contents` 可能是多個連續串流，且可在 `TJ`/`Tj` 文字執行中切開；`StripTextFromPage` 必須跨串流延續目前 `Tf` 字型狀態，否則只會清除第一段而留下後續幽靈原文（ASTER p1 摘要回歸案例）。
    *   **Pass 1/2 遮罩與 overlay**：`ShouldProtectDiagramRegionFromParagraph` 僅保護 `IsDiagram` 短標籤與圖表 bbox 內標籤；`IsTranslatableBodyProse`、章節標題、多句正文**一律**仍執行 Pass 1 白色遮罩與 Pass 2 中文 overlay。雙欄頁左欄正文不得因 gutter 與右欄 Figure 遮罩輕微重疊而 skipRender。
    *   **圖說遮罩邊界（硬性）**：`Figure` / `Fig.` 圖說可以翻譯，但白色遮罩的垂直範圍固定在來源圖說 bbox（只允許向下保留足夠 padding 清除多行原文），不得因譯文變高而向上侵入圖表。一般正文遮罩的上緣仍由 `ClampMaskTopBelowDiagrams` 截在圖表底邊之下；圖表四邊線與內容必須保留。
    *   **無損核心限制**：嚴禁解壓縮、修改或剝除 `/Form` XObjects 與 `/Image` XObjects 內部 content stream 的任何內容。
    *   **⚠️ 表格網格線誤判例外**：表格的向量框線/網格線（`page.ExperimentalAccess.Paths`）可能滿足圖表幾何門檻，使 `OverlapsWithLargeImage` 將儲存格文字標為 `IsDiagram = true`。圖表段落**不**參與 Pass 2 譯文重繪；若頁面同時執行 `StripTextFromPage`，儲存格英文會被剝除且無法補回，造成 Work description 等欄位整段消失。見 §2.E `ReclassifyWorkDivisionTableText`。
    *   **`ReclassifyTableMisclassifiedProse`（表格 bbox 內正文誤判清除）**：比較表 bbox 向下/向外擴張時，可能誤標貢獻 bullet（`•`）、「To sum up…」導言、表格腳註續行（`and attack techniques…`）、章節標題（如 `Background and Related Work`）為 `IsTable`。驗證基準：`PentestAgent_Agent Pentest.pdf` p2 的 Table 1 儲存格應 `table=True`，貢獻 bullet 與腳註應 `bypass=False`。
    *   **⚠️ 圓角標註框（Findings callout）誤判例外**：研究問題摘要框（如 `RQ2 Findings:`、`RQ3 Finding:`）的圓角向量路徑會觸發 `OverlapsWithLargeImage`，使框內長段落被標為 `IsDiagram = true`；strip 後 Pass 2 跳過圖表區重繪，造成框內譯文整段空白。見 `ReclassifyCalloutFindingsText` + **`IsTranslatableCalloutProse`**：符合 Finding/Findings、階段標記句型（`Intelligence Gathering:` 等章節正文）、章節標題（`IsHeadingParagraph`）或多句正文（`IsTranslatableBodyProse`）的段落改回可翻譯（`IsDiagram = false`）。**圖表軸/圖例/workflow 短標籤**（`IsLikelyChartLabel`）仍 bypass 並 letter-perfect 重繪英文。驗證基準：`TOGLL_Oracle Generation.pdf` p7/p8 Findings 框應有中文譯文；`PentestAgent_Agent Pentest.pdf` p5 §3.1 正文應可譯、Figure 2/3 workflow 短標籤仍為英文。
    *   **Findings 框線與底色保留（硬性）**：可翻譯的 `Finding` / `Findings` 圓角框屬於「可翻譯文字 + 固定向量容器」，不是一般欄正文。Pass 1 不得將遮罩擴展到整欄，也不得覆蓋框線、圓角或底色；遮罩必須限制在框內文字繪製區，並保留四周至少 1 pt 的邊界。若必須重建背景，必須以來源框的幾何與填色重繪後再畫譯文。健康檢查與視覺回歸須驗證框線四邊仍存在；不得只以 `clipped` / `overflow` 為通過條件。
    *   **⚠️ 圖表遮罩 skipRender 誤殺正文（PentestAgent p5 §3.1）**：舊邏輯對任何與 `DiagramMaskRegions` 幾何交集（20×3 pt）的段落一律跳過 Pass 1 遮罩與 Pass 2 譯文渲染。雙欄頁左欄正文 bbox 底部常與右欄 Figure 2 遮罩在 gutter 處輕微重疊，導致「3.1 System Overview」正文 strip 後整段空白。修正：**`ShouldProtectDiagramRegionFromParagraph`** + **`ShrinkDiagramMaskRegionsBottomGutter`** 僅在 `IsDiagram`、字元重疊比 ≥ 0.4、或短標籤中心落在圖表區時才 skip；`IsTranslatableBodyProse` / 章節標題 / 多句正文一律仍渲染譯文。鄰近度傳播僅限 `IsLikelyChartLabel` 短標籤。

### B. 獨立與行內數學公式 (Math Formulas & Equations)
*   **特徵**：
    *   段落結尾帶有公式編號（如 `(1)`, `(2)`）。
    *   文字結構符合數學定義樣式（如 `x : A -> B` 並包含 `→`, `↦`, `⇒`, `⊆`, `∈` 等符號）。
    *   段落剔除 `{v0}` 等公式占位符後，剩餘的英文字母極少，且運算子與標點符號的比例 $> 40\%$。
    *   個別字元字型名稱包含 `Math`、`Symbol`、`MSAM`、`MSBM`、`CMSY`，或單字平均字型大小小於本文平均字型大小的 $79\%$。
*   **處理**：將公式段落以預留占位符（如 `{v0}`, `{v1}`）進行替代並標記為避讓，翻譯時略過，渲染時依原始字型與相對位置重繪。數學/程式碼字型**不**參與選擇性 `StripTextFromPage`。

### C. 程式碼區塊 (Code Blocks)
*   **特徵**：使用等寬字型（包含 `Courier`, `Console`, `Inconsolata`, `Typewriter`, `NimbusMon`, `MonL`, `cmtt`, `ectt`, `sftt`, `Teletype`, `Mono`, `Code` 或正則匹配 `tt\d+`）。
*   **處理**：直接標記避讓，保留原始程式碼結構。等寬字型**不**參與選擇性 `StripTextFromPage`；Pass 2 **不**重畫（原始層 intact）。
*   **⚠️ 灰色 Prompt 框（Gray prompt boxes）— Plan A（現行）**：
    *   **整框 bypass**：灰色底向量框內**全部**段落標記 `IsGrayPromptContent = true`、`IsCode = true`；**僅保留英文**，不翻譯、不 strip 字型、Pass 2 不重繪。
    *   **Pass 1 禁止白遮罩**：`IsGrayPromptContent` / gray prompt `IsCode` 段落**絕不**繪製白色矩形（避免灰底上出現白塊）。
    *   **邊界判定（硬性）**：
        1.  灰色底向量 bbox（`GetGrayPromptShadedRegions`）為硬邊界：段落中心或 **≥50%** 字元落在 shaded rect 內才標記；**嚴禁**使用 `letterRatio >= 0.08` 等寬鬆門檻誤吞欄外正文。
        2.  標題觸發：`System Message (Simplified)`、`… Example`、`… Prompt`（含 `(Simplified)`，如 `Search Results Summary Prompt`）、`Prompt for` 等（`IsGrayPromptBoxParagraph`）啟動同欄延續區塊；shaded 區內指令型正文仍標 gray，但 `IsSectionIntroProse`（如 `The following prompt…`）與欄外 `IsTranslatableBodyProse` 仍須翻譯。
        3.  `IsTranslatableBodyProse`、章節標題、欄外正文 → **永不** `MarkAsGrayPromptContent`；Pass 1 亦跳過 gray shaded bbox 內段落（`IsParagraphInsideGrayShadedRegion`）；`FinalizeGrayPromptContentFlags` 清除 `IsDiagram`。
    *   **選擇性 strip**：灰色 prompt 所用字型**不**列入 `CollectTranslatableFontBaseNames`。
    *   **驗證基準**：`PentestAgent_Agent Pentest.pdf` **p1** 作者欄 `bypass=True`、`table=False`、無 TableMaskRegions；**p3/p6** Example/System Message 灰框全英文；**p4/p7/p8** 正文 `bypass=False`；**p14** Prompt 標題 `grayPrompt=True code=True`、框內無白遮罩；p5 §3.1 正文可譯。
*   **📋 Plan B（後續可選方案，尚未實作）**：將整個灰色 prompt 框**翻譯為中文**；Pass 1 仍不繪白遮罩（或使用灰底同色 tint mask）。完成 Plan A 穩定後再評估是否切換。

### D. 第一頁作者與機構資訊 (First Page Author Block)
*   **特徵**：位於第一頁，且在**最大字型段落（標題）底部（`titleBottom = titlePara.Y0`）到 `ABSTRACT`（或 `摘要`）標題頂部（`abstractTop = abstractPara.Y1`）之間**，且平均字型大小 $< 15.0\text{ pt}$ 的所有段落。
*   **處理**：標記為 `IsBypassed = true`，**不得**設 `IsTable=true` 或納入 `BuildTableMaskRegions`（避免 Pass 1 白遮罩破壞作者欄排版）；最終 bypass 迴圈須保留作者欄 `IsBypassed`。
*   **⚠️ 標題副標題例外**：緊貼主標題下方（gap ≤ 25 pt、水平置中、≤ 8 字、**段落寬度 ≤ 標題寬度 50%**）的副標題行（如 `with LLMs`）須與主標題合併為單一段落（`MergePageTitleWithSubtitle`），隨標題一併翻譯；不得落入作者區 bypass。
*   **⚠️ 歷史陷阱**：曾誤用 `para.Y0 > abstractY0` 作為條件，結果把 Abstract **之後**的所有小字體段落全部 bypass，導致 Abstract 正文、Index Terms、簡介（Introduction）等整頁正文都未翻譯。亦曾誤用 `abstractPara.Y0`（整段 Abstract 底部）作為上界，導致 Abstract 整段被 bypass。**正確條件為 `para.Y0 >= abstractTop && para.Y1 <= titleBottom`（PDFPig Y 軸向上增長，`titleBottom > abstractTop`）**，只 bypass 標題與 Abstract 之間的作者欄區域。

### E. 表格與數據 (Tables & Data)
*   **特徵**：
    1. 頁面包含表格標題（`IsTableCaptionWord` 判定之行首 `Table` / `表`，即 `isTablePage = true`）。
    2. 經 `MarkTableParagraphs` 幾何分群或 `MarkTableRegionByCaption` 列式延伸判定為表格單元格/列。
*   **處理**：
    *   所有被識別為表格的段落標記 `IsBypassed = true` 且 `IsTable = true`，**不翻譯**，保留原始英文/原文。
    *   **表格頁選擇性文字剝除**：含表格的頁面執行選擇性 `StripTextFromPage`（僅 strip 可譯正文所用字型），以消除幽靈英文；若表格儲存格與正文共用字型，Pass 2 仍重繪 bypass 儲存格。
    *   **Pass 2 重繪**：被 bypass 的表格儲存格在 Pass 2 以原始字型與位置重繪英文，補回被 strip 的文字層。
    *   **遮罩例外**：`IsTable = true` 的段落**絕對不可**繪製白色遮罩，以保留向量框線與網格線。
    *   **向量邊框保護**：表格區域遮罩採叢集式 `BuildTableMaskRegions`（見 §4.D），禁止單一全頁 bbox 跳過整頁譯文渲染。
*   **`ReclassifyWorkDivisionTableText`（表格網格誤判為圖表之修正）**：
    *   **觸發條件**：頁面含 `WORK DIVISION` 標題段落（如 `10. WORK DIVISION`）。
    *   **幾何範圍**：標題下方（`para.Y1 <= caption.Y0 + 5`）、水平中心落在標題欄寬內（`caption.X0 - 15` 至 `min(pageWidth - 20, caption.X1 + 230)`）的段落。
    *   **處理**：將符合條件且被 `OverlapsWithLargeImage` 誤標為 `IsDiagram` 的儲存格文字改為 `IsDiagram = false`、`IsTable = true`，使其走表格 bypass + Pass 2 英文重繪路徑，而非圖表避讓（無重繪）。
    *   **驗證基準**（`114423046_final_project.pdf` p14）：`dump-layout` 應顯示 `tableCount ≈ 36`、Work division 儲存格 `diagram=False`；修正前僅 `tableCount=5` 且多數儲存格 `diagram=True`，譯後 Work description 欄位空白。
*   **Pass 0.55 表格頁正文誤判清除**：寬度 > 頁寬 38% 且字數 > 10 的欄寬正文（如 RQ4 導言）不應標為 `IsTable`，否則僅英文重繪、譯文與幽靈英文疊加。驗證：`TOGLL_Oracle Generation.pdf` p8 RQ4 段落 `table=False`。
*   **`ReclassifyTableMisclassifiedProse`（表格 bbox 內正文誤判清除）**：比較表 bbox 擴張時可能誤標貢獻 bullet（`•`）、「To sum up…」、表格腳註續行、章節標題（如 `Background and Related Work`）為 `IsTable`。驗證：`PentestAgent_Agent Pentest.pdf` p2 Table 1 儲存格 `table=True`，貢獻 bullet 與腳註 `bypass=False`。

### F. 參考文獻區塊 (REFERENCES / BIBLIOGRAPHY Section)
*   **標題辨識**（`IsReferencesSectionHeadingText` / `IsReferencesSectionHeading`）：
    *   帶章節編號：`^(\d{1,2})\.\s*(?:REFERENCES?|BIBLIOGRAPHY|參考文獻)\s*\.?\s*$`（**大小寫不敏感**），如 `9. REFERENCE`、`9. REFERENCES`。
    *   無編號標題：`REFERENCES`、`REFERENCE`、`BIBLIOGRAPHY` 以 **大小寫不敏感** 匹配（如 ACM 論文 p13 的 `References`）；`參考文獻` 仍為精確匹配。表格欄位標籤由 `IsReferencesSectionHeading` 的 `IsTable` 排除，而非靠大小寫區分。
    *   `IsReferencesSectionHeading` 若 `para.IsTable == true` 則回傳 `false`，避免表格「Reference」欄觸發整頁書目 bypass。
*   **區塊範圍**：自標題段落之後，至下一個主要章節為止（跨頁延續）。終止條件包含 `APPENDIX`、`Appendix A`、`A Prompts` 等附錄字母章節（`^[A-Z]\s+Prompts`、`^[A-Z]\.\d+`、`^[A-Z]\.\s+`）、`WORK DIVISION`、`ACKNOWLEDGMENT` / `ACKNOWLEDGEMENT`，以及 `10.` 等編號章節標題（如 `10. WORK DIVISION`）。**附錄章節標題與說明正文可譯**；**灰色 prompt 框**依 §2.C **`MarkGrayPromptBoxesAsCode` bypass 英文**；workflow 圖表短標籤（`IsLikelyChartLabel`）與表格儲存格 bypass。
*   **終止排除**：`IsReferencesSectionTerminator` 對 `IsReferenceParagraph`（`[N]` 開頭、含 `http` / `doi:` / `www.`）回傳 `false`，避免書目條目被誤判為下一章節。
*   **閱讀順序**：雙欄頁面以左欄由上而下、再右欄由上而下（`GetPageReadingOrder`）判定區塊邊界，避免雙欄交錯誤判。
*   **處理**：
    *   區塊內所有段落（`IsTable` 除外）標記 `IsBypassed = true`，**不翻譯**，保留原始英文書目。
    *   **僅標題可譯**：標題本身不 bypass，翻譯為 **參考文獻**（`PostProcessTranslation` 保留 `9.` 等編號前綴）。
    *   `IsReferenceParagraph` 亦作為 Pass 2 重繪路徑判斷（見 §4.D）。
*   **渲染**：選擇性 `StripTextFromPage` 後，**僅**字型已被 strip 的 bypass 段落（書目、表格儲存格、作者欄等）走 `RenderBypassedParagraph`；圖表/workflow 標籤、程式碼、數學公式、灰色 Prompt 框因字型未 strip 而**跳過重繪**。


---

## 3. 📝 翻譯與術語修正規則 (Translation & Terminology Rules)

在傳送與接收翻譯時，必須保證高併發下的防 Ban 限流，並對專業術語進行後處理替換。

### A. 限流與雙軌翻譯機制 (Rate Limiting & Fallback)
*   **併發控制**：使用 `SemaphoreSlim(1, 1)` 強制單一執行緒進行翻譯請求，並在鎖定內加入 $150\text{ms}$ 至 $400\text{ms}$ 的隨機延遲。
*   **批次翻譯 (Batch Translate)**：優先調用 `TranslateBatchAsync` 進行整頁批次翻譯，減少 HTTP 請求往返。
*   **復原流程**：若批次翻譯因網絡或 API 限流失敗，先將批次拆半，再對最小批次逐段呼叫；每層都可使用另一個 provider fallback。每個 provider 有獨立 30 秒上限，fallback chain 預設 75 秒，避免 primary 重試時提前取消 fallback。
*   **完整性**：逐段復原仍失敗時，必須中止該次輸出並產生 health report；嚴禁以原文靜默填補失敗段落後宣稱翻譯完成。

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
4.  **簡繁轉換**：目標語言為 `zh-TW` / `zh-HK`（非 `zh-CN`）時，於 `PostProcessTranslation` 最後呼叫 `ChineseTextConverter.SimplifiedToTraditional`，將 API 回傳的簡體字統一轉為繁體。

---

## 4. 🎨 渲染與繪製排版規則 (Rendering & Typography Rules)

### 4.0 零裁切排版門檻（v3.6.4）

*   翻譯段落必須先量測並 reflow；一般正文的字型縮放下限統一為 **80%**。標題不適用縮放策略。
*   `guardClip` 必須為 0；圖表/欄位保護改由遮罩邊界與段落 reflow 完成，不得用外層 `IntersectClip` 靜默刪除譯文。health report 的 `GuardClipEntries` 與 `OverflowEntries` 都必須為 0。
*   實際 overflow、頁數變更或健康檢查失敗時，不得提交正式輸出檔案。

### 4.0.A 標題階層、字級與幾何錨點（硬性規則）
*   `PageTitle`、摘要標題、章節／子章節標題必須保留來源的視覺字級；輸出標題不得小於來源，也不得因主標題與副標題合併而使用平均字級。合併群組一律採主標題字級。
*   標題的來源中心點、左錨點或右錨點必須保留；單行文字框不得單獨用來推斷置中。health report 必須記錄錨點偏移，發布門檻為 **≤ 1.5 pt**。
*   標題增加行數時，只能推移同一欄、位於標題下方的可翻譯正文。作者欄、圖表、表格、程式碼、公式、灰色 prompt、參考文獻與頁首頁尾均為固定障礙，不得跨欄、整頁或跨頁重排。
*   若同欄推移會撞到固定障礙或頁底，layout planning 必須失敗並寫入 `LayoutFailureReason`；不得輸出半成品或以 clip 隱藏溢出。
*   每次執行的 JSON health report 必須包含標題數、來源／輸出字級比例、錨點偏移、推移段落數、固定區碰撞與底部溢位。

翻譯後的中文文字必須使用 PDFsharp 的 `XGraphics` 重新繪製，並嚴格遵循字型與佈局對齊規範。

### A. 系統字型 TTC 硬性映射 (Font Resolution)
*   **限制**：PDFsharp 不支援 TrueType Collection (`.ttc`) 字型。
*   **硬性映射表**：
    *   標楷體 (`dfkai-sb`, `dfkai`, `kaiu`) $\rightarrow$ **一律**載入 `kaiu.ttf`（含 `b`/`bi`）。**嚴禁**對 CJK 翻譯使用 `simsunb.ttf`（PDF 嵌入為 SimSun-ExtB，CJK cmap 損壞導致亂碼）。
    *   微軟正黑體 (`msjh`, `jhenghei`) $\rightarrow$ 常規樣式載入 `kaiu.ttf`；CJK 譯文粗體不切換 `simsunb.ttf`，由 renderer 以同字型二次描繪保留字重。
    *   微軟雅黑體 (`msyh`, `yahei`) $\rightarrow$ 常規 `kaiu.ttf`；CJK 譯文同樣使用 renderer 的二次描繪，不走不相容的粗體路徑。
    *   日文字型 (`msgothic`) $\rightarrow$ 載入實體檔 `kaiu.ttf` (以標楷體替代日文漢字)。
    *   韓文字型 (`malgun`) $\rightarrow$ 載入實體檔 `malgun.ttf` (若無則使用標楷體)。
    *   Cambria / Math 字型 $\rightarrow$ `times.ttf` (Times New Roman，避開 Cambria 預設 TTC 崩潰)。
    *   等寬字型 $\rightarrow$ `cour.ttf` (Courier New)。
*   **字型缺字 Fallback**：若渲染特定 Unicode 字元或數學運算子遇到缺字，`ClickraFontResolver` 必須能回退至 `Segoe UI Symbol` (`seguisym.ttf`)。
*   **來源字型粗體判定**（`IsSourceFontBold` / `IsLaTeXMediumFont`）：
    *   IEEE 的 Nimbus `NimbusRomNo9L-Medi` 是實際粗體 face，必須視為粗體；其他 TeX medium/math face 仍不視為粗體。
    *   粗體範圍以 `{b}` / `{/b}` inline markers 隨翻譯傳遞，禁止只用整段 `IsBold` 推測。
    *   `IsLineBold`：一行中超過 50% 字元為粗體時，該行視為粗體行。
*   **CJK 譯文字重保留**（`IsCjkTranslationFont`）：非 bypass、非程式碼的 CJK 譯文使用 `kaiu.ttf` 常規字型；來源粗體／標題以 0.18 pt 同字型二次描繪保留視覺字重，禁止切換不相容的 CJK bold TTC/ExtB。

### B. 動態字體縮放與行高 (Font Scaling & Spacing)
*   **行高乘數 (Line Spacing)**：
    *   預設中文字型為 **$1.35$** 倍字型大小。
    *   Arial 字型為 **$1.2$** 倍。
    *   參考文獻段落（References）為 **$1.15$** 倍。
    *   若來源段落的 bbox 明顯低估實際 glyph 行高，普通正文可使用 **$1.0$** 倍行距以保留來源字級；這是來源測量修正，不是任意縮小文字。
    *   **字體與佈局收縮（僅一般正文）**：
    *   若翻譯後中文字數增加，導致總渲染高度大於原始段落包圍盒高度：
        1. 優先壓縮行高倍數（計算 `limitHeight / (rows * fontSize)`），最低可壓縮至 **$1.0$** 倍。
        2. 若壓縮行高後高度依然超出，則對字型大小（FontSize）進行縮放，縮放比例最低限制為 **$0.8$**；標題禁止使用此步驟，必須交由同欄推移或失敗。

### C. 段落裁切策略 (Paragraph Clipping)
*   **禁止外層 guard clip**：不得在圖表或雙欄交界以 `IntersectClip` 截斷翻譯段落；這會讓流程表面成功、實際遺失下半段文字。
*   **唯一允許的段落 clip**：renderer 內部可用 `layoutWidth` 與 `Math.Max(paragraphHeight, renderedHeight)` 保護自身繪製邊界，但只要產生 overflow 就必須讓 health gate 失敗。
*   **原則**：優先透過 reflow、行高/字型縮放與遮罩幾何邊界解決碰撞；`GuardClipEntries` 與 `OverflowEntries` 均須為 0。
*   **正文續行**：不得因單行來源 bbox 過矮而把段尾縮到 80% 以下或縮成小字；續行沿用同欄正文有效字級。若自然行高撞到頁尾、表格或其他固定區，必須失敗，不得輸出。

### D. 遮罩與擦除規範 (Masking Rules)
*   **Pass 1 白色遮罩**：對需翻譯且非 bypass、非 `IsTable` 的段落，於原始段落座標外擴 **$1.5\text{ pt}$**（`maskPad`）繪製白色矩形，擦除英文本文。
*   **灰框 / 作者區禁遮罩**：Pass 1 **永不**對 `IsGrayPromptContent`、`IsGrayPromptCodeParagraph`、幾何重疊 `GrayPromptShadedRegions` 的段落，或 **p1 作者區**（`IsPageOneAuthorBlockParagraph`）繪製白遮罩。遮罩矩形與灰框 bbox **有任何交集**即整塊 `continue`（不做 clip 裁切）；標題譯文遮罩嚴格限制在標題段落 bbox 內（`Y0`–`Y1`），不得侵入作者帶。
*   **選擇性 strip**：`CollectFontsUsedOnlyInProtectedRegions` 排除僅用於灰框或 p1 作者帶的字型，避免 strip 後需重繪造成白方塊；Pass 2 bypass 重繪僅在 `ParagraphUsesStrippedFont` 時執行。
*   **遮罩高度**：`maskHeight = max(原始 bbox 高度, renderedHeight) + padding`；當譯文較高時，遮罩**僅向上延伸**（固定段落底部，增大 `maskPdfY1`），**禁止向下延伸**，以免上方段落遮罩抹除表格頂部框線。
*   **`ClampMaskBottomAboveTables`**：遮罩與表格叢集區域水平重疊 $\ge 10\text{ pt}$ 時，遮罩底邊（`maskPdfY0`）必須 $\le$ `region.Y1 - 9 + 1.5 pt`（`tableTopBorderInset = 9`，`borderClearance = 1.5`），保護表格最上方網格線。
*   **`ParagraphOverlapsTableMask`**：段落與表格遮罩區域若水平重疊 $\ge 30\text{ pt}$ **且**垂直重疊 $\ge 5\text{ pt}$，視為與表格重疊——Pass 1 **跳過遮罩**、Pass 2 **跳過譯文渲染**，避免誤蓋表格。
*   **`BuildTableMaskRegions`**：將 `IsTable` 且高度 $\le 35\text{ pt}$ 的儲存格依同欄、垂直距離 $< 45\text{ pt}$ **叢集**為多個獨立遮罩區（每叢集 $\ge 2$ 格），外擴邊界（左右 $-8/+8\text{ pt}$，上 $-8\text{ pt}$，下 $+12\text{ pt}$）。**禁止**使用單一全頁寬 bbox 跳過整頁渲染。
*   **表格防遮罩例外**：`IsTable = true` 的段落本身**絕對不可**繪製白色遮罩。
*   **表格頁 Strip**：含表格的頁面執行 `StripTextFromPage` 後，bypass 儲存格於 Pass 2 重繪英文（見 §2.E）。
*   **`RenderBypassedParagraph`（逐字元 letter-perfect 重繪）**：`IsBypassed` 且**字型已被 strip** 的段落（書目、表格儲存格、作者欄等）於 Pass 2 呼叫 `RenderBypassedParagraph`。`IsDiagram` workflow/圖表標籤、程式碼/數學公式/灰色 Prompt 框因字型未 strip 而**跳過重繪**。
*   **死碼清理**：嚴禁呼叫 `StripFormXObjects` 進行向量圖表文字抹除，以避免圖表結構損壞。

### E. 旋轉文字渲染 (Rotated Text Rendering)
*   支持 PDF 中的旋轉段落，透過 GDI+ 的矩陣變換進行坐標轉換：
    *   `Rotate270`：以段落左下角為基準，旋轉 $-90$ 度，佈局寬度改為原高度。
    *   `Rotate90`：以段落右上角為基準，旋轉 $90$ 度，佈局寬度改為原高度。
    *   `Rotate180`：以段落右上角為基準，旋轉 $180$ 度，佈局寬度為原寬度。

---

## 5. 🔗 超連結位置動態校正 (Link Annotation Realignment)

翻譯後本文寬度與長度會發生變化，原始 PDF 上的點擊超連結（Link Annotations）必須動態校正位置。

### A. Pass 0：關聯與空間指紋 (Analysis Phase)
1.  遍歷頁面 `page.Annotations`，找出與段落字母（`AllLetters`）幾何重疊（誤差 $\pm 2.5\text{ pt}$）的最佳段落。
2.  **段落評分**（`ScoreAnnotationParagraph`）：重疊字母數為基礎分；註解中心落在段落 bbox 內 **+1000**；段落為 bypass / 程式碼（`IsBypassed` / `IsCode`）**+500**（避免程式碼清單 `1)` 被誤掛到譯文段落）。
3.  自重疊字母組出 `searchText`，經 **`NormalizeAnnotationSearchText`** 降噪：
    *   優先擷取 `[N]` 引用標記。
    *   `Table II` / `TABLE III` 等表格羅馬數字 → 擷取 `II`、`III`。
    *   羅馬數字章節引用 `([IVXLCDM]+)-([A-Z])`（如 `II-A`），允許尾隨 `)` 或 `.`。
    *   `Section II)`、`III.`、`V,` 等 → 擷取羅馬數字本體。
    *   圖表引用 `(?:Figure|Fig\.?|圖)(\d+)`；清單編號 `1)`、`2)` 保留原樣。
    *   孤立數字 `^\d\)?$` 或短字串中的單一數字（**須**非更大數字的一部分）。
4.  記錄 **`OccurrenceIndex`**（同段內第幾次出現）及相對空間指紋：
    *   **`RelCenterX`** = `(annotCenterX - para.X0) / para.Width`
    *   **`RelCenterY`** = `(annotCenterY - para.Y0) / para.Height`
    *   **`RelWidth`** = 註解寬度 / 段落寬度

### B. Pass 2：字元座標記錄 (Render Phase)
在 `RenderParagraph` 繪製時，將每個字元的實際座標記錄至 `RenderedChar` 列表。Bypass / 程式碼段落走 `RenderBypassedParagraph`，**不**更新其 Link Annotation（保留原始座標）。

### C. Pass 3：重定位與空間消歧 (Realignment Phase)
1.  **`BuildAnnotationSearchPatterns`** 依原始 `searchText` 產生多組搜尋模式：
    *   `[N]` 方括號引用。
    *   `II-A` / `III-B` / `VIII-A` 等羅馬數字-字母章節引用。
    *   羅馬章節：`第{roman}`、`表{roman}`、中文數字（`III`→`三`）等。
    *   清單編號：`1)`、`第1`、`清單1`。
    *   圖表引用：`圖N`、`:圖N`、`即圖N`、`表N`、`N)`，以及 `Fig.` / `Figure` / `Table` + 數字。
2.  **`FindSectionRomanOccurrences`**：章節連結譯為「第 II 節」後，優先匹配 `第` + 羅馬數字；fallback 字面羅馬數字或中文數字（`RomanToChineseSectionNumeral`）。
3.  在 `RenderedChar` 序列中依模式尋找所有出現位置；純數字模式須通過 **`IsStandaloneDigitOccurrence`**（避免 `1` 命中 `1383` 內部）；純羅馬數字須通過 **`IsStandaloneRomanOccurrence`**（避免 `V` 命中英文單字內部）。孤立圖號數字另以 **`FindLooseFigureDigitOccurrences`** 回溯鄰近 `圖` 字匹配。
4.  若有多處匹配，呼叫 **`PickOccurrenceBySpatialPosition`**：
    *   以 Pass 0 記錄的 `RelCenterX` / `RelCenterY` 換算目標 PDF 座標。
    *   羅馬數字與圖表引用模式優先垂直對齊（`preferVerticalAlignment`：`|dy| * 4 + |dx|`）。
    *   若 `OccurrenceIndex > 0` 且與空間最近者距離相近（$\le 1.5 \times$ 最小距離 $+ 2\text{ pt}$），保留原始索引。
5.  若模式皆未命中，僅在 **`MapRenderedCharsBySpatialPosition`** 回傳中心距離 $\le \max(24\text{ pt}, 0.15 \times \text{paraWidth})$ 時採用空間映射；否則 **保留原始註解 rect**（不強制錯位）。
6.  命中時將 `PdfAnnotation.Rectangle` 重設為命中字元的外包矩形，X 軸外擴 $1.0\text{ pt}$、Y 軸外擴 $1.5\text{ pt}$。

### D. 已知限制 (Known Limitations)
*   **羅馬數字章節引用**（如 `II-A`）：當譯文以中文替換原文後，重繪字元序列可能不再包含 `II-A` 字面，導致連結包圍盒偏移或失效。此類引用目前僅能盡力以空間位置對齊，無法保證 100% 命中。
*   **程式碼清單內連結**：若註解正確掛載於 bypass 程式碼段落，保留原始英文座標；若誤掛至譯文段落則可能錯位。
*   **外部 URL 的來源條件**：流程只會保留並校正輸入 PDF 已存在的 `/Link` + `/URI` annotation；頁面上看得到的純文字 URL 若來源沒有 annotation，不會被誤宣稱為可點擊連結。ASTER 的 `ASTER .pdf` 是無 annotation 的翻譯來源；需要驗證外部連結保留時，必須使用含連結 annotation 的 `ASTER- .pdf` fixture。

---

## 6. ⚠️ 歷史陷阱速查 (Historical Pitfalls Quick Reference)

| 陷阱 | 症狀 | 正確做法 |
| :--- | :--- | :--- |
| 作者區塊 Y 軸 | Abstract 正文整段未翻譯 | `para.Y0 >= abstractTop && para.Y1 <= titleBottom` |
| SimSun-ExtB | CJK 譯文亂碼疊字 | CJK 譯文固定 `kaiu.ttf`；粗體／標題以 0.18 pt 二次描繪保留字重，禁走不相容 `simsunb.ttf` |
| 表格頁未切欄 | 全頁寬段落遮蓋整張表 | `GetMergedBlocks` 表格頁仍執行中央線切分 |
| 遮罩向下延伸 | 表格頂部框線被白塊抹除 | 譯文較高時遮罩僅向上延伸 + `ClampMaskBottomAboveTables` |
| 全頁表格遮罩 bbox | 表格頁正文譯文全部消失 | `BuildTableMaskRegions` 叢集式區域，非單一全頁 bbox |
| 表格頁不 strip | 幽靈英文殘留於向量框線上 | 表格頁執行 `StripTextFromPage`，Pass 2 重繪 bypass 儲存格 |
| 表格網格誤判為圖表 | WORK DIVISION 等表格 Work description 欄譯後整段消失 | 表格向量框線觸發 `OverlapsWithLargeImage` → `IsDiagram`；頁面 strip 後圖表區不重繪。`ReclassifyWorkDivisionTableText` 將標題下儲存格改回 `IsTable`（p14 `tableCount` 5→36） |
| Findings 框誤判為圖表 | RQ2/RQ3 Findings 框內譯文空白 | 圓角向量路徑觸發 `IsDiagram`。`IsTranslatableCalloutProse` + `ReclassifyCalloutFindingsText` 改回可譯 |
| Findings 框線被白遮罩擦除 | 翻譯後 Finding 5/6 只剩文字，左右或上下邊線消失 | `IsFindingCallout` 禁止整欄 mask expansion；保留來源圓角框的底色與四邊邊線，並以框線存在性做視覺回歸 |
| 圖說遮罩擦除圖表底線 | 翻譯後圖四等圖表只剩左右底線，中間底邊消失 | 圖說仍可翻譯，但白色遮罩必須套用 `ClampMaskTopBelowDiagrams`，禁止向上侵入圖表向量區 |
| 灰色 Prompt 框誤譯 | p7/p8 灰色框被翻成中文；p3/p6 框內白遮罩 | Plan A：`IsGrayPromptContent` 整框 bypass；Pass 1 跳過灰框；`letterRatio >= 0.5` 硬邊界；`FinalizeGrayPromptContentFlags` 防 diagram 覆寫；`… Example` 標題觸發 |
| 圖表遮罩 skipRender 誤殺正文 | PentestAgent p5 §3.1 正文 strip 後整段消失 | gutter 交集 + 誤標 `IsCode` 導致 strip 後不重繪。`ShouldProtectDiagramRegionFromParagraph` 排除 figure caption；正文 `IsCode` 清除後正常渲染譯文 |
| 標題副標題被作者 bypass | p1「with LLMs」等副標殘留英文 | 副標緊貼主標題下方（gap ≤ 25 pt、≤ 8 字）須排除作者區 bypass |
| 表格頁正文誤標 IsTable | RQ 導言段落英文幽靈疊於譯文下 | Pass 0.55：`Width > 38% 頁寬 && wordCount > 10` 清除 `IsTable` |
| 參考文獻半譯 | `[2]`–`[16]` 書目中英混雜、作者名被譯成中文 | `ApplyReferencesSectionBypass`：區塊內條目全 bypass；僅 `REFERENCES` / `9. REFERENCE` 等標題可譯為「參考文獻」。雙欄頁須用 `GetPageReadingOrder` 正確切換至 `10. WORK DIVISION` |
| 表格 Reference 欄誤觸發 bypass | 表格頁正文未譯、書目區異常全 bypass | `IsReferencesSectionHeading` 排除 `IsTable`；無編號標題大小寫不敏感（`References` / `REFERENCES` 皆可） |
| 書目 strip 後疊字錯位 | 參考文獻頁英文重影、行距擠壓（如 2407 p14） | 所有 strip 後 bypass 段落走 `RenderBypassedParagraph`；書目換行延續行（無 `[N]` 前綴）亦須逐字元重繪，不可用 `RenderParagraph` |
