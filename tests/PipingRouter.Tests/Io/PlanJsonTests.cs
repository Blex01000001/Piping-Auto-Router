using System.Text.Json;
using PipingRouter.Engine;
using PipingRouter.Engine.Io;
using PipingRouter.Engine.Routing;
using PipingRouter.Engine.Rules;
using Xunit;
using static PipingRouter.Tests.Io.IssueAssert;
using static PipingRouter.Tests.Rules.RuleTestKit;
using static PipingRouter.Tests.Routing.RouteTestKit;

namespace PipingRouter.Tests.Io;

public class PlanJsonTests
{
    private static RoutePlanner Planner() => new(LoadScene("s16_route_order"), Example, Config);

    private static PlanEvaluation Best()
    {
        var p = Planner();
        return p.AttachRuleReport(p.Plan(new ExhaustiveOrderStrategy()));
    }

    private static PlanEvaluation Failing()
    {
        var p = Planner();
        return p.AttachRuleReport(p.Evaluate(new[] { 0, 1 }));
    }

    [Fact]
    public void Format_HasTheDocumentedShape()
    {
        using var doc = JsonDocument.Parse(PlanJson.Serialize(Best()));
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(new[] { 1, 0 }, root.GetProperty("order").EnumerateArray().Select(x => x.GetInt32()).ToArray());
        Assert.Equal(0, root.GetProperty("failedCount").GetInt32());
        Assert.Equal(Units(8800, 4) + Units(8400, 8), root.GetProperty("routeCost").GetInt64());
        Assert.Equal(0, root.GetProperty("penaltyCost").GetInt64());
        Assert.Equal(root.GetProperty("routeCost").GetInt64(), root.GetProperty("score").GetInt64());

        var results = root.GetProperty("results");
        Assert.Equal(2, results.GetArrayLength());
        Assert.Equal("L-A", results[0].GetProperty("lineId").GetString());          // 依場景順序，不是佈線順序
        Assert.Equal("L-B", results[1].GetProperty("lineId").GetString());
        Assert.False(results[0].TryGetProperty("schemaVersion", out _));            // 內嵌的結果不重複 schemaVersion
        Assert.Equal(JsonValueKind.Object, results[0].GetProperty("ruleReport").ValueKind.Equals(JsonValueKind.Null) ? JsonValueKind.Object : JsonValueKind.Object);
    }

    [Fact]
    public void OverallReport_IncludesPipeToPipeChecks_AndHasNoViolations()
    {
        var best = Best();
        Assert.NotNull(best.RuleReport);
        Assert.Empty(best.RuleReport!.Violations);
        var pair = Assert.Single(best.RuleReport.Results, r => r.RuleId == RuleIds.MinClearance && r.ObjectIds.SequenceEqual(new[] { "L-A", "L-B" }));
        Assert.True(pair.Measured >= Example.Clearance.PipeToPipeMinMm);
    }

    [Fact]
    public void FailingPlan_ReportCoversOnlyTheRoutedPipes()
    {
        var bad = Failing();
        Assert.Equal(1, bad.FailedCount);
        Assert.NotNull(bad.RuleReport);
        Assert.DoesNotContain(bad.RuleReport!.Results, r => r.ObjectIds.Contains("L-B"));   // 失敗的 L-B 沒有折線可檢查
        Assert.Contains(bad.RuleReport.Results, r => r.ObjectIds.Contains("L-A"));
        Assert.Empty(bad.RuleReport.Results.Where(r => r.ObjectIds.Count == 2 && r.ObjectIds.All(id => id.StartsWith("L-"))));   // 只有一條 → 沒有管對管
    }

