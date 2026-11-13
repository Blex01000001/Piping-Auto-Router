using PipingRouter.Engine.Geometry;

namespace PipingRouter.Engine.Routing;

public readonly record struct CellIndex(int X, int Y, int Z)
{
    public override string ToString() => $"({X},{Y},{Z})";
}

/// <summary>格子被標記的原因。IsBlocked = 有 Obstacle／Zone／OutsideBounds／OtherPipe 其一；Penalized 只用於成本。</summary>
[Flags]
public enum CellFlags : byte
{
    None = 0,
    Obstacle = 1,
    Zone = 2,
    OutsideBounds = 4,
    Penalized = 8,

    /// <summary>已佈好的其他管線及其管間淨距範圍（多條管線依序佈線時由 <see cref="VoxelGrid.StampCapsule"/> 標記）。</summary>
    OtherPipe = 16,
}

/// <summary>
/// 體素格子：只給搜尋使用。原點在場景範圍的最小角，格子邊長固定，管線節點取格子中心。
/// 格子涵蓋整個場景範圍（最後一格可能略超出）。中心點在場景範圍外的格子標為 OutsideBounds。
/// 真實資料與最終檢查一律回到連續座標，格子只負責「找出一條候選路徑」。
/// </summary>
public sealed class VoxelGrid
{
    private const CellFlags BlockedMask = CellFlags.Obstacle | CellFlags.Zone | CellFlags.OutsideBounds | CellFlags.OtherPipe;

    private readonly byte[] _flags;

    public int Nx { get; }
    public int Ny { get; }
    public int Nz { get; }
    public double CellSize { get; }
    public Vec3 Origin { get; }
    public Aabb SceneBounds { get; }
    public long CellCount => (long)Nx * Ny * Nz;

    internal VoxelGrid(Aabb sceneBounds, double cellSize, int nx, int ny, int nz)
    {
        SceneBounds = sceneBounds;
        Origin = sceneBounds.Min;
        CellSize = cellSize;
        Nx = nx;
        Ny = ny;
        Nz = nz;
        _flags = new byte[(long)nx * ny * nz];
    }

    private VoxelGrid(VoxelGrid other)
    {
        SceneBounds = other.SceneBounds;
        Origin = other.Origin;
        CellSize = other.CellSize;
        Nx = other.Nx;
        Ny = other.Ny;
        Nz = other.Nz;
        _flags = (byte[])other._flags.Clone();
    }

    /// <summary>複製一份（旗標獨立）。多條管線評估一個佈線順序時，每條管線從乾淨的基底格子複製後再蓋上已佈好的管線。</summary>
    public VoxelGrid Clone() => new(this);

