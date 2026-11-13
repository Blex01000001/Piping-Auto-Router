using PipingRouter.Engine;
using PipingRouter.Engine.Routing;
using PipingRouter.Engine.Rules;
using Xunit;
using static PipingRouter.Tests.Rules.RuleTestKit;
using static PipingRouter.Tests.Routing.RouteTestKit;

namespace PipingRouter.Tests.Routing;

/// <summary>第 11 步驗收：每個成功的佈線結果，再丟回（連續座標、精確公式的）規則檢查器，不應有任何違規。</summary>
public class RouteAndCheckTests
{
    public static IEnumerable<object[]> Lines()
    {
        foreach (var f in Directory.EnumerateFiles(RepoPaths.Combine("samples", "scenes"), "*.json").OrderBy(x => x, StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(f);
            if (name.StartsWith("s15") || name.StartsWith("s09")) continue;      // s15 沒有待佈管線；s09 無解且很重
            int n = LoadScene(name).Lines.Count;
            for (int i = 0; i < n; i++) yield return new object[] { name, i };
        }
    }

    [Theory]
    [MemberData(nameof(Lines))]
    public void EverySuccessfulSampleRoute_PassesTheExactChecker(string scene, int lineIndex)
    {
        var s = LoadScene(scene);
        var cfg = scene.StartsWith("s14") ? Config with { VoxelSizeMm = 200 } : Config;
        var r = LineRouter.RouteAndCheck(s, s.Lines[lineIndex], Example, cfg);

        if (!r.Success)
        {
            Assert.Null(r.RuleReport);                                           // 失敗：沒有折線可檢查
            return;
        }

        var report = r.RuleReport;
        Assert.NotNull(report);
        Assert.True(report!.Violations.Count() == 0,
            "規則檢查器對成功的佈線結果報告了違規：\n" + string.Join("\n", report.Violations.Select(v => v.Message)));
        Assert.Equal((0, 0), (report.ErrorCount, report.WarningCount));

        // 報告真的跑過：有障礙物就有干涉與淨距的紀錄，有維修區就有侵入紀錄，兩個以上彎頭就有彎頭間直管紀錄
        var ids = report.Results.Select(x => x.RuleId).ToHashSet();
        if (s.Obstacles.Count > 0) Assert.Contains(RuleIds.Collision, ids);
        if (s.Obstacles.Count > 0) Assert.Contains(RuleIds.MinClearance, ids);
        if (s.ServiceZones.Count > 0) Assert.Contains(RuleIds.ServiceZoneIntrusion, ids);
        if (r.Metrics!.BendCount >= 2) Assert.Contains(RuleIds.MinStraightBetweenBends, ids);
        Assert.All(report.Results, x => Assert.All(x.ObjectIds.Take(1), id => Assert.Equal(s.Lines[lineIndex].Id, id)));
    }

    [Fact]
    public void RouteAndCheck_IsRoutePlusReport_AndNothingElseChanges()
    {
        var s = LoadScene("s07_service_zone");
        var plain = LineRouter.Route(s, s.Lines[0], Example, Config);
        var checkedRoute = LineRouter.RouteAndCheck(s, s.Lines[0], Example, Config);
        Assert.Equal(plain.Cells, checkedRoute.Cells);
        Assert.Equal(plain.Metrics, checkedRoute.Metrics);
        Assert.Null(plain.RuleReport);
        Assert.NotNull(checkedRoute.RuleReport);
    }

    [Fact]
    public void PenalizedZone_CrossingShowsUpAsAWarningNotAnError()
    {
        // 維修區改成「只提醒」：佈線會直接穿過（懲罰比繞路便宜），報告把它標為 warning；ErrorCount 仍為 0
        var rules = Example with { ServiceZonePolicy = new ServiceZonePolicyConfig(ZonePolicy.Penalize, ZonePolicy.Penalize) };
        var s = LoadScene("s07_service_zone");
        var r = LineRouter.RouteAndCheck(s, s.Lines[0], rules, Config);
        Assert.True(r.Success);
        Assert.Equal(0, r.Metrics!.BendCount);
        Assert.True(r.Metrics.Cost.Zone > 0);

        var report = r.RuleReport!;
        var v = Assert.Single(report.Violations);
        Assert.Equal(RuleIds.ServiceZoneIntrusion, v.RuleId);
        Assert.Equal(Severity.Warning, v.Severity);
        Assert.Equal((0, 1), (report.ErrorCount, report.WarningCount));
        Assert.False(report.HasErrors);
    }

    [Fact]
    public void Penalize_StillChecksEverythingElseStrictly()
    {
        var rules = Example with { ServiceZonePolicy = new ServiceZonePolicyConfig(ZonePolicy.Penalize, ZonePolicy.Penalize) };
        var s = LoadScene("s05_vessel_cylinder");                                // 沒有維修區：結果與預設相同
        var r = LineRouter.RouteAndCheck(s, s.Lines[0], rules, Config);
        Assert.True(r.Success);
        Assert.Empty(r.RuleReport!.Violations);
    }
}