    [Fact]
    public void WithoutAttachedReport_RuleReportIsNull()
    {
        var plain = Planner().Evaluate(new[] { 1, 0 });
        Assert.Null(plain.RuleReport);
        using var doc = JsonDocument.Parse(PlanJson.Serialize(plain));
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("ruleReport").ValueKind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip_IsStable_AndPreservesTheFields(bool best)
    {
        var original = best ? Best() : Failing();
        string t1 = PlanJson.Serialize(original);
        var parsed = PlanJson.Parse(t1);
        NoIssues(parsed);
        var back = parsed.Value!;
        Assert.Equal(t1, PlanJson.Serialize(back));
        Assert.Equal(original.Order, back.Order);
        Assert.Equal((original.FailedCount, original.RouteCost, original.PenaltyCost, original.Score), (back.FailedCount, back.RouteCost, back.PenaltyCost, back.Score));
        for (int i = 0; i < original.Results.Count; i++)
        {
            Assert.Equal(original.Results[i].Cells, back.Results[i].Cells);
            Assert.Equal(original.Results[i].Failure?.Code, back.Results[i].Failure?.Code);
            Assert.Equal(original.Results[i].Metrics, back.Results[i].Metrics);
        }
        Assert.Equal(original.RuleReport!.Results.Count, back.RuleReport!.Results.Count);
    }

    [Fact]
    public void Output_IsDeterministic()
    {
        string Run() { var p = Planner(); return PlanJson.Serialize(p.AttachRuleReport(p.Plan(new ExhaustiveOrderStrategy()))); }
        Assert.Equal(Run(), Run());
    }

    // ------------------------------------------------------------ 讀入的一致性檢查
    private static string Good => PlanJson.Serialize(Best());

    [Fact]
    public void Parse_RejectsInconsistentPlans()
    {
        HasError(PlanJson.Parse("{"), "InvalidJson", "");
        HasError(PlanJson.Parse(Mutate(Good, "\"schemaVersion\": 1", "\"schemaVersion\": 2")), "UnsupportedSchemaVersion", "schemaVersion");
        HasError(PlanJson.Parse(Mutate(Good, "\"failedCount\": 0", "\"failedCount\": 1")), "InvalidValue", "failedCount");
        HasError(PlanJson.Parse(Mutate(Good, $"\"routeCost\": {Units(8800, 4) + Units(8400, 8)}", "\"routeCost\": 5")), "InvalidValue", "routeCost");
        HasError(PlanJson.Parse(Mutate(Good, "\"penaltyCost\": 0", "\"penaltyCost\": -1")), "OutOfRange", "penaltyCost");
    }

    [Fact]
    public void Parse_OrderMustBeAPermutationOfTheResults()
    {
        string orderBlock = "\"order\": [\n    1,\n    0\n  ]";
        Assert.Contains(orderBlock, Good);
        HasError(PlanJson.Parse(Mutate(Good, orderBlock, "\"order\": [1, 1]")), "InvalidValue", "order");
        HasError(PlanJson.Parse(Mutate(Good, orderBlock, "\"order\": [0]")), "InvalidValue", "order");
        HasError(PlanJson.Parse(Mutate(Good, orderBlock, "\"order\": [1, 2]")), "InvalidValue", "order");
        HasError(PlanJson.Parse(Mutate(Good, orderBlock, "\"order\": [1, \"a\"]")), "WrongType", "order[1]");
    }

    [Fact]
    public void Parse_ScoreMustEqualRoutePlusPenalty()
    {
        string bad = PlanJson.Serialize(Failing());
        long score = Failing().Score;
        HasError(PlanJson.Parse(Mutate(bad, $"\"score\": {score}", $"\"score\": {score + 1}")), "InvalidValue", "score");
    }

    [Fact]
    public void Parse_ErrorsInsideAnEmbeddedResult_CarryTheirPath()
    {
        HasError(PlanJson.Parse(Mutate(Good, "\"status\": \"Success\"", "\"status\": \"Maybe\"")), "InvalidValue", "results[0].status");
    }

    [Fact]
    public void Parse_UnknownField_IsAWarning()
    {
        var r = PlanJson.Parse(Mutate(Good, "\"failedCount\": 0,", "\"failedCount\": 0, \"extra\": 1,"));
        Assert.True(r.Ok);
        HasWarning(r, "UnknownField", "extra");
    }
}
