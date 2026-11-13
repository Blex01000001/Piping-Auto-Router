using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Routing;
using Xunit;
using static PipingRouter.Tests.Rules.RuleTestKit;
using static PipingRouter.Tests.Routing.RouteTestKit;

namespace PipingRouter.Tests.Routing;

public class VoxelGridStampTests
{
    // 10×10×10 的空格子，邊長 100
    private static VoxelGrid Cube()
    {
        var scene = Scene() with { Bounds = new Aabb(V(0, 0, 0), V(1000, 1000, 1000)) };
        var r = Voxelizer.Build(scene, Example, 100, 50);
        Assert.True(r.Ok);
        return r.Grid!;
    }

    [Fact]
    public void Stamp_BlocksCellsWithinTheMarginOfTheSegment_Exactly()
    {
        // 線段在 y=550、z=550（格心），沿整個 X。margin 150：
        // 各軸格子間隙 0（索引 5）、50（索引 4、6）、150（索引 3、7，不 < 150 → 自由）→ y、z 各 3 格 → 10 欄 × 9 = 90
        var g = Cube();
        int added = g.StampCapsule(new Segment(V(50, 550, 550), V(950, 550, 550)), 150);
        Assert.Equal(90, added);
        Assert.Equal(90, g.CountWith(CellFlags.OtherPipe));
        Assert.Equal(90, g.CountBlocked());
        for (int x = 0; x < 10; x++)
        {
            Assert.True(g.IsBlocked(new CellIndex(x, 4, 4)));
            Assert.True(g.IsBlocked(new CellIndex(x, 6, 6)));
            Assert.False(g.IsBlocked(new CellIndex(x, 3, 5)));          // 間隙剛好等於 margin → 自由
            Assert.False(g.IsBlocked(new CellIndex(x, 7, 5)));
        }
        Assert.Equal((CellFlags.OtherPipe), g.FlagsAt(new CellIndex(5, 5, 5)));
    }

    [Fact]
    public void Stamp_UsesARoundedMargin_NotACube()
    {
        // margin 160：一軸間隙 150 也擋；兩軸 (150,50) = 158 < 160 擋；(150,150) = 212 不擋 → y、z 各 5 格去掉 4 個角 = 21 → 210
        var g = Cube();
        Assert.Equal(210, g.StampCapsule(new Segment(V(50, 550, 550), V(950, 550, 550)), 160));
        Assert.True(g.IsBlocked(new CellIndex(4, 3, 5)));
        Assert.True(g.IsBlocked(new CellIndex(4, 3, 4)));
        Assert.False(g.IsBlocked(new CellIndex(4, 3, 3)));
    }

    [Fact]
    public void Stamp_IsIdempotent_AndIgnoresNonPositiveMargins()
    {
        var g = Cube();
        var seg = new Segment(V(50, 550, 550), V(950, 550, 550));
        Assert.Equal(90, g.StampCapsule(seg, 150));
        Assert.Equal(0, g.StampCapsule(seg, 150));
        Assert.Equal(0, g.StampCapsule(seg, 0));
        Assert.Equal(0, g.StampCapsule(seg, -5));
        Assert.Equal(0, g.StampCapsule(seg, double.NaN));
        Assert.Equal(90, g.CountBlocked());
    }

    [Fact]
    public void Stamp_ClampsToTheGrid_WhenTheSegmentLeavesIt()
    {
        var g = Cube();
        int added = g.StampCapsule(new Segment(V(-500, 550, 550), V(50, 550, 550)), 150);
        Assert.True(added > 0);
        Assert.True(g.IsBlocked(new CellIndex(0, 5, 5)));
        Assert.Equal(0, g.StampCapsule(new Segment(V(-900, 5000, 550), V(-500, 5000, 550)), 150));   // 完全在格子外
    }

    [Fact]
    public void Clone_IsIndependentOfTheOriginal()
    {
        var g = Cube();
        var copy = g.Clone();
        Assert.Equal(g.ContentHash(), copy.ContentHash());
        copy.StampCapsule(new Segment(V(50, 550, 550), V(950, 550, 550)), 150);
        Assert.Equal(0, g.CountBlocked());                                 // 原本不受影響
        Assert.Equal(90, copy.CountBlocked());
        Assert.NotEqual(g.ContentHash(), copy.ContentHash());
        Assert.Equal((g.Nx, g.Ny, g.Nz, g.CellSize, g.Origin), (copy.Nx, copy.Ny, copy.Nz, copy.CellSize, copy.Origin));
    }

    [Fact]
    public void Stamp_MarginMatchesTheTwoPipeClearanceRule_OnAParallelPair()
    {
        // 半徑 50 + 50 + 管間淨距 100 = 200。管 A 在 y=2050；管 B 的格心至少要離 A 的中心線 ≥ 200 + 半格 → 第 23 列（y=2350）
        var s = LoadScene("s01_open_straight");
        var g = Voxelizer.Build(s, Example, 100, 50).Grid!.Clone();
        g.StampCapsule(new Segment(V(550, 2050, 1250), V(5550, 2050, 1250)), 200);
        Assert.True(g.IsBlocked(new CellIndex(10, 22, 12)));              // 格子 [2200,2300] 到中心線 150 < 200
        Assert.False(g.IsBlocked(new CellIndex(10, 23, 12)));             // [2300,2400] 到中心線 250 ≥ 200
        Assert.True(g.IsBlocked(new CellIndex(10, 18, 12)));              // [1800,1900] 到中心線 150
        Assert.False(g.IsBlocked(new CellIndex(10, 17, 12)));             // [1700,1800] 250
        // 實際中心線間距 300，管表面間隙 300 − 100 = 200 ≥ 100 ✓
        var cap = new Capsule(new Segment(V(550, 2350, 1250), V(5550, 2350, 1250)), 50);
        Assert.True(cap.ClearanceTo(new Capsule(new Segment(V(550, 2050, 1250), V(5550, 2050, 1250)), 50)) >= 100);
    }
}
