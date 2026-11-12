using PipingRouter.Engine;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Rules;
using PipingRouter.Engine.Scene;
using Xunit;
using static PipingRouter.Tests.Rules.RuleTestKit;

namespace PipingRouter.Tests.Rules;

/// <summary>
/// 範例規則：OD=100 → D=100。flowMeter 長 400、上游 10D=1000／下游 5D=500；
/// controlValve 長 500、2D／3D；checkValve 長 250、0D／0D；handValve 長 300、手輪高度 900–1500、
/// 手輪偏移 (0,0,250)、淨空圓柱 R=300 H=200。
/// </summary>
public class ComponentRuleTests
{
    // 長 3000 的水平直管，z=1000
    private static PipeDef Long(string id = "P1", double y = 0, double z = 1000, double od = 100, double ins = 0,
        params PlacedComponent[] comps) =>
        Pipe(id, od, ins, new[] { V(0, y, z), V(3000, y, z) }, comps);

    // ------------------------------------------------------------ 流量計直管段
    [Fact]
    public void FlowMeter_EnoughStraightRun_Passes()
    {
        var report = Check(Scene(pipes: new[] { Long(comps: Comp("FM", "flowMeter", 0, 1500)) }));
        Assert.Empty(report.Violations);
        var runs = Of(report, RuleIds.FlowMeterStraightRun);
        Assert.Equal(2, runs.Count);
        Assert.Equal(1300, runs[0].Measured!.Value, 6);    // 上游：0 → 1300（本體前端）
        Assert.Equal(1300, runs[1].Measured!.Value, 6);    // 下游：1700 → 3000
        Assert.Equal(1000, runs[0].Limit!.Min);
        Assert.Equal(500, runs[1].Limit!.Min);
    }

    [Theory]
    [InlineData(1200, false)]    // 上游剛好 1200−200=1000 = 10D：通過
    [InlineData(1199, true)]     // 上游 999
    public void FlowMeter_UpstreamBoundary(double position, bool violation)
    {
        var report = Check(Scene(pipes: new[] { Long(comps: Comp("FM", "flowMeter", 0, position)) }));
        var v = report.Violations.SingleOrDefault();
        Assert.Equal(violation, v != null);
        if (v != null)
        {
            Assert.Equal(RuleIds.FlowMeterStraightRun, v.RuleId);
            Assert.Equal(position - 200, v.Measured!.Value, 6);
            Assert.Equal(1000, v.Limit!.Min);
            Assert.Equal(new[] { "P1", "FM" }, v.ObjectIds);
            Assert.Equal((position - 200) / 2, v.Location.X, 6);   // 標在不足的那段直管中央
        }
    }

    [Theory]
    [InlineData(2300, false)]    // 下游 3000−2500=500 = 5D
    [InlineData(2301, true)]
    public void FlowMeter_DownstreamBoundary(double position, bool violation)
    {
        var report = Check(Scene(pipes: new[] { Long(comps: Comp("FM", "flowMeter", 0, position)) }));
        Assert.Equal(violation, report.Violations.Any());
        if (violation) Assert.Equal(499, Assert.Single(report.Violations).Measured!.Value, 6);
    }

    [Fact]
    public void StraightRun_EpsilonTolerance_IsApplied()
    {
        // 上游剛好差 0.4 mm：epsilon=0.5 視為通過，epsilon=1e-6 為違規
        var scene = Scene(pipes: new[] { Long(comps: Comp("FM", "flowMeter", 0, 1199.6)) });
        Assert.Empty(Check(scene, Example with { EpsilonMm = 0.5 }).Violations);
        Assert.Single(Check(scene).Violations);
    }

    [Fact]
    public void FlowMeter_BendInsideUpstreamRun_IsViolation()
    {
        // 彎頭在 s=500；流量計在第二段（沿 Y）距彎頭 800 處，上游直管 = 800−200 = 600
        var pipe = Pipe("P1", 100, 0, new[] { V(0, 0, 1000), V(500, 0, 1000), V(500, 2000, 1000) },
            Comp("FM", "flowMeter", 1, 800));
        var v = Assert.Single(Check(Scene(pipes: new[] { pipe })).Violations);
        Assert.Equal(600, v.Measured!.Value, 6);
        Assert.Equal(V(500, 300, 1000), v.Location);
    }

