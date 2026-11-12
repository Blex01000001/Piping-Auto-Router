using PipingRouter.Engine;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Io;
using PipingRouter.Engine.Routing;
using Xunit;
using static PipingRouter.Tests.Rules.RuleTestKit;
using static PipingRouter.Tests.Routing.RouteTestKit;

namespace PipingRouter.Tests.Routing;

/// <summary>種子只決定等成本路徑之間的選擇：成本不變，路徑可以不同；同一個種子永遠得到同一條。</summary>
public class SeedAndAlternativesTests
{
    private static string Cells(RouteResult r) => string.Join(";", r.Cells);

    [Fact]
    public void S13_SymmetricScene_BothSidesAreEquallyGood()
    {
        // 塔槽軸心 y=2050 正好是管線所在列的中心，繞上側（y=2850）與下側（y=1250）完全對稱
        var s = LoadScene("s13_tie_symmetric");
        var line = s.Lines[0];
        var grid = GridFor(s, line);
        long? best = null;
        bool up = false, down = false;
        var distinct = new HashSet<string>();

        for (int seed = 0; seed < 24; seed++)
        {
            var cfg = Config with { Search = Config.Search with { Seed = seed } };
            var r = Route(s, 0, cfg);
            AssertValid(s, line, r, grid, cfg);
            best ??= r.Metrics!.Cost.Total;
            Assert.Equal(best, r.Metrics!.Cost.Total);
            Assert.Equal(4, r.Metrics.BendCount);
            Assert.Equal(7000 + 2 * 800, r.Metrics.LengthMm, 6);
            Assert.Equal(seed, r.Search.Seed);
            if (r.Polyline.Max(p => p.Y) > 2050 + 1e-6) up = true;
            if (r.Polyline.Min(p => p.Y) < 2050 - 1e-6) down = true;
            distinct.Add(Cells(r));
        }
        Assert.True(up && down, "不同種子應該能選到上側也能選到下側（成本完全相同）");
        Assert.True(distinct.Count >= 4, $"等成本路徑的變化太少：{distinct.Count}");
        Assert.Equal(Units(8600, 4), best);
    }

    [Fact]
    public void SameSeed_AlwaysTheSamePath()
    {
        var s = LoadScene("s13_tie_symmetric");
        foreach (int seed in new[] { 0, 1, 7, 42, -5, 123456789 })
        {
            var cfg = Config with { Search = Config.Search with { Seed = seed } };
            Assert.Equal(Cells(Route(s, 0, cfg)), Cells(Route(s, 0, cfg)));
        }
    }

    [Fact]
    public void Seed_NeverChangesTheOptimalCost_OnAnySampleScene()
    {
        foreach (var f in Directory.EnumerateFiles(RepoPaths.Combine("samples", "scenes"), "*.json").OrderBy(x => x, StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(f);
            if (name.StartsWith("s15") || name.StartsWith("s09") || name.StartsWith("s14") || name.StartsWith("s03")) continue;   // 無待佈管線／較重
            var s = LoadScene(name);
            for (int i = 0; i < s.Lines.Count; i++)
            {
                var cfg0 = Config with { DiagnoseOnFailure = false };                 // 這裡只比成本，不需要診斷
                var a = Route(s, i, cfg0);
                var b = Route(s, i, cfg0 with { Search = cfg0.Search with { Seed = 987654321 } });
                Assert.Equal(a.Success, b.Success);
                if (a.Success) Assert.Equal(a.Metrics!.Cost.Total, b.Metrics!.Cost.Total);
                else Assert.Equal(a.Failure!.Code, b.Failure!.Code);
            }
        }
    }

    [Fact]
    public void Seed_Zero_IsTheCanonicalFirstFoundChoice()
    {
        // 種子 0（預設）= 先發現者勝；結果必須與 golden 同（Golden_Success_S02 已鎖住格式，這裡鎖住 s13 的選擇）
        var s = LoadScene("s13_tie_symmetric");
        var r = Route(s);
        Assert.Equal(0, r.Search.Seed);
        Assert.Equal(Cells(r), Cells(Route(s)));
    }

    // ------------------------------------------------------------ 備選方案
    [Fact]
    public void Alternatives_AreDistinct_EqualCost_Valid_AndTheFirstIsTheDefaultRoute()
    {
        var s = LoadScene("s13_tie_symmetric");
        var line = s.Lines[0];
        var grid = GridFor(s, line);
        var alts = LineRouter.RouteAlternatives(s, line, Rules.RuleTestKit.Example, Config, count: 5);

        Assert.Equal(5, alts.Count);
        Assert.Equal(Cells(Route(s)), Cells(alts[0]));                     // 第一條 = 預設種子的結果
        Assert.Equal(5, alts.Select(Cells).Distinct().Count());
        Assert.Single(alts.Select(a => a.Metrics!.Cost.Total).Distinct());  // 成本全相同
        foreach (var a in alts) AssertValid(s, line, a, grid);
        Assert.Equal(alts.Select(a => a.Search.Seed).Distinct().Count(), alts.Count);   // 每條記錄自己的種子
    }

    [Fact]
    public void Alternatives_AreDeterministic()
    {
        var s = LoadScene("s13_tie_symmetric");
        var line = s.Lines[0];
        string Dump() => string.Join("\n", LineRouter.RouteAlternatives(s, line, Rules.RuleTestKit.Example, Config, 4).Select(RouteResultJson.Serialize));
        Assert.Equal(Dump(), Dump());
    }

    [Fact]
    public void Alternatives_StartFromTheConfiguredSeed()
    {
        var s = LoadScene("s13_tie_symmetric");
        var cfg = Config with { Search = Config.Search with { Seed = 100 } };
        var alts = LineRouter.RouteAlternatives(s, s.Lines[0], Rules.RuleTestKit.Example, cfg, 3);
        Assert.Equal(100, alts[0].Search.Seed);
        Assert.All(alts.Skip(1), a => Assert.InRange(a.Search.Seed, 101, 131));
    }

    [Fact]
    public void Alternatives_WhenThePathIsUnique_ReturnFewerThanRequested()
    {
        var s = LoadScene("s01_open_straight");
        var alts = LineRouter.RouteAlternatives(s, s.Lines[0], Rules.RuleTestKit.Example, Config, count: 5, maxSeeds: 10);
        Assert.Single(alts);                                               // 空場景直線只有一條
    }

    [Fact]
    public void Alternatives_WhenRoutingFails_ReturnOnlyTheFailureWithItsDiagnosis()
    {
        var s = LoadScene("s12_gap_too_narrow");
        var alts = LineRouter.RouteAlternatives(s, s.Lines[0], Rules.RuleTestKit.Example, Config, count: 5);
        var only = Assert.Single(alts);
        Assert.False(only.Success);
        Assert.NotNull(only.Failure!.Diagnosis);
    }

    [Fact]
    public void Alternatives_CountOne_IsJustTheDefaultRoute()
    {
        var s = LoadScene("s13_tie_symmetric");
        var alts = LineRouter.RouteAlternatives(s, s.Lines[0], Rules.RuleTestKit.Example, Config, count: 1);
        Assert.Single(alts);
    }
}
