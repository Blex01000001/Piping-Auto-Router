using PipingRouter.Engine;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Io;
using PipingRouter.Engine.Routing;
using PipingRouter.Engine.Rules;
using PipingRouter.Engine.Scene;
using Xunit;
using static PipingRouter.Tests.Rules.RuleTestKit;

namespace PipingRouter.Tests.Routing;

internal static class RouteTestKit
{
    /// <summary>範例佈線設定：格子 100、長度 1、彎頭 50、區域 20（皆為範例值）。</summary>
    public static RoutingConfig Config { get; } =
        RoutingConfigJson.Load(RepoPaths.Combine("config", "routing.example.json")).ValueOrThrow();

    public static SceneModel LoadScene(string name) =>
        SceneJson.Load(RepoPaths.Combine("samples", "scenes", name + ".json")).ValueOrThrow();

    public static VoxelGrid GridFor(SceneModel scene, LineRequest line, RoutingConfig? config = null,
        RuleConfig? rules = null, VoxelizeOptions? options = null)
    {
        var r = Voxelizer.Build(scene, rules ?? Example, (config ?? Config).VoxelSizeMm, line.CollisionRadiusMm, options);
        Assert.True(r.Ok, r.Message);
        return r.Grid!;
    }

    public static RouteResult Route(SceneModel scene, int lineIndex = 0, RoutingConfig? config = null, RuleConfig? rules = null) =>
        LineRouter.Route(scene, scene.Lines[lineIndex], rules ?? Example, config ?? Config);

    /// <summary>以預設權重換算期望成本：長度 mm × 長度權重 + 彎頭數 × 彎頭權重 + 區域格數 × 區域權重（皆 ×1000 定點）。</summary>
    public static long Units(double mm, int bends, int zoneCells = 0, RoutingConfig? config = null)
    {
        var w = (config ?? Config).Weights;
        return (long)Math.Round(mm * w.Length * CostUnits.Scale)
             + bends * (long)Math.Round(w.Bend * CostUnits.Scale)
             + zoneCells * (long)Math.Round(w.ZonePenaltyPerCell * CostUnits.Scale);
    }

    public static AxisDirection DirectionOf(CellIndex a, CellIndex b)
    {
        var d = (b.X - a.X, b.Y - a.Y, b.Z - a.Z);
        return d switch
        {
            (1, 0, 0) => AxisDirection.PosX,
            (-1, 0, 0) => AxisDirection.NegX,
            (0, 1, 0) => AxisDirection.PosY,
            (0, -1, 0) => AxisDirection.NegY,
            (0, 0, 1) => AxisDirection.PosZ,
            (0, 0, -1) => AxisDirection.NegZ,
            _ => throw new Xunit.Sdk.XunitException($"{a}→{b} 不是相鄰的軸向一步"),
        };
    }

