using PipingRouter.Engine.Geometry;

namespace PipingRouter.Engine.Routing;

public enum RouteStatus
{
    Success,
    NoPath,
}

/// <summary>成本以「千分之一單位」的整數表示（定點數），避免浮點數造成平手判斷不穩。顯示時除以 <see cref="CostUnits.Scale"/>。</summary>
public static class CostUnits
{
    public const long Scale = 1000;
}

/// <summary>
/// 無解（或無法開始搜尋）的原因。Code：
/// InvalidInput／InvalidVoxelSize／InvalidPipeRadius／GridTooLarge／NotSupported／
/// StartOutOfBounds／GoalOutOfBounds／StartBlocked／GoalBlocked／NoPathExists／SearchLimitExceeded。
/// </summary>
public sealed record RouteFailure(
    string Code,
    string Message,
    long Expanded = 0,
    CellIndex? ClosestCell = null,
    double? ClosestDistanceMm = null);

public sealed record RouteCost(long Length, long Bend, long Zone)
{
    public long Total => Length + Bend + Zone;
}

public sealed record RouteMetrics(double LengthMm, int BendCount, RouteCost Cost);

/// <summary>接口與吸附：折線取格子中心，接口原座標與吸附距離記錄在這裡。</summary>
public sealed record GridSnap(
    Vec3 StartPort, CellIndex StartCell, double StartOffsetMm,
    Vec3 EndPort, CellIndex EndCell, double EndOffsetMm);

public sealed record RouteSearchInfo(long Expanded, long Seed, double VoxelSizeMm, int MinStraightCells = 1);

/// <summary>
/// 成功但值得注意的事。Code：SelfOverlap＝路徑重複經過同一個格子（管線與自己重疊，實體上無效）。
/// 軸向 A*（格子 × 方向）無法在搜尋中禁止自交；最佳路徑只有在彎頭成本很高、或接口方向強迫迴轉時才會自交。
/// </summary>
public sealed record RouteWarning(string Code, string Message);

public sealed record RouteResult(
    string LineId,
    RouteStatus Status,
    RouteFailure? Failure,
    IReadOnlyList<Vec3> Polyline,
    IReadOnlyList<CellIndex> Cells,
    RouteMetrics? Metrics,
    GridSnap? Snap,
    RouteSearchInfo Search)
{
    public bool Success => Status == RouteStatus.Success;

    public IReadOnlyList<RouteWarning> Warnings { get; init; } = Array.Empty<RouteWarning>();

    public static RouteResult Fail(string lineId, RouteFailure failure, RouteSearchInfo search, GridSnap? snap = null) =>
        new(lineId, RouteStatus.NoPath, failure, Array.Empty<Vec3>(), Array.Empty<CellIndex>(), null, snap, search);
}
