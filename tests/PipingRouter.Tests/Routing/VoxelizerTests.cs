using PipingRouter.Engine;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Io;
using PipingRouter.Engine.Routing;
using PipingRouter.Engine.Rules;
using PipingRouter.Engine.Scene;
using Xunit;
using static PipingRouter.Tests.Rules.RuleTestKit;

namespace PipingRouter.Tests.Routing;

public class VoxelizerTests
{
    // 場景範圍 (0,0,0)–(1000,1000,1000)，格子 100 → 10×10×10
    private static SceneModel Cube(IEnumerable<Obstacle>? obs = null, IEnumerable<ServiceZone>? zones = null) =>
        Scene(obs, zones) with { Bounds = new Aabb(V(0, 0, 0), V(1000, 1000, 1000)) };

    private static RuleConfig Clr(double clearance) =>
        Example with { Clearance = new ClearanceConfig(clearance, clearance) };

    private static VoxelGrid Build(SceneModel s, RuleConfig rules, double radius, VoxelizeOptions? o = null, double voxel = 100)
    {
        var r = Voxelizer.Build(s, rules, voxel, radius, o);
        Assert.True(r.Ok, r.Message);
        return r.Grid!;
    }

    // ------------------------------------------------------------ 手算格數
    [Fact]
    public void Box_ZeroMargin_BlocksOnlyOverlappingCells()
    {
        // 方塊 (450..550)³：與格子索引 4、5 重疊 → 2×2×2 = 8
        var g = Build(Cube(new[] { Box("EQ", 450, 450, 450, 550, 550, 550) }), Clr(0), 0);
        Assert.Equal(8, g.CountBlocked());
        Assert.Equal(8, g.CountWith(CellFlags.Obstacle));
        Assert.True(g.IsBlocked(new CellIndex(4, 4, 4)));
        Assert.False(g.IsBlocked(new CellIndex(3, 4, 4)));
    }

    [Fact]
    public void Box_MarginIsRounded_NotACube()
    {
        // 膨脹量 70（半徑 20 + 淨距 50）。各軸格子間隙為 0（索引 4,5）、50（索引 3,6）、其餘 ≥150。
        // 距離 = √(Σ 間隙²) < 70：零個或一個軸有間隙 50 才擋；兩個軸 → 70.7，不擋。
        //   全 0：2³ = 8；恰一軸 50：3 軸 × 2 位置 × 2×2 = 24 → 共 32（外擴成方塊會是 64）
        var g = Build(Cube(new[] { Box("EQ", 450, 450, 450, 550, 550, 550) }), Clr(50), 20);
        Assert.Equal(32, g.CountBlocked());
        Assert.True(g.IsBlocked(new CellIndex(3, 4, 4)));     // 一軸間隙 50
        Assert.False(g.IsBlocked(new CellIndex(3, 3, 4)));    // 兩軸 → 70.7 > 70
        Assert.False(g.IsBlocked(new CellIndex(3, 3, 3)));
    }

    [Fact]
    public void Box_LargerMargin_BlocksTheWholeNeighbourhood()
    {
        // 膨脹量 100：角落格距離 √(3×50²) = 86.6 < 100 → 索引 3..6 全擋 = 4³
        var g = Build(Cube(new[] { Box("EQ", 450, 450, 450, 550, 550, 550) }), Clr(50), 50);
        Assert.Equal(64, g.CountBlocked());
    }

    [Fact]
    public void ClearanceExactlyEqualToCellGap_CellStaysFree()
    {
        // 方塊 450..550，格子索引 3 間隙 50；膨脹量剛好 50 → 距離 50 不 < 50 → 自由（剛好等於淨距算通過）
        var g = Build(Cube(new[] { Box("EQ", 450, 450, 450, 550, 550, 550) }), Clr(50), 0);
        Assert.False(g.IsBlocked(new CellIndex(3, 4, 4)));
        Assert.Equal(8, g.CountBlocked());
    }

