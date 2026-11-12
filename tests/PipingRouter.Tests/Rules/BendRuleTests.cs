using PipingRouter.Engine;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Rules;
using PipingRouter.Engine.Scene;
using Xunit;
using static PipingRouter.Tests.Rules.RuleTestKit;

namespace PipingRouter.Tests.Rules;

/// <summary>兩彎頭之間最小直管（範例：300 mm，頂點中心到中心）。</summary>
public class BendRuleTests
{
    private static RuleReport CheckPts(params Vec3[] pts) =>
        Check(Scene(pipes: new[] { Pipe("P1", 100, 0, pts) }));

    [Fact]
    public void ShortRunBetweenBends_IsViolation()
    {
        var report = CheckPts(V(0, 0, 0), V(1000, 0, 0), V(1000, 200, 0), V(1500, 200, 0));
        var v = Assert.Single(report.Violations);
        Assert.Equal(RuleIds.MinStraightBetweenBends, v.RuleId);
        Assert.Equal(200, v.Measured!.Value, 6);
        Assert.Equal(300, v.Limit!.Min);
        Assert.Equal(V(1000, 100, 0), v.Location);       // 兩彎頭中點
        Assert.Equal(new[] { "P1" }, v.ObjectIds);
    }

    [Fact]
    public void RunExactlyAtMinimum_Passes()
    {
        var report = CheckPts(V(0, 0, 0), V(1000, 0, 0), V(1000, 300, 0), V(1500, 300, 0));
        Assert.Empty(report.Violations);
        var r = One(report, RuleIds.MinStraightBetweenBends);
        Assert.False(r.IsViolation);
        Assert.Equal(300, r.Measured!.Value, 6);
    }

    [Fact]
    public void RunJustBelowMinimum_IsViolation()
    {
        var report = CheckPts(V(0, 0, 0), V(1000, 0, 0), V(1000, 299.9, 0), V(1500, 299.9, 0));
        Assert.Equal(299.9, Assert.Single(report.Violations).Measured!.Value, 6);
    }

    [Fact]
    public void PortSegments_AreNotConstrained()
    {
        // 起訖接口段只有 50 mm，但它們不是「兩彎頭之間」，不受限
        var report = CheckPts(V(0, 0, 0), V(50, 0, 0), V(50, 1000, 0), V(100, 1000, 0));
        Assert.Empty(report.Violations);
        Assert.Equal(1000, One(report, RuleIds.MinStraightBetweenBends).Measured!.Value, 6);
    }

    [Fact]
    public void SingleBendOrStraight_HasNoEntry()
    {
        Assert.Empty(CheckPts(V(0, 0, 0), V(100, 0, 0), V(100, 100, 0)).Results);
        Assert.Empty(CheckPts(V(0, 0, 0), V(1000, 0, 0)).Results);
    }

    [Fact]
    public void CollinearInteriorVertex_IsNotABend()
    {
        // 第二個頂點在直線上，不算彎頭；真正的彎頭在 (1000,0,0) 與 (1000,300,0)
        var report = CheckPts(V(0, 0, 0), V(500, 0, 0), V(1000, 0, 0), V(1000, 300, 0), V(1500, 300, 0));
        Assert.Empty(report.Violations);
        Assert.Equal(300, One(report, RuleIds.MinStraightBetweenBends).Measured!.Value, 6);
    }

    [Fact]
    public void FoldBack180Degrees_CountsAsBend()
    {
        var report = CheckPts(V(0, 0, 0), V(1000, 0, 0), V(200, 0, 0), V(200, 500, 0));
        // 彎頭在 s=1000 與 s=1800，間距 800
        Assert.Empty(report.Violations);
        Assert.Equal(800, One(report, RuleIds.MinStraightBetweenBends).Measured!.Value, 6);
    }

    [Fact]
    public void Staircase_ReportsEachShortRun()
    {
        var report = CheckPts(V(0, 0, 0), V(500, 0, 0), V(500, 100, 0), V(600, 100, 0), V(600, 200, 0), V(1100, 200, 0));
        var vs = Of(report, RuleIds.MinStraightBetweenBends).Where(x => x.IsViolation).ToList();
        Assert.Equal(new[] { 100.0, 100.0, 100.0 }, vs.Select(x => x.Measured!.Value).ToArray());
        Assert.Equal(3, vs.Count);
    }

    [Fact]
    public void ThreeDimensionalBends_Work()
    {
        var report = CheckPts(V(0, 0, 0), V(1000, 0, 0), V(1000, 0, 250), V(1500, 0, 250));
        Assert.Equal(250, Assert.Single(report.Violations).Measured!.Value, 6);
    }

    [Fact]
    public void BendRun_EpsilonTolerance_IsApplied()
    {
        var scene = Scene(pipes: new[] { Pipe("P1", 100, 0,
            new[] { V(0, 0, 0), V(1000, 0, 0), V(1000, 299.6, 0), V(1500, 299.6, 0) }) });
        Assert.Empty(Check(scene, Example with { EpsilonMm = 0.5 }).Violations);
        Assert.Single(Check(scene).Violations);
    }

    [Fact]
    public void MinimumComesFromConfig()
    {
        var rules = Example with { MinStraightBetweenBendsMm = 100 };
        var report = Check(Scene(pipes: new[] { Pipe("P1", 100, 0,
            new[] { V(0, 0, 0), V(1000, 0, 0), V(1000, 200, 0), V(1500, 200, 0) }) }), rules);
        Assert.Empty(report.Violations);
    }
}