    [Fact]
    public void FlowMeter_UpstreamBlockedByOtherComponent()
    {
        // 手閥（長 300）在 s=900（本體 750–1050），流量計在 s=1500（本體 1300–1700）
        var pipe = Long(comps: new[] { Comp("HV", "handValve", 0, 900), Comp("FM", "flowMeter", 0, 1500) });
        var report = Check(Scene(pipes: new[] { pipe }));
        var v = Assert.Single(report.Violations);
        Assert.Equal(RuleIds.FlowMeterStraightRun, v.RuleId);
        Assert.Equal(250, v.Measured!.Value, 6);           // 1300 − 1050
        Assert.Equal(new[] { "P1", "FM", "HV" }, v.ObjectIds);
        Assert.Contains("HV", v.Message);
    }

    [Fact]
    public void FlowMeter_DownstreamBlockedByOtherComponent()
    {
        var pipe = Long(comps: new[] { Comp("FM", "flowMeter", 0, 1500), Comp("CV", "controlValve", 0, 2000) });
        // 控制閥本體 1750–2250；流量計下游 = 1750 − 1700 = 50 < 500
        var report = Check(Scene(pipes: new[] { pipe }));
        var v = Assert.Single(report.Violations, x => x.RuleId == RuleIds.FlowMeterStraightRun);
        Assert.Equal(50, v.Measured!.Value, 6);
        Assert.Equal(new[] { "P1", "FM", "CV" }, v.ObjectIds);
    }

    [Fact]
    public void PipeEndsLimitTheRun_NothingAssumedBeyond()
    {
        // 流量計靠近管線起點：上游只有 100，不假設管線之外還有直管
        var report = Check(Scene(pipes: new[] { Long(comps: Comp("FM", "flowMeter", 0, 300)) }));
        Assert.Equal(100, Assert.Single(report.Violations).Measured!.Value, 6);
    }

    [Fact]
    public void RequirementScalesWithPipeDiameter()
    {
        // OD=200 → 上游需 2000；位置 1500 → 上游 1300 不足；下游需 1000，1300 足夠
        var report = Check(Scene(pipes: new[] { Long(od: 200, comps: Comp("FM", "flowMeter", 0, 1500)) }));
        var v = Assert.Single(report.Violations);
        Assert.Equal(2000, v.Limit!.Min);
    }

    [Fact]
    public void ThroughCollinearVertex_RunContinues()
    {
        var pipe = Pipe("P1", 100, 0, new[] { V(0, 0, 1000), V(1000, 0, 1000), V(3000, 0, 1000) },
            Comp("FM", "flowMeter", 1, 500));                 // s=1500
        var report = Check(Scene(pipes: new[] { pipe }));
        Assert.Empty(report.Violations);                      // 共線頂點不截斷直管
        Assert.Equal(1300, Of(report, RuleIds.FlowMeterStraightRun)[0].Measured!.Value, 6);
    }

    // ------------------------------------------------------------ 控制閥／其他元件
    [Fact]
    public void ControlValve_UsesItsOwnRuleAndMultiples()
    {
        var report = Check(Scene(pipes: new[] { Long(comps: Comp("CV", "controlValve", 0, 1500)) }));
        Assert.Empty(report.Violations);
        var runs = Of(report, RuleIds.ControlValveStraightRun);
        Assert.Equal(new[] { 200.0, 300.0 }, runs.Select(r => r.Limit!.Min!.Value).ToArray());   // 2D、3D
        Assert.Equal(1250, runs[0].Measured!.Value, 6);       // 本體 1250–1750
    }

    [Theory]
    [InlineData(450, false)]     // 上游 450−250=200 = 2D
    [InlineData(449, true)]
    public void ControlValve_UpstreamBoundary(double pos, bool violation)
    {
        var report = Check(Scene(pipes: new[] { Long(comps: Comp("CV", "controlValve", 0, pos)) }));
        Assert.Equal(violation, report.Violations.Any(v => v.RuleId == RuleIds.ControlValveStraightRun));
    }

