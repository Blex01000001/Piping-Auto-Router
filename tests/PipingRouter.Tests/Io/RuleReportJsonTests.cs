using System.Text.Json;
using PipingRouter.Engine;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Io;
using PipingRouter.Engine.Rules;
using Xunit;
using static PipingRouter.Tests.Io.IssueAssert;
using static PipingRouter.Tests.Rules.RuleTestKit;
using static PipingRouter.Tests.Routing.RouteTestKit;

namespace PipingRouter.Tests.Io;

public class RuleReportJsonTests
{
    private static RuleReport S15() => RuleChecker.Check(LoadScene("s15_manual_pipes"), Example);

    [Fact]
    public void Format_KeepsEveryFieldOfEveryResult()
    {
        string text = RuleReportJson.Serialize(S15());
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(6, root.GetProperty("errorCount").GetInt32());
        Assert.Equal(0, root.GetProperty("warningCount").GetInt32());

        var clearance = root.GetProperty("results").EnumerateArray()
            .Single(r => r.GetProperty("isViolation").GetBoolean() && r.GetProperty("ruleId").GetString() == "MinClearance");
        Assert.Equal("error", clearance.GetProperty("severity").GetString());
        Assert.Equal(new[] { "P-CLEAR", "EQ-01" }, clearance.GetProperty("objectIds").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Equal(90, clearance.GetProperty("measured").GetDouble(), 6);
        Assert.Equal(100, clearance.GetProperty("limit").GetProperty("min").GetDouble());
        Assert.Equal(JsonValueKind.Null, clearance.GetProperty("limit").GetProperty("max").ValueKind);
        Assert.Equal("mm", clearance.GetProperty("unit").GetString());
        Assert.Equal(3, clearance.GetProperty("location").GetArrayLength());
        Assert.Contains("90", clearance.GetProperty("message").GetString());

        // 手輪高度：min 與 max 都有
        var height = root.GetProperty("results").EnumerateArray().Single(r => r.GetProperty("ruleId").GetString() == "HandwheelHeight" && r.GetProperty("isViolation").GetBoolean());
        Assert.Equal(900, height.GetProperty("limit").GetProperty("min").GetDouble());
        Assert.Equal(1500, height.GetProperty("limit").GetProperty("max").GetDouble());
    }

    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        var original = S15();
        string t1 = RuleReportJson.Serialize(original);
        var parsed = RuleReportJson.Parse(t1);
        NoIssues(parsed);
        var back = parsed.Value!;
        Assert.Equal(t1, RuleReportJson.Serialize(back));
        Assert.Equal(original.Results.Count, back.Results.Count);
        for (int i = 0; i < original.Results.Count; i++)
        {
            var a = original.Results[i];
            var b = back.Results[i];
            Assert.Equal((a.RuleId, a.IsViolation, a.Severity, a.Unit, a.Message), (b.RuleId, b.IsViolation, b.Severity, b.Unit, b.Message));
            Assert.Equal(a.ObjectIds, b.ObjectIds);
            // 數值輸出時四捨五入到 1e-6 mm
            Assert.True(a.Location.DistanceTo(b.Location) < 2e-6, $"{a.RuleId} 位置 {a.Location} → {b.Location}");
            Assert.Equal(a.Measured.HasValue, b.Measured.HasValue);
            if (a.Measured.HasValue) Assert.Equal(a.Measured.Value, b.Measured!.Value, 6);
            Assert.Equal(a.Limit == null, b.Limit == null);
            if (a.Limit != null)
            {
                Assert.Equal(a.Limit.Min.HasValue, b.Limit!.Min.HasValue);
                Assert.Equal(a.Limit.Max.HasValue, b.Limit.Max.HasValue);
                if (a.Limit.Min.HasValue) Assert.Equal(a.Limit.Min.Value, b.Limit.Min!.Value, 6);
                if (a.Limit.Max.HasValue) Assert.Equal(a.Limit.Max.Value, b.Limit.Max!.Value, 6);
            }
        }
        Assert.Equal((original.ErrorCount, original.WarningCount), (back.ErrorCount, back.WarningCount));
    }

