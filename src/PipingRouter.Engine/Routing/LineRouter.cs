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
}
