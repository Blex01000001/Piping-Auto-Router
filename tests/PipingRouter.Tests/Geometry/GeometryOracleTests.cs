using PipingRouter.Engine.Geometry;
using Xunit;

namespace PipingRouter.Tests.Geometry;

/// <summary>
/// 以固定種子的隨機案例，與獨立的檢查方式交叉驗證：
/// 1) slab 相交 ⇔ 精確距離為 0；
/// 2) 取樣法（沿線段密集取點）與公式結果在 Lipschitz 誤差內一致。
/// 距離函數沿線段是 1-Lipschitz（斜率 ≤ 線段長），故
///   公式值 ≤ 取樣最小值，且 ≥ 取樣最小值 − 線段長 × 0.5/N。
/// </summary>
public class GeometryOracleTests
{
    private const int Samples = 2000;

    private static double Coord(Random r, int lo, int hi) => r.Next(lo, hi + 1);

    private static Vec3 RandInt(Random r) => new(Coord(r, -10, 25), Coord(r, -10, 25), Coord(r, -10, 25));
    /// <summary>落在方塊外擴 6 的範圍內的整數點，讓相交與不相交案例都夠多。</summary>
    private static Vec3 Near(Random r, Vec3 lo, Vec3 hi) => new(
        r.Next((int)lo.X - 6, (int)hi.X + 7),
        r.Next((int)lo.Y - 6, (int)hi.Y + 7),
        r.Next((int)lo.Z - 6, (int)hi.Z + 7));

    private static Vec3 RandReal(Random r) => new(r.NextDouble() * 35 - 10, r.NextDouble() * 35 - 10, r.NextDouble() * 35 - 10);

    [Fact]
    public void SegmentAabb_SlabAgreesWithDistance_IntegerCases()
    {
        var rng = new Random(20240601);
        int intersecting = 0, separated = 0;
        for (int i = 0; i < 3000; i++)
        {
            var p = RandInt(rng);
            var q = new Vec3(p.X + rng.Next(1, 6), p.Y + rng.Next(1, 6), p.Z + rng.Next(1, 6));
            var box = new Aabb(p, q);
            var s = new Segment(Near(rng, p, q), Near(rng, p, q));

            bool slab = SegmentAabb.Intersects(s, box);
            double dist = SegmentAabb.Distance(s, box);
            Assert.Equal(slab, dist <= 1e-9);
            if (slab) intersecting++; else separated++;
        }
        Assert.True(intersecting > 100 && separated > 100, $"案例分布失衡：{intersecting}/{separated}");
    }

    [Fact]
    public void SegmentAabb_DistanceMatchesSampling()
    {
        var rng = new Random(7);
        for (int i = 0; i < 400; i++)
        {
            var p = RandReal(rng);
            var box = new Aabb(p, p + new Vec3(rng.NextDouble() * 8, rng.NextDouble() * 8, rng.NextDouble() * 8));
            var s = new Segment(RandReal(rng), RandReal(rng));

            double exact = SegmentAabb.Distance(s, box);
            double sampled = double.MaxValue;
            for (int k = 0; k <= Samples; k++)
                sampled = Math.Min(sampled, box.DistanceTo(s.PointAt((double)k / Samples)));

            Assert.True(exact <= sampled + 1e-9, $"exact={exact} 大於取樣最小值 {sampled}");
            Assert.True(exact >= sampled - s.Length * 0.5 / Samples - 1e-9, $"exact={exact} 遠小於取樣最小值 {sampled}");
        }
    }

    [Fact]
    public void SegmentCylinder_DistanceMatchesSampling()
    {
        var rng = new Random(11);
        for (int i = 0; i < 600; i++)
        {
            var axis = (Axis)rng.Next(0, 3);
            var cyl = new AxisCylinder(RandReal(rng), 1 + rng.NextDouble() * 5, 1 + rng.NextDouble() * 10, axis);
            var s = new Segment(RandReal(rng), RandReal(rng));

            double dist = SegmentCylinder.Distance(s, cyl);
            double sampled = double.MaxValue;
            for (int k = 0; k <= Samples; k++)
                sampled = Math.Min(sampled, cyl.DistanceTo(s.PointAt((double)k / Samples)));

            Assert.True(dist <= sampled + 1e-9, $"dist={dist} 大於取樣最小值 {sampled}");
            Assert.True(dist >= sampled - s.Length * 0.5 / Samples - 1e-9, $"dist={dist} 遠小於取樣最小值 {sampled}");
        }
    }

    [Fact]
    public void SegmentSegment_DistanceMatchesSampling()
    {
        const int n = 150;
        var rng = new Random(23);
        for (int i = 0; i < 200; i++)
        {
            var a = new Segment(RandReal(rng), RandReal(rng));
            var b = new Segment(RandReal(rng), RandReal(rng));

            double exact = SegmentSegment.Distance(a, b);
            double sampled = double.MaxValue;
            for (int u = 0; u <= n; u++)
            {
                var pa = a.PointAt((double)u / n);
                for (int v = 0; v <= n; v++)
                    sampled = Math.Min(sampled, pa.DistanceTo(b.PointAt((double)v / n)));
            }

            Assert.True(exact <= sampled + 1e-9);
            Assert.True(exact >= sampled - (a.Length + b.Length) * 0.5 / n - 1e-9);
        }
    }
}
