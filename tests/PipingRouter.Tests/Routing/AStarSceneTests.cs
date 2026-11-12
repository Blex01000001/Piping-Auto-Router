using PipingRouter.Engine;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Routing;
using PipingRouter.Engine.Rules;
using Xunit;
using static PipingRouter.Tests.Rules.RuleTestKit;
using static PipingRouter.Tests.Routing.RouteTestKit;

namespace PipingRouter.Tests.Routing;

/// <summary>每個場景的期望值都由手算得出（見場景檔與註解），並與參考 Dijkstra 的最佳成本交叉驗證。</summary>
public class AStarSceneTests
{
    private static RouteResult RunAndValidate(string scene, out SceneModelHolder h, int lineIndex = 0,
        RoutingConfig? config = null, RuleConfig? rules = null)
    {
        var s = LoadScene(scene);
        var line = s.Lines[lineIndex];
        var cfg = config ?? Config;
        var grid = GridFor(s, line, cfg, rules);
        var r = Route(s, lineIndex, cfg, rules);
        AssertValid(s, line, r, grid, cfg, rules);
        h = new SceneModelHolder(s, line, grid);
        long? best = ReferenceOptimum(grid, line, cfg, MinCells(rules ?? Example, cfg));
        Assert.Equal(best, r.Metrics!.Cost.Total);                        // A* 的成本 = Dijkstra 的最佳成本
        return r;
    }

    private sealed record SceneModelHolder(Engine.Scene.SceneModel Scene, Engine.Scene.LineRequest Line, VoxelGrid Grid);

    [Fact]
    public void S01_OpenStraight_ZeroBends()
    {
        var r = RunAndValidate("s01_open_straight", out _);
        Assert.Equal(0, r.Metrics!.BendCount);
        Assert.Equal(5000, r.Metrics.LengthMm, 6);
        Assert.Equal(2, r.Polyline.Count);
        Assert.Equal(new Vec3(550, 2050, 1250), r.Polyline[0]);
        Assert.Equal(new Vec3(5550, 2050, 1250), r.Polyline[1]);
        Assert.Equal(Units(5000, 0), r.Metrics.Cost.Total);
        // 吸附：接口原座標與吸附距離記錄在 snap 裡（接口 (500,2000,1200) → 格心 (550,2050,1250)）
        Assert.Equal(new Vec3(500, 2000, 1200), r.Snap!.StartPort);
        Assert.Equal(Math.Sqrt(3) * 50, r.Snap.StartOffsetMm, 6);
        Assert.Equal(new Vec3(5500, 2000, 1200), r.Snap.EndPort);
        Assert.Equal(Math.Sqrt(3) * 50, r.Snap.EndOffsetMm, 6);
    }

    [Fact]
    public void S02_OpenOneBend()
    {
        var r = RunAndValidate("s02_open_one_bend", out _);
        Assert.Equal(1, r.Metrics!.BendCount);
        Assert.Equal(5000, r.Metrics.LengthMm, 6);
        Assert.Equal(
            new[] { new Vec3(550, 550, 1050), new Vec3(3550, 550, 1050), new Vec3(3550, 2550, 1050) },
            r.Polyline.ToArray());
        Assert.Equal(Units(5000, 1), r.Metrics.Cost.Total);
    }

    [Fact]
    public void S03_WallDetour_GoesAroundAndIsOptimal()
    {
        var r = RunAndValidate("s03_wall_detour", out var h);
        // 牆從 y=0 到 4000、z=0 到 4000（整個高度），必須繞到 y > 4150 以外，再繞過塔槽：至少 +Y、+X、−Y、+X 四個彎頭
        Assert.True(r.Metrics!.BendCount >= 4, $"彎頭數 {r.Metrics.BendCount}");
        Assert.True(r.Metrics.LengthMm > 6000);
        Assert.Contains(r.Cells, c => h.Grid.CellCenter(c).Y > 4150);
    }

    [Fact]
    public void S04_RackClimb_OneBend()
    {
        var r = RunAndValidate("s04_pipe_rack_climb", out _);
        Assert.Equal(1, r.Metrics!.BendCount);
        Assert.Equal(8000, r.Metrics.LengthMm, 6);                 // 向上 3000（z 550→3550）＋水平 5000
        Assert.Equal(
            new[] { new Vec3(550, 2050, 550), new Vec3(550, 2050, 3550), new Vec3(5550, 2050, 3550) },
            r.Polyline.ToArray());
    }

