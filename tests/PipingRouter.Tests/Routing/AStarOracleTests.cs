using PipingRouter.Engine;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Routing;
using PipingRouter.Engine.Rules;
using PipingRouter.Engine.Scene;
using Xunit;
using static PipingRouter.Tests.Rules.RuleTestKit;
using static PipingRouter.Tests.Routing.RouteTestKit;

namespace PipingRouter.Tests.Routing;

/// <summary>隨機場景：A* 的成本必須等於獨立的 Dijkstra 最佳成本；無解的判斷也必須一致。</summary>
public class AStarOracleTests
{
    private static SceneModel RandomScene(Random rng, int minObstacles, int maxObstacles)
    {
        var obstacles = new List<Obstacle>();
        int n = rng.Next(minObstacles, maxObstacles + 1);
        for (int i = 0; i < n; i++)
        {
            double x = rng.Next(0, 13) * 100, y = rng.Next(0, 13) * 100, z = rng.Next(0, 9) * 100;
            if (rng.Next(4) == 0)
                obstacles.Add(new Obstacle($"C{i}", ObstacleKind.Vessel, new CylinderShape(
                    new AxisCylinder(V(x + 50, y + 50, z), 50 + rng.Next(0, 3) * 50, 100 + rng.Next(0, 5) * 100, (Axis)rng.Next(3)))));
            else
                obstacles.Add(Box($"B{i}", x, y, z, x + 100 + rng.Next(0, 4) * 100, y + 100 + rng.Next(0, 4) * 100, z + 100 + rng.Next(0, 4) * 100));
        }
        var zones = new List<ServiceZone>();
        if (rng.Next(2) == 0)
        {
            double x = rng.Next(2, 9) * 100, y = rng.Next(2, 9) * 100;
            zones.Add(Zone("Z", ServiceZoneKind.Maintenance, x, y, 0, x + 300, y + 300, 800));
        }
        return Scene(obstacles, zones) with { Bounds = new Aabb(V(0, 0, 0), V(1500, 1500, 1000)) };
    }

    /// <summary>從自由格中隨機挑接口（挑不到就回傳 null），方向任意。</summary>
    private static LineRequest? RandomLine(Random rng, VoxelGrid probe)
    {
        CellIndex? PickFree()
        {
            for (int t = 0; t < 300; t++)
            {
                var c = new CellIndex(rng.Next(probe.Nx), rng.Next(probe.Ny), rng.Next(probe.Nz));
                if (!probe.IsBlocked(c)) return c;
            }
            return null;
        }
        var a = PickFree();
        var b = PickFree();
        if (a == null || b == null) return null;
        return new LineRequest("L", 100, 0,
            new PortSpec(probe.CellCenter(a.Value), (AxisDirection)rng.Next(6)),
            new PortSpec(probe.CellCenter(b.Value), (AxisDirection)rng.Next(6)),
            Array.Empty<string>());
    }

    private static readonly LineRequest ProbeLine = new("L", 100, 0,
        new PortSpec(V(50, 50, 50), AxisDirection.PosX), new PortSpec(V(150, 50, 50), AxisDirection.PosX), Array.Empty<string>());

    // 稀疏場景（多為有解）與密集場景（常見無解）；偶數組用「禁止」、奇數組用「懲罰」維修區
    [Theory]
    [InlineData(1, false, 3, 8, 30)]
    [InlineData(2, false, 3, 8, 30)]
    [InlineData(3, true, 3, 8, 30)]
    [InlineData(4, true, 3, 8, 30)]
    [InlineData(5, false, 14, 26, 5)]
    [InlineData(6, true, 14, 26, 5)]
    public void MatchesDijkstra_OnRandomScenes(int seed, bool penalizeZones, int minObs, int maxObs, int minSuccess)
    {
        var rng = new Random(seed * 7919);
        var rules = penalizeZones
            ? Example with { ServiceZonePolicy = new ServiceZonePolicyConfig(ZonePolicy.Penalize, ZonePolicy.Penalize) }
            : Example;
        int success = 0, noPath = 0, skipped = 0, overlaps = 0;
        const int cases = 60;

        for (int iter = 0; iter < cases; iter++)
        {
            var scene = RandomScene(rng, minObs, maxObs);
            var cfg = Config with
            {
                Weights = new RoutingWeights(
                    Length: new[] { 1.0, 0.37, 2.5 }[rng.Next(3)],
                    Bend: rng.Next(0, 4) * 60,
                    ZonePenaltyPerCell: rng.Next(0, 4) * 30),
                Search = Config.Search with { Seed = rng.Next(0, 3) == 0 ? 0 : rng.Next(1, 1000) },
            };
            var probe = GridFor(scene, ProbeLine, cfg, rules);
            var line = RandomLine(rng, probe);
            if (line == null) { skipped++; continue; }
            var scene2 = scene with { Lines = new[] { line } };

            var grid = GridFor(scene2, line, cfg, rules);
            var r = LineRouter.Route(scene2, line, rules, cfg);
            long? best = ReferenceOptimum(grid, line, cfg);

            if (r.Success)
            {
                Assert.NotNull(best);
                AssertValid(scene2, line, r, grid, cfg, rules, allowSelfOverlap: true);
                Assert.Equal(best, r.Metrics!.Cost.Total);
                success++;
                if (r.Warnings.Count > 0) overlaps++;
            }
            else if (r.Failure!.Code == "InvalidInput")
            {
                skipped++;                      // 兩個接口落在同一格
            }
            else
            {
                Assert.Equal("NoPathExists", r.Failure.Code);
                Assert.Null(best);
                noPath++;
            }
        }
        Assert.True(success >= minSuccess, $"成功案例太少：success={success} noPath={noPath} skipped={skipped}");
        if (minObs >= 14) Assert.True(noPath >= 3, $"密集場景應該出現無解：noPath={noPath}");
        Assert.True(overlaps <= success / 4, $"自交過於頻繁：{overlaps}/{success}");
    }

    [Fact]
    public void LargeGrid_UsesDictionaryStore_AndStillWorks()
    {
        // 狀態數 > 16M 時改用字典：場景 4000³ / 格子 50 = 80³ = 512k 格 × 6 = 3M（陣列）；用 2400³/ 格 20 = 120³ = 1.7M × 6 = 10M（陣列）。
        // 這裡刻意放大到 > 16M 狀態：3000×3000×2000 / 格 25 = 120×120×80 = 1.15M 格 × 6 = 6.9M…… 改用 200×200×80 = 3.2M 格 × 6 = 19M
        var scene = Scene() with { Bounds = new Aabb(V(0, 0, 0), V(5000, 5000, 2000)) };
        var line = new LineRequest("L", 100, 0,
            new PortSpec(V(50, 50, 50), AxisDirection.PosX),
            new PortSpec(V(450, 50, 50), AxisDirection.NegX), Array.Empty<string>());
        var cfg = Config with { VoxelSizeMm = 25 };
        var grid = GridFor(scene, line, cfg);
        Assert.True(grid.CellCount * 6 > 16_000_000);
        var r = LineRouter.Route(scene, line, Example, cfg);
        Assert.True(r.Success, r.Failure?.Message);
        Assert.Equal(0, r.Metrics!.BendCount);
    }
}
