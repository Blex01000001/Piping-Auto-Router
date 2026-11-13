using PipingRouter.Engine;
using PipingRouter.Engine.Io;
using PipingRouter.Engine.Routing;
using PipingRouter.Engine.Rules;
using Xunit;
using static PipingRouter.Tests.Io.IssueAssert;
using static PipingRouter.Tests.Rules.RuleTestKit;
using static PipingRouter.Tests.Routing.RouteTestKit;

namespace PipingRouter.Tests.Routing;

/// <summary>
/// samples/results/ 底下是給 Dashboard 開發直接讀取的範例輸出，同時也是 golden 檔（內容由程式產生並鎖住）：
///   *.route.json = 單條管線的佈線結果（成功者含規則報告）；*.plan.json = 多條管線整體結果；*.rules.json = 人工管線的規則報告。
/// 重新產生：UPDATE_GOLDEN=1 dotnet test --filter Golden_ （務必檢視 git diff 再提交）。
/// </summary>
public class SampleResultsTests
{
    private static string RouteChecked(string scene, int line = 0, RoutingConfig? config = null)
    {
        var s = LoadScene(scene);
        return RouteResultJson.Serialize(LineRouter.RouteAndCheck(s, s.Lines[line], Example, config ?? Config));
    }

    [Fact]
    public void Golden_Route_S07_ServiceZoneDetour() =>
        GoldenFiles.Match("s07_service_zone.route.json", RouteChecked("s07_service_zone"));

    [Fact]
    public void Golden_Route_S12b_GridConservatismFalseNegative() =>
        GoldenFiles.Match("s12b_gap_grid_conservative.route.json", RouteChecked("s12b_gap_grid_conservative"));

    [Fact]
    public void Golden_Plan_S16_BestOrder()
    {
        var planner = new RoutePlanner(LoadScene("s16_route_order"), Example, Config);
        var best = planner.AttachRuleReport(planner.Plan(new ExhaustiveOrderStrategy()));
        Assert.Equal(new[] { 1, 0 }, best.Order);
        GoldenFiles.Match("s16_route_order.plan.json", PlanJson.Serialize(best));
    }

    [Fact]
    public void Golden_Plan_S16_FailingOrder()
    {
        var planner = new RoutePlanner(LoadScene("s16_route_order"), Example, Config);
        var bad = planner.AttachRuleReport(planner.Evaluate(new[] { 0, 1 }));
        Assert.Equal(1, bad.FailedCount);
        GoldenFiles.Match("s16_route_order.plan-failing.json", PlanJson.Serialize(bad));
    }

    [Fact]
    public void Golden_RuleReport_S15_ManualPipes()
    {
        var report = RuleChecker.Check(LoadScene("s15_manual_pipes"), Example);
        Assert.Equal(6, report.ErrorCount);
        GoldenFiles.Match("s15_manual_pipes.rules.json", RuleReportJson.Serialize(report));
    }

    // ------------------------------------------------------------ 每個範例輸出都必須有效、乾淨、能解析回來
    public static IEnumerable<object[]> ResultFiles() =>
        Directory.EnumerateFiles(RepoPaths.Combine("samples", "results"), "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new object[] { Path.GetFileName(f) });

    [Fact]
    public void ResultsDirectory_HasTheDocumentedSamples()
    {
        var names = ResultFiles().Select(x => (string)x[0]).ToHashSet();
        foreach (var expected in new[]
        {
            "s02_open_one_bend.route.json", "s05_vessel_cylinder.seed7.route.json", "s07_service_zone.route.json",
            "s08b_min_straight_infeasible.route.json", "s12b_gap_grid_conservative.route.json",
            "s16_route_order.plan.json", "s16_route_order.plan-failing.json", "s15_manual_pipes.rules.json",
        })
            Assert.Contains(expected, names);
    }

    [Theory]
    [MemberData(nameof(ResultFiles))]
    public void EveryResultFile_ParsesWithoutIssues_AndRoundTripsByteForByte(string name)
    {
        string path = RepoPaths.Combine("samples", "results", name);
        string text = File.ReadAllText(path).Replace("\r\n", "\n");
        if (name.EndsWith(".route.json"))
        {
            var r = RouteResultJson.Parse(text);
            NoIssues(r);
            Assert.Equal(text, RouteResultJson.Serialize(r.Value!));
        }
        else if (name.EndsWith(".plan.json") || name.EndsWith(".plan-failing.json"))
        {
            var r = PlanJson.Parse(text);
            NoIssues(r);
            Assert.Equal(text, PlanJson.Serialize(r.Value!));
        }
        else if (name.EndsWith(".rules.json"))
        {
            var r = RuleReportJson.Parse(text);
            NoIssues(r);
            Assert.Equal(text, RuleReportJson.Serialize(r.Value!));
        }
        else
        {
            Assert.Fail($"未知的範例輸出類型：{name}");
        }
    }

    [Fact]
    public void SuccessfulRouteSamples_CarryAReport_WithoutCollisionClassViolations()
    {
        foreach (var name in new[] { "s02_open_one_bend", "s05_vessel_cylinder.seed7", "s07_service_zone" })
        {
            var r = RouteResultJson.Load(RepoPaths.Combine("samples", "results", name + ".route.json")).ValueOrThrow();
            Assert.True(r.Success);
            Assert.NotNull(r.RuleReport);
            Assert.Empty(r.RuleReport!.Violations);
        }
    }

    [Fact]
    public void FailedRouteSamples_HaveDiagnosis_AndNoReport()
    {
        foreach (var name in new[] { "s08b_min_straight_infeasible", "s12b_gap_grid_conservative" })
        {
            var r = RouteResultJson.Load(RepoPaths.Combine("samples", "results", name + ".route.json")).ValueOrThrow();
            Assert.False(r.Success);
            Assert.Null(r.RuleReport);
            Assert.NotNull(r.Failure!.Diagnosis);
        }
    }
}
