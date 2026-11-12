using PipingRouter.Engine.Geometry;
using Xunit;

namespace PipingRouter.Tests.Geometry;

/// <summary>方塊固定為 (0,0,0)–(10,10,10)。</summary>
public class SegmentAabbTests
{
    private static readonly Aabb Box = new(new Vec3(0, 0, 0), new Vec3(10, 10, 10));

    private static Segment Seg(double ax, double ay, double az, double bx, double by, double bz) =>
        new(new Vec3(ax, ay, az), new Vec3(bx, by, bz));

    // ---- 已知相交 ----
    [Theory]
    [InlineData(-5, 5, 5, 15, 5, 5)]       // 貫穿
    [InlineData(-5, 5, 5, 5, 5, 5)]        // 一端在內部
    [InlineData(2, 2, 2, 8, 8, 8)]         // 完全在內部
    [InlineData(-1, -1, -1, 11, 11, 11)]   // 對角線貫穿
    [InlineData(5, 5, -5, 5, 5, 15)]       // 垂直貫穿
    [InlineData(5, 5, 5, 5, 5, 5)]         // 退化成點，在內部
    public void Intersects_KnownIntersecting(double ax, double ay, double az, double bx, double by, double bz)
    {
        var s = Seg(ax, ay, az, bx, by, bz);
        Assert.True(SegmentAabb.Intersects(s, Box));
        Assert.Equal(0.0, SegmentAabb.Distance(s, Box), 12);
    }

    // ---- 已知不相交 ----
    [Theory]
    [InlineData(-5, -1, 5, 15, -1, 5)]     // 平行擦過，隔 1
    [InlineData(-5, 5, 5, -1, 5, 5)]       // 線段太短，差 1
    [InlineData(11, 5, 5, 20, 5, 5)]       // 在方塊另一側
    [InlineData(-5, 3, 5, 3, -5, 5)]       // 斜線擦過角落
    [InlineData(13, 14, 5, 13, 14, 5)]     // 退化成點，在外部
    public void Intersects_KnownNotIntersecting(double ax, double ay, double az, double bx, double by, double bz)
    {
        var s = Seg(ax, ay, az, bx, by, bz);
        Assert.False(SegmentAabb.Intersects(s, Box));
        Assert.True(SegmentAabb.Distance(s, Box) > 0.5);
    }

    // ---- 剛好相切／接觸（閉集合：算相交，距離為 0）----
    [Theory]
    [InlineData(-5, 5, 5, 0, 5, 5)]        // 端點剛好碰到面
    [InlineData(10, -5, 5, 10, 15, 5)]     // 整條躺在 x=10 的面上
    [InlineData(-1, 1, 5, 1, -1, 5)]       // 只碰到一條邊 (0,0,z)
    [InlineData(-1, 1, 0, 1, -1, 0)]       // 只碰到一個角 (0,0,0)
    [InlineData(-5, 0, 0, 0, 0, 0)]        // 端點在角上
    [InlineData(0, 0, 10, 0, 10, 10)]      // 沿頂面的邊
    public void Intersects_Tangent_CountsAsIntersecting(double ax, double ay, double az, double bx, double by, double bz)
    {
        var s = Seg(ax, ay, az, bx, by, bz);
        Assert.True(SegmentAabb.Intersects(s, Box));
        Assert.True(SegmentAabb.Intersects(s, Box, tolerance: 0));   // 不靠容差緩衝，slab 本身就必須是閉集合
        Assert.Equal(0.0, SegmentAabb.Distance(s, Box), 12);
    }

    [Fact]
    public void Intersects_JustOutsideTolerance_IsFalse()
    {
        // 離面 1e-3 mm，遠大於預設容差 1e-9
        Assert.False(SegmentAabb.Intersects(Seg(-5, 5, 5, -0.001, 5, 5), Box));
        // 但給足夠容差就算接觸
        Assert.True(SegmentAabb.Intersects(Seg(-5, 5, 5, -0.001, 5, 5), Box, tolerance: 0.01));
    }

    // ---- 距離的精確值 ----
    public static IEnumerable<object[]> DistanceCases()
    {
        yield return new object[] { Seg(-3, 2, 2, -3, 8, 8), 3.0 };                 // 面外 3
        yield return new object[] { Seg(13, 14, 5, 13, 20, 5), 5.0 };               // 3-4-5，最近點在線段端點
        yield return new object[] { Seg(2, 20, 5, 20, 2, 5), Math.Sqrt(2) };       // 最近點在線段中段（x+y=22 到角 (10,10)）
        yield return new object[] { Seg(-5, 3, 5, 3, -5, 5), Math.Sqrt(2) };       // 最近點在中段（x+y=-2 到角 (0,0)）
        yield return new object[] { Seg(13, 14, 22, 13, 14, 30), 13.0 };            // 3-4-12 角外
        yield return new object[] { Seg(13, 14, 5, 13, 14, 5), 5.0 };               // 退化點
        yield return new object[] { Seg(-4, -3, -12, 20, -3, -12), Math.Sqrt(9 + 144) };  // 平行擦過邊，最近點在中段
    }

    [Theory]
    [MemberData(nameof(DistanceCases))]
    public void Distance_ExactValues(Segment s, double expected)
    {
        Assert.Equal(expected, SegmentAabb.Distance(s, Box), 9);
    }

    [Fact]
    public void Intersects_IsSymmetricUnderReversal()
    {
        var s = Seg(-5, 5, 5, 15, 5, 5);
        var r = new Segment(s.B, s.A);
        Assert.Equal(SegmentAabb.Intersects(s, Box), SegmentAabb.Intersects(r, Box));
        Assert.Equal(SegmentAabb.Distance(s, Box), SegmentAabb.Distance(r, Box), 12);
    }
}
