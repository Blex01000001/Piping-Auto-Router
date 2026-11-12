using PipingRouter.Engine.Geometry;
using Xunit;

namespace PipingRouter.Tests.Geometry;

public class Vec3Tests
{
    [Fact]
    public void Operators_Work()
    {
        var a = new Vec3(1, 2, 3);
        var b = new Vec3(4, 6, 8);
        Assert.Equal(new Vec3(5, 8, 11), a + b);
        Assert.Equal(new Vec3(3, 4, 5), b - a);
        Assert.Equal(new Vec3(2, 4, 6), a * 2);
        Assert.Equal(new Vec3(-1, -2, -3), -a);
        Assert.Equal(1 * 4 + 2 * 6 + 3 * 8, a.Dot(b));
    }

    [Fact]
    public void Length_And_Distance()
    {
        Assert.Equal(5.0, new Vec3(3, 4, 0).Length, 12);
        Assert.Equal(13.0, new Vec3(0, 0, 0).DistanceTo(new Vec3(3, 4, 12)), 12);
    }

    [Fact]
    public void Indexer_Get_With()
    {
        var v = new Vec3(1, 2, 3);
        Assert.Equal(2, v[1]);
        Assert.Equal(3, v.Get(Axis.Z));
        Assert.Equal(new Vec3(1, 9, 3), v.With(Axis.Y, 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => v[3]);
    }

    [Fact]
    public void IsFinite_DetectsNaNAndInfinity()
    {
        Assert.True(new Vec3(1, 2, 3).IsFinite);
        Assert.False(new Vec3(double.NaN, 0, 0).IsFinite);
        Assert.False(new Vec3(0, double.PositiveInfinity, 0).IsFinite);
    }
}