    /// <summary>
    /// 驗證一條成功的路徑：格子相鄰且全自由、起訖方向正確、不回頭、折線全為軸向且共線已合併、
    /// 彎頭數／長度／成本與重新計算一致，並且通過精確的規則檢查（不干涉、淨距、不穿越維修區）。
    /// </summary>
    public static void AssertValid(SceneModel scene, LineRequest line, RouteResult r, VoxelGrid grid,
        RoutingConfig? config = null, RuleConfig? rules = null, bool allowSelfOverlap = false)
    {
        config ??= Config;
        rules ??= Example;
        Assert.True(r.Success, r.Failure?.ToString());
        Assert.Null(r.Failure);
        var cells = r.Cells;
        Assert.True(cells.Count >= 2);
        Assert.Equal(r.Snap!.StartCell, cells[0]);
        Assert.Equal(r.Snap.EndCell, cells[^1]);

        var dirs = new List<AxisDirection>();
        for (int i = 1; i < cells.Count; i++) dirs.Add(DirectionOf(cells[i - 1], cells[i]));
        Assert.Equal(line.Start.Direction, dirs[0]);                       // 第一步沿起點接口方向
        Assert.Equal(line.End.Direction.Opposite(), dirs[^1]);             // 最後一步沿終點接口方向的反向
        for (int i = 1; i < dirs.Count; i++) Assert.NotEqual(dirs[i - 1].Opposite(), dirs[i]);   // 不回頭
        foreach (var c in cells) Assert.False(grid.IsBlocked(c), $"路徑穿過被擋的格子 {c}");
        // 自交只能以警告標示：有重複格子 ⇔ 有 SelfOverlap 警告（若 allowSelfOverlap 為 false 則不得自交）
        bool overlaps = cells.Distinct().Count() != cells.Count;
        Assert.Equal(overlaps, r.Warnings.Any(w => w.Code == "SelfOverlap"));
        if (!allowSelfOverlap) Assert.False(overlaps, "路徑自己和自己重疊");
        Assert.DoesNotContain(cells.Skip(1), c => c == cells[0]);          // 起點格是端點專用，不會再經過

        // 折線
        var poly = r.Polyline;
        Assert.Equal(grid.CellCenter(cells[0]), poly[0]);
        Assert.Equal(grid.CellCenter(cells[^1]), poly[^1]);
        double length = 0;
        for (int i = 0; i + 1 < poly.Count; i++)
        {
            var seg = new Segment(poly[i], poly[i + 1]);
            Assert.False(seg.IsDegenerate);
            Assert.True(seg.IsAxisAligned(), $"線段 {i} 不是軸向");
            length += seg.Length;
            if (i + 2 < poly.Count)                                        // 相鄰兩段不共線（共線點已合併）
            {
                var d1 = seg.Direction;
                var d2 = poly[i + 2] - poly[i + 1];
                Assert.True(Math.Abs(d1.Dot(d2)) < 1e-9, $"頂點 {i + 1} 不是 90 度彎頭");
            }
        }
        int bends = dirs.Zip(dirs.Skip(1), (a, b) => a != b).Count(x => x);
        Assert.Equal(bends, r.Metrics!.BendCount);
        Assert.Equal(poly.Count - 2, bends);
        Assert.Equal(length, r.Metrics.LengthMm, 6);
        Assert.Equal((cells.Count - 1) * grid.CellSize, r.Metrics.LengthMm, 6);

        // 成本重算
        long step = (long)Math.Round(config.Weights.Length * grid.CellSize * CostUnits.Scale);
        long zoneEntered = cells.Skip(1).Count(c => grid.IsPenalized(c));
        Assert.Equal((cells.Count - 1) * step, r.Metrics.Cost.Length);
        Assert.Equal(bends * (long)Math.Round(config.Weights.Bend * CostUnits.Scale), r.Metrics.Cost.Bend);
        Assert.Equal(zoneEntered * (long)Math.Round(config.Weights.ZonePenaltyPerCell * CostUnits.Scale), r.Metrics.Cost.Zone);

        // 精確規則檢查（連續座標）：格子層通過的路徑不應有干涉、淨距或維修區違規
        var pipe = new PipeDef(line.Id, null, line.OuterDiameterMm, line.InsulationThicknessMm, poly, Array.Empty<PlacedComponent>());
        var report = RuleChecker.Check(scene, rules, new[] { pipe });
        var ids = new List<string> { RuleIds.Collision, RuleIds.MinClearance };
        if (rules.ServiceZonePolicy.Maintenance == ZonePolicy.Forbid && rules.ServiceZonePolicy.Operation == ZonePolicy.Forbid)
            ids.Add(RuleIds.ServiceZoneIntrusion);
        var bad = report.Violations.Where(v => ids.Contains(v.RuleId)).ToList();
        Assert.True(bad.Count == 0, "精確檢查發現違規：\n" + string.Join("\n", bad.Select(Line)));
    }

    /// <summary>獨立的參考解：沒有啟發函數的 Dijkstra，與 A* 共用同一份規格（不是同一份程式）。回傳最佳成本，無解回傳 null。</summary>
    public static long? ReferenceOptimum(VoxelGrid g, LineRequest line, RoutingConfig config)
    {
        if (!g.TrySnap(line.Start.Position, out var s, out _) || !g.TrySnap(line.End.Position, out var e, out _)) return null;
        if (g.IsBlocked(s) || g.IsBlocked(e) || s == e) return null;

        long step = (long)Math.Round(config.Weights.Length * g.CellSize * CostUnits.Scale);
        long bend = (long)Math.Round(config.Weights.Bend * CostUnits.Scale);
        long zone = (long)Math.Round(config.Weights.ZonePenaltyPerCell * CostUnits.Scale);
        int startDir = (int)line.Start.Direction, arrive = (int)line.End.Direction.Opposite();

        var dist = new Dictionary<(CellIndex, int), long>();
        var pq = new PriorityQueue<(CellIndex, int), long>();
        dist[(s, startDir)] = 0;
        pq.Enqueue((s, startDir), 0);
        var done = new HashSet<(CellIndex, int)>();
        while (pq.TryDequeue(out var cur, out long cd))
        {
            if (!done.Add(cur)) continue;
            var (c, d) = cur;
            if (c == e && d == arrive) return cd;
            foreach (var nd in Enumerable.Range(0, 6))
            {
                bool isInitial = c == s && d == startDir && cd == 0;
                if (isInitial && nd != startDir) continue;
                if (nd == (d ^ 1)) continue;
                var v = ((AxisDirection)nd).ToVector();
                var n = new CellIndex(c.X + (int)v.X, c.Y + (int)v.Y, c.Z + (int)v.Z);
                if (g.IsBlocked(n)) continue;
                if (n == s) continue;                                  // 起點格是端點專用
                if (n == e && nd != arrive) continue;                  // 終點格只能以抵達方向進入
                long nc = cd + step + (nd != d ? bend : 0) + (g.IsPenalized(n) ? zone : 0);
                var key = (n, nd);
                if (dist.TryGetValue(key, out long old) && old <= nc) continue;
                dist[key] = nc;
                pq.Enqueue(key, nc);
            }
        }
        return null;
    }
}
