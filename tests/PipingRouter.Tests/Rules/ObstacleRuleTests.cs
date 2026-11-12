using PipingRouter.Engine;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Rules;
using PipingRouter.Engine.Scene;
using Xunit;
using static PipingRouter.Tests.Rules.RuleTestKit;

namespace PipingRouter.Tests.Rules;

/// <summary>干涉與最小淨距。範例規則：管線—障礙最小淨距 100 mm。管 OD 100 → 半徑 50。</summary>
public class ObstacleRuleTests
{
    // 管沿 X，y=0、z=1000，x 從 0 到 1000
    private static PipeDef Straight(double od = 100, double ins = 0) =>
        Pipe("P1", od, ins, new[] { V(0, 0, 1000), V(1000, 0, 1000) });

    private static SceneModel WithBoxAtY(double yMin, double od = 100, double ins = 0) =>
        Scene(new[] { Box("EQ", 0, yMin, 0, 1000, yMin + 500, 2000) }, pipes: new[] { Straight(od, ins) });

    [Fact]
    public void FarAway_PassesWithTightestGap()
    {
        var report = Check(WithBoxAtY(1000));          // 距離 1000 − 50 = 950
        Assert.Empty(report.Violations);
        var col = One(report, RuleIds.Collision);
        var clr = One(report, RuleIds.MinClearance);
        Assert.False(col.IsViolation);
        Assert.False(clr.IsViolation);
        Assert.Equal(950, clr.Measured!.Value, 6);
        Assert.Equal(100, clr.Limit!.Min);
        Assert.Equal(new[] { "P1", "EQ" }, clr.ObjectIds);
        Assert.Equal("mm", clr.Unit);
    }

    [Fact]
    public void ClearanceExactlyEqualToMinimum_Passes()
    {
        var report = Check(WithBoxAtY(150));           // 150 − 50 = 100 = 限值
        Assert.Empty(report.Violations);
        Assert.Equal(100, One(report, RuleIds.MinClearance).Measured!.Value, 6);
    }

    [Fact]
    public void ClearanceJustBelowMinimum_IsViolation()
    {
        var report = Check(WithBoxAtY(149.9));
        var v = Assert.Single(report.Violations);
        Assert.Equal(RuleIds.MinClearance, v.RuleId);
        Assert.Equal(Severity.Error, v.Severity);
        Assert.Equal(99.9, v.Measured!.Value, 6);
        Assert.Equal(100, v.Limit!.Min);
        Assert.Equal(new[] { "P1", "EQ" }, v.ObjectIds);
        // 位置在管中心線上
        Assert.Equal(0, v.Location.Y, 9);
        Assert.Equal(1000, v.Location.Z, 9);
        Assert.InRange(v.Location.X, 0, 1000);
        // 有淨距違規時不產生 MinClearance 通過紀錄，但沒有干涉所以 Collision 有通過紀錄
        Assert.False(One(report, RuleIds.Collision).IsViolation);
    }

    [Fact]
    public void InsulationThickness_CountsTowardRadius()
    {
        // OD 100 + 保溫 50 → 半徑 100；距離 150 → 間隙 50 < 100
        var report = Check(WithBoxAtY(150, od: 100, ins: 50));
        var v = Assert.Single(report.Violations);
        Assert.Equal(RuleIds.MinClearance, v.RuleId);
        Assert.Equal(50, v.Measured!.Value, 6);
    }

    [Fact]
    public void PipePassingThroughBox_IsCollision()
    {
        var pipe = Pipe("P1", 100, 0, new[] { V(-500, 0, 1000), V(1500, 0, 1000) });
        var scene = Scene(new[] { Box("EQ", 0, -500, 0, 1000, 500, 2000) }, pipes: new[] { pipe });
        var report = Check(scene);
        var v = Assert.Single(report.Violations);
        Assert.Equal(RuleIds.Collision, v.RuleId);
        Assert.Equal(-50, v.Measured!.Value, 6);       // 中心線距離 0 − 半徑 50
        Assert.Equal(0, v.Limit!.Min);
        Assert.InRange(v.Location.X, 0, 1000);
        Assert.Equal(1000, v.Location.Z, 9);
        // 干涉時不會再有 MinClearance 紀錄（避免重複回報同一件事）
        Assert.Empty(Of(report, RuleIds.MinClearance));
    }

    [Fact]
    public void TouchingSurface_IsNotCollision_WhenMinClearanceIsZero()
    {
        var rules = Example with { Clearance = new ClearanceConfig(0, 0) };
        var report = Check(WithBoxAtY(50), rules);     // 間隙剛好 0
        Assert.Empty(report.Violations);
        Assert.Equal(0, One(report, RuleIds.Collision).Measured!.Value, 6);
    }

    [Fact]
    public void Penetration_IsCollisionEvenWithinEpsilonLimit()
    {
        var rules = Example with { Clearance = new ClearanceConfig(0, 0) };
        var report = Check(WithBoxAtY(49), rules);     // 穿透 1 mm
        Assert.Equal(RuleIds.Collision, Assert.Single(report.Violations).RuleId);
    }