    [Fact]
    public void Numbers_AreRoundedToAMicrometerFraction_SoNoiseNeverReachesTheFile()
    {
        // s15 的 Collision 在圓柱上，內部量測值約為 -49.99999999999909、位置 x 約為 6399.999999999999
        var report = S15();
        var collision = Assert.Single(report.Violations, v => v.RuleId == RuleIds.Collision);
        Assert.NotEqual(-50.0, collision.Measured!.Value);                       // 內部確實有雜訊
        Assert.Equal(-50.0, collision.Measured.Value, 9);

        using var doc = JsonDocument.Parse(RuleReportJson.Serialize(report));
        var c = doc.RootElement.GetProperty("results").EnumerateArray()
            .Single(r => r.GetProperty("ruleId").GetString() == "Collision" && r.GetProperty("isViolation").GetBoolean());
        Assert.Equal(-50.0, c.GetProperty("measured").GetDouble());              // 輸出是乾淨的 -50
        Assert.Equal(6400.0, c.GetProperty("location")[0].GetDouble());
        Assert.DoesNotContain("99999", RuleReportJson.Serialize(report));
        Assert.DoesNotContain("-0,", RuleReportJson.Serialize(report));
    }

    [Fact]
    public void NullMeasuredAndNullLimit_RoundTrip()
    {
        // UnsupportedComponent 這類紀錄沒有量測值也沒有限值
        var pipe = Pipe("P1", 100, 0, new[] { V(0, 0, 0), V(0, 0, 3000) }, Comp("HV", "handValve", 0, 1000));
        var report = RuleChecker.Check(Scene(pipes: new[] { pipe }), Example);
        var u = Assert.Single(report.Results, r => r.RuleId == RuleIds.UnsupportedComponent);
        Assert.Null(u.Measured);
        Assert.Null(u.Limit);
        var back = RuleReportJson.Parse(RuleReportJson.Serialize(report)).ValueOrThrow();
        var u2 = Assert.Single(back.Results, r => r.RuleId == RuleIds.UnsupportedComponent);
        Assert.Null(u2.Measured);
        Assert.Null(u2.Limit);
        Assert.Equal(Severity.Warning, u2.Severity);
        Assert.Equal((0, 1), (back.ErrorCount, back.WarningCount));
    }

    [Fact]
    public void Parse_RejectsInconsistentOrBrokenReports()
    {
        string good = RuleReportJson.Serialize(S15());
        HasError(RuleReportJson.Parse("{"), "InvalidJson", "");
        HasError(RuleReportJson.Parse(Mutate(good, "\"schemaVersion\": 1", "\"schemaVersion\": 3")), "UnsupportedSchemaVersion", "schemaVersion");
        HasError(RuleReportJson.Parse(Mutate(good, "\"errorCount\": 6", "\"errorCount\": 5")), "InvalidValue", "errorCount");
        HasError(RuleReportJson.Parse(Mutate(good, "\"warningCount\": 0", "\"warningCount\": 2")), "InvalidValue", "warningCount");
        HasError(RuleReportJson.Parse(Mutate(good, "\"severity\": \"error\"", "\"severity\": \"fatal\"")), "InvalidValue", "results[0].severity");
        HasError(RuleReportJson.Parse(Mutate(good, "\"location\":", "\"locatoin\":")), "MissingField", "results[0].location");
    }

    [Fact]
    public void SaveThenLoad()
    {
        string dir = Path.Combine(Path.GetTempPath(), "piping-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "report.json");
        try
        {
            var report = S15();
            RuleReportJson.Save(path, report);
            var loaded = RuleReportJson.Load(path);
            NoIssues(loaded);
            Assert.Equal(RuleReportJson.Serialize(report), RuleReportJson.Serialize(loaded.Value!));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
