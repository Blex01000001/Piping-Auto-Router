using PipingRouter.Engine.Geometry;
using Xunit;

namespace PipingRouter.Tests.Geometry;

/// <summary>主要圓柱：Z 軸向，底面圓心 (0,0,0)，半徑 5，高 10（z 從 0 到 10）。</summary>
public class SegmentCylinderTests
{
    private static readonly AxisCylinder CylZ = new(new Vec3(0, 0, 0), 5, 10, Axis.Z);

    private static Segment Seg(double ax, double ay, double az, double bx, double by, double bz) =>
        new(new Vec3(ax, ay, az), new Vec3(bx, by, bz));

    // ---- 已知相交 ----
    [Theory]
    [InlineData(-10, 0, 5, 10, 0, 5)]          // 水平貫穿軸線
    [InlineData(0, 0, -5, 0, 0, 5)]            // 沿軸線由底面外進入
    [InlineData(0, 0, 2, 0, 0, 8)]             // 完全在內部
    [InlineData(3, 0, 15, 3, 0, 5)]            // 由頂面上方垂直插入
    [InlineData(-10, -10, -5, 10, 10, 15)]     // 斜向貫穿
    [InlineData(-10, 3, 5, 10, 3, 5)]          // 弦：偏離軸線但在半徑內
    public void Intersects_KnownIntersecting(double ax, double ay, double az, double bx, double by, double bz)
    {
        var s = Seg(ax, ay, az, bx, by, bz);
        Assert.True(SegmentCylinder.Intersects(s, CylZ));
        Assert.Equal(0.0, SegmentCylinder.Distance(s, CylZ), 9);
    }

    // ---- 已知不相交（含期望距離）----
    [Theory]
    [InlineData(-10, 0, 11, 10, 0, 11, 1.0)]               // 頂面上方 1
    [InlineData(-10, 6, 5, 10, 6, 5, 1.0)]                 // 側面外 1
    [InlineData(0, 0, -3, 0, 0, -1, 1.0)]                  // 底面下方，沿軸線
    [InlineData(6, 0, 11, 6, 0, 15, 1.4142135623730951)]   // 邊緣外：徑向 1、軸向 1
    [InlineData(-10, -6, 5, 10, -6, 5, 1.0)]
    public void Intersects_KnownNotIntersecting(double ax, double ay, double az, double bx, double by, double bz, double expected)
    {
        var s = Seg(ax, ay, az, bx, by, bz);
        Assert.False(SegmentCylinder.Intersects(s, CylZ));
        Assert.Equal(expected, SegmentCylinder.Distance(s, CylZ), 9);
    }

    // ---- 剛好相切／接觸（閉集合）----
    [Theory]
    [InlineData(-10, 5, 5, 10, 5, 5)]      // 與側面相切於 (0,5,5)
    [InlineData(-10, 0, 10, 10, 0, 10)]    // 躺在頂面平面上
    [InlineData(0, 0, 15, 0, 0, 10)]       // 端點剛好碰頂面
    [InlineData(5, 0, 12, 5, 0, 10)]       // 端點剛好在頂面圓周上
    [InlineData(0, 0, -5, 0, 0, 0)]        // 端點剛好碰底面
    [InlineData(5, -5, 5, 5, 5, 5)]        // 與側面相切於 (5,0,5)
    public void Intersects_Tangent_CountsAsIntersecting(double ax, double ay, double az, double bx, double by, double bz)
    {
        var s = Seg(ax, ay, az, bx, by, bz);
        Assert.True(SegmentCylinder.Intersects(s, CylZ));
        Assert.Equal(0.0, SegmentCylinder.Distance(s, CylZ), 9);
    }

    [Fact]
    public void Intersects_JustOutsideTolerance_IsFalse()
    {
        Assert.False(SegmentCylinder.Intersects(Seg(-10, 5.001, 5, 10, 5.001, 5), CylZ));
        Assert.True(SegmentCylinder.Intersects(Seg(-10, 5.001, 5, 10, 5.001, 5), CylZ, tolerance: 0.01));
    }

    // ---- 其他軸向：確認軸向與底面圓心的語意 ----
    [Fact]
    public void XAxisCylinder()
    {
        var c = new AxisCylinder(new Vec3(0, 0, 0), 2, 10, Axis.X);   // x 從 0 到 10
        Assert.True(SegmentCylinder.Intersects(Seg(5, 0, 3, 5, 0, -3), c));           // 穿過軸線
        Assert.Equal(0.5, SegmentCylinder.Distance(Seg(5, 0, 3, 5, 0, 2.5), c), 9);   // 徑向最近 2.5 → 0.5
        Assert.Equal(1.0, SegmentCylinder.Distance(Seg(-3, 0, 0, -1, 0, 0), c), 9);   // 底面外 1
        Assert.Equal(3.0, SegmentCylinder.Distance(Seg(13, 0, 0, 15, 0, 0), c), 9);   // 頂面外 3
    }

    [Fact]
    public void YAxisCylinder()
    {
        var c = new AxisCylinder(new Vec3(1, 0, 1), 2, 10, Axis.Y);   // y 從 0 到 10
        Assert.True(SegmentCylinder.Intersects(Seg(1, -5, 1, 1, 5, 1), c));
        Assert.Equal(1.0, SegmentCylinder.Distance(Seg(1, -3, 1, 1, -1, 1), c), 9);
        Assert.Equal(1.0, SegmentCylinder.Distance(Seg(1, 5, 4, 1, 5, 4.5), c), 9);   // 徑向 3 − 半徑 2
    }

    [Fact]
    public void SegmentParallelToAxis_AtRadialOffset()
    {
        // 平行於軸線、距離 8（徑向外 3），整段在軸向範圍內
        Assert.Equal(3.0, SegmentCylinder.Distance(Seg(8, 0, 1, 8, 0, 9), CylZ), 9);
    }

    [Fact]
    public void DegenerateSegment_ActsAsPoint()
    {
        Assert.Equal(CylZ.DistanceTo(new Vec3(8, 0, 14)), SegmentCylinder.Distance(Seg(8, 0, 14, 8, 0, 14), CylZ), 12);
    }
}