    [Theory]
    [InlineData(300, true)]     // 圓柱側面距管中心 300−100=200 → 間隙 150
    [InlineData(250, true)]     // 間隙剛好 100
    [InlineData(249, false)]    // 間隙 99
    public void VerticalCylinderObstacle_Clearance(double baseY, bool passes)
    {
        var scene = Scene(new[] { Cyl("V1", 500, baseY, 0, 100, 2000) }, pipes: new[] { Straight() });
        var report = Check(scene);
        Assert.Equal(passes, !report.Violations.Any());
        if (!passes)
        {
            var v = Assert.Single(report.Violations);
            Assert.Equal(RuleIds.MinClearance, v.RuleId);
            Assert.Equal(99, v.Measured!.Value, 5);
        }
    }

    [Fact]
    public void PipeThroughCylinder_IsCollision()
    {
        var scene = Scene(new[] { Cyl("V1", 500, 0, 0, 100, 2000) }, pipes: new[] { Straight() });
        var v = Assert.Single(Check(scene).Violations);
        Assert.Equal(RuleIds.Collision, v.RuleId);
        Assert.Equal(-50, v.Measured!.Value, 5);
    }

    [Fact]
    public void HorizontalCylinderObstacle_AlongX()
    {
        // 臥式容器沿 X，軸心 y=400、z=1000，半徑 300 → 表面距管 100 → 間隙 50
        var scene = Scene(new[] { Cyl("V2", 0, 400, 1000, 300, 1000, Axis.X) }, pipes: new[] { Straight() });
        var v = Assert.Single(Check(scene).Violations);
        Assert.Equal(RuleIds.MinClearance, v.RuleId);
        Assert.Equal(50, v.Measured!.Value, 5);
    }

    [Fact]
    public void TightestObstacle_IsReported_InPassRecord()
    {
        var scene = Scene(new[]
        {
            Box("FAR", 0, 2000, 0, 1000, 2500, 2000),
            Box("NEAR", 0, 600, 0, 1000, 900, 2000),
        }, pipes: new[] { Straight() });
        var report = Check(scene);
        Assert.Empty(report.Violations);
        var clr = One(report, RuleIds.MinClearance);
        Assert.Equal(new[] { "P1", "NEAR" }, clr.ObjectIds);
        Assert.Equal(550, clr.Measured!.Value, 6);
    }

    [Fact]
    public void MultipleViolations_AreAllListed_InObstacleOrder()
    {
        var scene = Scene(new[]
        {
            Box("A", 0, 140, 0, 1000, 500, 2000),     // 淨距違規
            Box("B", 0, -500, 0, 1000, -140, 2000),   // 淨距違規
        }, pipes: new[] { Straight() });
        var report = Check(scene);
        Assert.Equal(new[] { "A", "B" }, Of(report, RuleIds.MinClearance).Select(x => x.ObjectIds[1]).ToArray());
        Assert.Equal(2, report.ErrorCount);
    }

    [Fact]
    public void NoObstacles_ProducesNoObstacleEntries()
    {
        var report = Check(Scene(pipes: new[] { Straight() }));
        Assert.Empty(report.Results);
    }

    [Fact]
    public void ObstacleInMultiSegmentPipe_FindsTightestSegment()
    {
        var pipe = Pipe("P1", 100, 0, new[] { V(0, 0, 1000), V(1000, 0, 1000), V(1000, 1000, 1000) });
        var scene = Scene(new[] { Box("EQ", 1180, 800, 0, 1500, 1200, 2000) }, pipes: new[] { pipe });
        // 第二段 x=1000，距箱面 180 → 間隙 130，通過
        var report = Check(scene);
        Assert.Empty(report.Violations);
        Assert.Equal(130, One(report, RuleIds.MinClearance).Measured!.Value, 6);
        // 位置落在第二段上（x=1000）
        Assert.Equal(1000, One(report, RuleIds.MinClearance).Location.X, 6);
    }

    [Fact]
    public void ViolationSeverity_FollowsConfig()
    {
        var rules = Example with
        {
            Severities = new Dictionary<string, Severity>(Example.Severities) { [RuleIds.MinClearance] = Severity.Warning },
        };
        var report = Check(WithBoxAtY(149), rules);
        var v = Assert.Single(report.Violations);
        Assert.Equal(Severity.Warning, v.Severity);
        Assert.False(report.HasErrors);
        Assert.Equal(1, report.WarningCount);
    }

    [Fact]
    public void EpsilonConfig_ControlsBoundary()
    {
        var rules = Example with { EpsilonMm = 0.5 };
        Assert.Empty(Check(WithBoxAtY(149.6), rules).Violations);   // 間隙 99.6，容差 0.5 內視為通過
        Assert.Single(Check(WithBoxAtY(149.4), rules).Violations);  // 99.4 超出容差
    }

    [Fact]
    public void Checker_AcceptsPipesPassedExplicitly()
    {
        var scene = Scene(new[] { Box("EQ", 0, 149, 0, 1000, 600, 2000) });
        var report = RuleChecker.Check(scene, Example, new[] { Straight() });
        Assert.Single(report.Violations);
    }
}
