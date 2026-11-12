using System.Text.Json;
using PipingRouter.Engine;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Io;
using PipingRouter.Engine.Routing;
using Xunit;
using static PipingRouter.Tests.Io.IssueAssert;
using static PipingRouter.Tests.Routing.RouteTestKit;

namespace PipingRouter.Tests.Io;

public class RouteResultJsonTests
{
    private static string Json(string scene, int line = 0) => RouteResultJson.Serialize(Route(LoadScene(scene), line));

    // ------------------------------------------------------------ 格式
    [Fact]
    public void Success_HasTheDocumentedShape()
    {
        string text = Json("s02_open_one_bend");
        using var doc = JsonDocument.Parse(text);                       // 嚴格 JSON
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("L-001", root.GetProperty("lineId").GetString());
        Assert.Equal("Success", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("failure").ValueKind);
        Assert.Equal(3, root.GetProperty("polyline").GetArrayLength());
        Assert.Equal(51, root.GetProperty("cells").GetArrayLength());     // 30 + 20 步 → 51 格
        var m = root.GetProperty("metrics");
        Assert.Equal(1, m.GetProperty("bendCount").GetInt32());
        Assert.Equal(5000, m.GetProperty("lengthMm").GetDouble());
        Assert.Equal(Units(5000, 1), m.GetProperty("cost").GetProperty("total").GetInt64());
        Assert.Equal(new[] { 500.0, 500.0, 1000.0 }, root.GetProperty("gridSnap").GetProperty("startPort").EnumerateArray().Select(x => x.GetDouble()).ToArray());
        Assert.Equal(0, root.GetProperty("warnings").GetArrayLength());
        Assert.Equal(0, root.GetProperty("search").GetProperty("seed").GetInt64());
        Assert.Equal(3, root.GetProperty("search").GetProperty("minStraightCells").GetInt32());
        // 不含任何與時間有關的欄位 → 輸出才可能逐位元相同
        Assert.DoesNotContain("elapsed", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("duration", text, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("\n", text);
    }

    [Fact]
    public void Failure_IncludesDiagnosis()
    {
        string text = Json("s08b_min_straight_infeasible");
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.Equal("NoPath", root.GetProperty("status").GetString());
        Assert.Equal(0, root.GetProperty("polyline").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("metrics").ValueKind);
        var f = root.GetProperty("failure");
        Assert.Equal("NoPathExists", f.GetProperty("code").GetString());
        var d = f.GetProperty("diagnosis");
        Assert.Equal(4, d.GetProperty("relaxations").GetArrayLength());
        Assert.Contains("最小直管長度", d.GetProperty("summary").GetString());
        Assert.Equal("MinStraight", d.GetProperty("relaxations")[0].GetProperty("id").GetString());
        Assert.True(d.GetProperty("relaxations")[0].GetProperty("succeeds").GetBoolean());
        Assert.Equal(JsonValueKind.Null, d.GetProperty("relaxations")[2].GetProperty("bendCount").ValueKind);   // Clearance 失敗
    }

    // ------------------------------------------------------------ 來回一致
    public static IEnumerable<object[]> Cases() => new[]
    {
        new object[] { "s01_open_straight", 0 }, new object[] { "s02_open_one_bend", 0 },
        new object[] { "s04_pipe_rack_climb", 0 }, new object[] { "s05_vessel_cylinder", 0 },
        new object[] { "s07_service_zone", 0 }, new object[] { "s08b_min_straight_infeasible", 0 },
        new object[] { "s10_start_goal_blocked", 0 }, new object[] { "s10_start_goal_blocked", 2 },
        new object[] { "s12b_gap_grid_conservative", 0 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void RoundTrip_IsStable_AndPreservesTheFields(string scene, int line)
    {
        var original = Route(LoadScene(scene), line);
        string t1 = RouteResultJson.Serialize(original);
        var parsed = RouteResultJson.Parse(t1);
        NoIssues(parsed);
        var r = parsed.Value!;
        Assert.Equal(t1, RouteResultJson.Serialize(r));                // 序列化 → 解析 → 序列化 完全一致

        Assert.Equal(original.LineId, r.LineId);
        Assert.Equal(original.Status, r.Status);
        Assert.Equal(original.Cells, r.Cells);
        Assert.Equal(original.Polyline, r.Polyline);
        Assert.Equal(original.Metrics, r.Metrics);
        Assert.Equal(original.Snap, r.Snap);
        Assert.Equal(original.Search, r.Search);
        Assert.Equal(original.Failure?.Code, r.Failure?.Code);
        Assert.Equal(original.Failure?.ClosestCell, r.Failure?.ClosestCell);
        Assert.Equal(original.Failure?.Diagnosis?.Summary, r.Failure?.Diagnosis?.Summary);
        Assert.Equal(original.Failure?.Diagnosis?.Relaxations, r.Failure?.Diagnosis?.Relaxations);
    }

    [Fact]
    public void Warnings_RoundTrip()
    {
        var r = Route(LoadScene("s02_open_one_bend")) with { };
        r = r with { Warnings = new[] { new RouteWarning("SelfOverlap", "路徑重複經過 2 個格子（例如 (3,4,5)）") } };
        var back = RouteResultJson.Parse(RouteResultJson.Serialize(r)).ValueOrThrow();
        Assert.Equal(r.Warnings, back.Warnings);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips_AndHasNoBom()
    {
        string dir = Path.Combine(Path.GetTempPath(), "piping-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "nested", "route.json");
        try
        {
            var r = Route(LoadScene("s02_open_one_bend"));
            RouteResultJson.Save(path, r);
            var loaded = RouteResultJson.Load(path);
            NoIssues(loaded);
            Assert.Equal(RouteResultJson.Serialize(r), RouteResultJson.Serialize(loaded.Value!));
            Assert.False(File.ReadAllBytes(path).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------------------------ 讀入錯誤
    private static string Good => Json("s02_open_one_bend");

    [Fact]
    public void Parse_ReportsInconsistentOrBrokenFiles()
    {
        HasError(RouteResultJson.Parse("{"), "InvalidJson", "");
        HasError(RouteResultJson.Parse(Mutate(Good, "\"schemaVersion\": 1", "\"schemaVersion\": 7")), "UnsupportedSchemaVersion", "schemaVersion");
        HasError(RouteResultJson.Parse(Mutate(Good, "\"status\": \"Success\"", "\"status\": \"Maybe\"")), "InvalidValue", "status");
        HasError(RouteResultJson.Parse(Mutate(Good, "\"metrics\": {", "\"metricz\": {")), "MissingField", "metrics");
        // 成本分項與總和不一致
        string total = Units(5000, 1).ToString();
        HasError(RouteResultJson.Parse(Mutate(Good, $"\"total\": {total}", $"\"total\": {Units(5000, 1) + 1}")), "InvalidValue", "metrics.cost.total");
        // 格子索引必須是整數
        HasError(RouteResultJson.Parse(Mutate(Good, "\"startCell\": [5, 5, 10]", "\"startCell\": [5.5, 5, 10]")), "WrongType", "gridSnap.startCell");
    }

    [Fact]
    public void Parse_NoPathWithoutFailure_Or_SuccessWithFailure_AreRejected()
    {
        string failed = Json("s08b_min_straight_infeasible");
        HasError(RouteResultJson.Parse(Mutate(failed, "\"status\": \"NoPath\"", "\"status\": \"Success\"")), "InvalidValue", "failure");
        HasError(RouteResultJson.Parse(Mutate(Good, "\"status\": \"Success\"", "\"status\": \"NoPath\"")), "MissingField", "failure");
    }

    [Fact]
    public void Parse_UnknownField_IsAWarning()
    {
        var r = RouteResultJson.Parse(Mutate(Good, "\"lineId\": \"L-001\",", "\"lineId\": \"L-001\", \"lineID\": 1,"));
        Assert.True(r.Ok);
        HasWarning(r, "UnknownField", "lineID");
    }
}
