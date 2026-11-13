using PipingRouter.Engine.Rules;
using PipingRouter.Engine.Scene;

namespace PipingRouter.Engine.Routing;

/// <summary>
/// 便利入口：依管線的外徑與保溫建立格子，再執行 A*。一條管線的完整流程。
/// 失敗時（RoutingConfig.DiagnoseOnFailure 開啟）補上需要重新體素化的診斷：障礙物淨距 = 0，以及「全部放寬」。
/// 診斷只會在 NoPathExists／StartBlocked／GoalBlocked 時發生，多跑最多兩次搜尋。
/// </summary>
public static class LineRouter
{
    public static RouteResult Route(
        SceneModel scene,
        LineRequest line,
        RuleConfig rules,
        RoutingConfig config,
        VoxelizeOptions? options = null,
        IRouter? router = null)
    {
        var grid = Voxelizer.Build(scene, rules, config.VoxelSizeMm, line.CollisionRadiusMm, options);
        if (grid.Grid == null)
        {
            return RouteResult.Fail(line.Id,
                new RouteFailure(grid.ErrorCode ?? "InvalidInput", grid.Message ?? "無法建立格子"),
                new RouteSearchInfo(0, config.Search.Seed, config.VoxelSizeMm));
        }
        router ??= new AStarRouter();
        var constraints = RouteConstraints.From(rules);
        var result = router.Route(grid.Grid, line, config, constraints);

        if (result.Failure is { Code: "NoPathExists" or "StartBlocked" or "GoalBlocked" } failure && config.DiagnoseOnFailure)
        {
            var existing = failure.Diagnosis?.Relaxations ?? Array.Empty<RelaxationOutcome>();
            var outcomes = new List<RelaxationOutcome>(existing);
            var quiet = config with { DiagnoseOnFailure = false };

            // 放寬淨距要重新體素化
            var relaxedRules = rules with { Clearance = rules.Clearance with { PipeToObstacleMinMm = 0 } };
            var relaxedGrid = Voxelizer.Build(scene, relaxedRules, config.VoxelSizeMm, line.CollisionRadiusMm, options).Grid;
            if (relaxedGrid != null)
            {
                if (rules.Clearance.PipeToObstacleMinMm > 0)
                    outcomes.Add(AStarRouter.Outcome("Clearance", "障礙物淨距",
                        router.Route(relaxedGrid, line, quiet, constraints)));
                outcomes.Add(AStarRouter.Outcome("AllRelaxed", "全部條件",
                    router.Route(relaxedGrid, line, quiet, new RouteConstraints(0, EnforceArrivalDirection: false))));
            }
            result = result with { Failure = failure with { Diagnosis = RouteDiagnosis.From(outcomes) } };
        }
        return result;
    }

    /// <summary>佈線後立刻做規則檢查，報告掛在結果上（失敗的結果沒有報告）。</summary>
    public static RouteResult RouteAndCheck(
        SceneModel scene,
        LineRequest line,
        RuleConfig rules,
        RoutingConfig config,
        VoxelizeOptions? options = null,
        IRouter? router = null) =>
        Route(scene, line, rules, config, options, router).WithRuleReport(scene, line, rules);

    /// <summary>
    /// 同成本的備選方案：以 config 的種子為起點，依序換種子（種子 + 1、+ 2…）重跑，依路徑去重，最多嘗試 maxSeeds 個種子，
    /// 湊滿 count 條即停。格子只建一次。種子只影響等成本路徑之間的選擇，所以每條備選的成本都相同、都合法。
    /// 第一條永遠是 config 種子的結果；若第一條就失敗，只回傳那個失敗（含診斷）。結果順序確定。
    /// 路徑只有一種時（例如空場景直線）會少於 count 條。
    /// </summary>
    public static IReadOnlyList<RouteResult> RouteAlternatives(
        SceneModel scene,
        LineRequest line,
        RuleConfig rules,
        RoutingConfig config,
        int count,
        int maxSeeds = 32,
        VoxelizeOptions? options = null,
        IRouter? router = null)
    {
        router ??= new AStarRouter();
        var first = Route(scene, line, rules, config, options, router);
        if (!first.Success || count <= 1) return new[] { first };

        var built = Voxelizer.Build(scene, rules, config.VoxelSizeMm, line.CollisionRadiusMm, options);
        if (built.Grid == null) return new[] { first };

        var constraints = RouteConstraints.From(rules);
        var results = new List<RouteResult> { first };
        var seen = new HashSet<string> { string.Join(";", first.Cells) };
        for (int i = 1; i < maxSeeds && results.Count < count; i++)
        {
            var cfg = config with { Search = config.Search with { Seed = config.Search.Seed + i } };
            var r = router.Route(built.Grid, line, cfg, constraints);
            if (r.Success && seen.Add(string.Join(";", r.Cells))) results.Add(r);
        }
        return results;
    }
}
