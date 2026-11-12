using PipingRouter.Engine;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Io;
using PipingRouter.Engine.Routing;
using Xunit;
using static PipingRouter.Tests.Rules.RuleTestKit;
using static PipingRouter.Tests.Routing.RouteTestKit;

namespace PipingRouter.Tests.Routing;

/// <summary>同一輸入必須得到逐位元相同的結果；與執行順序、執行緒、先前跑過什麼無關。</summary>
public class DeterminismTests
{
    private static string Run(string scene, int line = 0, RoutingConfig? config = null) =>
        RouteResultJson.Serialize(Route(LoadScene(scene), line, config));

    // 兩個很重的場景（s09 要跑 5 次搜尋做診斷、s14 繞很遠）用較粗的格子，其餘用範例設定
    private static RoutingConfig Cfg(string scene) =>
        scene.StartsWith("s14") ? Config with { VoxelSizeMm = 200 } : Config;

    public static IEnumerable<object[]> AllSampleLines()
    {
        foreach (var f in Directory.EnumerateFiles(RepoPaths.Combine("samples", "scenes"), "*.json").OrderBy(x => x, StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(f);
            if (name.StartsWith("s15")) continue;                       // 人工管線，沒有待佈的管線需求
            if (name.StartsWith("s09")) continue;                       // 另有專門的測試（很重）
            int lines = LoadScene(name).Lines.Count;
            for (int i = 0; i < lines; i++) yield return new object[] { name, i };
        }
    }

    [Theory]
    [MemberData(nameof(AllSampleLines))]
    public void EverySampleLine_TwoFreshRuns_AreByteIdentical(string scene, int line)
    {
        string a = Run(scene, line, Cfg(scene));
        string b = Run(scene, line, Cfg(scene));
        Assert.Equal(a, b);
    }

    [Fact]
    public void S09_HeavyFailureWithDiagnosis_IsByteIdentical()
    {
        string cached = RouteResultJson.Serialize(Cached("s09_enclosed_nopath"));
        Assert.Equal(cached, Run("s09_enclosed_nopath"));
    }

    [Fact]
    public void ResultsDoNotDependOnWhatRanBefore()
    {
        // s10 有三條管線：正序與逆序佈線，每一條的結果都必須相同（沒有藏在靜態狀態裡的東西）
        var s = LoadScene("s10_start_goal_blocked");
        var forward = Enumerable.Range(0, 3).Select(i => RouteResultJson.Serialize(Route(s, i))).ToArray();
        var backward = Enumerable.Range(0, 3).Reverse().Select(i => (i, RouteResultJson.Serialize(Route(s, i)))).OrderBy(x => x.i).Select(x => x.Item2).ToArray();
        Assert.Equal(forward, backward);

        // 先跑別的場景再跑，也一樣
        string before = Run("s05_vessel_cylinder");
        _ = Run("s03_wall_detour");
        _ = Run("s08b_min_straight_infeasible");
        Assert.Equal(before, Run("s05_vessel_cylinder"));
    }

    [Fact]
    public void ParallelRuns_ProduceTheSameResultsAsSequentialOnes()
    {
        string[] scenes = { "s02_open_one_bend", "s05_vessel_cylinder", "s06_horizontal_vessel", "s07_service_zone", "s08_min_straight", "s08b_min_straight_infeasible" };
        var expected = scenes.ToDictionary(x => x, x => Run(x));
        var bag = new System.Collections.Concurrent.ConcurrentBag<(string Scene, string Json)>();
        Parallel.For(0, 24, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            string scene = scenes[i % scenes.Length];
            bag.Add((scene, Run(scene)));
        });
        Assert.Equal(24, bag.Count);
        Assert.All(bag, x => Assert.Equal(expected[x.Scene], x.Json));
    }

    [Fact]
    public void GridContentHash_IsStable_AcrossRebuilds()
    {
        var s = LoadScene("s03_wall_detour");
        var line = s.Lines[0];
        Assert.Equal(GridFor(s, line).ContentHash(), GridFor(s, line).ContentHash());
    }

    // ------------------------------------------------------------ Golden：鎖住輸出格式與演算法結果
    [Fact]
    public void Golden_Success_S02()
    {
        GoldenFiles.Match("s02_open_one_bend.route.json", Run("s02_open_one_bend"));
    }

    [Fact]
    public void Golden_FailureWithDiagnosis_S08b()
    {
        GoldenFiles.Match("s08b_min_straight_infeasible.route.json", Run("s08b_min_straight_infeasible"));
    }

    [Fact]
    public void Golden_SeededTieBreak_S05_Seed7()
    {
        var cfg = Config with { Search = Config.Search with { Seed = 7 } };
        GoldenFiles.Match("s05_vessel_cylinder.seed7.route.json", Run("s05_vessel_cylinder", 0, cfg));
    }

    [Fact]
    public void Golden_FilesAreStrictJson_AndParseBack()
    {
        foreach (var f in Directory.EnumerateFiles(RepoPaths.Combine("tests", "PipingRouter.Tests", "Golden"), "*.route.json"))
        {
            var r = RouteResultJson.Load(f);
            Assert.True(r.Ok, f + "\n" + string.Join("\n", r.Issues));
        }
    }
}
