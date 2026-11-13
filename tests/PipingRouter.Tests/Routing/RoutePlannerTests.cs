using PipingRouter.Engine;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Io;
using PipingRouter.Engine.Routing;
using PipingRouter.Engine.Rules;
using PipingRouter.Engine.Scene;
using Xunit;
using static PipingRouter.Tests.Rules.RuleTestKit;
using static PipingRouter.Tests.Routing.RouteTestKit;

namespace PipingRouter.Tests.Routing;

/// <summary>
/// 多條管線依序佈線與順序策略。s16 的數字都是手算的（見場景檔）：
/// L-A 單獨佈走縫 1（y=2050）：多繞 2×700，4 彎頭，8400 mm，成本 8.6M；走縫 2（y=3650）：多繞 2×900，8800 mm，成本 9.0M。
/// L-B 單獨佈：淨位移 −1 格，垂直段各 ≥ 3 格 → 3 + 4，兩側各 4 個彎頭共 8 個，8400 mm，成本 8.8M。
/// 順序 [A,B]：A 佔縫 1，B 失敗；順序 [B,A]：B 佔縫 1，A 改走縫 2，兩條都成功，總成本 17.8M。
/// </summary>
public class RoutePlannerTests
{
    private static readonly RoutingConfig Cfg = Config with { DiagnoseOnFailure = false };

    private static RoutePlanner Planner(SceneModel? scene = null, RoutingConfig? cfg = null, IRouter? router = null) =>
        new(scene ?? LoadScene("s16_route_order"), Example, cfg ?? Cfg, router);

    private static string Dump(PlanEvaluation e) =>
        e.OrderKey + "|" + e.Score + "|" + string.Join("\n", e.Results.Select(RouteResultJson.Serialize));

    // ------------------------------------------------------------ 基本行為與數字
    [Fact]
    public void S16_StandaloneNumbers()
    {
        var s = LoadScene("s16_route_order");
        var a = Route(s, 0, Cfg);
        var b = Route(s, 1, Cfg);
        Assert.Equal((4, 8400.0, Units(8400, 4)), (a.Metrics!.BendCount, a.Metrics.LengthMm, a.Metrics.Cost.Total));
        Assert.Equal((8, 8400.0, Units(8400, 8)), (b.Metrics!.BendCount, b.Metrics.LengthMm, b.Metrics.Cost.Total));
        Assert.Equal(2050, a.Polyline.Min(p => p.Y), 6);                  // A 單獨走縫 1
    }

    [Fact]
    public void S16_OrderMatters()
    {
        var planner = Planner();
        var ab = planner.Evaluate(new[] { 0, 1 });
        var ba = planner.Evaluate(new[] { 1, 0 });

        // [A,B]：A 先佔縫 1，B 的終點格被 A 的管間淨距範圍蓋住
        Assert.Equal(1, ab.FailedCount);
        Assert.True(ab.Results[0].Success);
        Assert.False(ab.Results[1].Success);
        Assert.Equal("GoalBlocked", ab.Results[1].Failure!.Code);
        Assert.Contains("其他管線", ab.Results[1].Failure!.Message);
        Assert.Equal(Units(8400, 4), ab.RouteCost);
        Assert.Equal(1_000_000L * CostUnits.Scale, ab.PenaltyCost);
        Assert.Equal(ab.RouteCost + ab.PenaltyCost, ab.Score);

        // [B,A]：B 先走縫 1，A 改走縫 2（y=3650）
        Assert.True(ba.AllSucceeded);
        Assert.Equal(0, ba.PenaltyCost);
        Assert.Equal(Units(8800, 4), ba.Results[0].Metrics!.Cost.Total);   // A：8800 mm、4 彎頭
        Assert.Equal(Units(8400, 8), ba.Results[1].Metrics!.Cost.Total);   // B：與單獨佈相同
        Assert.Equal(3650, ba.Results[0].Polyline.Max(p => p.Y), 6);
        Assert.Equal(Units(8800, 4) + Units(8400, 8), ba.Score);
        Assert.True(ba.Score < ab.Score);
    }

    [Fact]
    public void ResultsAreIndexedBySceneOrder_NotByRoutingOrder()
    {
        var ba = Planner().Evaluate(new[] { 1, 0 });
        Assert.Equal(new[] { 1, 0 }, ba.Order);
        Assert.Equal("L-A", ba.Results[0].LineId);
        Assert.Equal("L-B", ba.Results[1].LineId);
        Assert.Equal("1,0", ba.OrderKey);
    }

