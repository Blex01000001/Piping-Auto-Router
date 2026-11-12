namespace PipingRouter.Engine.Rules;

/// <summary>規則檢查項目的識別字串（規則設定的 severities 與檢查結果的 ruleId 共用）。</summary>
public static class RuleIds
{
    public const string Collision = "Collision";
    public const string MinClearance = "MinClearance";
    public const string HandwheelHeight = "HandwheelHeight";
    public const string HandwheelClearance = "HandwheelClearance";
    public const string FlowMeterStraightRun = "FlowMeterStraightRun";
    public const string ControlValveStraightRun = "ControlValveStraightRun";
    public const string ServiceZoneIntrusion = "ServiceZoneIntrusion";
    public const string MinStraightBetweenBends = "MinStraightBetweenBends";
    public const string UnsupportedComponent = "UnsupportedComponent";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Collision, MinClearance, HandwheelHeight, HandwheelClearance,
        FlowMeterStraightRun, ControlValveStraightRun, ServiceZoneIntrusion,
        MinStraightBetweenBends, UnsupportedComponent,
    };

    /// <summary>未在設定檔指定時的預設嚴重度：不支援的元件屬提醒，其餘為 error。</summary>
    public static Severity DefaultSeverity(string ruleId) =>
        ruleId == UnsupportedComponent ? Severity.Warning : Severity.Error;
}
