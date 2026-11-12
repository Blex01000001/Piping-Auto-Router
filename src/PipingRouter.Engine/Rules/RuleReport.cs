using PipingRouter.Engine.Geometry;

namespace PipingRouter.Engine.Rules;

/// <summary>數值限值：Min／Max 為 null 表示該側無限制（邊界值本身算通過）。</summary>
public sealed record RuleLimit(double? Min, double? Max);

/// <summary>
/// 一筆檢查結果。違規與通過都會輸出：通過的項目讓檢視器能顯示餘裕。
/// Location 是供 3D 檢視器標示的位置（管線中心線上的點，或手輪中心）。
/// Measured／Limit 為量到的數值與限值；Severity 在通過時沒有意義，仍填入該規則的設定值。
/// </summary>
public sealed record RuleCheckResult(
    string RuleId,
    bool IsViolation,
    Severity Severity,
    IReadOnlyList<string> ObjectIds,
    Vec3 Location,
    double? Measured,
    RuleLimit? Limit,
    string Unit,
    string Message);

public sealed class RuleReport
{
    public IReadOnlyList<RuleCheckResult> Results { get; }

    public RuleReport(IReadOnlyList<RuleCheckResult> results) => Results = results;

    public IEnumerable<RuleCheckResult> Violations => Results.Where(r => r.IsViolation);
    public int ErrorCount => Violations.Count(r => r.Severity == Severity.Error);
    public int WarningCount => Violations.Count(r => r.Severity == Severity.Warning);
    public bool HasErrors => ErrorCount > 0;
}
