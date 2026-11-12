using PipingRouter.Engine;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Routing;
using PipingRouter.Engine.Rules;
using PipingRouter.Engine.Scene;
using Xunit;
using static PipingRouter.Tests.Rules.RuleTestKit;
using static PipingRouter.Tests.Routing.RouteTestKit;

namespace PipingRouter.Tests.Routing;

/// <summary>失敗時放寬條件重跑，回答「是什麼造成無解」。每項各自單獨放寬，另有「全部放寬」做彙總。</summary>
public class AStarDiagnosisTests
{
    private static RelaxationOutcome Find(RouteResult r, string id) =>
        Assert.Single(r.Failure!.Diagnosis!.Relaxations, o => o.Id == id);

    private static string[] Ids(RouteResult r) => r.Failure!.Diagnosis!.Relaxations.Select(o => o.Id).ToArray();

    // ------------------------------------------------------------ s08b：最小直管造成無解
    [Fact]
    public void S08b_MinStraightIsTheCause_AndArrivalDirectionToo()
    {
        var s = LoadScene("s08b_min_straight_infeasible");
        var line = s.Lines[0];
        var r = Route(s);
        Assert.Equal("NoPathExists", r.Failure!.Code);

        Assert.Equal(new[] { "MinStraight", "ArrivalDirection", "Clearance", "AllRelaxed" }, Ids(r));

        var min = Find(r, "MinStraight");
        Assert.True(min.Succeeds);
        Assert.Equal(2, min.BendCount);                              // 放寬最小直管後：2 格的小跳接
        Assert.Equal(5200, min.LengthMm!.Value, 6);

        var arrive = Find(r, "ArrivalDirection");
        Assert.True(arrive.Succeeds);
        Assert.Equal(1, arrive.BendCount);                           // 沿 +Y 直接進終點格（最後一段不受直管限制）
        Assert.Equal(5200, arrive.LengthMm!.Value, 6);

        var clearance = Find(r, "Clearance");                        // 場景沒有障礙物，放寬淨距沒有幫助
        Assert.False(clearance.Succeeds);
        Assert.Equal("NoPathExists", clearance.FailureCode);

        Assert.True(Find(r, "AllRelaxed").Succeeds);
        Assert.Equal("放寬以下任一項即可找到路徑：最小直管長度、終點抵達方向。", r.Failure.Diagnosis!.Summary);

        // 每項放寬的結果與獨立的參考 Dijkstra 一致
        var grid = GridFor(s, line);
        Assert.NotNull(ReferenceOptimum(grid, line, Config, minCells: 1));
        Assert.NotNull(ReferenceOptimum(grid, line, Config, minCells: 3, enforceArrival: false));
        Assert.Null(ReferenceOptimum(grid, line, Config, minCells: 3));
    }

    // ------------------------------------------------------------ s09：封閉空間，怎麼放寬都無解
    [Fact]
    public void S09_EnclosedGoal_EverythingRelaxedStillFails()
    {
        var r = Cached("s09_enclosed_nopath");
        Assert.Equal("NoPathExists", r.Failure!.Code);
        Assert.Equal(new[] { "MinStraight", "ArrivalDirection", "Clearance", "AllRelaxed" }, Ids(r));
        Assert.All(r.Failure.Diagnosis!.Relaxations, o =>
        {
            Assert.False(o.Succeeds);
            Assert.Equal("NoPathExists", o.FailureCode);
            Assert.True(o.Expanded > 0);
        });
        Assert.Contains("幾何上被阻隔", r.Failure.Diagnosis.Summary);
    }

    // ------------------------------------------------------------ s12 / s12b：縫隙
    [Fact]
    public void S12_GapNarrowerThanThePipe_IsGeometricallyImpossible()
    {
        var r = Route(LoadScene("s12_gap_too_narrow"));
        Assert.Equal("NoPathExists", r.Failure!.Code);
        Assert.False(Find(r, "Clearance").Succeeds);
        Assert.False(Find(r, "AllRelaxed").Succeeds);
        Assert.Contains("幾何上被阻隔", r.Failure.Diagnosis!.Summary);
    }

