using PipingRouter.Engine;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Routing;
using PipingRouter.Engine.Rules;
using PipingRouter.Engine.Scene;
using Xunit;
using static PipingRouter.Tests.Rules.RuleTestKit;
using static PipingRouter.Tests.Routing.RouteTestKit;

namespace PipingRouter.Tests.Routing;

/// <summary>兩個彎頭之間的最小直管長度（頂點中心到中心）；起點接口段與終點最後一段不受限。</summary>
public class AStarMinStraightTests
{
    private static RuleConfig MinStraight(double mm) => Example with { MinStraightBetweenBendsMm = mm };

    private static RouteResult RunAndValidate(string scene, RuleConfig rules, RoutingConfig? config = null)
    {
        var s = LoadScene(scene);
        var line = s.Lines[0];
        var cfg = config ?? Config;
        var grid = GridFor(s, line, cfg, rules);
        var r = Route(s, 0, cfg, rules);
        AssertValid(s, line, r, grid, cfg, rules);
        Assert.Equal(ReferenceOptimum(grid, line, cfg, MinCells(rules, cfg)), r.Metrics!.Cost.Total);
        return r;
    }

    // ------------------------------------------------------------ 格數換算
    [Theory]
    [InlineData(300, 100, 3)]
    [InlineData(250, 100, 3)]       // 向上取整：3 格 = 300 ≥ 250
    [InlineData(301, 100, 4)]
    [InlineData(200, 100, 2)]
    [InlineData(201, 100, 3)]
    [InlineData(100, 100, 1)]
    [InlineData(50, 100, 1)]        // 不到一格也至少 1 格
    [InlineData(0, 100, 1)]         // 0 = 無限制
    [InlineData(300, 130, 3)]       // 3 × 130 = 390 ≥ 300（比需要的長，保守）
    [InlineData(0.3, 0.1, 3)]       // 0.3/0.1 = 3.0000000000000004，不可被浮點誤差推成 4
    [InlineData(1e-10, 100, 1)]
    public void MinStraightCells_RoundsUp(double minMm, double cell, int expected)
    {
        Assert.Equal(expected, new RouteConstraints(minMm).MinStraightCells(cell));
        Assert.True(expected * cell >= minMm - 1e-9);               // 規則檢查器必然通過
    }

    [Fact]
    public void Constraints_AreTakenFromTheRuleConfig()
    {
        Assert.Equal(300, RouteConstraints.From(Example).MinStraightBetweenBendsMm);
        Assert.Equal(1, RouteConstraints.None.MinStraightCells(100));
    }

    // ------------------------------------------------------------ s08：階梯／小跳接必須變長
    [Fact]
    public void S08_Unconstrained_UsesTheShortJog()
    {
        var r = RunAndValidate("s08_min_straight", MinStraight(0));
        Assert.Equal(2, r.Metrics!.BendCount);
        Assert.Equal(5200, r.Metrics.LengthMm, 6);                  // 5000 + 偏移 200
        Assert.Equal(Units(5200, 2), r.Metrics.Cost.Total);
        Assert.Equal(1, r.Search.MinStraightCells);
    }

    [Fact]
    public void S08_WithMinimumStraight_MustGoLongerWithMoreBends()
    {
        // 偏移 2 格的小跳接（+Y 2 格）低於 3 格。垂直方向的淨位移必須由「各 ≥ 3 格」的垂直段組成：
        // 5 − 3 = 2（總長 8 格）是最短，故多出 800 mm、4 個彎頭。
        var r = RunAndValidate("s08_min_straight", Example);
        Assert.Equal(4, r.Metrics!.BendCount);
        Assert.Equal(5800, r.Metrics.LengthMm, 6);
        Assert.Equal(Units(5800, 4), r.Metrics.Cost.Total);
        Assert.Equal(3, r.Search.MinStraightCells);
        Assert.Equal(6, r.Polyline.Count);
        // 兩彎頭之間的每一段都 ≥ 300（第一段與最後一段是接口段，不受限）
        for (int i = 1; i + 2 < r.Polyline.Count; i++)
            Assert.True(r.Polyline[i].DistanceTo(r.Polyline[i + 1]) >= 300 - 1e-9);
    }

