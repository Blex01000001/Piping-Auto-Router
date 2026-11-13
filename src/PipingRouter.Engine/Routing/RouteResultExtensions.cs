using PipingRouter.Engine.Rules;
using PipingRouter.Engine.Scene;

namespace PipingRouter.Engine.Routing;

public static class RouteResultExtensions
{
    /// <summary>
    /// 把佈線出來的折線丟給規則檢查器（連續座標、精確公式），報告掛在結果上。失敗的結果原樣回傳。
    /// 只檢查這一條管線對場景裡的障礙物與維修區；場景裡原有的人工管線（scene.Pipes）不納入——佈線也沒有把它們當障礙。
    /// 這是對「格子層通過 ⇒ 精確層通過」的獨立確認：保守式體素化下，成功的路徑不應該有干涉、淨距、維修區或最小直管的違規。
    /// </summary>
    public static RouteResult WithRuleReport(this RouteResult result, SceneModel scene, LineRequest line, RuleConfig rules)
    {
        if (!result.Success) return result;
        var pipe = ToPipe(line, result);
        return result with { RuleReport = RuleChecker.Check(scene, rules, new[] { pipe }) };
    }

    internal static PipeDef ToPipe(LineRequest line, RouteResult result) =>
        new(line.Id, null, line.OuterDiameterMm, line.InsulationThicknessMm, result.Polyline, Array.Empty<PlacedComponent>());
}
