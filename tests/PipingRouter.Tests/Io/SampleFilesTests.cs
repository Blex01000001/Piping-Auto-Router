using PipingRouter.Engine.Io;
using Xunit;
using static PipingRouter.Tests.Io.IssueAssert;

namespace PipingRouter.Tests.Io;

/// <summary>samples/scenes 底下的每個場景檔都必須有效、乾淨、標示為假資料，且來回穩定。</summary>
public class SampleFilesTests
{
    public static IEnumerable<object[]> SceneFiles() =>
        Directory.EnumerateFiles(RepoPaths.Combine("samples", "scenes"), "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new object[] { Path.GetFileName(f) });

    [Fact]
    public void SceneDirectory_HasSamples()
    {
        Assert.True(SceneFiles().Count() >= 2);
    }

    [Theory]
    [MemberData(nameof(SceneFiles))]
    public void Scene_IsValid_Synthetic_AndRoundTrips(string fileName)
    {
        var r = SceneJson.Load(RepoPaths.Combine("samples", "scenes", fileName));
        NoIssues(r);
        Assert.True(r.Value!.IsSynthetic, "範例場景必須標示 isSynthetic=true");

        string t1 = SceneJson.Serialize(r.Value);
        var again = SceneJson.Parse(t1);
        NoIssues(again);
        Assert.Equal(t1, SceneJson.Serialize(again.Value!));
    }

    [Fact]
    public void WallDetourScene_HasExpectedContent()
    {
        var s = SceneJson.Load(RepoPaths.Combine("samples", "scenes", "s03_wall_detour.json")).ValueOrThrow();
        Assert.Equal(3, s.Obstacles.Count);
        Assert.Single(s.ServiceZones);
        Assert.Equal(114.3 / 2 + 50, s.Lines[0].CollisionRadiusMm, 9);
    }
}
