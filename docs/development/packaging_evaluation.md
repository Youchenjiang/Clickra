# 發布與套件化評估 (Distribution & Packaging Evaluation)

> 狀態：待決策（僅記錄疑慮與評估考量，尚未實作任何變更）
> 來源：2026/08 討論「是否有需要把 Clickra 發成 package」

## 背景

Clickra 目前以 **MSIX 套件** 透過 **Microsoft Store** 正式發行，CI/CD 已整合自動上架流程（自 v3.6.3.0 起）。本文件記錄「是否還需要以其他形式發成 package」的評估考量。

## 結論摘要

產品本身已經是「已打包、已上架」的狀態；額外發行 package 的價值取決於目標：

| 形式 | 評估 | 結論 |
| :--- | :--- | :--- |
| MSIX / Store | 已存在，即產品的正式 package | 無需再做 |
| NuGet 函式庫（Clickra.Core） | 僅在出現明確外部消費者時才有意義 | 待評估（見下） |
| winget / choco / scoop | 補充管道，非必要 | 低優先 |

## 一、NuGet 函式庫（Clickra.Core）— 疑慮清單

若要把 `Clickra.Core` 發成 NuGet 函式庫供外部 .NET 專案重用，需要先解決以下疑慮：

1. **消費面窄**：專案為 `net10.0-windows` 且依賴 Win32/GDI+，可用場景有限。
2. **產品定位衝突**：Clickra 的核心價值是「零延遲的原生二進位」（NativeAOT），不是可重用的 DLL；函式庫化反而稀釋此賣點。
3. **AOT/trim 相容性是長期包袱**：CLI 已為 PdfPig 手動維護 `TrimmerRootAssembly` 清單；函式庫化後需對所有消費端承擔同樣的 trim 相容義務，維護成本會被放大。
4. **公開 API 面未定義**：目前對外僅有 `FileProcessor` facade，尚未以函式庫消費者的角度設計穩定 API 契約。
5. **版本同步與維護義務**：需制定函式庫版本與產品 4 碼版本（`X.Y.Z.0`）的同步策略，並承擔持續維護的責任。

## 二、winget / choco / scoop 等替代發布管道

- 可作為非 Store 使用者的補充管道（例如 winget 直接指向 GitHub Release 的 MSIX）。
- 屬於額外維護成本，且 Store 已提供自動更新，邊際效益有限。

## 決策門檻

僅當出現 **「外部 .NET 專案重用核心邏輯」的明確需求** 時，才啟動 NuGet 函式庫化評估；否則維持 **MSIX + Store 為唯一正式發布形式**。
