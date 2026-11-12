using PipingRouter.Engine.Geometry;
using Xunit;

namespace PipingRouter.Tests.Geometry;

public class CapsuleTests
{
    private static readonly Aabb Box = new(new Vec3(0, 0, 0), new Vec3(10, 10, 10));
    private static readonly AxisCylinder CylZ = new(new Vec3(0, 0, 0), 5, 10, Axis.Z);

    private static Segment Seg(double ax, double ay, double az, double bx, double by, double bz) =>
        new(new Vec3(ax, ay, az), new Vec3(bx, by, bz));

    [Fact]
    public void Constructor_RejectsInvalidRadius()
    {
        Assert.Throws<ArgumentException>(() => new Capsule(Seg(0, 0, 0, 1, 0, 0), -1));
        Assert.Throws<ArgumentException>(() => new Capsule(Seg(0, 0, 0, 1, 0, 0), double.NaN));
        _ = new Capsule(Seg(0, 0, 0, 1, 0, 0), 0);   // 0 合法（理想中心線）
    }

    [Theory]
    [InlineData(1.0, 2.0)]    // 中心線距離 3，半徑 1 → 間隙 2
    [InlineData(3.0, 0.0)]    // 剛好接觸：間隙 0（相切）
    [InlineData(4.0, -1.0)]   // 穿透 1
    public void ClearanceToBox(double radius, double expected)
    {
        var pipe = new Capsule(Seg(-10, 5, 5, -3, 5, 5), radius);
        Assert.Equal(expected, pipe.ClearanceTo(Box), 9);
    }

    [Fact]
    public void ClearanceToBox_CenterlineInside_IsNegative()
    {
        var pipe = new Capsule(Seg(-5, 5, 5, 5, 5, 5), 1);
        Assert.True(pipe.ClearanceTo(Box) < 0);
    }

    [Fact]
    public void ClearanceToCylinder()
    {
        // 軸線最近點 (-8,0,5)，距圓柱側面 3；半徑 1.5 → 1.5
        var pipe = new Capsule(Seg(-10, 0, 5, -8, 0, 5), 1.5);
        Assert.Equal(1.5, pipe.ClearanceTo(CylZ), 9);
    }

    [Fact]
    public void ClearanceToCapsule()
    {
        var a = new Capsule(Seg(0, 0, 0, 10, 0, 0), 2);
        var b = new Capsule(Seg(0, 10, 0, 10, 10, 0), 3);
        Assert.Equal(5.0, a.ClearanceTo(b), 9);
        Assert.Equal(5.0, b.ClearanceTo(a), 9);
    }
}
