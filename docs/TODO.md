# 目前狀態（待辦／已知問題／想法）

這份檔案記錄專案「現在」的待辦、已知問題與想法，讓新的 session 一開始就能接手；歷史請看 `git log`。
待辦標記：【直接做】使用者點名就能動手；【先討論】會改架構、輸出格式、引入新工具或方向未定，先提出分析與建議；
【使用者】需要使用者自己操作。維護規則寫在 `CLAUDE.md` 的「TODO 維護規則」。

## 待辦

1. 【使用者】在本機 Windows + Visual Studio 開啟 `PipingAutoRouter.sln`，跑一次 `dotnet build`、`dotnet test`。目前只在 Linux 容器驗證過；要特別看 golden 比對（`tests/PipingRouter.Tests/Routing/GoldenFiles.cs` 已把 `\r\n` 轉成 `\n`）與 `RepoPaths` 找 repo 根目錄。
2. 【使用者】把雲端環境的安裝步驟存成 setup script：`apt-get update && apt-get install -y dotnet-sdk-8.0`（容器回收後要重裝）。這是環境設定，要在 claude.ai 的環境設定裡改，或由使用者同意 Claude 用環境工具提出變更。
3. 【使用者】Dashboard（第二階段，使用者表示自己在本機處理）：新增 `src/PipingRouter.Dashboard`（Blazor Web App、Interactive Server，Three.js 用 JS interop）並加入 sln。注意 `tests/PipingRouter.Tests/ArchitectureTests.cs` 的 `Solution_DoesNotYetContainDashboard` 會失敗，要改成「sln 含 Dashboard，且 Dashboard 只參考 Engine」；資料直接讀 `samples/scenes/` 與 `samples/results/`（格式見 `Io/RouteResultJson.cs`、`Io/PlanJson.cs`、`Io/RuleReportJson.cs`）。
4. 【先討論】元件序列佈線（第二階段核心，例如逆止閥 → 手閥 → 流量計 → 控制閥 → 手閥）：移除 `AStarRouter.RouteCore` 開頭的 `NotSupported`，`StateLayout` 的 `ComponentCount` 改成「元件數 + 1」並在放置元件時遞增 comp；難點是放置規則（手輪高度、手輪淨空、前後直管段）如何變成搜尋中的可行性判斷與成本，以及放置結果怎麼輸出成 `PlacedComponent`。動手前先寫規劃。
5. 【先討論】失敗診斷加第五項「維修區改成只提醒」：在 `LineRouter.Route` 的診斷區塊仿照 Clearance，用 `rules with { ServiceZonePolicy = Penalize }` 重建格子重跑；只在場景有維修／操作區時列出。會改變診斷輸出（`failure.diagnosis.relaxations` 多一項），所以先問；要同步改 `CLAUDE.md` 的診斷說明與 `AStarDiagnosisTests` 的 `Ids(...)` 期望值。
6. 【先討論】規則檢查器加「同一條管線非相鄰線段」的干涉／淨距（目前 `RuleChecker.PipePairs` 只比不同管線，自交只靠 `RouteResult.Warnings` 的 SelfOverlap）：會讓既有測試裡刻意折返或階梯狀的人工管線（例如 `BendRuleTests.FoldBack180Degrees_CountsAsBend`、`Staircase_ReportsEachShortRun`）多出違規，也會改變 `s15` 的 golden；要先決定相鄰幾段以內不比、是否只報干涉不報淨距。
7. 【直接做】啟發函數加「還需要的最少彎頭數」下界（`AStarRouter.RouteCore` 的 `H`）：必須保持可採納且一致；以 `RouteTestKit.ReferenceOptimum` 的隨機對照測試（`AStarOracleTests`）確認成本不變。`samples/results/*.json` 的 `search.expanded` 會變，用 `UPDATE_GOLDEN=1 dotnet test --filter Golden_` 重產並確認 diff 只有 `expanded`。目標：`s03` 展開數（目前約 86 萬）明顯下降。
8. 【直接做】升級測試套件以消除 `VSTEST_ARTIFACTS_DIRECTORY` 提示：`tests/PipingRouter.Tests/PipingRouter.Tests.csproj` 的 `xunit` 2.4.2、`xunit.runner.visualstudio` 2.4.5 升到目前穩定版。注意新版 xUnit analyzers 可能多出規則，`TreatWarningsAsErrors` 會讓建置失敗，需逐條修正測試寫法。
9. 【直接做】補 `README.md`：一段專案定位（研究與初稿輔助，只用假資料）、建置與測試指令、資料夾說明（可直接引用 CLAUDE.md 的檔案地圖），不要重複 CLAUDE.md 的細節。
10. 【先討論】順序搜尋策略：實作 `IRouteOrderStrategy`（模擬退火、遺傳演算法或 MCTS），放在 `src/PipingRouter.Engine/Routing/`；隨機性必須完全由種子決定，Engine 內用可重現的雜湊（例如 `AStarRouter.Tie` 的 splitmix64），不要依賴 `System.Random` 的實作細節；需要能比 `ExhaustiveOrderStrategy` 更多管線的測試場景。
11. 【先討論】45 度彎頭：`DirectionSet` 新增方向集合、`BendClass` 區分 45／90 度、`RoutingConfigJson` 目前把 `"axis6+diag45"` 回報為 `NotSupported`。注意保守式體素化的保證（「相鄰自由格中心連線一定通過精確檢查」）只對軸向一步成立，斜向一步的連線會經過其他格子，需要重新證明或另外檢查。