    /// <summary>
    /// 把一段已佈好的管線標成「其他管線」：格子方塊到線段的精確距離 &lt; margin 的格子都擋住。
    /// margin = 已佈管線的半徑 + 這條管線的半徑 + 管間最小淨距；道理與障礙物相同——
    /// 新管線的中心線只走自由格，所以離已佈管線的中心線 ≥ margin，膠囊體間隙 ≥ 管間淨距。
    /// 回傳這次新標記的格子數。margin ≤ 0 不做事。
    /// </summary>
    public int StampCapsule(Segment segment, double margin)
    {
        if (!(margin > 0) || !double.IsFinite(margin)) return 0;
        var reach = new Aabb(Vec3.Min(segment.A, segment.B), Vec3.Max(segment.A, segment.B)).Inflate(margin);
        if (!Voxelizer.Range(Origin.X, Nx, CellSize, reach.Min.X, reach.Max.X, out int x0, out int x1) ||
            !Voxelizer.Range(Origin.Y, Ny, CellSize, reach.Min.Y, reach.Max.Y, out int y0, out int y1) ||
            !Voxelizer.Range(Origin.Z, Nz, CellSize, reach.Min.Z, reach.Max.Z, out int z0, out int z1))
            return 0;

        int added = 0;
        for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var c = new CellIndex(x, y, z);
                    if (SegmentAabb.Distance(segment, CellBox(c)) >= margin) continue;
                    int i = LinearIndex(c);
                    if (((CellFlags)_flags[i] & CellFlags.OtherPipe) == 0) added++;
                    _flags[i] |= (byte)CellFlags.OtherPipe;
                }
        return added;
    }

    public bool InRange(CellIndex c) =>
        (uint)c.X < (uint)Nx && (uint)c.Y < (uint)Ny && (uint)c.Z < (uint)Nz;

    /// <summary>線性索引（X 最快）。呼叫端需先確認 InRange。</summary>
    public int LinearIndex(CellIndex c) => (c.Z * Ny + c.Y) * Nx + c.X;

    public CellFlags FlagsAt(CellIndex c) => (CellFlags)_flags[LinearIndex(c)];

    /// <summary>超出格子範圍視為被擋住。</summary>
    public bool IsBlocked(CellIndex c) => !InRange(c) || ((CellFlags)_flags[LinearIndex(c)] & BlockedMask) != 0;

    public bool IsPenalized(CellIndex c) => InRange(c) && ((CellFlags)_flags[LinearIndex(c)] & CellFlags.Penalized) != 0;

    internal void AddFlag(int linearIndex, CellFlags flag) => _flags[linearIndex] |= (byte)flag;

    public Vec3 CellCenter(CellIndex c) => new(
        Origin.X + (c.X + 0.5) * CellSize,
        Origin.Y + (c.Y + 0.5) * CellSize,
        Origin.Z + (c.Z + 0.5) * CellSize);

    public Aabb CellBox(CellIndex c) => new(
        new Vec3(Origin.X + c.X * CellSize, Origin.Y + c.Y * CellSize, Origin.Z + c.Z * CellSize),
        new Vec3(Origin.X + (c.X + 1) * CellSize, Origin.Y + (c.Y + 1) * CellSize, Origin.Z + (c.Z + 1) * CellSize));

    /// <summary>
    /// 點所在的格子：floor((p − 原點) / 邊長)。剛好落在格子邊界時歸入座標較大的那格；
    /// 格子範圍上緣（含）的點歸入最後一格。範圍外的點回傳 false。
    /// </summary>
    public bool TryCellOf(Vec3 p, out CellIndex cell)
    {
        cell = default;
        if (!p.IsFinite) return false;
        if (!Axis(p.X, Origin.X, Nx, out int x) || !Axis(p.Y, Origin.Y, Ny, out int y) || !Axis(p.Z, Origin.Z, Nz, out int z))
            return false;
        cell = new CellIndex(x, y, z);
        return true;
    }

    private bool Axis(double v, double origin, int n, out int index)
    {
        double f = Math.Floor((v - origin) / CellSize);
        index = 0;
        if (f < 0 || f > n) return false;
        if (f == n)
        {
            // 剛好在格子範圍上緣才算在內
            if (v - origin > n * CellSize) return false;
            f = n - 1;
        }
        index = (int)f;
        return true;
    }

    /// <summary>點吸附到所在格子的中心，並回傳吸附距離（供結果回報 gridSnap）。</summary>
    public bool TrySnap(Vec3 p, out CellIndex cell, out double offsetMm)
    {
        offsetMm = 0;
        if (!TryCellOf(p, out cell)) return false;
        offsetMm = CellCenter(cell).DistanceTo(p);
        return true;
    }

    public long CountWith(CellFlags flag)
    {
        long n = 0;
        foreach (byte b in _flags)
            if (((CellFlags)b & flag) != 0) n++;
        return n;
    }

    public long CountBlocked()
    {
        long n = 0;
        foreach (byte b in _flags)
            if (((CellFlags)b & BlockedMask) != 0) n++;
        return n;
    }

    /// <summary>所有格子旗標的內容雜湊（FNV-1a），用於確定性測試。</summary>
    public ulong ContentHash()
    {
        ulong h = 14695981039346656037UL;
        foreach (byte b in _flags)
        {
            h ^= b;
            h *= 1099511628211UL;
        }
        return h;
    }
}
