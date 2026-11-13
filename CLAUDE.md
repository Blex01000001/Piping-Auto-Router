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
- 規則檢查嚴重度分 error / warning（可由設定檔 severities 調整）；垂直或傾斜管上的閥目前為 UnsupportedComponent（warning）。
- 規則檢查器（Rules/RuleChecker）使用連續座標與精確公式，與佈線無關，可檢查人工管線；
  每項輸出含 IsViolation／Location／Measured／Limit，並附各規則「最緊」那筆的通過紀錄。
  流向 = 折線第一點到最後一點；直管段必須是純直管（被彎頭、其他元件本體、管線端點截斷）；
  手輪淨空區不與該元件自己的管線比較。
- 體素化（Routing/Voxelizer，保守式）：格子方塊到障礙物的精確距離 < 管半徑＋保溫＋最小淨距 才標為佔用
  （剛好等於算自由）；因此「相鄰自由格中心連線」必定通過精確淨距檢查。格子原點在場景範圍最小角，
  節點取格子中心，中心在場景範圍外的格子標 OutsideBounds。格子過大回報 GridTooLarge，不丟例外。
  維修／操作區：策略 Forbid → 標為不可通過，預設膨脹管半徑（ZoneInflation.PipeRadius），
  使佈線結果與規則檢查器的 ServiceZoneIntrusion 一致；改成 None 則區域不膨脹，管半徑 > 半格時管表面可能侵入區域；
  策略 Penalize → 只標 Penalized，供成本函數使用。
- 軸向 A*（Routing/AStarRouter）：狀態 = (格子, 方向, 連續直管格數 run∈[1,N])；起點第一步必須沿起點接口方向，抵達終點必須沿終點接口方向的反向
  （Direction = 接口外向法線 = 管線離開接口時行進的方向）；禁止 180 度回頭；起點格與終點格是端點專用（不可回到起點、
  終點格只能以抵達方向進入一次）。成本是整數定點（×1000）：長度權重 × 每步毫米 + 彎頭權重 × 彎頭 + 區域懲罰 × 進入 Penalized 格。
  啟發函數 = 曼哈頓距離 × 每步成本。折線一律取格子中心（保證全為軸向、共線點合併），接口原座標與吸附誤差記錄在 RouteResult.Snap。
  最小直管長度（RouteConstraints，來自規則設定）：N = max(1, ceil(最小直管mm / 格子mm))（向上取整，保守）；
  轉彎只在 run ≥ N 時允許，只約束「兩個彎頭之間」——起點接口段（初始 run = N）與抵達終點的最後一段不受限。
  狀態數 = 格子 × 6 × N，超過 int 範圍回報 GridTooLarge。
  種子只影響「同一狀態有多個等成本前驅」時留哪一個（備選方案成本相同）。(格子,方向) 狀態無法禁止自交，
  發生時以 RouteResult.Warnings 的 SelfOverlap 標示。找不到路徑回傳 NoPath 與 Failure.Code（StartBlocked／GoalBlocked／
  StartOutOfBounds／GoalOutOfBounds／NoPathExists／SearchLimitExceeded／NotSupported／InvalidInput／GridTooLarge…）。
- 失敗診斷（RoutingConfig.DiagnoseOnFailure）：NoPathExists／StartBlocked／GoalBlocked 時，把條件各自單獨放寬、重跑一次搜尋，
  結果放在 RouteFailure.Diagnosis：MinStraight（最小直管 = 0）、ArrivalDirection（不要求抵達方向，AStarRouter 內以同一個格子重跑）、
  Clearance（障礙物淨距 = 0，需重新體素化，由 LineRouter 補上）、AllRelaxed（全部放寬，用來區分「需要多項一起調整」與
  「幾何上被阻隔」）。本來就已放寬的條件不列出。Summary 只在證據充分時下結論：AllRelaxed 以 NoPathExists 失敗才說「幾何上被阻隔」，
  達到搜尋上限者標示「無法判定」。SearchLimitExceeded 不做診斷。
  注意：保守式體素化可能比精確幾何多擋（例如縫隙差 1 mm），這種假性無解會由 Clearance 放寬後有解顯示出來（s12b）。
- 佈線結果的 JSON（Io/RouteResultJson，讀寫皆有）：欄位順序固定、數字用最短可還原格式、不含任何耗時，
  所以同一輸入兩次佈線的輸出逐位元相同；成本是 ×1000 定點整數。Dashboard 讀的就是這個格式。
  確定性測試涵蓋：每個樣本場景連跑兩次、與執行順序無關、多執行緒平行、golden 檔（tests/PipingRouter.Tests/Golden/*.route.json）。
  有意改變輸出格式或演算法結果時，用 `UPDATE_GOLDEN=1 dotnet test --filter Golden_` 重新產生，**務必檢視 git diff 再提交**。
- 備選方案（LineRouter.RouteAlternatives）：換種子（config 種子 + 1、+ 2…）重跑並依路徑去重；每條成本相同、都合法；
  路徑只有一種時會少於要求的條數。
- 多條管線（Routing/RoutePlanner，預留給 MCTS／遺傳演算法／模擬退火）：依「佈線順序」逐條佈線；每條管線從乾淨的基底格子
  Clone 出來，蓋上前面「成功」的管線（VoxelGrid.StampCapsule，邊距 = 兩管半徑和 + 管間最小淨距；旗標 OtherPipe），
  再交給 IRouter。PlanEvaluation.Score = 成功管線成本總和 + 失敗條數 × 失敗懲罰（設定檔 planning.failurePenalty，範例值 1,000,000，
  ×1000 定點）——懲罰要大到「少失敗」永遠優先於「少成本」，太小時失敗一條反而比繞路便宜（有測試示範）。
  順序由 IRouteOrderStrategy.Choose(lineCount, evaluate) 決定（內建：GivenOrder／FixedOrder／ExhaustiveOrder；
  新策略只要實作這個介面，Router 與 Planner 都不用改）。Evaluate 對同一順序有快取；Results[i] 對應 scene.Lines[i]。
  Planner 不是執行緒安全的。第一條佈線的管線不受影響，後面的管線成本只會不減（只多了限制）。
- 搜尋狀態編碼（Routing/StateLayout）：(格子, 方向, run, 元件序列索引 comp)；第一階段 comp 恆為 0、ComponentCount = 1。
  方向表（Routing/DirectionSet）：目前只有 Axis6；45 度日後新增方向集合並擴充 BendClass 即可，搜尋主體不用改。
- 形狀重疊（Geometry/ShapeOverlap）的語意：兩形狀各內縮 eps 後做閉集合相交，貼合不算重疊。

## 驗證（改完必跑，通過才算完成）
    dotnet build
    dotnet test
新增或修改幾何公式、佈線邏輯時，必須同步補測試（含相交、不相交、相切）。

## 環境備註
雲端容器需先 `apt-get update && apt-get install -y dotnet-sdk-8.0`（容器回收後要重裝）。

## 範圍
目前為第一階段（進行到第 10 步：軸向 A* 含彎頭成本、最小直管長度、失敗診斷、確定性與結果 JSON、多條管線順序評估介面，尚未含元件序列）：Engine + Tests。Dashboard、元件序列、多條管線整體搜尋是後續階段，
架構已預留位置，但尚未實作。