    [Fact]
    public void ObstacleOutsideGrid_IsIgnored()
    {
        var g = Build(Cube(new[] { Box("FAR", 5000, 5000, 5000, 6000, 6000, 6000) }), Clr(50), 50);
        Assert.Equal(0, g.CountBlocked());
    }

    [Fact]
    public void ObstacleOverlappingGridEdge_IsClamped()
    {
        var g = Build(Cube(new[] { Box("EDGE", 910, 910, 910, 5000, 5000, 5000) }), Clr(0), 0);
        Assert.Equal(1, g.CountBlocked());                      // 只有最角落那一格 (9,9,9)
        Assert.True(g.IsBlocked(new CellIndex(9, 9, 9)));
    }

    [Fact]
    public void ObstacleAlignedToCellBoundary_AlsoBlocksTouchingCells()
    {
        // 障礙物剛好對齊格線 900：相鄰的 (8,·,·) 格只是「貼著表面」，仍保守地擋住（距離 0 < eps）→ 2³
        var g = Build(Cube(new[] { Box("EDGE", 900, 900, 900, 5000, 5000, 5000) }), Clr(0), 0);
        Assert.Equal(8, g.CountBlocked());
    }

    // ------------------------------------------------------------ 場景範圍外
    [Fact]
    public void CellsWithCenterOutsideBounds_AreMarked()
    {
        var scene = Scene() with { Bounds = new Aabb(V(0, 0, 0), V(940, 1000, 1000)) };
        var g = Build(scene, Clr(0), 0);
        Assert.Equal(10, g.Nx);
        Assert.Equal(g.Ny * g.Nz, g.CountWith(CellFlags.OutsideBounds));     // 索引 9 的中心 x=950 > 940
        Assert.True(g.IsBlocked(new CellIndex(9, 3, 3)));
        Assert.False(g.IsBlocked(new CellIndex(8, 3, 3)));
    }

    [Fact]
    public void CenterExactlyOnBoundsEdge_IsInside()
    {
        var scene = Scene() with { Bounds = new Aabb(V(0, 0, 0), V(950, 1000, 1000)) };
        Assert.Equal(0, Build(scene, Clr(0), 0).CountWith(CellFlags.OutsideBounds));
    }

    // ------------------------------------------------------------ 維修／操作區
    private static ServiceZone TestZone(ServiceZoneKind kind = ServiceZoneKind.Maintenance, double x0 = 450) =>
        Zone("Z", kind, x0, x0, 0, x0 + 100, x0 + 100, 250);

    [Fact]
    public void Zone_Forbid_BlocksIntersectingCells_WithoutInflation()
    {
        // 區域 (450..550, 450..550, 0..250)：x、y 各 2 格，z 為 0、1、2 → 12
        var g = Build(Cube(zones: new[] { TestZone() }), Example, 60, new VoxelizeOptions { ZoneInflation = ZoneInflation.None });
        Assert.Equal(12, g.CountBlocked());
        Assert.Equal(12, g.CountWith(CellFlags.Zone));
        Assert.Equal(0, g.CountWith(CellFlags.Obstacle));
        Assert.False(g.IsBlocked(new CellIndex(4, 4, 3)));        // 間隙 50，管半徑不膨脹 → 自由
    }

    [Fact]
    public void Zone_PipeRadiusInflation_AddsRoundedMargin()
    {
        // 半徑 60：各軸格子間隙為 0（核心）或 50（鄰接）；距離 = √(Σ 間隙²) < 60，所以至多一個軸鄰接。
        //   全核心 2×2×3 = 12；僅 x 鄰接 2×2×3 = 12；僅 y 鄰接 12；僅 z 鄰接（只有上方一格）1×2×2 = 4 → 40
        var g = Build(Cube(zones: new[] { TestZone() }), Example, 60);          // 預設就是 PipeRadius
        Assert.Equal(40, g.CountBlocked());
        Assert.True(g.IsBlocked(new CellIndex(4, 4, 3)));
    }

