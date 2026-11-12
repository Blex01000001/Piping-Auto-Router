using PipingRouter.Engine.Geometry;
using Xunit;

namespace PipingRouter.Tests.Geometry;

public class AabbTests
{
    private static Aabb Box(double x0, double y0, double z0, double x1, double y1, double z1) =>
        new(new Vec3(x0, y0, z0), new Vec3(x1, y1, z1));

    [Fact]
    public void Constructor_RejectsInvalid()
    {
        Assert.Throws<ArgumentException>(() => Box(1, 0, 0, 0, 1, 1));
        Assert.Throws<ArgumentException>(() => Box(double.NaN, 0, 0, 1, 1, 1));
        Assert.Throws<ArgumentException>(() => Box(0, 0, 0, double.PositiveInfinity, 1, 1));
    }

    [Fact]
    public void Constructor_AllowsFlatBox()
    {
        var b = Box(0, 0, 0, 10, 10, 0);
        Assert.Equal(new Vec3(10, 10, 0), b.Size);
    }

    [Fact]
    public void Center_Size_Inflate()
    {
        var b = Box(0, 0, 0, 10, 20, 30);
        Assert.Equal(new Vec3(5, 10, 15), b.Center);
        Assert.Equal(new Vec3(10, 20, 30), b.Size);
        var g = b.Inflate(2);
        Assert.Equal(new Vec3(-2, -2, -2), g.Min);
        Assert.Equal(new Vec3(12, 22, 32), g.Max);
        Assert.Throws<ArgumentException>(() => b.Inflate(-6));
    }

    [Fact]
    public void Contains_IsClosed()
    {
        var b = Box(0, 0, 0, 10, 10, 10);
        Assert.True(b.Contains(new Vec3(5, 5, 5)));
        Assert.True(b.Contains(new Vec3(0, 0, 0)));
        Assert.True(b.Contains(new Vec3(10, 10, 10)));
        Assert.False(b.Contains(new Vec3(10.0001, 5, 5)));
    }

    [Fact]
    public void Intersects_Overlap_Touch_Separated()
    {
        var a = Box(0, 0, 0, 10, 10, 10);
        Assert.True(a.Intersects(Box(5, 5, 5, 15, 15, 15)));    // 重疊
        Assert.True(a.Intersects(Box(10, 0, 0, 20, 10, 10)));   // 面接觸
        Assert.True(a.Intersects(Box(10, 10, 10, 20, 20, 20))); // 角接觸
        Assert.False(a.Intersects(Box(10.001, 0, 0, 20, 10, 10)));
        Assert.True(a.Intersects(Box(2, 2, 2, 3, 3, 3)));       // 包含
    }

    [Theory]
    [InlineData(5, 5, 5, 0)]       // 內部
    [InlineData(10, 5, 5, 0)]      // 表面
    [InlineData(13, 5, 5, 3)]      // 面外
    [InlineData(13, 14, 5, 5)]     // 邊外（3-4-5）
    [InlineData(13, 14, 22, 13)]   // 角外（3-4-12）
    [InlineData(-3, 5, 5, 3)]
    public void DistanceTo_Point(double x, double y, double z, double expected)
    {
        var b = Box(0, 0, 0, 10, 10, 10);
        Assert.Equal(expected, b.DistanceTo(new Vec3(x, y, z)), 12);
    }
}
