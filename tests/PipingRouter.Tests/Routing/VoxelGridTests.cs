using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Routing;
using Xunit;
using static PipingRouter.Tests.Rules.RuleTestKit;

namespace PipingRouter.Tests.Routing;

public class VoxelGridTests
{
    internal static VoxelGrid Empty(double sx, double sy, double sz, double voxel, Vec3? origin = null)
    {
        var o = origin ?? Vec3.Zero;
        var bounds = new Aabb(o, o + new Vec3(sx, sy, sz));
        var scene = Scene() with { Bounds = bounds };
        var r = Voxelizer.Build(scene, Example, voxel, 50);
        Assert.True(r.Ok, r.Message);
        return r.Grid!;
    }

    [Fact]
    public void Dimensions_CoverTheBounds()
    {
        var g = Empty(1000, 500, 300, 100);
        Assert.Equal((10, 5, 3), (g.Nx, g.Ny, g.Nz));
        Assert.Equal(150, g.CellCount);
    }

    [Fact]
    public void Dimensions_RoundUp_WhenNotMultiple()
    {
        var g = Empty(950, 1000, 1000, 100);
        Assert.Equal(10, g.Nx);
        Assert.Equal(11, Empty(1001, 100, 100, 100).Nx);
    }

    [Fact]
    public void Dimensions_AreRobustToFloatingPointNoise()
    {
        Assert.Equal(3, Empty(0.3, 0.3, 0.3, 0.1).Nx);       // 0.3/0.1 = 3.0000000000000004
    }

    [Fact]
    public void CellCenterAndBox()
    {
        var g = Empty(1000, 500, 300, 100, new Vec3(-200, 50, 0));
        Assert.Equal(new Vec3(-150, 100, 50), g.CellCenter(new CellIndex(0, 0, 0)));
        Assert.Equal(new Vec3(550, 500, 250), g.CellCenter(new CellIndex(7, 4, 2)));
        var box = g.CellBox(new CellIndex(1, 2, 0));
        Assert.Equal(new Vec3(-100, 250, 0), box.Min);
        Assert.Equal(new Vec3(0, 350, 100), box.Max);
    }

    [Theory]
    [InlineData(0, 0, 0, true, 0, 0, 0)]
    [InlineData(99.9, 0, 0, true, 0, 0, 0)]
    [InlineData(100, 0, 0, true, 1, 0, 0)]          // 剛好在邊界：歸入座標較大的格
    [InlineData(550, 250, 150, true, 5, 2, 1)]
    [InlineData(1000, 500, 300, true, 9, 4, 2)]     // 範圍上緣（含）歸入最後一格
    [InlineData(1000.1, 0, 0, false, 0, 0, 0)]
    [InlineData(-0.1, 0, 0, false, 0, 0, 0)]
    [InlineData(0, 500.1, 0, false, 0, 0, 0)]
    [InlineData(0, 0, -1, false, 0, 0, 0)]
    public void CellOf(double x, double y, double z, bool ok, int ix, int iy, int iz)
    {
        var g = Empty(1000, 500, 300, 100);
        Assert.Equal(ok, g.TryCellOf(new Vec3(x, y, z), out var c));
        if (ok) Assert.Equal(new CellIndex(ix, iy, iz), c);
    }

    [Fact]
    public void CellOf_RejectsNonFinite()
    {
        var g = Empty(1000, 500, 300, 100);
        Assert.False(g.TryCellOf(new Vec3(double.NaN, 0, 0), out _));
        Assert.False(g.TryCellOf(new Vec3(0, double.PositiveInfinity, 0), out _));
    }

    [Fact]
    public void Snap_ReportsDistanceToCellCenter()
    {
        var g = Empty(1000, 500, 300, 100);
        Assert.True(g.TrySnap(new Vec3(120, 50, 50), out var c, out double off));
        Assert.Equal(new CellIndex(1, 0, 0), c);
        Assert.Equal(30, off, 9);                                    // 中心 (150,50,50)
        Assert.True(g.TrySnap(new Vec3(150, 50, 50), out _, out off));
        Assert.Equal(0, off, 9);                                     // 已在中心
        Assert.True(g.TrySnap(new Vec3(100, 100, 100), out c, out off));
        Assert.Equal(new CellIndex(1, 1, 1), c);
        Assert.Equal(Math.Sqrt(3) * 50, off, 9);
    }

    [Fact]
    public void OutOfRangeCells_AreBlocked_AndLinearIndexIsUnique()
    {
        var g = Empty(300, 200, 100, 100);
        Assert.True(g.IsBlocked(new CellIndex(-1, 0, 0)));
        Assert.True(g.IsBlocked(new CellIndex(3, 0, 0)));
        Assert.True(g.IsBlocked(new CellIndex(0, 0, 1)));
        var seen = new HashSet<int>();
        for (int z = 0; z < g.Nz; z++)
            for (int y = 0; y < g.Ny; y++)
                for (int x = 0; x < g.Nx; x++)
                    Assert.True(seen.Add(g.LinearIndex(new CellIndex(x, y, z))));
        Assert.Equal(g.CellCount, seen.Count);
    }
}
