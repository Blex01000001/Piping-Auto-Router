using PipingRouter.Engine.Geometry;
using Xunit;

namespace PipingRouter.Tests.Geometry;

public class SegmentTests
{
    [Fact]
    public void Length_PointAt()
    {
        var s = new Segment(new Vec3(0, 0, 0), new Vec3(3, 4, 0));
        Assert.Equal(5.0, s.Length, 12);
        Assert.Equal(new Vec3(1.5, 2, 0), s.PointAt(0.5));
    }

    [Fact]
    public void IsAxisAligned()
    {
        Assert.True(new Segment(new Vec3(0, 0, 0), new Vec3(5, 0, 0)).IsAxisAligned());
        Assert.True(new Segment(new Vec3(1, 1, 1), new Vec3(1, 1, 9)).IsAxisAligned());
        Assert.True(new Segment(new Vec3(1, 1, 1), new Vec3(1, 1, 1)).IsAxisAligned());   // 退化
        Assert.False(new Segment(new Vec3(0, 0, 0), new Vec3(1, 1, 0)).IsAxisAligned());
    }

    [Fact]
    public void ClosestT_ClampsToEnds()
    {
        var s = new Segment(new Vec3(0, 0, 0), new Vec3(10, 0, 0));
        Assert.Equal(0.5, s.ClosestT(new Vec3(5, 7, 0)), 12);
        Assert.Equal(0.0, s.ClosestT(new Vec3(-4, 1, 0)), 12);
        Assert.Equal(1.0, s.ClosestT(new Vec3(14, 1, 0)), 12);
        Assert.Equal(0.0, new Segment(new Vec3(1, 1, 1), new Vec3(1, 1, 1)).ClosestT(new Vec3(5, 5, 5)));
    }

    [Fact]
    public void DistanceTo_Point()
    {
        var s = new Segment(new Vec3(0, 0, 0), new Vec3(10, 0, 0));
        Assert.Equal(7.0, s.DistanceTo(new Vec3(5, 7, 0)), 12);
        Assert.Equal(5.0, s.DistanceTo(new Vec3(13, 4, 0)), 12);
    }
}