    [Fact]
    public void CheckValve_ZeroRequirement_StillMustNotCrossABend()
    {
        // 彎頭在 s=100；逆止閥中心 s=150、長 250（本體 25–275），本體壓過彎頭
        var pipe = Pipe("P1", 100, 0, new[] { V(0, 0, 1000), V(100, 0, 1000), V(100, 2000, 1000) },
            Comp("CK", "checkValve", 1, 50));
        var v = Assert.Single(Check(Scene(pipes: new[] { pipe })).Violations);
        Assert.Equal(RuleIds.ComponentStraightRun, v.RuleId);
        Assert.Equal(-75, v.Measured!.Value, 6);
        Assert.Equal(0, v.Limit!.Min);
    }

    [Fact]
    public void CheckValve_FitsInStraightPipe_Passes()
    {
        var report = Check(Scene(pipes: new[] { Long(comps: Comp("CK", "checkValve", 0, 1500)) }));
        Assert.Empty(report.Violations);
        Assert.All(Of(report, RuleIds.ComponentStraightRun), r => Assert.False(r.IsViolation));
    }

    [Fact]
    public void StraightRunSeverity_FollowsConfig()
    {
        var rules = Example with
        {
            Severities = new Dictionary<string, Severity>(Example.Severities) { [RuleIds.FlowMeterStraightRun] = Severity.Warning },
        };
        var report = Check(Scene(pipes: new[] { Long(comps: Comp("FM", "flowMeter", 0, 300)) }), rules);
        Assert.Equal(Severity.Warning, Assert.Single(report.Violations).Severity);
    }

    // ------------------------------------------------------------ 手輪高度
    private static RuleReport HandValveAt(double pipeZ, double floorZ = 0, Vec3[]? pts = null) =>
        Check(Scene(pipes: new[]
        {
            Pipe("P1", 100, 0, pts ?? new[] { V(0, 0, pipeZ), V(3000, 0, pipeZ) }, Comp("HV", "handValve", 0, 1500)),
        }, floorZ: floorZ));

    [Theory]
    [InlineData(1000, false, 1250)]
    [InlineData(650, false, 900)]      // 手輪中心剛好 900：通過
    [InlineData(1250, false, 1500)]    // 剛好 1500：通過
    [InlineData(649, true, 899)]
    [InlineData(1251, true, 1501)]
    public void HandwheelHeight_RangeBoundaries(double pipeZ, bool violation, double expectedHeight)
    {
        var report = HandValveAt(pipeZ);
        var r = One(report, RuleIds.HandwheelHeight);
        Assert.Equal(violation, r.IsViolation);
        Assert.Equal(expectedHeight, r.Measured!.Value, 6);
        Assert.Equal(900, r.Limit!.Min);
        Assert.Equal(1500, r.Limit.Max);
        Assert.Equal(V(1500, 0, pipeZ + 250), r.Location);          // 位置 = 手輪中心
        Assert.Equal(new[] { "P1", "HV" }, r.ObjectIds);
    }

    [Fact]
    public void HandwheelHeight_IsRelativeToFloor()
    {
        // floorZ=100：管 z=1000 → 手輪中心 1250 → 離地 1150
        var r = One(HandValveAt(1000, floorZ: 100), RuleIds.HandwheelHeight);
        Assert.Equal(1150, r.Measured!.Value, 6);
        // floorZ=500 → 離地 750 < 900
        Assert.True(One(HandValveAt(1000, floorZ: 500), RuleIds.HandwheelHeight).IsViolation);
    }

    [Fact]
    public void HandwheelHeight_WorksOnPipeAlongY()
    {
        var report = HandValveAt(1000, pts: new[] { V(0, 0, 1000), V(0, 3000, 1000) });
        Assert.False(One(report, RuleIds.HandwheelHeight).IsViolation);
        Assert.Equal(V(0, 1500, 1250), One(report, RuleIds.HandwheelHeight).Location);
    }

    [Fact]
    public void ValveOnVerticalPipe_IsUnsupportedWarning()
    {
        var report = HandValveAt(0, pts: new[] { V(0, 0, 0), V(0, 0, 3000) });
        Assert.Empty(Of(report, RuleIds.HandwheelHeight));
        Assert.Empty(Of(report, RuleIds.HandwheelClearance));
        var u = Assert.Single(Of(report, RuleIds.UnsupportedComponent));
        Assert.True(u.IsViolation);
        Assert.Equal(Severity.Warning, u.Severity);
        Assert.False(report.HasErrors);
        Assert.Equal(new[] { "P1", "HV" }, u.ObjectIds);
    }