    [Fact]
    public void S12b_GridConservatism_ClearanceIsTheCause_AndTheRelaxedPathIsActuallyFineExactly()
    {
        var s = LoadScene("s12b_gap_grid_conservative");
        var r = Route(s);
        Assert.Equal("NoPathExists", r.Failure!.Code);
        Assert.False(Find(r, "MinStraight").Succeeds);
        Assert.False(Find(r, "ArrivalDirection").Succeeds);
        Assert.True(Find(r, "Clearance").Succeeds);
        Assert.True(Find(r, "AllRelaxed").Succeeds);
        Assert.Equal("放寬以下任一項即可找到路徑：障礙物淨距。", r.Failure.Diagnosis!.Summary);

        // 放寬淨距得到的路徑，用「原本的」規則做精確檢查也完全合格：
        // 縫隙 399 ≥ 2×(半徑 50 + 淨距 100) − 1，精確幾何上其實過得去，是保守式格子多擋了 1 mm。
        var relaxed = Example with { Clearance = new ClearanceConfig(0, 100) };
        var line = s.Lines[0];
        var rr = Route(s, 0, Config, relaxed);
        Assert.True(rr.Success);
        var pipe = new PipeDef(line.Id, null, line.OuterDiameterMm, line.InsulationThicknessMm, rr.Polyline, Array.Empty<PlacedComponent>());
        var report = RuleChecker.Check(s, Example, new[] { pipe });
        Assert.DoesNotContain(report.Violations, v => v.RuleId is RuleIds.Collision or RuleIds.MinClearance);
    }

    // ------------------------------------------------------------ s11：剛好相切，通得過
    [Fact]
    public void S11_GapExactlyAtTheGridLimit_Passes()
    {
        var s = LoadScene("s11_tight_gap_tangent");
        var line = s.Lines[0];
        var grid = GridFor(s, line);
        // 縫隙 [1850, 2250]、半徑 50、淨距 100：格子 [2000,2100] 到兩側剛好都是 150，不小於 150 → 自由
        Assert.False(grid.IsBlocked(new CellIndex(29, 20, 10)));
        Assert.True(grid.IsBlocked(new CellIndex(29, 19, 10)));
        Assert.True(grid.IsBlocked(new CellIndex(29, 21, 10)));
        var r = Route(s);
        AssertValid(s, line, r, grid);
        Assert.Equal(0, r.Metrics!.BendCount);
        Assert.Equal(5000, r.Metrics.LengthMm, 6);
        Assert.Equal(Units(5000, 0), r.Metrics.Cost.Total);
    }

    // ------------------------------------------------------------ s10：接口被擋
    [Fact]
    public void S10_StartInsideObstacle_NoRelaxationHelps()
    {
        var r = Route(LoadScene("s10_start_goal_blocked"), 0);
        Assert.Equal("StartBlocked", r.Failure!.Code);
        Assert.Equal(new[] { "Clearance", "AllRelaxed" }, Ids(r));          // 最小直管與抵達方向和「起點被擋」無關
        Assert.All(r.Failure.Diagnosis!.Relaxations, o => Assert.False(o.Succeeds));
        Assert.Contains("障礙物內", r.Failure.Diagnosis.Summary);
    }

    [Fact]
    public void S10_GoalInsideVessel_NoRelaxationHelps()
    {
        var r = Route(LoadScene("s10_start_goal_blocked"), 1);
        Assert.Equal("GoalBlocked", r.Failure!.Code);
        Assert.All(r.Failure.Diagnosis!.Relaxations, o => Assert.False(o.Succeeds));
    }

    [Fact]
    public void S10_StartTooCloseToObstacle_ClearanceIsTheCause()
    {
        var r = Route(LoadScene("s10_start_goal_blocked"), 2);
        Assert.Equal("StartBlocked", r.Failure!.Code);
        Assert.True(Find(r, "Clearance").Succeeds);
        Assert.Equal("放寬以下任一項即可找到路徑：障礙物淨距。", r.Failure.Diagnosis!.Summary);
    }

    // ------------------------------------------------------------ 診斷的行為
    [Fact]
    public void DiagnoseOnFailureOff_SkipsTheExtraSearches()
    {
        var cfg = Config with { DiagnoseOnFailure = false };
        var r = Route(LoadScene("s08b_min_straight_infeasible"), 0, cfg);
        Assert.Equal("NoPathExists", r.Failure!.Code);
        Assert.Null(r.Failure.Diagnosis);
    }

