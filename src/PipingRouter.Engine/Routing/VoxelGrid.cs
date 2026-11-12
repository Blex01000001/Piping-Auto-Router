using PipingRouter.Engine.Geometry;

namespace PipingRouter.Engine.Routing;

public readonly record struct CellIndex(int X, int Y, int Z)
{
    public override string ToString() => $"({X},{Y},{Z})";
}

/// <summary>格子被標記的原因。IsBlocked = 有 Obstacle／Zone／OutsideBounds 其一；Penalized 只用於成本。</summary>
[Flags]
public enum CellFlags : byte
{
    None = 0,
    Obstacle = 1,
    Zone = 2,
    OutsideBounds = 4,
    Penalized = 8,
}

/// <summary>
/// 體素格子：只給搜尋使用。原點在場景範圍的最小角，格子邊長固定，管線節點取格子中心。
/// 格子涵蓋整個場景範圍（最後一格可能略超出）。中心點在場景範圍外的格子標為 OutsideBounds。
/// 真實資料與最終檢查一律回到連續座標，格子只負責「找出一條候選路徑」。
/// </summary>
public sealed class VoxelGrid
{
    private const CellFlags BlockedMask = CellFlags.Obstacle | CellFlags.Zone | CellFlags.OutsideBounds;

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
