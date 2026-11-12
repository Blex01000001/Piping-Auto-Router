using PipingRouter.Engine;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Rules;
using PipingRouter.Engine.Scene;
using Xunit;
using static PipingRouter.Tests.Rules.RuleTestKit;

namespace PipingRouter.Tests.Rules;

public class ZoneAndPairRuleTests
{
    private static PipeDef Straight(string id = "P1", double y = 0, double z = 1000, double od = 100, double ins = 0) =>
        Pipe(id, od, ins, new[] { V(0, y, z), V(2000, y, z) });

    // ------------------------------------------------------------ 維修／操作區
    [Fact]
    public void PipeThroughMaintenanceZone_IsErrorByDefault()
    {
        var scene = Scene(zones: new[] { Zone("MZ", ServiceZoneKind.Maintenance, 800, -300, 0, 1200, 300, 2000) },
            pipes: new[] { Straight() });
        var report = Check(scene);
        var v = Assert.Single(report.Violations);
        Assert.Equal(RuleIds.ServiceZoneIntrusion, v.RuleId);
        Assert.Equal(Severity.Error, v.Severity);
        Assert.Equal(new[] { "P1", "MZ" }, v.ObjectIds);
        Assert.Equal(-50, v.Measured!.Value, 6);
        Assert.InRange(v.Location.X, 800, 1200);
    }

    [Theory]
    [InlineData(50, false)]      // 管表面剛好貼到區域邊界：通過
    [InlineData(49.9, true)]     // 侵入 0.1
    [InlineData(60, false)]
    public void ZoneBoundary_TouchIsAllowed(double zoneMinY, bool violation)
    {
        var scene = Scene(zones: new[] { Zone("MZ", ServiceZoneKind.Maintenance, 800, zoneMinY, 0, 1200, 500, 2000) },
            pipes: new[] { Straight() });
        Assert.Equal(violation, Check(scene).Violations.Any());
    }

    [Fact]
    public void ZoneNotCrossed_ProducesPassRecord()
    {
        var scene = Scene(zones: new[] { Zone("MZ", ServiceZoneKind.Maintenance, 800, 1000, 0, 1200, 1500, 2000) },
            pipes: new[] { Straight() });
        var report = Check(scene);
        Assert.Empty(report.Violations);
        var r = One(report, RuleIds.ServiceZoneIntrusion);
        Assert.False(r.IsViolation);
        Assert.Equal(950, r.Measured!.Value, 6);
    }

    [Fact]
    public void PenalizePolicy_IsOnlyWarning_AndPolicyIsPerKind()
    {
        var rules = Example with
        {
            ServiceZonePolicy = new ServiceZonePolicyConfig(ZonePolicy.Forbid, ZonePolicy.Penalize),
        };
        var scene = Scene(zones: new[]
        {
            Zone("MZ", ServiceZoneKind.Maintenance, 100, -300, 0, 300, 300, 2000),
            Zone("OZ", ServiceZoneKind.Operation, 1000, -300, 0, 1200, 300, 2000),
        }, pipes: new[] { Straight() });
        var report = Check(scene, rules);
        var mz = Assert.Single(report.Violations, v => v.ObjectIds[1] == "MZ");
        var oz = Assert.Single(report.Violations, v => v.ObjectIds[1] == "OZ");
        Assert.Equal(Severity.Error, mz.Severity);
        Assert.Equal(Severity.Warning, oz.Severity);
        Assert.Equal(1, report.ErrorCount);
        Assert.Equal(1, report.WarningCount);
    }

    // ------------------------------------------------------------ 管對管
    [Theory]
    [InlineData(250, false, 150)]     // 中心距 250 − 100 = 150
    [InlineData(200, false, 100)]     // 剛好等於 100：通過
    [InlineData(199, true, 99)]
    public void ParallelPipes_Clearance(double separation, bool violation, double expectedGap)
    {
        var report = Check(Scene(pipes: new[] { Straight("A", 0), Straight("B", separation) }));
        Assert.Equal(violation, report.Violations.Any());
        if (violation)
        {
            var v = Assert.Single(report.Violations);
            Assert.Equal(RuleIds.MinClearance, v.RuleId);
            Assert.Equal(new[] { "A", "B" }, v.ObjectIds);
            Assert.Equal(expectedGap, v.Measured!.Value, 6);
            Assert.Equal(100, v.Limit!.Min);
            Assert.Equal(separation / 2, v.Location.Y, 6);     // 位置在兩管之間
        }
        else
        {
            Assert.Equal(expectedGap, One(report, RuleIds.MinClearance).Measured!.Value, 6);
        }
    }

    [Fact]
    public void CrossingPipes_AtDifferentHeights_CanCollide()
    {
        var a = Pipe("A", 100, 0, new[] { V(0, 0, 1000), V(2000, 0, 1000) });
        var b = Pipe("B", 100, 0, new[] { V(1000, -1000, 1050), V(1000, 1000, 1050) });   // 垂直交叉，高差 50 < 100
        var v = Assert.Single(Check(Scene(pipes: new[] { a, b })).Violations);
        Assert.Equal(RuleIds.Collision, v.RuleId);
        Assert.Equal(-50, v.Measured!.Value, 6);               // 中心線距離 50 − (50+50)
        Assert.Equal(1000, v.Location.X, 6);
        Assert.Equal(1025, v.Location.Z, 6);
    }

    [Fact]
    public void SinglePipe_HasNoPairEntries()
    {
        var report = Check(Scene(pipes: new[] { Straight() }));
        Assert.Empty(report.Results);
    }

    [Fact]
    public void ThreePipes_ReportOnlyViolatingPairs()
    {
        var report = Check(Scene(pipes: new[] { Straight("A", 0), Straight("B", 150), Straight("C", 2000) }));
        var v = Assert.Single(report.Violations);
        Assert.Equal(new[] { "A", "B" }, v.ObjectIds);
    }
}
