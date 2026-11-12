using PipingRouter.Engine.Geometry;
using Xunit;

namespace PipingRouter.Tests.Geometry;

public class AxisCylinderTests
{
    [Fact]
    public void Constructor_RejectsInvalid()
    {
        Assert.Throws<ArgumentException>(() => new AxisCylinder(Vec3.Zero, 0, 5, Axis.Z));
        Assert.Throws<ArgumentException>(() => new AxisCylinder(Vec3.Zero, -1, 5, Axis.Z));
        Assert.Throws<ArgumentException>(() => new AxisCylinder(Vec3.Zero, 1, 0, Axis.Z));
        Assert.Throws<ArgumentException>(() => new AxisCylinder(Vec3.Zero, double.NaN, 5, Axis.Z));
        Assert.Throws<ArgumentException>(() => new AxisCylinder(new Vec3(double.NaN, 0, 0), 1, 5, Axis.Z));
        Assert.Throws<ArgumentException>(() => new AxisCylinder(Vec3.Zero, 1, 5, (Axis)7));
    }

    [Fact]
    public void BaseCenter_IsBottom_ExtendsAlongPositiveAxis()
    {
        var c = new AxisCylinder(new Vec3(0, 0, 0), 5, 10, Axis.Z);
        Assert.Equal(0, c.AxialMin);
        Assert.Equal(10, c.AxialMax);
        Assert.Equal(new Vec3(0, 0, 10), c.TopCenter);
        Assert.Equal(0.0, c.DistanceTo(new Vec3(0, 0, 5)), 12);    // 中段在內
        Assert.Equal(1.0, c.DistanceTo(new Vec3(0, 0, -1)), 12);   // 底面下方在外（不是對稱延伸）
    }

    [Theory]
    [InlineData(Axis.X)]
    [InlineData(Axis.Y)]
    [InlineData(Axis.Z)]
    public void Bounds_PerAxis(Axis axis)
    {
        // 底面圓心 (3,2,3)，半徑 2，高 10：沿 axis 延伸 10，其他兩軸 ±2
        var c = new AxisCylinder(new Vec3(3, 2, 3), 2, 10, axis);
        var expectedMin = axis switch
        {
            Axis.X => new Vec3(3, 0, 1),
            Axis.Y => new Vec3(1, 2, 1),
            _ => new Vec3(1, 0, 3),
        };
        var expectedMax = axis switch
        {
            Axis.X => new Vec3(13, 4, 5),
            Axis.Y => new Vec3(5, 12, 5),
            _ => new Vec3(5, 4, 13),
        };
        Assert.Equal(expectedMin, c.Bounds.Min);
        Assert.Equal(expectedMax, c.Bounds.Max);
    }

    [Theory]
    [InlineData(0, 0, 5, 0)]         // 軸上
    [InlineData(5, 0, 5, 0)]         // 側面上
    [InlineData(8, 0, 5, 3)]         // 側面外
    [InlineData(0, 0, 13, 3)]        // 頂面外
    [InlineData(0, 0, -2, 2)]        // 底面外
    [InlineData(8, 0, 14, 5)]        // 邊緣外：徑向 3、軸向 4
    [InlineData(3, 4, 5, 0)]         // 半徑 5 剛好在側面上（3-4-5）
    public void DistanceTo_Point(double x, double y, double z, double expected)
    {
        var c = new AxisCylinder(new Vec3(0, 0, 0), 5, 10, Axis.Z);
        Assert.Equal(expected, c.DistanceTo(new Vec3(x, y, z)), 12);
    }
}
