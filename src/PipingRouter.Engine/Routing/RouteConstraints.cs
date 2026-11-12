using PipingRouter.Engine.Rules;

namespace PipingRouter.Engine.Routing;

/// <summary>
/// 佈線時要遵守的規則值（來自規則設定，不是成本權重）。
/// 最小直管長度只約束「兩個彎頭之間」：起點接口段與抵達終點的最後一段不受限；以彎頭頂點中心到中心量測。
/// EnforceArrivalDirection = false 時不要求終點的抵達方向（任何方向抵達終點格都算到達），
/// 只用於失敗診斷（「放寬抵達方向後是否有解」），正式佈線一律為 true。
/// </summary>
public sealed record RouteConstraints(double MinStraightBetweenBendsMm, bool EnforceArrivalDirection = true)
{
    public static RouteConstraints None { get; } = new(0);

    public static RouteConstraints From(RuleConfig rules) => new(rules.MinStraightBetweenBendsMm);

    /// <summary>
    /// 兩彎頭之間至少要走幾步。向上取整（保守）：N = max(1, ceil(最小直管 / 格子大小))，
    /// 因此實際長度 N × 格子 ≥ 最小直管，規則檢查器必然通過；代價是格子不整除時會稍微多繞。
    /// </summary>
    public int MinStraightCells(double cellSizeMm)
    {
        if (!(MinStraightBetweenBendsMm > 0)) return 1;
        double n = Math.Ceiling(MinStraightBetweenBendsMm / cellSizeMm - 1e-9);
        return n < 1 ? 1 : n > int.MaxValue ? int.MaxValue : (int)n;
    }
}