    [Fact]
    public void Zone_Penalize_FlagsButDoesNotBlock()
    {
        var rules = Example with { ServiceZonePolicy = new ServiceZonePolicyConfig(ZonePolicy.Penalize, ZonePolicy.Penalize) };
        var g = Build(Cube(zones: new[] { TestZone() }), rules, 60, new VoxelizeOptions { ZoneInflation = ZoneInflation.None });
        Assert.Equal(0, g.CountBlocked());
        Assert.Equal(12, g.CountWith(CellFlags.Penalized));
        Assert.True(g.IsPenalized(new CellIndex(4, 4, 0)));
        Assert.False(g.IsBlocked(new CellIndex(4, 4, 0)));
        Assert.False(g.IsPenalized(new CellIndex(4, 4, 3)));
    }

    [Fact]
    public void Zone_Penalize_UsesTheSameInflationAsForbid()
    {
        var rules = Example with { ServiceZonePolicy = new ServiceZonePolicyConfig(ZonePolicy.Penalize, ZonePolicy.Penalize) };
        var g = Build(Cube(zones: new[] { TestZone() }), rules, 60);          // 預設 PipeRadius
        Assert.Equal(0, g.CountBlocked());
        Assert.Equal(40, g.CountWith(CellFlags.Penalized));
    }

    [Fact]
    public void Zone_PolicyIsPerKind()
    {
        var rules = Example with { ServiceZonePolicy = new ServiceZonePolicyConfig(ZonePolicy.Forbid, ZonePolicy.Penalize) };
        var zones = new[]
        {
            Zone("MZ", ServiceZoneKind.Maintenance, 50, 50, 0, 150, 150, 50),
            Zone("OZ", ServiceZoneKind.Operation, 750, 750, 0, 850, 850, 50),
        };
        var g = Build(Cube(zones: zones), rules, 10);
        Assert.True(g.IsBlocked(new CellIndex(0, 0, 0)));
        Assert.False(g.IsBlocked(new CellIndex(7, 7, 0)));
        Assert.True(g.IsPenalized(new CellIndex(7, 7, 0)));
    }

    [Fact]
    public void Zone_None_CanLetThePipeSurfaceIntrudeWhenRadiusExceedsHalfAVoxel_PipeRadiusFixesIt()
    {
        // 這是「維修區不膨脹（ZoneInflation.None）」的已知取捨，這個測試把它固定下來（預設已改為 PipeRadius）：
        // 區域 x≥401。索引 3 的格子（x 300..400）距離 1，沒被擋；其中心線 x=350 離區域 51，
        // 管半徑 80 → 管表面侵入 29 mm。
        var zone = Zone("Z", ServiceZoneKind.Maintenance, 401, 0, 0, 600, 1000, 1000);
        var scene = Cube(zones: new[] { zone });
        var seg = new Segment(V(350, 150, 150), V(350, 250, 150));
        var pipe = new Capsule(seg, 80);

        var none = Build(scene, Example, 80, new VoxelizeOptions { ZoneInflation = ZoneInflation.None });
        Assert.False(none.IsBlocked(new CellIndex(3, 1, 1)));
        Assert.True(pipe.ClearanceTo(zone.Box) < 0);               // 精確檢查會抓到（規則檢查器的 ServiceZoneIntrusion）

        var inflated = Build(scene, Example, 80);                  // 預設 PipeRadius
        Assert.True(inflated.IsBlocked(new CellIndex(3, 1, 1)));   // 膨脹後同一格被擋
        var farSeg = new Segment(V(250, 150, 150), V(250, 250, 150));
        Assert.False(inflated.IsBlocked(new CellIndex(2, 1, 1)));
        Assert.True(new Capsule(farSeg, 80).ClearanceTo(zone.Box) >= 0);   // 通過的格子確實不侵入
    }