    [Fact]
    public void Diagnosis_DoesNotChangeTheMainFailure()
    {
        var s = LoadScene("s08b_min_straight_infeasible");
        var with = Route(s);
        var without = Route(s, 0, Config with { DiagnoseOnFailure = false });
        Assert.Equal(without.Failure!.Code, with.Failure!.Code);
        Assert.Equal(without.Failure.Message, with.Failure.Message);
        Assert.Equal(without.Failure.Expanded, with.Failure.Expanded);
        Assert.Equal(without.Failure.ClosestCell, with.Failure.ClosestCell);
        Assert.Equal(without.Search, with.Search);
    }

    [Fact]
    public void Diagnosis_IsDeterministic()
    {
        var s = LoadScene("s08b_min_straight_infeasible");
        string Dump(RouteResult r) => string.Join("|", r.Failure!.Diagnosis!.Relaxations) + r.Failure.Diagnosis.Summary;
        Assert.Equal(Dump(Route(s)), Dump(Route(s)));
    }

    [Fact]
    public void SuccessfulRoutes_HaveNoDiagnosis()
    {
        var r = Route(LoadScene("s02_open_one_bend"));
        Assert.True(r.Success);
        Assert.Null(r.Failure);
    }

    [Fact]
    public void RelaxationsThatAlreadyHold_AreNotListed()
    {
        var s = LoadScene("s12_gap_too_narrow");                                   // 很快就窮盡的無解場景
        var noMin = Example with { MinStraightBetweenBendsMm = 0 };              // N = 1：沒有最小直管可放寬
        var r = Route(s, 0, Config, noMin);
        Assert.Equal(new[] { "ArrivalDirection", "Clearance", "AllRelaxed" }, Ids(r));

        var noClearance = Example with { Clearance = new ClearanceConfig(0, 0) };  // 淨距本來就是 0
        var r2 = Route(s, 0, Config, noClearance);
        Assert.Equal(new[] { "MinStraight", "ArrivalDirection", "AllRelaxed" }, Ids(r2));
    }

    [Fact]
    public void UsingTheRouterDirectly_GivesTheGridLevelRelaxationsOnly()
    {
        var s = LoadScene("s08b_min_straight_infeasible");
        var line = s.Lines[0];
        var grid = GridFor(s, line);
        var r = new AStarRouter().Route(grid, line, Config, RouteConstraints.From(Example));
        Assert.Equal(new[] { "MinStraight", "ArrivalDirection" }, Ids(r));      // 淨距要重新體素化，由 LineRouter 補上
        Assert.Equal("放寬以下任一項即可找到路徑：最小直管長度、終點抵達方向。", r.Failure!.Diagnosis!.Summary);

        // 已經不要求抵達方向 → 不再列出該項（s08b 此時有解，所以改用封閉空間的 s09 才會失敗）
        var s9 = LoadScene("s09_enclosed_nopath");
        var line9 = s9.Lines[0];
        var r2 = new AStarRouter().Route(GridFor(s9, line9), line9, Config, new RouteConstraints(300, EnforceArrivalDirection: false));
        Assert.Equal(new[] { "MinStraight" }, Ids(r2));
    }

    [Fact]
    public void ArrivalDirectionNotEnforced_AcceptsAnyArrival()
    {
        var s = LoadScene("s08b_min_straight_infeasible");
        var line = s.Lines[0];
        var grid = GridFor(s, line);
        var r = new AStarRouter().Route(grid, line, Config, new RouteConstraints(300, EnforceArrivalDirection: false));
        Assert.True(r.Success);
        Assert.Equal(1, r.Metrics!.BendCount);
        Assert.Equal(AxisDirection.PosY, DirectionOf(r.Cells[^2], r.Cells[^1]));   // 實際以 +Y 抵達，不是接口要求的 +X
        Assert.Equal(ReferenceOptimum(grid, line, Config, 3, enforceArrival: false), r.Metrics.Cost.Total);
    }

    // ------------------------------------------------------------ 結論文字
    private static RelaxationOutcome O(string id, bool ok, string? code = null) =>
        new(id, id switch { "MinStraight" => "最小直管長度", "ArrivalDirection" => "終點抵達方向", "Clearance" => "障礙物淨距", _ => "全部條件" },
            ok, ok ? 1 : null, ok ? 100 : null, 10, ok ? null : code ?? "NoPathExists");

