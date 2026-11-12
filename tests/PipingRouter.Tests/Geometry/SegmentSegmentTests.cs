using PipingRouter.Engine.Geometry;
using Xunit;

namespace PipingRouter.Tests.Geometry;

public class SegmentSegmentTests
{
    private static Segment Seg(double ax, double ay, double az, double bx, double by, double bz) =>
        new(new Vec3(ax, ay, az), new Vec3(bx, by, bz));

    [Theory]
    [InlineData(-5, 0, 0, 5, 0, 0, 0, -5, 0, 0, 5, 0, 0.0)]       // 垂直相交
    [InlineData(-5, 0, 0, 5, 0, 0, 0, -5, 3, 0, 5, 3, 3.0)]       // 歪斜垂直，間隔 3
    [InlineData(0, 0, 0, 10, 0, 0, 0, 2, 0, 10, 2, 0, 2.0)]       // 平行重疊，間隔 2
    [InlineData(0, 0, 0, 10, 0, 0, 13, 4, 0, 20, 4, 0, 5.0)]      // 平行錯開：端點對端點 3-4-5
    [InlineData(0, 0, 0, 10, 0, 0, 15, 0, 0, 20, 0, 0, 5.0)]      // 共線不重疊
    [InlineData(0, 0, 0, 10, 0, 0, 5, 3, 0, 5, 10, 0, 3.0)]       // T 型未接觸
    [InlineData(0, 0, 0, 1, 0, 0, 4, 4, 0, 5, 5, 0, 5.0)]         // 最近點為兩端點
    [InlineData(0, 0, 0, 10, 0, 0, 10, 0, 0, 10, 5, 0, 0.0)]      // 端點接觸
    [InlineData(5, 3, 0, 5, 3, 0, 0, 0, 0, 10, 0, 0, 3.0)]        // 其中一條退化成點
    [InlineData(0, 0, 0, 0, 0, 0, 3, 4, 0, 3, 4, 0, 5.0)]         // 兩條都退化
    [InlineData(0, 0, 0, 10, 0, 0, 5, 0, 0, 5, 0, 0, 0.0)]        // 退化點在線段上
    public void Distance_ExactValues(
        double ax, double ay, double az, double bx, double by, double bz,
        double cx, double cy, double cz, double dx, double dy, double dz, double expected)
    {
        var s1 = Seg(ax, ay, az, bx, by, bz);
        var s2 = Seg(cx, cy, cz, dx, dy, dz);
        Assert.Equal(expected, SegmentSegment.Distance(s1, s2), 9);
        Assert.Equal(expected, SegmentSegment.Distance(s2, s1), 9);                       // 對稱
        Assert.Equal(expected, SegmentSegment.Distance(new Segment(s1.B, s1.A), s2), 9);  // 反向
    }
}