    // ------------------------------------------------------------ 與取樣法交叉驗證
    private static double SampledMin(VoxelGrid g, CellIndex c, Func<Vec3, double> pointDistance, int n)
    {
        var box = g.CellBox(c);
        double best = double.MaxValue;
        for (int x = 0; x <= n; x++)
            for (int y = 0; y <= n; y++)
                for (int z = 0; z <= n; z++)
                {
                    var p = new Vec3(box.Min.X + g.CellSize * x / n, box.Min.Y + g.CellSize * y / n, box.Min.Z + g.CellSize * z / n);
                    best = Math.Min(best, pointDistance(p));
                }
        return best;
    }

    private static List<Obstacle> RandomObstacles(Random rng, double extent)
    {
        var list = new List<Obstacle>();
        for (int i = 0; i < 4; i++)
        {
            var min = V(rng.NextDouble() * extent * 0.7, rng.NextDouble() * extent * 0.7, rng.NextDouble() * extent * 0.7);
            list.Add(Box($"B{i}", min.X, min.Y, min.Z,
                min.X + 100 + rng.NextDouble() * 300, min.Y + 100 + rng.NextDouble() * 300, min.Z + 100 + rng.NextDouble() * 300));
        }
        for (int i = 0; i < 2; i++)
        {
            list.Add(new Obstacle($"C{i}", ObstacleKind.Vessel, new CylinderShape(new AxisCylinder(
                V(rng.NextDouble() * extent * 0.8, rng.NextDouble() * extent * 0.8, rng.NextDouble() * extent * 0.8),
                60 + rng.NextDouble() * 200, 100 + rng.NextDouble() * 500, (Axis)rng.Next(3)))));
        }
        return list;
    }

    [Fact]
    public void BlockedFlags_AgreeWithPointSampling()
    {
        // 取樣最小距離 ≥ 真距離，且 ≤ 真距離 + 取樣間距半對角線。
        //   取樣最小距離 < 膨脹量 ⇒ 必須被擋；被擋 ⇒ 取樣最小距離 < 膨脹量 + 半對角線。
        const int n = 6;
        double halfDiag = Math.Sqrt(3) * 0.5 * (100.0 / n);
        for (int seed = 1; seed <= 4; seed++)
        {
            var rng = new Random(seed);
            var obstacles = RandomObstacles(rng, 1200);
            var scene = Scene(obstacles) with { Bounds = new Aabb(V(0, 0, 0), V(1200, 1200, 1200)) };
            const double radius = 40, clearance = 60, margin = radius + clearance;
            var g = Build(scene, Clr(clearance), radius);

            int blocked = 0, free = 0;
            for (int z = 0; z < g.Nz; z++)
                for (int y = 0; y < g.Ny; y++)
                    for (int x = 0; x < g.Nx; x++)
                    {
                        var c = new CellIndex(x, y, z);
                        double sampled = obstacles.Min(o => SampledMin(g, c, p => PointDist(o, p), n));
                        bool isBlocked = (g.FlagsAt(c) & CellFlags.Obstacle) != 0;
                        if (sampled < margin)
                            Assert.True(isBlocked, $"seed {seed} 格子 {c}：取樣距離 {sampled} < {margin} 卻未被擋");
                        if (isBlocked)
                            Assert.True(sampled < margin + halfDiag + 1e-9, $"seed {seed} 格子 {c}：被擋但取樣距離 {sampled} 遠大於 {margin}");
                        if (isBlocked) blocked++; else free++;
                    }
            Assert.True(blocked > 30 && free > 300, $"seed {seed} 案例分布失衡 blocked={blocked} free={free}");
        }
    }

    private static double PointDist(Obstacle o, Vec3 p) => o.Shape switch
    {
        BoxShape b => b.Box.DistanceTo(p),
        CylinderShape c => c.Cylinder.DistanceTo(p),
        _ => throw new InvalidOperationException(),
    };