    [Fact]
    public void TheFirstRoutedLine_IsNeverAffected_AndLaterOnesCanOnlyGetWorse()
    {
        var s = LoadScene("s16_route_order");
        var alone = new[] { Route(s, 0, Cfg), Route(s, 1, Cfg) };
        var planner = Planner();
        foreach (var order in new[] { new[] { 0, 1 }, new[] { 1, 0 } })
        {
            var e = planner.Evaluate(order);
            Assert.Equal(string.Join(";", alone[order[0]].Cells), string.Join(";", e.Results[order[0]].Cells));   // 第一條 = 單獨佈
            var second = e.Results[order[1]];
            if (second.Success)
                Assert.True(second.Metrics!.Cost.Total >= alone[order[1]].Metrics!.Cost.Total);                  // 只多了限制，成本不會更低
        }
    }

    [Fact]
    public void SuccessfulPlan_PassesTheExactPipeToPipeAndObstacleChecks()
    {
        var s = LoadScene("s16_route_order");
        var e = Planner().Evaluate(new[] { 1, 0 });
        var pipes = s.Lines.Select((l, i) => new PipeDef(l.Id, null, l.OuterDiameterMm, l.InsulationThicknessMm,
            e.Results[i].Polyline, Array.Empty<PlacedComponent>())).ToList();
        var report = RuleChecker.Check(s, Example, pipes);
        Assert.Empty(report.Violations);                                   // 干涉、淨距、最小直管、管對管全部合格
        // 管對管確實有被檢查（有通過紀錄、最緊的間隙 ≥ 淨距）
        var pair = Assert.Single(report.Results, r => r.RuleId == RuleIds.MinClearance && r.ObjectIds.Count == 2 && r.ObjectIds.All(id => id.StartsWith("L-")));
        Assert.True(pair.Measured >= Example.Clearance.PipeToPipeMinMm);
    }

    [Fact]
    public void ParallelLines_UseExactStampMargins()
    {
        // s01 空場景：A 沿 y=2050。B 的接口在第 23 列（y=2350）→ 不受影響；在第 22 列（y=2250）→ 被擋
        var s = LoadScene("s01_open_straight");
        var a = s.Lines[0];
        LineRequest Parallel(string id, double y) => a with { Id = id, Start = a.Start with { Position = V(500, y, 1200) }, End = a.End with { Position = V(5500, y, 1200) } };

        var ok = s with { Lines = new[] { a with { Start = a.Start with { Position = V(500, 2000, 1200) }, End = a.End with { Position = V(5500, 2000, 1200) } }, Parallel("B", 2300) } };
        var good = new RoutePlanner(ok, Example, Cfg).Evaluate(new[] { 0, 1 });
        Assert.True(good.AllSucceeded);
        Assert.All(good.Results, r => Assert.Equal(0, r.Metrics!.BendCount));
        Assert.Equal(2 * Units(5000, 0), good.Score);

        var tooClose = s with { Lines = new[] { ok.Lines[0], Parallel("B", 2200) } };
        var bad = new RoutePlanner(tooClose, Example, Cfg).Evaluate(new[] { 0, 1 });
        Assert.Equal(1, bad.FailedCount);
        Assert.Equal("StartBlocked", bad.Results[1].Failure!.Code);
    }

    // ------------------------------------------------------------ 輸入檢查
    [Theory]
    [InlineData(new[] { 0 })]
    [InlineData(new[] { 0, 1, 1 })]
    [InlineData(new[] { 0, 0 })]
    [InlineData(new[] { 0, 2 })]
    [InlineData(new[] { -1, 0 })]
    public void InvalidOrders_AreRejected(int[] order)
    {
        Assert.Throws<ArgumentException>(() => Planner().Evaluate(order));
    }

    [Fact]
    public void NoLines_IsAnEmptyPlan()
    {
        var empty = LoadScene("s01_open_straight") with { Lines = Array.Empty<LineRequest>() };
        var e = new RoutePlanner(empty, Example, Cfg).Plan(new ExhaustiveOrderStrategy());
        Assert.Empty(e.Results);
        Assert.Equal(0, e.Score);
        Assert.True(e.AllSucceeded);
    }

    [Fact]
    public void SingleLine_MatchesTheLineRouter()
    {
        var s = LoadScene("s05_vessel_cylinder");
        var e = new RoutePlanner(s, Example, Cfg).Plan(new GivenOrderStrategy());
        var direct = Route(s, 0, Cfg);
        Assert.Equal(string.Join(";", direct.Cells), string.Join(";", e.Results[0].Cells));
        Assert.Equal(direct.Metrics!.Cost.Total, e.Score);
    }