    [Fact]
    public void S05_VerticalVessel_GoesAroundTheSideWithFourBends()
    {
        // 塔槽 R=600 加管半徑 50 與淨距 100 → 軸心 750 內被擋；繞側面最近的格心 y=2850（偏移 800）
        var r = RunAndValidate("s05_vessel_cylinder", out var h);
        Assert.Equal(4, r.Metrics!.BendCount);
        Assert.Equal(7000 + 2 * 800, r.Metrics.LengthMm, 6);
        Assert.Equal(6, r.Polyline.Count);
        Assert.Equal(Units(8600, 4), r.Metrics.Cost.Total);
        Assert.Equal(2850, r.Polyline.Max(p => p.Y), 6);
        Assert.All(r.Polyline, p => Assert.Equal(1050, p.Z, 6));   // 沒有爬高：繞側面比越頂便宜
    }

    [Fact]
    public void S06_HorizontalVessel_GoesOverTheTop()
    {
        // 臥式容器沿 Y（y 500→3500），頂 z=1500。越頂格心 z=1750（偏移 700）比繞端部（≥1700）便宜
        var r = RunAndValidate("s06_horizontal_vessel", out _);
        Assert.Equal(4, r.Metrics!.BendCount);
        Assert.Equal(7000 + 2 * 700, r.Metrics.LengthMm, 6);
        Assert.Equal(1750, r.Polyline.Max(p => p.Z), 6);
        Assert.All(r.Polyline, p => Assert.Equal(2050, p.Y, 6));   // 沒有橫向繞行
    }

    [Fact]
    public void S07_ServiceZone_Forbidden_DetoursAroundIt()
    {
        // 維修區 y 1000→3000（管半徑膨脹 50），上側格心 y=3150（偏移 1100）比下側 850（偏移 1200）近
        var r = RunAndValidate("s07_service_zone", out _);
        Assert.Equal(4, r.Metrics!.BendCount);
        Assert.Equal(5000 + 2 * 1100, r.Metrics.LengthMm, 6);
        Assert.Equal(3150, r.Polyline.Max(p => p.Y), 6);
    }

    [Fact]
    public void S07_ServiceZone_Penalized_CrossesWhenCheaper()
    {
        // 區域懲罰 20/格：穿越 12 格 = 240，遠小於繞行 2200 + 4 個彎頭 200
        var rules = Example with { ServiceZonePolicy = new ServiceZonePolicyConfig(ZonePolicy.Penalize, ZonePolicy.Penalize) };
        var r = RunAndValidate("s07_service_zone", out _, rules: rules);
        Assert.Equal(0, r.Metrics!.BendCount);
        Assert.Equal(5000, r.Metrics.LengthMm, 6);
        Assert.Equal(12 * 20 * CostUnits.Scale, r.Metrics.Cost.Zone);
        Assert.Equal(Units(5000, 0, zoneCells: 12), r.Metrics.Cost.Total);
    }

    [Fact]
    public void S07_ServiceZone_HeavyPenalty_MakesItDetourAgain()
    {
        var rules = Example with { ServiceZonePolicy = new ServiceZonePolicyConfig(ZonePolicy.Penalize, ZonePolicy.Penalize) };
        var heavy = Config with { Weights = Config.Weights with { ZonePenaltyPerCell = 2000 } };
        var r = RunAndValidate("s07_service_zone", out _, config: heavy, rules: rules);
        Assert.Equal(4, r.Metrics!.BendCount);
        Assert.Equal(0, r.Metrics.Cost.Zone);
    }

    [Fact]
    public void BendWeight_ChangesTheShapeOfTheOptimum()
    {
        // 彎頭成本為 0 時，階梯狀路徑與 L 型一樣便宜；成本很大時一定選最少彎頭
        var cheap = Config with { Weights = Config.Weights with { Bend = 0 } };
        var r0 = RunAndValidate("s02_open_one_bend", out _, config: cheap);
        Assert.Equal(5000, r0.Metrics!.LengthMm, 6);                       // 長度仍最短
        var huge = Config with { Weights = Config.Weights with { Bend = 100000 } };
        var r1 = RunAndValidate("s02_open_one_bend", out _, config: huge);
        Assert.Equal(1, r1.Metrics!.BendCount);
    }