## 已知問題／限制

- 路徑可能自己和自己重疊：（格子, 方向, run）狀態的 A* 無法禁止自交；起點格／終點格已設為端點專用，其餘以 `RouteResult.Warnings` 的 `SelfOverlap` 標示（成功但實體上無效）。
- 保守式體素化會多擋：縫隙比精確幾何需要的寬度窄約 1 格以內時可能被判為無解（`s12b`：縫隙 399 mm 精確上可通過）；失敗診斷會顯示「放寬障礙物淨距後有解」。
- 折線端點是格子中心，不是接口原座標；兩者最多差半格的 √3 倍（格子 100 mm 時約 87 mm），只記在 `RouteResult.Snap`。規則檢查也用格心折線。
- 最小直管格數向上取整：格子不整除最小直管時會比需要的多繞（例如格子 130、最小直管 300 → 390）。
- 失敗診斷不涵蓋「維修區禁止」造成的無解（見待辦 5），也不在 `RoutePlanner` 內做需要重新體素化的淨距放寬。
- 搜尋效能：曼哈頓啟發函數在繞牆時很弱（`s03` 約 86 萬狀態、1.2 秒）；狀態數 = 格子 × 6 × N，最小直管大、格子小時會很慢或回報 `GridTooLarge`。
- 規則檢查的近似與限制：穿透時的 `measured` 只是「中心線距離 − 半徑」的估計；`Collision` 的位置落在距離為 0 區段的邊緣而非最深處；`HandwheelClearance` 的量測值是侵入物件數；垂直或傾斜管上的閥只回報 `UnsupportedComponent`；手輪偏移是世界座標（只支援朝上）。
- 規則檢查器不比同一條管線的非相鄰線段（見待辦 6）。
- `RouteAndCheck`／`RoutePlanner` 都不把 `scene.Pipes`（人工管線）當障礙，也不納入報告。
- 場景的 `componentSequence` 只檢查非空字串，沒有和規則設定的元件型錄交叉驗證。
- 管線是否跑出場景範圍只看節點中心，不看管半徑。
- `RoutePlanner` 不是執行緒安全；失敗懲罰設太小時，失敗一條會比繞路便宜（有測試示範）。
- 線段對圓柱的距離是三分搜尋（誤差約 1e-12 mm）；規則報告 JSON 已四捨五入到 1e-6 mm，記憶體內的值沒有。
- `dotnet test` 約 30 秒（參考 Dijkstra 對照與「每個場景跑兩次」的確定性測試佔大部分）。
- 只在 Linux 驗證過（見待辦 1）。

## 想法

- 接口連接選項：折線端點改接到接口原座標（容許非軸向的接口段，或用額外的軸向小段接上），規則檢查要跟著放寬軸向判斷。
- 命令列工具：讀場景與設定、輸出 `samples/results` 格式的檔案（目前只能靠測試產生）；會新增專案，屬於【先討論】。
- 偏好管架高度或區分垂直方向成本的權重（第一階段使用者決定垂直與水平同權）。
- 多條管線同時搜尋（協同 A*），取代逐條蓋章。
- 平行評估多個佈線順序（每個執行緒一個 `RoutePlanner`）。
- 規則報告依規則／嚴重度匯出 CSV，供設計審查清單使用。
- 支援非軸向圓柱或一般方向的方塊（OBB）。