    [Fact]
    public void Summary_Cases()
    {
        Assert.Equal("放寬以下任一項即可找到路徑：最小直管長度、障礙物淨距。",
            RouteDiagnosis.Summarize(new[] { O("MinStraight", true), O("ArrivalDirection", false), O("Clearance", true), O("AllRelaxed", true) }));

        Assert.Equal("單獨放寬任何一項都無法解決，但同時放寬全部條件後有解：需要多項一起調整。",
            RouteDiagnosis.Summarize(new[] { O("MinStraight", false), O("Clearance", false), O("AllRelaxed", true) }));

        Assert.Contains("幾何上被阻隔",
            RouteDiagnosis.Summarize(new[] { O("MinStraight", false), O("AllRelaxed", false) }));

        Assert.Equal("放寬已嘗試的條件都無法解決。",
            RouteDiagnosis.Summarize(new[] { O("MinStraight", false), O("ArrivalDirection", false) }));

        // 達到搜尋上限者無法判定，另外註記
        var s = RouteDiagnosis.Summarize(new[] { O("MinStraight", false, "SearchLimitExceeded"), O("AllRelaxed", false) });
        Assert.EndsWith("（有 1 項因達到搜尋上限而無法判定）", s);

        // 「全部放寬」本身達到上限時，不可以宣稱「幾何上被阻隔」——沒有證據
        var inconclusive = RouteDiagnosis.Summarize(new[] { O("MinStraight", false), O("AllRelaxed", false, "SearchLimitExceeded") });
        Assert.Equal("放寬全部條件後的搜尋達到搜尋上限，無法判定是否有解。", inconclusive);
        Assert.DoesNotContain("幾何上被阻隔", inconclusive);
    }

    // ------------------------------------------------------------ s14：搜尋上限
    [Fact]
    public void S14_SearchLimit_IsReportedWithAHint_AndACoarserGridSolvesIt()
    {
        var s = LoadScene("s14_search_limit");
        var coarse = Config with { VoxelSizeMm = 200 };

        var ok = Route(s, 0, coarse);
        Assert.True(ok.Success, ok.Failure?.Message);
        Assert.True(ok.Search.Expanded > 1500, $"場景太簡單，無法測試搜尋上限：展開 {ok.Search.Expanded}");
        Assert.True(ok.Metrics!.BendCount >= 4);

        var limited = coarse with { Search = coarse.Search with { MaxExpandedNodes = 1000 } };
        var r = Route(s, 0, limited);
        Assert.False(r.Success);
        Assert.Equal("SearchLimitExceeded", r.Failure!.Code);
        Assert.Equal(1000, r.Failure.Expanded);
        Assert.NotNull(r.Failure.ClosestCell);
        Assert.True(r.Failure.ClosestDistanceMm > 0);
        Assert.Contains("voxelSizeMm", r.Failure.Message);
        Assert.Contains("maxExpandedNodes", r.Failure.Message);
        Assert.Null(r.Failure.Diagnosis);                              // 達到上限無法下結論，不做診斷
        Assert.Empty(r.Polyline);
    }

    [Fact]
    public void SearchLimitInsideADiagnosisRun_IsMarkedInconclusive()
    {
        // 上限剛好等於主搜尋窮盡失敗所需的展開數 E。放寬淨距後可達區域變大（需要 > E 個展開才窮盡）→ 診斷中的該次重跑達到上限
        var s = LoadScene("s12_gap_too_narrow");
        long e = Route(s, 0, Config with { DiagnoseOnFailure = false }).Failure!.Expanded;
        var tight = Config with { Search = Config.Search with { MaxExpandedNodes = e } };

        var r = Route(s, 0, tight);
        Assert.Equal("NoPathExists", r.Failure!.Code);                  // 主搜尋剛好在上限內窮盡
        var clearance = Find(r, "Clearance");
        Assert.False(clearance.Succeeds);
        Assert.Equal("SearchLimitExceeded", clearance.FailureCode);     // 無法判定，不是「無解」
        Assert.Equal(e, clearance.Expanded);
        Assert.Contains("無法判定", r.Failure.Diagnosis!.Summary);
    }
}
