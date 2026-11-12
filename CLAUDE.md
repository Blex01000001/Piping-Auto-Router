# Piping Auto Router

化工配管自動佈線的個人研究專案：在簡化的 3D 幾何場景中，用 A* 產生
符合基本配管規則的管線佈局初稿。定位是研究與初稿輔助，不取代真實的
3D 設計軟體、應力分析或設計審查。

## 硬性規則
- 只使用自編的假資料。不得放入任何公司、客戶或真實專案資料。
- 規則數值（手輪高度、直管段倍數、最小淨距、權重等）不得寫死在程式中，
  一律放 JSON 設定檔；預設值必須標示為「範例」。
- **Engine 不得引用 UI**：不得引用 Blazor、ASP.NET、JS interop，也不得使用
  System.Drawing、System.Windows、Registry 等 Windows 專用 API。
  Engine 沒有任何 ProjectReference／PackageReference（JSON 用內建 System.Text.Json）。
- 路徑一律用 Path.Combine；程式必須能在 Linux 執行。
- 不使用 EF Core；場景與結果都是 JSON 檔。

## 專案結構與相依方向
- `src/PipingRouter.Engine`   幾何、規則、佈線（Geometry/ Scene/ Rules/ Routing/ Io/）
- `src/PipingRouter.Dashboard` Blazor Web App（Interactive Server）——**第二階段才建立**
- `tests/PipingRouter.Tests`  xUnit，只參考 Engine

相依：Dashboard → Engine，Tests → Engine。禁止反向，禁止 Engine 參考其他專案。
ArchitectureTests 會檢查這些約束。

## 設定檔與範例
- `config/rules.example.json`    規則數值（範例）
- `config/routing.example.json`  體素大小、權重、搜尋上限
- `samples/scenes/*.json`        假場景

## 約定
- 單位 mm，Z 向上，地面 Z = floorZ。管徑一律指外徑；淨距從保溫外表面算起。
- 圓柱 center 為底面圓心，只支援 X/Y/Z 軸向。
- 搜尋用體素格子，最終檢查用連續座標的精確公式。
- 剛好等於最小淨距算通過（以設定檔的 epsilon 處理浮點誤差）。
- 維修區／操作區：穿越為禁止（可由設定檔 zonePolicy 調整）。
- 最小直管長度：mm，只約束兩個彎頭之間（彎頭中心到中心）。
- 同一輸入必須得到相同結果；同成本平手只由 seed 決定。成本用整數（定點數）計算。
- 失敗要回傳明確的失敗結果與原因，不可丟例外或當機。
- 規則檢查嚴重度分 error / warning；垂直管上的閥目前為 NotSupported。

## 驗證（改完必跑，通過才算完成）
    dotnet build
    dotnet test
新增或修改幾何公式、佈線邏輯時，必須同步補測試（含相交、不相交、相切）。

## 環境備註
雲端容器需先 `apt-get update && apt-get install -y dotnet-sdk-8.0`（容器回收後要重裝）。

## 範圍
目前為第一階段：Engine + Tests。Dashboard、元件序列、多條管線整體搜尋是後續階段，
架構已預留位置，但尚未實作。
