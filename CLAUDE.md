# Piping Auto Router

化工配管自動佈線的個人研究專案：在簡化的 3D 幾何場景中，用 A* 產生
符合基本配管規則的管線佈局初稿。定位是研究與初稿輔助，不取代真實的
3D 設計軟體、應力分析或設計審查。

**開始新工作前先讀 `docs/TODO.md`**（目前的待辦、已知問題與想法；維護規則見本檔「TODO 維護規則」）。

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

## 檔案地圖（先看這裡決定要讀哪個檔案）
依任務從這張表找到要讀的檔案，只讀相關檔案，不要整個 repo 掃過。表上沒有的檔案通常不需要動；新增或搬移檔案時同步更新這張表。

| 檔案／資料夾 | 用途 |
|---|---|
| `docs/TODO.md` | 目前狀態（待辦／已知問題／想法）；每次 commit 時同步更新 |
| `config/rules.example.json` | 規則數值（範例）：淨距、最小直管、維修區策略、嚴重度、元件型錄 |
| `config/routing.example.json` | 體素大小、成本權重、搜尋上限與種子、失敗懲罰、是否診斷 |
| `samples/scenes/sNN_*.json` | 假場景；檔名前綴 sNN 被測試用來挑選／排除場景 |
| `samples/results/*.json` | 範例輸出（給 Dashboard 開發直接讀取，同時是 golden 檔）：`*.route.json` 單條佈線結果、`*.plan.json`／`*.plan-failing.json` 多條管線整體結果、`*.rules.json` 人工管線的規則報告 |
| `src/PipingRouter.Engine/Geometry/` | 向量、方塊、軸向圓柱、線段、膠囊體；`SegmentAabb`／`SegmentCylinder`／`SegmentSegment` 距離與相交；`ShapeDistance`（體素化用）、`ShapeOverlap`（手輪淨空用） |
| `src/PipingRouter.Engine/Scene/SceneModel.cs` | 場景模型：障礙物、維修區、管線需求（`LineRequest`）、人工管線（`PipeDef`） |
| `src/PipingRouter.Engine/Rules/RuleChecker.cs` | 精確規則檢查（干涉、淨距、維修區、彎頭間直管、元件直管段、手輪）；`RuleIds.cs` 規則 id；`RuleConfig.cs` 規則設定模型；`PolylineInfo.cs` 折線沿程參數化 |
| `src/PipingRouter.Engine/Routing/Voxelizer.cs`、`VoxelGrid.cs` | 保守式體素化、格子（旗標、吸附、Clone、StampCapsule） |
| `src/PipingRouter.Engine/Routing/AStarRouter.cs` | 單條管線軸向 A*（成本、最小直管、接口方向、種子、同格子的失敗診斷） |
| `src/PipingRouter.Engine/Routing/LineRouter.cs` | 一條管線的完整流程（建格子 → A* → 需重新體素化的診斷）、`RouteAndCheck`、`RouteAlternatives` |
| `src/PipingRouter.Engine/Routing/RoutePlanner.cs`、`RoutePlan.cs`、`IRouteOrderStrategy.cs` | 多條管線依序佈線、整體評估（Score）、順序策略 |
| `src/PipingRouter.Engine/Routing/RouteResult.cs`、`RouteConstraints.cs`、`RoutingConfig.cs` | 佈線結果／失敗／診斷模型、規則約束、佈線設定模型 |
| `src/PipingRouter.Engine/Routing/StateLayout.cs`、`DirectionSet.cs`、`StateStore.cs` | 搜尋狀態編碼（含預留的元件序列維度）、方向表、狀態儲存 |
| `src/PipingRouter.Engine/Io/` | 所有 JSON 讀寫：`JsonObj`（讀，帶路徑收集錯誤）、`JsonOut`（寫，確定的格式）、各模型的 `*Json.cs` |
| `tests/PipingRouter.Tests/Rules/RuleTestKit.cs`、`Routing/RouteTestKit.cs` | 測試共用工具：範例設定、場景建構、`AssertValid`、參考 Dijkstra（`ReferenceOptimum`） |
| `tests/PipingRouter.Tests/Routing/GoldenFiles.cs` | golden 比對與 `UPDATE_GOLDEN=1` 重新產生 |
| `tests/PipingRouter.Tests/ArchitectureTests.cs` | 相依方向與「sln 還沒有 Dashboard」的檢查 |

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
  確定性測試涵蓋：每個樣本場景連跑兩次、與執行順序無關、多執行緒平行、golden 檔（samples/results/*.json）。
  有意改變輸出格式或演算法結果時，用 `UPDATE_GOLDEN=1 dotnet test --filter Golden_` 重新產生，**務必檢視 git diff 再提交**。
  結果可內嵌規則報告：`ruleReport`（選填；LineRouter.RouteAndCheck 或 RouteResult.WithRuleReport 產生，失敗的結果沒有）。
  只檢查這一條管線對障礙物／維修區／最小直管，不含 scene.Pipes（佈線也沒把它們當障礙）。
  規則報告 JSON（Io/RuleReportJson，讀寫皆有）完整保留每筆檢查結果的所有欄位並附 errorCount／warningCount；
  量測值、限值、位置輸出時四捨五入到 1e-6 mm（避免精確公式的 1e-12 雜訊進檔案），輸出解析後再輸出逐位元相同。
  多條管線整體結果的 JSON（Io/PlanJson）：order／failedCount／routeCost／penaltyCost／score／results（依場景順序，內嵌佈線結果）／
  ruleReport（RoutePlanner.AttachRuleReport 產生，含管對管檢查）。讀入時檢查一致性（計數、成本總和、score、order 為排列）。
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

## 常見任務怎麼改
常做的修改照這裡的步驟走，不用重新調查。每項格式：任務 → 改哪些檔案／函式 → 注意事項 → 怎麼驗證。做完一種新類型的修改，若之後可能重複，就補一條進來。

- **新增假場景** → `samples/scenes/sNN_描述.json`（`isSynthetic: true`，`floorZ` 要在 bounds 內） → `SampleFilesTests` 要求零警告；`DeterminismTests`／`RouteAndCheckTests`／`SeedAndAlternativesTests` 會依檔名自動納入，很重的場景要在這些測試裡用前綴排除或改粗格子（參考 s09、s14 的處理） → 期望值先手算寫在測試註解，再跑 `dotnet test`。
- **新增規則** → `Rules/RuleIds.cs`（常數、`All`、`DefaultSeverity`）、`Rules/RuleChecker.cs`（新方法並在 `Execute` 呼叫，違規與「最緊的通過紀錄」都要輸出）、`config/rules.example.json` 的 `severities` → 剛好等於限值算通過、比較用 `_eps` → 在 `tests/PipingRouter.Tests/Rules/` 補邊界測試（剛好等於、差一點）；若改到 `s15` 的報告，重產 golden 並檢視 diff。
- **新增設定欄位** → 模型（`Rules/RuleConfig.cs` 或 `Routing/RoutingConfig.cs`）、對應的 `Io/*ConfigJson.cs` 的 `Read` 與 `Serialize`、`config/*.example.json` → 選填欄位要有預設值、數值要驗證範圍、範例值標示為範例 → 補「範例檔可讀」「來回一致」「缺省用預設」「超出範圍回報錯誤（含路徑）」的測試。
- **改佈線演算法或成本** → `Routing/AStarRouter.cs` 的 `RouteCore`；同時更新測試的 `RouteTestKit.ReferenceOptimum`（照規格獨立重寫，不要呼叫 A* 的程式） → 保持確定性（不可依賴雜湊表列舉順序、不可用時間） → `AStarOracleTests` 必須一致；golden 若變動，用 `UPDATE_GOLDEN=1 dotnet test --filter Golden_` 重產並逐檔確認 diff 合理。
- **改輸出 JSON 格式** → 先問使用者；`Io/*Json.cs` 的 `ToObj`／`Read`（兩邊一起改）→ 新欄位用選填、`schemaVersion` 不變以相容舊檔；欄位順序固定 → 補來回一致與舊檔可讀的測試，重產 golden（`samples/results/` 也是 Dashboard 的範例輸入）。
- **新增失敗診斷的放寬項** → 同一格子可做的放在 `AStarRouter.Diagnose`；要重新體素化的放在 `LineRouter.Route` 的診斷區塊 → 名稱要能讓 `RouteDiagnosis.Summarize` 組出句子；本來就已放寬的條件不要列 → 更新 `AStarDiagnosisTests` 的 `Ids(...)` 期望值與本檔的診斷說明。
- **新增順序策略** → 實作 `IRouteOrderStrategy.Choose`（新檔放 `Routing/`） → 隨機性只能來自種子；不要修改 `RoutePlanner` 或 `IRouter` → 仿照 `RoutePlannerTests` 的自訂策略測試，並與 `ExhaustiveOrderStrategy` 比較 Score。
- **重產 golden／範例輸出** → `UPDATE_GOLDEN=1 dotnet test --filter Golden_` → golden 是被測程式自己產生的，**一定要人工檢查內容**（數值與手算一致、diff 只有預期的變化）再提交 → 不帶旗標再跑一次 `dotnet test`。

## 架構決策（不要改回去）
這些是已經討論過、刻意選擇的設計。不要因為「看起來可以更好」就改回舊做法；真的認為該推翻，先提出來討論（標【先討論】），不要直接改。每項格式：決策 — 原因 — 不要做什麼。新的架構決定在 commit 時補進來。

- 格子只用來搜尋，最終一律用連續座標的精確公式檢查 — 格子有量化誤差，結果要能被獨立驗證 — 不要用格子結果直接當成合規判斷。
- 保守式體素化（格子「方塊」到障礙物的精確距離、圓角膨脹）— 保證相鄰自由格中心連線必定通過精確淨距檢查 — 不要改成「格心取樣」或把方塊直接外擴。
- 維修區預設 Forbid 並膨脹管半徑（`ZoneInflation.PipeRadius`）— 使用者決定，佈線結果才會與規則檢查器一致 — 不要把預設改回 `None`。
- 折線一律取格子中心，接口原座標只記在 `RouteResult.Snap` — 保證全為軸向（照字面把接口原座標放進折線會產生非軸向段）— 不要把接口原座標塞進 `Polyline`。
- 最小直管長度向上取整成格數，只約束兩個彎頭之間 — 使用者確認；向上取整保證檢查器必然通過 — 不要約束起點接口段與最後一段，也不要改成四捨五入。
- 接口 `direction` = 接口外向法線（管線離開接口時行進的方向）— 起訖兩端用同一種語意 — 不要把終點的方向解讀成「抵達方向」。
- 成本用整數定點（×1000，`CostUnits.Scale`）— 平手判斷與確定性 — 不要用 double 比較或累加成本。
- 錯誤以結果回傳（`LoadResult`、`RouteFailure`），不丟例外 — 使用者要求「無解要有原因、不可當機」— 例外只用於程式員誤用（例如無效的佈線順序、窮舉策略超過上限）。
- 種子只決定「同一狀態有多個等成本前驅時留哪一個」— 這樣備選方案保證等成本；在優先佇列鍵裡加雜湊試過無效（f 值本來就不同）— 不要改回在排序鍵裡加雜湊。
- 失敗診斷每項單獨放寬，另有 `AllRelaxed`；只在證據充分時下結論 — 避免誤導 — 不要在 `AllRelaxed` 達到搜尋上限時宣稱「幾何上被阻隔」。
- 多條管線：每條從乾淨的基底格子 Clone 後蓋上已成功的管線；Score 含大的失敗懲罰 — 順序評估之間不能互相污染、「少失敗」優先 — 不要直接在快取的基底格子上蓋章。
- JSON 由 `JsonObj`／`JsonOut` 手寫讀寫，不用 `JsonSerializer` 序列化模型 — 錯誤要帶路徑一次回報、輸出要逐位元確定 — 不要改成反射式序列化。
- golden 檔放在 `samples/results/`，同時是 Dashboard 的範例輸入 — 避免兩份幾乎一樣的檔案各自漂移 — 不要另外再建一個 golden 資料夾。
- 規則報告 JSON 的數值四捨五入到 1e-6 mm — 報告給人看，不讓 1e-12 的數值雜訊進檔案 — 不要在 `RuleChecker` 本身做四捨五入（判斷要用原值）。
- 搜尋狀態一律經 `StateLayout` 編碼、方向一律經 `DirectionSet` — 元件序列與 45 度只需擴充這兩處 — 不要在 `AStarRouter` 寫死方向數或狀態公式。
- 測試的參考解（`ReferenceOptimum`）照規格獨立實作 — 才能抓到 A* 的錯 — 不要讓它呼叫 `AStarRouter` 的程式碼。

## 設計規範
寫新程式時要遵守、但不容易從現有程式碼一眼看出的慣例。

- 確定性：Engine 內不用 `System.Random`、時間或 GUID；需要「隨機」時用由種子決定的雜湊（`AStarRouter.Tie` 的 splitmix64）。輸出的集合要有固定順序（`SortedDictionary`、`OrderBy(..., StringComparer.Ordinal)`），不要依賴 `Dictionary` 的列舉順序。
- 字串比較一律 `StringComparison.Ordinal`／`StringComparer.Ordinal`（`InvariantGlobalization` 已開啟），數字輸出一律 `CultureInfo.InvariantCulture`。
- 浮點比較：與限值比較時用設定檔的 `EpsilonMm`（剛好等於算通過）；把 mm 換成格數時用 `Math.Ceiling(x - 1e-9)` 之類的寫法擋掉浮點誤差（`0.3 / 0.1 = 3.0000000000000004`）。
- 使用者看得到的訊息、程式註解用繁體中文；識別字、檔名、commit 訊息用英文。
- 新的讀入檔案格式要：未知欄位回報 warning、錯誤帶 JSON 路徑、一次回報所有問題、沒有 error 才有 `Value`。
- 測試：期望值盡量手算並把算式寫在註解；幾何公式要有相交、不相交、剛好相切；改完重要邏輯可以做「變異檢查」（暫時改壞程式、確認測試變紅、再還原）。
- `TreatWarningsAsErrors` 開著：xUnit analyzers 會擋 `Assert.Equal(實際, 常數)` 的參數順序；做變異檢查時避免寫出「不可達程式碼」或「自己比自己」（會變成編譯錯誤而不是測試失敗）。
- 測試找 repo 內檔案一律用 `RepoPaths.Combine(...)`（從輸出目錄往上找 sln），不要用相對路徑。

## 容易踩的雷
看起來像錯誤或可以簡化，但其實是刻意的寫法，或改了會壞掉其他地方的地方。修改相關檔案前先看這節。每項格式：位置 — 現象 — 為什麼不能動／該怎麼處理。

- `Voxelizer.Mark`、`VoxelGrid.StampCapsule` 的 `< margin` — 看起來該用 `<=` — 「剛好等於淨距算通過」，改成 `<=` 會讓 `s11`（縫隙剛好在下限）變無解。
- `Voxelizer.Build` 的 `Math.Max(..., eps)` 邊距 — 看起來多餘 — 邊距為 0 時 `distance < 0` 永遠不成立，會變成什麼都不擋。
- `AStarRouter.RouteCore` 的「起點格不可再進入、終點格只能以抵達方向進入」— 看起來多餘 — 拿掉會出現穿過終點再回頭的自交路徑。
- `AStarRouter.RouteCore` 的「禁止 180 度回頭」與彈出時再檢查抵達方向 — 變異檢查顯示拿掉不影響結果（等價變異）— 是防禦性寫法，不要刪。
- `RouteConstraints.MinStraightCells`、`Voxelizer.Dim` 的 `- 1e-9` — 看起來像魔術數字 — 擋浮點誤差，拿掉會讓 0.3／0.1 變成 4 格。
- `RuleReportJson` 的四捨五入 — 記憶體中的 `RuleCheckResult` 沒有四捨五入 — 比較序列化前後的數值要用容差；不要把四捨五入搬進 `RuleChecker`。
- `RouteResultJson`／`PlanJson`／`RuleReportJson` 的欄位順序與明確的 `null` — 看起來可以省略 — golden 檔逐位元比對，Dashboard 也依賴固定的形狀；改了要重產 golden 並先問使用者。
- `samples/scenes/` 的數字（例如 `s08b` 的 Y 方向只有 4 格寬、`s11` 的縫隙剛好 400、`s13` 的塔槽軸心在 y=2050）— 看起來很隨意 — 都是為了讓測試的手算期望值成立，改了會讓多個測試失敗。
- `SampleFilesTests` — 每個場景檔必須「零警告」— 新場景的 `floorZ` 要在 bounds 的 Z 區間內、形狀不可超出 bounds。
- `DeterminismTests`、`RouteAndCheckTests`、`SeedAndAlternativesTests` 依檔名前綴排除 `s09`／`s15`、`s14` 改用 200 mm 格子 — 是為了控制測試時間 — 新增很重的場景時照同樣方式處理。
- `RoutePlanner.RouteOne` 的 `built.Grid.Clone()` — 看起來可以省掉 — 不 Clone 會把蓋章留在快取的基底格子上，污染之後的順序評估。
- `PlanEvaluation.Results[i]` 對應 `scene.Lines[i]`，不是佈線順序 — 讀 `Order` 才知道佈線順序。
- `EngineAssembly.cs` 的 `InternalsVisibleTo("PipingRouter.Tests")` — 測試直接測 internal 的 `StateLayout` — 不要刪；它只是組件屬性，不違反「Engine 沒有專案參考」。

## 驗證（改完必跑，通過才算完成）
    dotnet build
    dotnet test
新增或修改幾何公式、佈線邏輯時，必須同步補測試（含相交、不相交、相切）。

## 環境備註
雲端容器需先 `apt-get update && apt-get install -y dotnet-sdk-8.0`（容器回收後要重裝）。

## 範圍
第一階段（Engine + Tests）已完成：幾何、場景／設定 JSON、精確規則檢查、保守式體素化、單條管線軸向 A*
（彎頭成本、最小直管長度、失敗診斷、確定性與種子）、結果／規則報告／整體計畫 JSON、多條管線順序評估介面。
後續階段（架構已預留位置，尚未實作）：元件序列（逆止閥 → 手閥 → 流量計…，StateLayout 的 comp 維度）、
Dashboard 與 3D 檢視器（Three.js）、MCTS／遺傳演算法／模擬退火的順序策略、45 度彎頭（DirectionSet）。
具體的待辦與已知問題見 `docs/TODO.md`。

## 使用者的工作習慣
- 回覆用繁體中文；commit 訊息用英文。
- 訊息常分「待處理事項」（直接改）與「待討論事項」（只分析、給建議，不改程式）。
- 改輸出格式（`Io/` 下的結果／規則報告／整體計畫 JSON 欄位，以及 `samples/results/` 的 golden）、引入建置工具或新套件、改架構前先問。
- 習慣先看規劃再動手：較大的工作先寫規劃與需要確認的設計問題，使用者同意（常回「照你的建議」）後才實作；設計問題要給出建議選項。

## Git 規則
- 在 session 指定的開發分支上開發（目前是 `claude/magical-lamport-1nahxu`）。它的 PR 合併後，新工作先從最新 `main` 重開。
- 使用者說「合併到 main」時：用 GitHub MCP 工具建立 PR 再 merge（`expectedHeadSha` 用完整 40 字元 SHA）。
- 每完成一個獨立的修改就 commit。commit 訊息第一行簡述改了什麼，需要時空一行補充原因。
- 需要了解過去的修改時，用 `git log` 查詢。

## TODO 維護規則（`docs/TODO.md`）
- 每次 commit 時一併更新：做完的刪掉、新發現的加進去、不適用的刪掉或改寫。
- 只記目前狀態，不寫完成紀錄或歷史（歷史看 `git log`）；每項一行為主，保持精簡。
- 使用者說「做待辦 N」時以這份檔案的編號為準；整理時可重新編號。

## Commit 後的回報規則
每次 commit 並 push 後，回覆必須包含以下三段，內容要和 `docs/TODO.md` 一致：
1. **已改善項目／問題**：這次 commit 完成了什麼。
2. **尚待改善項目／問題、已知問題**：還沒做完的、發現但未處理的問題、已知限制。
3. **建議改善方向或功能**：之後可以考慮的改進或新功能。