    [Fact]
    public void AllLinesFailing_StillProducesAScoreWithPenalties()
    {
        var s = LoadScene("s10_start_goal_blocked");                        // 三條管線各自在起訖點被擋
        var planner = new RoutePlanner(s, Example, Config);
        var e = planner.Plan(new ExhaustiveOrderStrategy());
        Assert.Equal(3, e.FailedCount);
        Assert.Equal(3 * 1_000_000L * CostUnits.Scale, e.Score);
        Assert.Equal(new[] { 0, 1, 2 }, e.Order);                          // 全部同分 → 字典序最前者
        Assert.Equal(6, planner.EvaluationCount);                          // 3! = 6 種順序
    }

    // ------------------------------------------------------------ 失敗懲罰
    [Fact]
    public void FailurePenalty_ComesFromTheConfig_AndDecidesWhetherFailingBeatsDetouring()
    {
        var s = LoadScene("s16_route_order");
        var small = Cfg with { Planning = new PlanningSettings(5000) };      // 失敗只罰 5,000（×1000 = 5M）
        var p = new RoutePlanner(s, Example, small);
        var ab = p.Evaluate(new[] { 0, 1 });
        Assert.Equal(5_000L * CostUnits.Scale, ab.PenaltyCost);
        Assert.Equal(Units(8400, 4) + 5_000L * CostUnits.Scale, ab.Score);   // 13.6M

        // 懲罰太小：失敗一條（13.6M）比兩條都成功（17.8M）還「便宜」→ 窮舉選 [A,B]。這就是懲罰必須夠大的原因。
        Assert.Equal(new[] { 0, 1 }, p.Plan(new ExhaustiveOrderStrategy()).Order);
        // 預設的大懲罰：少失敗永遠優先
        Assert.Equal(new[] { 1, 0 }, Planner().Plan(new ExhaustiveOrderStrategy()).Order);
    }

    // ------------------------------------------------------------ 策略
    [Fact]
    public void Exhaustive_FindsTheBestOrder_AndMemoizesEvaluations()
    {
        var planner = Planner();
        var best = planner.Plan(new ExhaustiveOrderStrategy());
        Assert.Equal(new[] { 1, 0 }, best.Order);
        Assert.Equal(2, planner.EvaluationCount);                           // 2! = 2；Plan 最後再 Evaluate 一次是快取命中
        Assert.Same(best, planner.Evaluate(new[] { 1, 0 }));
        Assert.Equal(2, planner.EvaluationCount);
    }

    [Fact]
    public void GivenAndFixedStrategies()
    {
        var planner = Planner();
        var given = planner.Plan(new GivenOrderStrategy());
        Assert.Equal(new[] { 0, 1 }, given.Order);
        Assert.Equal(1, planner.EvaluationCount);                           // 不做搜尋，只評估最後選的那一個

        var fixedBa = planner.Plan(new FixedOrderStrategy(new[] { 1, 0 }));
        Assert.Equal(new[] { 1, 0 }, fixedBa.Order);
        Assert.Throws<ArgumentException>(() => planner.Plan(new FixedOrderStrategy(new[] { 0 })));
    }

    [Fact]
    public void Exhaustive_RefusesTooManyLines()
    {
        var many = LoadScene("s10_start_goal_blocked");
        Assert.Throws<ArgumentException>(() => new RoutePlanner(many, Example, Config).Plan(new ExhaustiveOrderStrategy(maxLines: 2)));
    }

    // 測試專案自己寫的策略：證明新策略不需要改 Engine 任何一行就能接上
    private sealed class SwapHillClimbStrategy : IRouteOrderStrategy
    {
        public int Evaluations;

        public IReadOnlyList<int> Choose(int lineCount, Func<IReadOnlyList<int>, PlanEvaluation> evaluate)
        {
            var current = Enumerable.Range(0, lineCount).ToArray();
            long best = evaluate(current).Score;
            Evaluations++;
            bool improved = true;
            while (improved)
            {
                improved = false;
                for (int i = 0; i < lineCount; i++)
                    for (int j = i + 1; j < lineCount; j++)
                    {
                        var candidate = (int[])current.Clone();
                        (candidate[i], candidate[j]) = (candidate[j], candidate[i]);
                        long score = evaluate(candidate).Score;
                        Evaluations++;
                        if (score < best)
                        {
                            best = score;
                            current = candidate;
                            improved = true;
                        }
                    }
            }
            return current;
        }
    }

    private sealed class SeededShuffleStrategy : IRouteOrderStrategy
    {
        private readonly int _seed, _tries;

        public SeededShuffleStrategy(int seed, int tries) => (_seed, _tries) = (seed, tries);