    [Fact]
    public void LengthWeight_ScalesCostsWithFixedPoint()
    {
        var cfg = Config with { Weights = Config.Weights with { Length = 0.37 } };
        var r = RunAndValidate("s02_open_one_bend", out _, config: cfg);
        Assert.Equal(1, r.Metrics!.BendCount);
        Assert.Equal(Units(5000, 1, config: cfg), r.Metrics.Cost.Total);
    }

    [Fact]
    public void LargerVoxel_StillValid()
    {
        var cfg = Config with { VoxelSizeMm = 200 };
        var r = RunAndValidate("s05_vessel_cylinder", out var h, config: cfg);
        Assert.Equal(200, h.Grid.CellSize);
        Assert.True(r.Metrics!.BendCount >= 4);
    }

    // ------------------------------------------------------------ 接口方向約束
    [Theory]
    [InlineData(AxisDirection.NegX, AxisDirection.NegX)]    // 起點朝反方向離開
    [InlineData(AxisDirection.PosY, AxisDirection.NegX)]    // 起點朝側面
    [InlineData(AxisDirection.PosZ, AxisDirection.NegX)]
    [InlineData(AxisDirection.PosX, AxisDirection.PosX)]    // 終點接口朝外（要從外側繞進來）
    [InlineData(AxisDirection.PosX, AxisDirection.NegY)]
    [InlineData(AxisDirection.PosX, AxisDirection.PosZ)]
    public void PortDirections_AreHonoured_AndMatchTheReference(AxisDirection startDir, AxisDirection endDir)
    {
        var s = LoadScene("s01_open_straight");
        var line = s.Lines[0] with
        {
            Start = s.Lines[0].Start with { Direction = startDir },
            End = s.Lines[0].End with { Direction = endDir },
        };
        var scene = s with { Lines = new[] { line } };
        var grid = GridFor(scene, line);
        var r = Route(scene);
        AssertValid(scene, line, r, grid);
        Assert.Equal(ReferenceOptimum(grid, line, Config, MinCells(Example, Config)), r.Metrics!.Cost.Total);
        Assert.True(r.Metrics.BendCount >= 2);
    }

    [Fact]
    public void StartingAwayFromTheGoal_NeedsTheExpectedBendCount()
    {
        // 起點朝 −X，終點在 +X 方向、抵達方向 +X：−X、+Y、+X、−Y、+X → 4 個彎頭
        var s = LoadScene("s01_open_straight");
        var line = s.Lines[0] with { Start = s.Lines[0].Start with { Direction = AxisDirection.NegX } };
        var scene = s with { Lines = new[] { line } };
        var r = Route(scene);
        AssertValid(scene, line, r, GridFor(scene, line));
        Assert.Equal(4, r.Metrics!.BendCount);
        Assert.Equal(AxisDirection.NegX, DirectionOf(r.Cells[0], r.Cells[1]));
    }

    // ------------------------------------------------------------ 確定性與種子
    [Fact]
    public void SameInput_SameOutput()
    {
        string Dump(RouteResult r) => string.Join(";", r.Cells) + "|" + r.Metrics!.Cost + "|" + r.Search.Expanded;
        var s = LoadScene("s03_wall_detour");
        Assert.Equal(Dump(Route(s)), Dump(Route(s)));
        var cfg = Config with { Search = Config.Search with { Seed = 123 } };
        Assert.Equal(Dump(Route(s, config: cfg)), Dump(Route(s, config: cfg)));
    }

    [Fact]
    public void Seeds_OnlyChangeTiesNeverTheOptimalCost()
    {
        var s = LoadScene("s05_vessel_cylinder");
        var line = s.Lines[0];
        var grid = GridFor(s, line);
        var paths = new HashSet<string>();
        long? best = null;
        for (int seed = 0; seed < 12; seed++)
        {
            var cfg = Config with { Search = Config.Search with { Seed = seed } };
            var r = Route(s, config: cfg);
            AssertValid(s, line, r, grid, cfg);
            best ??= r.Metrics!.Cost.Total;
            Assert.Equal(best, r.Metrics!.Cost.Total);
            Assert.Equal(seed, r.Search.Seed);
            paths.Add(string.Join(";", r.Cells));
        }
        Assert.True(paths.Count >= 2, "不同種子應該能產生不同的等價路徑（此場景有很多等成本的轉彎位置）");
    }
}
