using PipingRouter.Engine;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Io;
using PipingRouter.Engine.Rules;
using PipingRouter.Engine.Scene;
using Xunit;
using static PipingRouter.Tests.Rules.RuleTestKit;

namespace PipingRouter.Tests.Rules;

/// <summary>
/// 第 4 步驗收：samples/scenes/s15_manual_pipes.json 恰好含六種違規，
/// 每一項的物件、數值、限值、位置都正確；合格的 P-OK 沒有任何誤報。
/// </summary>
public class ManualPipesSampleTests
{
    private static readonly SceneModel Scene15 =
        SceneJson.Load(RepoPaths.Combine("samples", "scenes", "s15_manual_pipes.json")).ValueOrThrow();

    private static RuleReport Report() => RuleChecker.Check(Scene15, Example);

    [Fact]
    public void ExactlySixViolations_AllErrors()
    {
        var report = Report();
        Assert.Equal(6, report.Violations.Count());
        Assert.Equal(6, report.ErrorCount);
        Assert.Equal(0, report.WarningCount);
        Assert.True(report.HasErrors);
        Assert.Equal(
            new[]
            {
                RuleIds.Collision, RuleIds.FlowMeterStraightRun, RuleIds.HandwheelHeight,
                RuleIds.MinClearance, RuleIds.MinStraightBetweenBends, RuleIds.ServiceZoneIntrusion,
            },
            report.Violations.Select(v => v.RuleId).OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void CleanPipe_HasNoViolationsAndNoFalseAlarms()
    {
        var report = Report();
        Assert.DoesNotContain(report.Violations, v => v.ObjectIds.Contains("P-OK") || v.ObjectIds.Contains("HV-OK"));
        var height = Of(report, RuleIds.HandwheelHeight).Single(r => r.ObjectIds[1] == "HV-OK");
        Assert.False(height.IsViolation);
        Assert.Equal(1450, height.Measured!.Value, 6);
        var clearance = Of(report, RuleIds.HandwheelClearance).Single(r => r.ObjectIds[1] == "HV-OK");
        Assert.False(clearance.IsViolation);
    }

    [Fact]
    public void MinClearance_PipeToEquipment()
    {
        var v = Single(RuleIds.MinClearance);
        Assert.Equal(new[] { "P-CLEAR", "EQ-01" }, v.ObjectIds);
        Assert.Equal(90, v.Measured!.Value, 6);                // 3640 − 3500 − 50
        Assert.Equal(100, v.Limit!.Min);
        Assert.Equal(3500, v.Location.Y, 6);
        Assert.Equal(1000, v.Location.Z, 6);
        Assert.InRange(v.Location.X, 1500, 2500);
    }

    [Fact]
    public void Collision_PipeThroughVessel()
    {
        var v = Single(RuleIds.Collision);
        Assert.Equal(new[] { "P-COLLIDE", "V-101" }, v.ObjectIds);
        Assert.Equal(-50, v.Measured!.Value, 6);
        Assert.Equal(0, v.Limit!.Min);
        Assert.Equal(2000, v.Location.Y, 6);
        Assert.InRange(v.Location.X, 6400 - 1e-6, 7600 + 1e-6);   // 三分搜尋在「距離為 0」的平台上收斂到邊緣
    }

    [Fact]
    public void ServiceZoneIntrusion_Maintenance()
    {
        var v = Single(RuleIds.ServiceZoneIntrusion);
        Assert.Equal(new[] { "P-ZONE", "MZ-01" }, v.ObjectIds);
        Assert.Equal(-50, v.Measured!.Value, 6);
        Assert.Equal(4500, v.Location.Y, 6);
        Assert.InRange(v.Location.X, 9000, 10000);
    }

    [Fact]
    public void MinStraightBetweenBends_ShortStep()
    {
        var v = Single(RuleIds.MinStraightBetweenBends);
        Assert.Equal(new[] { "P-BENDS" }, v.ObjectIds);
        Assert.Equal(200, v.Measured!.Value, 6);
        Assert.Equal(300, v.Limit!.Min);
        Assert.Equal(new Vec3(1500, 6100, 1000), v.Location);
    }

    [Fact]
    public void FlowMeter_InsufficientUpstream()
    {
        var v = Single(RuleIds.FlowMeterStraightRun);
        Assert.Equal(new[] { "P-FLOW", "FM-1" }, v.ObjectIds);
        Assert.Equal(600, v.Measured!.Value, 6);               // 本體前端 s=600，管線起點 s=0
        Assert.Equal(1000, v.Limit!.Min);
        Assert.Equal(new Vec3(800, 7000, 1500), v.Location);   // 不足那段直管的中央（s=300）
        // 下游 3000 − 1000 = 2000 ≥ 500，通過
        var down = Of(Report(), RuleIds.FlowMeterStraightRun).Single(r => !r.IsViolation);
        Assert.Equal(2000, down.Measured!.Value, 6);
    }

    [Fact]
    public void HandwheelHeight_TooHigh()
    {
        var v = Single(RuleIds.HandwheelHeight);
        Assert.Equal(new[] { "P-HEIGHT", "HV-HI" }, v.ObjectIds);
        Assert.Equal(1650, v.Measured!.Value, 6);
        Assert.Equal(900, v.Limit!.Min);
        Assert.Equal(1500, v.Limit.Max);
        Assert.Equal(new Vec3(2000, 2000, 1650), v.Location);
    }

    [Fact]
    public void Output_IsDeterministic()
    {
        Assert.Equal(Dump(Report()), Dump(Report()));
    }

    private static RuleCheckResult Single(string ruleId) =>
        Assert.Single(Report().Violations, v => v.RuleId == ruleId);
}
