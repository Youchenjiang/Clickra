# Clickra Architecture

Welcome to the Clickra architecture repository. This directory centralizes all architectural specifications, interactive layer diagrams, and technical debt records.

---

## 🗺️ Reading Guide (新人閱讀路徑)

- ⏱️ **3-Minute Quick Visual Overview**: Open [`diagrams/overview.html`](diagrams/overview.html) to explore the system's end-to-end layered structure (L0 through L4) locally, or preview the [SVG Maps](#github-preview-svg-maps) directly on GitHub.
- 🏛️ **System Specification & Boundaries**: Read [`spec/clickra_architecture.md`](spec/clickra_architecture.md) for canonical module ownership, Mermaid runtime topologies, and dependency rules.
- 📋 **Technical Debt & Boundary Leaks**: Check [`spec/architecture_backlog.md`](spec/architecture_backlog.md) for audited layer violations, circular dependencies, and current migration status.
- 🔄 **Regenerating Diagrams**: Run `powershell -ExecutionPolicy Bypass -File scripts\architecture\update_architecture_diagrams.ps1` from the repository root.

---

## 📂 Directory Structure

```
docs/architecture/
├── README.md               # This document (Single entry point & reading guide)
│
├── spec/                   # 🏛️ Specifications & Reports
│   ├── clickra_architecture.md       # Canonical system architecture & module invariants
│   ├── architecture_backlog.md       # 10/20 Technical debt & layer violation audit
│   └── evaluation_report_20261012.md # Initial baseline architecture evaluation
│
└── diagrams/               # 🗺️ Interactive Maps & Exports
    ├── overview.html                 # All layers (L0 to L4) interactive map
    ├── l0_entry_points.html          # L0: OS Entry points & process handoffs
    ├── l1_presentation.html          # L1: UI renderers & Presentation controllers
    ├── l2_application.html           # L2: Application use cases & task lifecycle
    ├── l3_infrastructure.html        # L3: Processors, storage, Office engines
    ├── l4_modules_internal.html      # L4: Module-internal file graphs & loop analysis
    └── svg/                          # High-resolution SVG vectors (GitHub Previewable)
```

---

<a id="github-preview-svg-maps"></a>
## 📊 Maps: Interactive (HTML) & GitHub Direct Preview (SVG)

GitHub 不支援直接渲染 HTML 檔案（只會顯示原始碼），但**可以直接預覽原生向量 SVG**！
在本地瀏覽時推薦開 HTML 享有搜尋、高亮與縮放；在 GitHub 網頁上請直接點選 **SVG 預覽**：

| Layer | Interactive Map (Local) | GitHub Preview (SVG) | Description |
|---|---|---|---|
| **Overview** | [`diagrams/overview.html`](diagrams/overview.html) | [🖼️ `clickra_layers.svg`](diagrams/svg/clickra_layers.svg) | All layers (L0 through L4) on one canvas |
| **L0 Entry Points** | [`diagrams/l0_entry_points.html`](diagrams/l0_entry_points.html) | [🖼️ `l0_entry_points.svg`](diagrams/svg/l0_entry_points.svg) | Shell context menu, CLI, Launcher, Fluent entry points |
| **L1 Presentation** | [`diagrams/l1_presentation.html`](diagrams/l1_presentation.html) | [🖼️ `l1_presentation.svg`](diagrams/svg/l1_presentation.svg) | Screen renderers and shared Presentation controllers |
| **L2 Application** | [`diagrams/l2_application.html`](diagrams/l2_application.html) | [🖼️ `l2_application.svg`](diagrams/svg/l2_application.svg) | Application services, 19 conversion use cases |
| **L3 Infrastructure** | [`diagrams/l3_infrastructure.html`](diagrams/l3_infrastructure.html) | [🖼️ `l3_infrastructure.svg`](diagrams/svg/l3_infrastructure.svg) | Processors, storage, Office engines, external deps |
| **L4 Modules Internal** | [`diagrams/l4_modules_internal.html`](diagrams/l4_modules_internal.html) | [🖼️ `l4_inside.svg`](diagrams/svg/l4_inside.svg) | File references under `src/`, circular groups (red) |

---

## 🖼️ GitHub Direct Preview (L0 Entry Points)

> [!TIP]
> 下方直接嵌入 L0 入口層架構圖。若需檢視包含 L0 至 L4 的完整超長總表（1556×22330），請點擊 [🖼️ `clickra_layers.svg`](diagrams/svg/clickra_layers.svg) 開啟。

[![Clickra L0 Entry Points Architecture](diagrams/svg/l0_entry_points.svg)](diagrams/svg/l0_entry_points.svg)

---

## 🔄 Regenerating Architecture Diagrams

Clickra diagrams are powered by Roslyn compiler semantic analysis (`scripts/architecture/SemanticGraph`). To regenerate both HTML and SVG maps:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\architecture\update_architecture_diagrams.ps1
```

**Workflow:**
1. Roslyn compiles the solution and produces `semantic-msbuild.json` containing exact symbol references.
2. `file_lines.py` recalculates line counts.
3. `generate_layers.py` renders all HTML viewers and SVGs under `diagrams/` and `diagrams/svg/`.
4. The generator validates that all types and files in L0–L3 match existing code (`names ok`).

### 產生器分工說明 (Automation vs Manual Design)

| 層級 (Layer) | 產生器自動推導 (Automated) | 人工設計與佈局 (Manual Design) |
|---|---|---|
| **L4** | 節點、檔案行數、呼叫／被呼叫引用關係、循環依賴群組 (SCC)、資料夾分組卡片 | 無需人工介入 (100% Roslyn 推導) |
| **L1, L2** | 卡片內檔案列表、連線存在性驗證、"also called by" 外部呼叫註記 | 新卡片定義：在 `generate_layers.py` 內的 `CARDS`、`MOD_ORDER`、`PANEL_INFO` 增加方塊並安排擺放位置 |
| **L3** | 各處理器、Office 引擎與儲存層之內部檔案列表 | 處理器方塊版面擺放 |
| **L0** | — (由入口門面手繪拓撲) | 手繪拓撲；下方 `names ok` 檢查仍涵蓋所有型別與檔案存在性 |
| **已知問題連線** | 程式碼修復解耦後，`problem_link` 自動消失 | — |

### 產生器自我檢查機制 (Checks printed by `generate_layers.py`)

執行 `update_architecture_diagrams.ps1` 時，產生器會進行以下靜態驗證：
- `names ok`：若 L0–L3 卡片上列出的任何型別或檔案在 `src/` 中已不存在或改名，會立即印出警示清單。
- **卡片覆蓋率**：若 `src/Clickra.Core/Presentation` 或 `Application` 中有任何新增檔案未被收錄至 `generate_layers.py` 內的 `CARDS`，會直接拋出錯誤中斷。
- **廢棄連線檢查**：若圖面上規劃的箭頭在實際程式碼中已無依賴關係，會主動提示移除。