    // ------------------------------------------------------------ 核心不變量：格子通過 ⇒ 精確檢查通過
    [Fact]
    public void AdjacentFreeCells_NeverViolateExactClearance()
    {
        const double radius = 40, clearance = 60;
        var rules = Clr(clearance);
        long segmentsChecked = 0;

        for (int seed = 11; seed <= 16; seed++)
        {
            var rng = new Random(seed);
            var obstacles = RandomObstacles(rng, 1500);
            var scene = Scene(obstacles) with { Bounds = new Aabb(V(0, 0, 0), V(1500, 1500, 1500)) };
            var g = Build(scene, rules, radius);
            Assert.True(g.CountBlocked() > 100 && g.CountBlocked() < g.CellCount * 0.9);

            var steps = new[] { new CellIndex(1, 0, 0), new CellIndex(0, 1, 0), new CellIndex(0, 0, 1) };
            for (int z = 0; z < g.Nz; z++)
                for (int y = 0; y < g.Ny; y++)
                    for (int x = 0; x < g.Nx; x++)
                    {
                        var a = new CellIndex(x, y, z);
                        if (g.IsBlocked(a)) continue;
                        foreach (var st in steps)
                        {
                            var b = new CellIndex(x + st.X, y + st.Y, z + st.Z);
                            if (g.IsBlocked(b)) continue;
                            var capsule = new Capsule(new Segment(g.CellCenter(a), g.CellCenter(b)), radius);
                            foreach (var o in obstacles)
                            {
                                double gap = o.Shape switch
                                {
                                    BoxShape bx => capsule.ClearanceTo(bx.Box),
                                    CylinderShape cy => capsule.ClearanceTo(cy.Cylinder),
                                    _ => throw new InvalidOperationException(),
                                };
                                Assert.True(gap >= clearance - rules.EpsilonMm,
                                    $"seed {seed}：{a}→{b} 與 {o.Id} 的間隙 {gap} < 淨距 {clearance}");
                            }
                            segmentsChecked++;
                        }
                    }
        }
        Assert.True(segmentsChecked > 5000, $"檢查的線段太少：{segmentsChecked}");
    }

    [Fact]
    public void AdjacentFreeCells_NeverIntrudeIntoZones_WhenInflated()
    {
        const double radius = 45;
        var rng = new Random(5);
        var zones = new[]
        {
            Zone("MZ1", ServiceZoneKind.Maintenance, 300 + rng.Next(50), 200, 0, 700, 650, 900),
            Zone("MZ2", ServiceZoneKind.Maintenance, 900, 700 + rng.Next(50), 200, 1300, 1100, 800),
        };
        var scene = Scene(zones: zones) with { Bounds = new Aabb(V(0, 0, 0), V(1500, 1500, 1500)) };
        var g = Build(scene, Example, radius);

        long n = 0;
        for (int z = 0; z < g.Nz; z++)
            for (int y = 0; y < g.Ny; y++)
                for (int x = 0; x < g.Nx; x++)
                {
                    var a = new CellIndex(x, y, z);
                    var b = new CellIndex(x + 1, y, z);
                    if (g.IsBlocked(a) || g.IsBlocked(b)) continue;
                    var cap = new Capsule(new Segment(g.CellCenter(a), g.CellCenter(b)), radius);
                    foreach (var zn in zones)
                        Assert.True(cap.ClearanceTo(zn.Box) >= -Example.EpsilonMm, $"{a}→{b} 侵入 {zn.Id}");
                    n++;
                }
        Assert.True(n > 500);
    }