    // 偏移 2 格。N ≤ 2：+Y 2 格的小跳接合法 → 2 個彎頭、多 2 格。
    // N ≥ 3：垂直段各需 ≥ N 格，淨位移 2 = (N+2) − N，總共多走 2N+2 格、4 個彎頭。
    [Theory]
    [InlineData(100, 2, 5200)]      // N=1
    [InlineData(200, 2, 5200)]      // N=2：2 格 = 200 ≥ 200，小跳接剛好合法
    [InlineData(201, 4, 5800)]      // 向上取整成 N=3：多 8 格
    [InlineData(300, 4, 5800)]      // N=3
    [InlineData(400, 4, 6000)]      // N=4：多 10 格
    [InlineData(500, 4, 6200)]      // N=5：多 12 格
    public void S08_ThresholdBehaviour(double minMm, int expectedBends, double expectedLengthMm)
    {
        var r = RunAndValidate("s08_min_straight", MinStraight(minMm));
        Assert.Equal(expectedBends, r.Metrics!.BendCount);
        Assert.Equal(expectedLengthMm, r.Metrics.LengthMm, 6);
        Assert.Equal(Units(expectedLengthMm, expectedBends), r.Metrics.Cost.Total);
    }

    [Fact]
    public void S08_CostNeverDecreasesAsTheMinimumGrows()
    {
        long previous = 0;
        foreach (double min in new[] { 0.0, 100, 200, 201, 300, 400, 500, 800 })
        {
            long total = RunAndValidate("s08_min_straight", MinStraight(min)).Metrics!.Cost.Total;
            Assert.True(total >= previous, $"最小直管 {min} 的成本 {total} 小於較寬鬆限制的 {previous}");
            previous = total;
        }
    }

    [Fact]
    public void S08_LongerMinimum_StillMeetsEveryMiddleSegment()
    {
        var r = RunAndValidate("s08_min_straight", MinStraight(500));     // 5 格
        for (int i = 1; i + 2 < r.Polyline.Count; i++)
            Assert.True(r.Polyline[i].DistanceTo(r.Polyline[i + 1]) >= 500 - 1e-9);
    }

    // ------------------------------------------------------------ s08b：限制讓問題變成無解
    [Fact]
    public void S08b_Infeasible_WithMinimumStraight_ButFeasibleWithoutIt()
    {
        var s = LoadScene("s08b_min_straight_infeasible");
        var line = s.Lines[0];

        var strict = Route(s);                                        // 範例規則：300 mm = 3 格
        Assert.False(strict.Success);
        Assert.Equal("NoPathExists", strict.Failure!.Code);
        Assert.True(strict.Failure.Expanded > 0);
        Assert.Equal(3, strict.Search.MinStraightCells);
        Assert.Null(ReferenceOptimum(GridFor(s, line), line, Config, 3));

        foreach (double min in new[] { 0.0, 100, 200 })               // ≤ 2 格：2 格的跳接合法
        {
            var rules = MinStraight(min);
            var r = Route(s, 0, Config, rules);
            Assert.True(r.Success, $"最小直管 {min} 應有解");
            AssertValid(s, line, r, GridFor(s, line, Config, rules), Config, rules);
            Assert.Equal(2, r.Metrics!.BendCount);
        }
        Assert.False(Route(s, 0, Config, MinStraight(201)).Success);  // 201 → 3 格
    }

    [Fact]
    public void S08b_WiderBand_AllowsALongerZigZag()
    {
        // 帶寬若再多 1 格（5 格，索引 0..4），就能做 +3、−4、+3 的三段垂直跳接（6 個彎頭，多走 10 格）。
        // 這個案例是手算時漏掉的解，由 A* 與參考 Dijkstra 同時找到：用來確認約束的「可達性」判斷是對的。
        var s = LoadScene("s08b_min_straight_infeasible");
        var wider = s with { Bounds = new Aabb(s.Bounds.Min, s.Bounds.Max + V(0, 100, 0)) };
        var line = wider.Lines[0];
        var rules = Example;                                           // N = 3
        var grid = GridFor(wider, line, Config, rules);
        var r = Route(wider, 0, Config, rules);
        AssertValid(wider, line, r, grid, Config, rules);
        Assert.Equal(6, r.Metrics!.BendCount);
        Assert.Equal(5000 + 10 * 100, r.Metrics.LengthMm, 6);
        Assert.Equal(ReferenceOptimum(grid, line, Config, 3), r.Metrics.Cost.Total);
    }

