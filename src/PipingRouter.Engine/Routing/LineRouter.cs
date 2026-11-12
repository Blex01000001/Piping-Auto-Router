using PipingRouter.Engine.Rules;
using PipingRouter.Engine.Scene;

namespace PipingRouter.Engine.Routing;

/// <summary>便利入口：依管線的外徑與保溫建立格子，再執行 A*。一條管線的完整流程。</summary>
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
        return (router ?? new AStarRouter()).Route(grid.Grid, line, config, RouteConstraints.From(rules));
    }
}