    [Fact]
    public void ValveOnSlopedPipe_IsUnsupported()
    {
        var report = HandValveAt(0, pts: new[] { V(0, 0, 0), V(3000, 0, 300) });
        Assert.Single(Of(report, RuleIds.UnsupportedComponent));
        Assert.Empty(Of(report, RuleIds.HandwheelHeight));
    }

    [Fact]
    public void UnknownComponentType_IsUnsupported_NotACrash()
    {
        var pipe = Long(comps: Comp("X1", "mysteryDevice", 0, 1000));
        var report = Check(Scene(pipes: new[] { pipe }));
        var u = Assert.Single(report.Violations);
        Assert.Equal(RuleIds.UnsupportedComponent, u.RuleId);
        Assert.Equal(Severity.Warning, u.Severity);
        Assert.Contains("mysteryDevice", u.Message);
        Assert.Equal(V(1000, 0, 1000), u.Location);
    }

    [Fact]
    public void InvalidSegmentIndex_IsUnsupported_NotACrash()
    {
        var pipe = Long(comps: Comp("X1", "handValve", 5, 100));
        var u = Assert.Single(Check(Scene(pipes: new[] { pipe })).Violations);
        Assert.Equal(RuleIds.UnsupportedComponent, u.RuleId);
    }

    // ------------------------------------------------------------ 手輪淨空
    // 閥在 (1500,0,1000)，手輪中心 (1500,0,1250)；淨空圓柱 R=300、z∈[1150,1350]
    private static RuleReport ClearanceWith(Obstacle? obstacle = null, PipeDef? other = null, double ownIns = 0,
        RuleConfig? rules = null)
    {
        var own = Pipe("P1", 100, ownIns, new[] { V(0, 0, 1000), V(3000, 0, 1000) }, Comp("HV", "handValve", 0, 1500));
        var pipes = new List<PipeDef> { own };
        if (other != null) pipes.Add(other);
        return Check(Scene(obstacle != null ? new[] { obstacle } : null, pipes: pipes), rules);
    }

    [Fact]
    public void Clearance_Empty_Passes()
    {
        var r = One(ClearanceWith(), RuleIds.HandwheelClearance);
        Assert.False(r.IsViolation);
        Assert.Equal(0, r.Measured!.Value);
        Assert.Equal(0, r.Limit!.Max);
        Assert.Equal(V(1500, 0, 1250), r.Location);
    }

    [Fact]
    public void Clearance_ObstacleInsideZone_IsViolation()
    {
        var report = ClearanceWith(Box("EQ", 1700, -100, 1200, 2000, 100, 1300));    // 距手輪中心 x=200 < 300
        var r = One(report, RuleIds.HandwheelClearance);
        Assert.True(r.IsViolation);
        Assert.Equal(1, r.Measured!.Value);
        Assert.Equal(new[] { "P1", "HV", "EQ" }, r.ObjectIds);
        Assert.Equal(Severity.Error, r.Severity);
    }

    [Theory]
    [InlineData(1800, false)]    // 方塊面距手輪中心剛好 300 = 淨空半徑：貼合，通過
    [InlineData(1799, true)]
    public void Clearance_RadialBoundary(double boxMinX, bool violation)
    {
        var report = ClearanceWith(Box("EQ", boxMinX, -100, 1200, boxMinX + 300, 100, 1300));
        Assert.Equal(violation, One(report, RuleIds.HandwheelClearance).IsViolation);
    }

    [Theory]
    [InlineData(1350, false)]    // 方塊底面貼著淨空區頂面
    [InlineData(1349, true)]
    public void Clearance_VerticalBoundary(double boxMinZ, bool violation)
    {
        var report = ClearanceWith(Box("EQ", 1400, -100, boxMinZ, 1600, 100, boxMinZ + 500));
        Assert.Equal(violation, One(report, RuleIds.HandwheelClearance).IsViolation);
    }