        public IReadOnlyList<int> Choose(int lineCount, Func<IReadOnlyList<int>, PlanEvaluation> evaluate)
        {
            var rng = new Random(_seed);
            int[] best = Enumerable.Range(0, lineCount).ToArray();
            long bestScore = evaluate(best).Score;
            for (int t = 0; t < _tries; t++)
            {
                var order = Enumerable.Range(0, lineCount).OrderBy(_ => rng.Next()).ToArray();
                long score = evaluate(order).Score;
                if (score < bestScore) { bestScore = score; best = order; }
            }
            return best;
        }
    }

    private sealed class CountingRouter : IRouter
    {
        private readonly AStarRouter _inner = new();
        public int Calls;

        public RouteResult Route(VoxelGrid grid, LineRequest line, RoutingConfig config, RouteConstraints constraints)
        {
            Calls++;
            return _inner.Route(grid, line, config, constraints);
        }
    }

    [Fact]
    public void ReplacingTheStrategy_NeverTouchesTheRouter()
    {
        var s = LoadScene("s16_route_order");
        var strategies = new IRouteOrderStrategy[]
        {
            new GivenOrderStrategy(), new FixedOrderStrategy(new[] { 1, 0 }), new ExhaustiveOrderStrategy(),
            new SwapHillClimbStrategy(), new SeededShuffleStrategy(seed: 3, tries: 6),
        };

        // 同一個佈線器實例，被所有策略共用；planner 只透過 IRouter 呼叫它
        var router = new CountingRouter();
        var byOrder = new Dictionary<string, string>();
        var scores = new List<long>();
        foreach (var strategy in strategies)
        {
            var e = new RoutePlanner(s, Example, Cfg, router).Plan(strategy);
            scores.Add(e.Score);
            // 同一個順序，不論由哪個策略選出來，結果逐位元相同
            string dump = Dump(e);
            if (byOrder.TryGetValue(e.OrderKey, out var seen)) Assert.Equal(seen, dump);
            else byOrder[e.OrderKey] = dump;
        }
        Assert.True(router.Calls > 0);
        Assert.Equal(17_800_000L, scores[2]);                              // 窮舉 = 最佳
        Assert.Equal(scores[2], scores[3]);                                // 爬山找到同樣的最佳（從 [A,B] 交換一次）
        Assert.True(scores[4] <= scores[0]);                               // 隨機重啟不會比起點更差
        Assert.True(scores[3] < scores[0]);                                // 搜尋確實比照給定順序好
    }

    [Fact]
    public void RouterCallCount_IsLinesTimesDistinctOrders()
    {
        var router = new CountingRouter();
        var planner = new RoutePlanner(LoadScene("s16_route_order"), Example, Cfg, router);
        planner.Plan(new ExhaustiveOrderStrategy());
        Assert.Equal(2 * 2, router.Calls);                                 // 2 條 × 2 種順序；快取命中不再呼叫
        planner.Evaluate(new[] { 0, 1 });
        Assert.Equal(4, router.Calls);
    }

    [Fact]
    public void HillClimb_UsesTheMemoizedEvaluations()
    {
        var planner = Planner();
        var hill = new SwapHillClimbStrategy();
        var e = planner.Plan(hill);
        Assert.Equal(new[] { 1, 0 }, e.Order);
        Assert.True(hill.Evaluations >= 2);
        Assert.Equal(2, planner.EvaluationCount);                          // 重複評估不重算
    }

    // ------------------------------------------------------------ 確定性與隔離
    [Fact]
    public void Evaluate_IsDeterministic_AcrossFreshPlanners()
    {
        Assert.Equal(Dump(Planner().Evaluate(new[] { 1, 0 })), Dump(Planner().Evaluate(new[] { 1, 0 })));
        Assert.Equal(Dump(Planner().Evaluate(new[] { 0, 1 })), Dump(Planner().Evaluate(new[] { 0, 1 })));
    }

    [Fact]
    public void EvaluationOrder_DoesNotLeakStateBetweenOrders()
    {
        // 先評估 [A,B] 再評估 [B,A]，與只評估 [B,A]，後者結果必須相同（基底格子沒有被前一次的蓋章污染）
        var p1 = Planner();
        p1.Evaluate(new[] { 0, 1 });
        string after = Dump(p1.Evaluate(new[] { 1, 0 }));
        string fresh = Dump(Planner().Evaluate(new[] { 1, 0 }));
        Assert.Equal(fresh, after);
    }

    [Fact]
    public void ComponentSequenceLines_CountAsFailuresInAPlan()
    {
        var s = LoadScene("s01_open_straight");
        var scene = s with { Lines = new[] { s.Lines[0] with { ComponentSequence = new[] { "handValve" } } } };
        var e = new RoutePlanner(scene, Example, Cfg).Plan(new GivenOrderStrategy());
        Assert.Equal("NotSupported", e.Results[0].Failure!.Code);
        Assert.Equal(1, e.FailedCount);
    }
}