    // ------------------------------------------------------------ 接口段不受限
    [Fact]
    public void LastSegment_MayBeShorterThanTheMinimum()
    {
        var s = LoadScene("s02_open_one_bend");
        var l = s.Lines[0];
        var line = l with { End = l.End with { Position = V(3500, 600, 1000) } };     // 終點只比起點高 1 格：最後一段 +Y 只有 1 格
        var scene = s with { Lines = new[] { line } };
        var r = Route(scene);
        AssertValid(scene, line, r, GridFor(scene, line));
        Assert.Equal(1, r.Metrics!.BendCount);
        Assert.Equal(100, r.Polyline[^2].DistanceTo(r.Polyline[^1]), 6);                // 100 < 300
    }

    [Fact]
    public void FirstSegment_MayBeShorterThanTheMinimum()
    {
        var s = LoadScene("s02_open_one_bend");
        var l = s.Lines[0];
        var line = l with
        {
            Start = l.Start with { Direction = AxisDirection.PosY },                    // 先沿 +Y 離開
            End = l.End with { Position = V(3500, 600, 1000), Direction = AxisDirection.NegX },   // 抵達時朝 +X
        };
        var scene = s with { Lines = new[] { line } };
        var r = Route(scene);
        AssertValid(scene, line, r, GridFor(scene, line));
        Assert.Equal(1, r.Metrics!.BendCount);
        Assert.Equal(100, r.Polyline[0].DistanceTo(r.Polyline[1]), 6);                  // 第一段 1 格 < 300
    }

    [Fact]
    public void SingleBend_IsNeverConstrained_NoMatterHowLongTheMinimum()
    {
        // 只有 1 個彎頭 → 沒有「兩彎頭之間」的線段，最小直管再大也不影響
        var r = RunAndValidate("s02_open_one_bend", MinStraight(10_000));
        Assert.Equal(1, r.Metrics!.BendCount);
        Assert.Equal(5000, r.Metrics.LengthMm, 6);
    }

    // ------------------------------------------------------------ 其他場景
    [Fact]
    public void S05_LargeMinimum_StillOptimalAndValid()
    {
        var r = RunAndValidate("s05_vessel_cylinder", MinStraight(2000));        // 20 格
        Assert.True(r.Metrics!.BendCount >= 4);
        Assert.True(r.Metrics.LengthMm >= 8600);
    }

    [Fact]
    public void S03_WallDetour_RespectsTheMinimum()
    {
        var r = RunAndValidate("s03_wall_detour", Example);
        Assert.True(r.Metrics!.BendCount >= 4);
    }

    [Fact]
    public void HugeMinimum_ReportsGridTooLarge_NotAnException()
    {
        var r = Route(LoadScene("s01_open_straight"), 0, Config, MinStraight(1e9));
        Assert.False(r.Success);
        Assert.Equal("GridTooLarge", r.Failure!.Code);
    }

    [Fact]
    public void Seeds_StillDoNotChangeTheOptimalCost_WithConstraint()
    {
        var s = LoadScene("s08_min_straight");
        var line = s.Lines[0];
        var grid = GridFor(s, line);
        long? best = null;
        var paths = new HashSet<string>();
        for (int seed = 0; seed < 10; seed++)
        {
            var cfg = Config with { Search = Config.Search with { Seed = seed } };
            var r = Route(s, 0, cfg);
            AssertValid(s, line, r, grid, cfg);
            best ??= r.Metrics!.Cost.Total;
            Assert.Equal(best, r.Metrics!.Cost.Total);
            paths.Add(string.Join(";", r.Cells));
        }
        Assert.True(paths.Count >= 2, "s08 有「先上後下」與「先下後上」兩種等成本跳接，種子應能選出不同的");
    }
}