    // ------------------------------------------------------------ 錯誤與確定性
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidVoxelSize_ReturnsError(double voxel)
    {
        var r = Voxelizer.Build(Cube(), Example, voxel, 50);
        Assert.False(r.Ok);
        Assert.Equal("InvalidVoxelSize", r.ErrorCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void InvalidPipeRadius_ReturnsError(double radius)
    {
        var r = Voxelizer.Build(Cube(), Example, 100, radius);
        Assert.Equal("InvalidPipeRadius", r.ErrorCode);
    }

    [Fact]
    public void TooManyCells_ReturnsError_NotException()
    {
        var r = Voxelizer.Build(Cube(), Example, 1, 50, new VoxelizeOptions { MaxCells = 1_000_000 });   // 10⁹ 格
        Assert.False(r.Ok);
        Assert.Equal("GridTooLarge", r.ErrorCode);
        // 極小的格子（維度會超出 int）也不能丟例外
        var tiny = Voxelizer.Build(Cube(), Example, 1e-9, 50);
        Assert.Equal("GridTooLarge", tiny.ErrorCode);
    }

    [Fact]
    public void SameInput_SameGrid_AndChangesWhenInputChanges()
    {
        var a = Cube(new[] { Box("EQ", 450, 450, 450, 550, 550, 550) });
        var b = Cube(new[] { Box("EQ", 450, 450, 450, 650, 550, 550) });
        Assert.Equal(Build(a, Example, 50).ContentHash(), Build(a, Example, 50).ContentHash());
        Assert.NotEqual(Build(a, Example, 50).ContentHash(), Build(b, Example, 50).ContentHash());
    }

    // ------------------------------------------------------------ 範例場景
    private static bool Reachable(VoxelGrid g, CellIndex from, CellIndex to)
    {
        if (g.IsBlocked(from) || g.IsBlocked(to)) return false;
        var seen = new bool[g.CellCount];
        var q = new Queue<CellIndex>();
        q.Enqueue(from);
        seen[g.LinearIndex(from)] = true;
        var dirs = new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            if (c == to) return true;
            foreach (var (dx, dy, dz) in dirs)
            {
                var n = new CellIndex(c.X + dx, c.Y + dy, c.Z + dz);
                if (g.IsBlocked(n) || seen[g.LinearIndex(n)]) continue;
                seen[g.LinearIndex(n)] = true;
                q.Enqueue(n);
            }
        }
        return false;
    }

    private static VoxelGrid WallDetourGrid(out SceneModel scene)
    {
        scene = SceneJson.Load(RepoPaths.Combine("samples", "scenes", "s03_wall_detour.json")).ValueOrThrow();
        var routing = RoutingConfigJson.Load(RepoPaths.Combine("config", "routing.example.json")).ValueOrThrow();
        return Build(scene, Example, scene.Lines[0].CollisionRadiusMm, voxel: routing.VoxelSizeMm);
    }

    [Fact]
    public void WallDetourScene_PortsAreFree_AndGoalIsReachableAroundTheWall()
    {
        var g = WallDetourGrid(out var scene);
        var line = scene.Lines[0];
        Assert.True(g.TrySnap(line.Start.Position, out var s, out _));
        Assert.True(g.TrySnap(line.End.Position, out var e, out _));
        Assert.False(g.IsBlocked(s));
        Assert.False(g.IsBlocked(e));
        Assert.Equal((80, 60, 40), (g.Nx, g.Ny, g.Nz));

        // 牆本體被擋；牆旁（膨脹範圍內）也被擋
        Assert.True(g.TryCellOf(V(4000, 1000, 1500), out var wall));
        Assert.True(g.IsBlocked(wall));
        Assert.True(g.TryCellOf(V(3750, 1000, 1500), out var nearWall));
        Assert.True(g.IsBlocked(nearWall));
        // 空曠處自由
        Assert.True(g.TryCellOf(V(1000, 5500, 1500), out var open));
        Assert.False(g.IsBlocked(open));

        Assert.True(g.CountWith(CellFlags.Zone) > 0);
        Assert.True(Reachable(g, s, e));
    }

    [Fact]
    public void FullBarrier_MakesGoalUnreachable()
    {
        var wall = Box("WALL", 3900, -100, -100, 4100, 6100, 4100, ObstacleKind.Wall);
        var scene = SceneJson.Load(RepoPaths.Combine("samples", "scenes", "s03_wall_detour.json")).ValueOrThrow();
        var walled = scene with { Obstacles = scene.Obstacles.Append(wall).ToList() };
        var g = Build(walled, Example, scene.Lines[0].CollisionRadiusMm);
        Assert.True(g.TrySnap(scene.Lines[0].Start.Position, out var s, out _));
        Assert.True(g.TrySnap(scene.Lines[0].End.Position, out var e, out _));
        Assert.False(Reachable(g, s, e));
    }
}