    [Fact]
    public void Clearance_CylinderObstacle_AnyAxis()
    {
        // 沿 X 的細桿容器穿過手輪上方（z=1250）
        var report = ClearanceWith(Cyl("V", 1000, 0, 1250, 50, 1000, Axis.X));
        Assert.True(One(report, RuleIds.HandwheelClearance).IsViolation);
        // 沿 Z 的立柱貼近手輪
        report = ClearanceWith(Cyl("V", 1700, 0, 0, 100, 3000, Axis.Z));
        Assert.True(One(report, RuleIds.HandwheelClearance).IsViolation);
        report = ClearanceWith(Cyl("V", 1900, 0, 0, 100, 3000, Axis.Z));            // 距離 400−100=300，剛好外切
        Assert.False(One(report, RuleIds.HandwheelClearance).IsViolation);
    }

    [Fact]
    public void Clearance_OtherPipeThroughZone_IsViolation()
    {
        var other = Pipe("P2", 100, 0, new[] { V(1500, -1000, 1250), V(1500, 1000, 1250) });
        var r = One(ClearanceWith(other: other), RuleIds.HandwheelClearance);
        Assert.True(r.IsViolation);
        Assert.Equal(new[] { "P1", "HV", "P2" }, r.ObjectIds);
    }

    [Fact]
    public void Clearance_OtherPipeFarAway_Passes()
    {
        var other = Pipe("P2", 100, 0, new[] { V(1500, 800, 1250), V(1500, 1800, 1250) });
        Assert.False(One(ClearanceWith(other: other), RuleIds.HandwheelClearance).IsViolation);
    }

    [Fact]
    public void Clearance_OwnPipeIsExcluded_EvenWithThickInsulation()
    {
        // 自己的管線加厚保溫（半徑 450）會碰到淨空區，但依約定排除該元件自己的管
        var report = ClearanceWith(ownIns: 400);
        Assert.False(One(report, RuleIds.HandwheelClearance).IsViolation);
    }

    [Fact]
    public void Clearance_BoxShapedZone_FromConfig()
    {
        var spec = Example.ComponentTypes["handValve"];
        var rules = Example with
        {
            ComponentTypes = new Dictionary<string, ComponentTypeSpec>(Example.ComponentTypes)
            {
                ["handValve"] = spec with
                {
                    Handwheel = spec.Handwheel! with { ClearanceZone = new BoxClearanceZone(V(600, 600, 200)) },
                },
            },
        };
        // 方形淨空：x∈[1200,1800]；方塊貼在 x=1800 → 通過；x=1799 → 侵入
        Assert.False(One(ClearanceWith(Box("EQ", 1800, -100, 1200, 2000, 100, 1300), rules: rules), RuleIds.HandwheelClearance).IsViolation);
        Assert.True(One(ClearanceWith(Box("EQ", 1799, -100, 1200, 2000, 100, 1300), rules: rules), RuleIds.HandwheelClearance).IsViolation);
        // 圓柱淨空在對角方向 (250,250) 不會碰，但方形會：方塊 x∈[1700,1750]、y∈[250,290]
        Assert.True(One(ClearanceWith(Box("EQ", 1700, 250, 1200, 1750, 290, 1300), rules: rules), RuleIds.HandwheelClearance).IsViolation);
        Assert.False(One(ClearanceWith(Box("EQ", 1700, 250, 1200, 1750, 290, 1300)), RuleIds.HandwheelClearance).IsViolation);
    }

    // ------------------------------------------------------------ 確定性
    [Fact]
    public void SameInput_SameOutput_InSameOrder()
    {
        var pipe = Long(comps: new[] { Comp("HV", "handValve", 0, 900), Comp("FM", "flowMeter", 0, 1500), Comp("CV", "controlValve", 0, 2200) });
        var other = Pipe("P2", 100, 0, new[] { V(1500, -1000, 1250), V(1500, 1000, 1250) });
        var scene = Scene(new[] { Box("EQ", 0, 140, 0, 3000, 400, 2000) }, pipes: new[] { pipe, other });
        Assert.Equal(Dump(Check(scene)), Dump(Check(scene)));
        Assert.True(Check(scene).Results.Count > 10);
    }
}
