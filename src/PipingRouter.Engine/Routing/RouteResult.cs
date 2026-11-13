using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Rules;

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
    double? ClosestDistanceMm = null)
{
    /// <summary>失敗後放寬條件重跑的結果（RoutingConfig.DiagnoseOnFailure 開啟且屬於可診斷的失敗時才有）。</summary>
    public RouteDiagnosis? Diagnosis { get; init; }
}

/// <summary>
/// 放寬一項條件後重跑一次搜尋的結果。Id：MinStraight（最小直管 = 0）、ArrivalDirection（不要求終點抵達方向）、
/// Clearance（障礙物最小淨距 = 0）、AllRelaxed（以上全部放寬）。
/// FailureCode 為 SearchLimitExceeded 時代表「無法判定」（達到搜尋上限）。
/// </summary>
public sealed record RelaxationOutcome(
    string Id,
    string Name,
    bool Succeeds,
    int? BendCount,
    double? LengthMm,
    long Expanded,
    string? FailureCode);

public sealed record RouteDiagnosis(IReadOnlyList<RelaxationOutcome> Relaxations, string Summary)
{
    public static RouteDiagnosis From(IReadOnlyList<RelaxationOutcome> outcomes) => new(outcomes, Summarize(outcomes));

    /// <summary>
    /// 彙整結論：單項放寬即有解 → 列出是哪些；單項都不行但全部放寬有解 → 需要多項一起調整；
    /// 全部放寬以 NoPathExists 失敗 → 幾何上被阻隔；全部放寬本身達到搜尋上限 → 無法判定（不下結論）。
    /// 其他因達到搜尋上限而無法判定的項目另外註記。
    /// </summary>
    public static string Summarize(IReadOnlyList<RelaxationOutcome> outcomes)
    {
        var single = outcomes.Where(o => o.Id != "AllRelaxed" && o.Succeeds).ToList();
        var all = outcomes.FirstOrDefault(o => o.Id == "AllRelaxed");
        bool allInconclusive = all is { Succeeds: false, FailureCode: "SearchLimitExceeded" };
        int inconclusive = outcomes.Count(o => !o.Succeeds && o.FailureCode == "SearchLimitExceeded" && !(allInconclusive && o.Id == "AllRelaxed"));

        string text;
        if (single.Count > 0)
            text = $"放寬以下任一項即可找到路徑：{string.Join("、", single.Select(o => o.Name))}。";
        else if (allInconclusive)
            text = "放寬全部條件後的搜尋達到搜尋上限，無法判定是否有解。";
        else if (all is { Succeeds: true })
            text = "單獨放寬任何一項都無法解決，但同時放寬全部條件後有解：需要多項一起調整。";
        else if (all != null)
            text = "即使放寬全部條件仍無解：路徑在幾何上被阻隔（封閉空間、障礙物完全擋住，或接口在障礙物內）。";
        else
            text = "放寬已嘗試的條件都無法解決。";
        if (inconclusive > 0) text += $"（有 {inconclusive} 項因達到搜尋上限而無法判定）";
        return text;
    }
}

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

    /// <summary>
    /// 這條管線在連續座標下的規則檢查報告（對障礙物、維修區、最小直管等做精確檢查；見 <see cref="RouteResultExtensions.WithRuleReport"/>）。
    /// 佈線本身不會填它；失敗的結果沒有。
    /// </summary>
    public RuleReport? RuleReport { get; init; }

    public static RouteResult Fail(string lineId, RouteFailure failure, RouteSearchInfo search, GridSnap? snap = null) =>
        new(lineId, RouteStatus.NoPath, failure, Array.Empty<Vec3>(), Array.Empty<CellIndex>(), null, snap, search);
}
