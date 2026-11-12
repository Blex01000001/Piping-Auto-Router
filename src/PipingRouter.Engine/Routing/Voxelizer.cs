using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Rules;
using PipingRouter.Engine.Scene;

namespace PipingRouter.Engine.Routing;

/// <summary>維修／操作區（策略為 Forbid 時）要不要額外膨脹管半徑。預設 None（不膨脹）。</summary>
public enum ZoneInflation
{
    /// <summary>區域本身當障礙，不膨脹。管線中心線可能貼近區域邊界，管表面是否侵入由規則檢查器把關。</summary>
    None,

    /// <summary>區域膨脹管半徑（含保溫），使「格子通過 ⇒ 管表面不侵入區域」。</summary>
    PipeRadius,
}

public sealed record VoxelizeOptions
{
    /// <summary>格子總數上限，超過回報 GridTooLarge（避免耗盡記憶體；每格 1 byte）。</summary>
    public long MaxCells { get; init; } = 64_000_000;

    public ZoneInflation ZoneInflation { get; init; } = ZoneInflation.None;
}

public sealed record VoxelizeResult(VoxelGrid? Grid, string? ErrorCode, string? Message)
{
    public bool Ok => Grid != null;
}

/// <summary>
/// 把場景光柵化成佔用格。保守式（寧可多擋、不可少擋）：
///   障礙物：格子（整個方塊）到障礙物的精確距離 &lt; 管半徑 + 保溫 + 最小淨距 時標為佔用。
///   因為管線走的是自由格中心的連線，整條線都落在自由格的聯集內，
///   所以「格子層通過 ⇒ 中心線離障礙物 ≥ 半徑＋淨距」，精確檢查必然通過（剛好等於也算通過）。
/// 距離使用精確的 Minkowski 膨脹（圓角），不是把方塊直接外擴。
/// 窄縫可能因此被誤判為不通——這是設計取捨。
/// </summary>
public static class Voxelizer
{
    public static VoxelizeResult Build(
        SceneModel scene,
        RuleConfig rules,
        double voxelSizeMm,
        double pipeRadiusMm,
        VoxelizeOptions? options = null)
    {
        options ??= new VoxelizeOptions();

        if (!(voxelSizeMm > 0) || !double.IsFinite(voxelSizeMm))
            return Fail("InvalidVoxelSize", $"格子大小必須是 > 0 的有限數值，實際為 {voxelSizeMm}");
        if (!(pipeRadiusMm >= 0) || !double.IsFinite(pipeRadiusMm))
            return Fail("InvalidPipeRadius", $"管半徑必須是 ≥ 0 的有限數值，實際為 {pipeRadiusMm}");

        var size = scene.Bounds.Size;
        int nx = Dim(size.X, voxelSizeMm), ny = Dim(size.Y, voxelSizeMm), nz = Dim(size.Z, voxelSizeMm);
        double cells = (double)Math.Max(1, nx) * Math.Max(1, ny) * Math.Max(1, nz);
        if (nx < 0 || ny < 0 || nz < 0 || cells > options.MaxCells)
            return Fail("GridTooLarge",
                $"格子數 {size.X / voxelSizeMm:0.#} × {size.Y / voxelSizeMm:0.#} × {size.Z / voxelSizeMm:0.#} 超過上限 {options.MaxCells}；請加大格子或縮小場景範圍");

        var grid = new VoxelGrid(scene.Bounds, voxelSizeMm, nx, ny, nz);
        double eps = rules.EpsilonMm;

        MarkOutsideBounds(grid);

        // ---- 障礙物：膨脹量 = 管半徑 + 最小淨距（至少 eps，使貼著表面的格子也被擋）----
        double obstacleMargin = Math.Max(pipeRadiusMm + rules.Clearance.PipeToObstacleMinMm, eps);
        foreach (var ob in scene.Obstacles)
            Mark(grid, ob.Shape, obstacleMargin, CellFlags.Obstacle);

        // ---- 維修／操作區 ----
        double zoneMargin = Math.Max(options.ZoneInflation == ZoneInflation.PipeRadius ? pipeRadiusMm : 0, eps);
        foreach (var zone in scene.ServiceZones)
        {
            var policy = zone.Kind == ServiceZoneKind.Maintenance
                ? rules.ServiceZonePolicy.Maintenance
                : rules.ServiceZonePolicy.Operation;
            var flag = policy == ZonePolicy.Forbid ? CellFlags.Zone : CellFlags.Penalized;
            Mark(grid, new BoxShape(zone.Box), zoneMargin, flag);
        }

        return new VoxelizeResult(grid, null, null);
    }

    private static VoxelizeResult Fail(string code, string message) => new(null, code, message);

    private static int Dim(double extent, double cell)
    {
        double n = Math.Ceiling(extent / cell - 1e-9);
        if (n < 1) return 1;
        return n > int.MaxValue ? -1 : (int)n;
    }

    private static void MarkOutsideBounds(VoxelGrid g)
    {
        var b = g.SceneBounds;
        for (int z = 0; z < g.Nz; z++)
        {
            double cz = g.Origin.Z + (z + 0.5) * g.CellSize;
            bool zOut = cz < b.Min.Z || cz > b.Max.Z;
            for (int y = 0; y < g.Ny; y++)
            {
                double cy = g.Origin.Y + (y + 0.5) * g.CellSize;
                bool yOut = zOut || cy < b.Min.Y || cy > b.Max.Y;
                for (int x = 0; x < g.Nx; x++)
                {
                    double cx = g.Origin.X + (x + 0.5) * g.CellSize;
                    if (yOut || cx < b.Min.X || cx > b.Max.X)
                        g.AddFlag(g.LinearIndex(new CellIndex(x, y, z)), CellFlags.OutsideBounds);
                }
            }
        }
    }

    /// <summary>標記所有「格子方塊到形狀的距離 &lt; margin」的格子。</summary>
    private static void Mark(VoxelGrid g, Shape shape, double margin, CellFlags flag)
    {
        var reach = shape.Bounds.Inflate(margin);
        if (!Range(g.Origin.X, g.Nx, g.CellSize, reach.Min.X, reach.Max.X, out int x0, out int x1) ||
            !Range(g.Origin.Y, g.Ny, g.CellSize, reach.Min.Y, reach.Max.Y, out int y0, out int y1) ||
            !Range(g.Origin.Z, g.Nz, g.CellSize, reach.Min.Z, reach.Max.Z, out int z0, out int z1))
            return;

        for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var c = new CellIndex(x, y, z);
                    if (Distance(g.CellBox(c), shape) < margin)
                        g.AddFlag(g.LinearIndex(c), flag);
                }
    }

    private static double Distance(Aabb cell, Shape shape) => shape switch
    {
        BoxShape b => ShapeDistance.Between(cell, b.Box),
        CylinderShape c => ShapeDistance.Between(cell, c.Cylinder),
        _ => throw new ArgumentException($"未知形狀 {shape.GetType()}"),
    };

    /// <summary>世界座標區間 [lo,hi] 涵蓋的格子索引範圍（夾在格子內）；與格子完全不相交回傳 false。</summary>
    private static bool Range(double origin, int n, double cell, double lo, double hi, out int i0, out int i1)
    {
        i0 = i1 = 0;
        double f0 = Math.Floor((lo - origin) / cell);
        double f1 = Math.Floor((hi - origin) / cell);
        if (f1 < 0 || f0 > n - 1) return false;
        i0 = (int)Math.Max(0, f0);
        i1 = (int)Math.Min(n - 1, f1);
        return true;
    }
}
