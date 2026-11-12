using PipingRouter.Engine.Geometry;
using Xunit;

namespace PipingRouter.Tests.Geometry;

public class ShapeDistanceTests
{
    private static Aabb Box(double x0, double y0, double z0, double x1, double y1, double z1) =>
        new(new Vec3(x0, y0, z0), new Vec3(x1, y1, z1));

    [Theory]
    [InlineData(13, 0, 0, 15, 10, 10, 3.0)]        // 面對面
    [InlineData(13, 14, 0, 15, 20, 10, 5.0)]       // 邊對邊（3-4-5）
    [InlineData(13, 14, 22, 15, 20, 30, 13.0)]     // 角對角（3-4-12）
    [InlineData(10, 0, 0, 20, 10, 10, 0.0)]        // 貼合
    [InlineData(5, 5, 5, 15, 15, 15, 0.0)]         // 重疊
    [InlineData(2, 2, 2, 3, 3, 3, 0.0)]            // 包含
    public void BoxBox(double x0, double y0, double z0, double x1, double y1, double z1, double expected)
    {
        var a = Box(0, 0, 0, 10, 10, 10);
        var b = Box(x0, y0, z0, x1, y1, z1);
        Assert.Equal(expected, ShapeDistance.Between(a, b), 12);
        Assert.Equal(expected, ShapeDistance.Between(b, a), 12);
    }

    // Z 軸圓柱：底面圓心原點、R=5、z 0..10
    [Theory]
    [InlineData(8, -1, 2, 9, 1, 4, 3.0)]                 // 側面外：軸心到方形 8，扣半徑 5
    [InlineData(-1, -1, 13, 1, 1, 14, 3.0)]              // 頂面上方：軸向間隙 3
    [InlineData(8, -1, 13, 9, 1, 14, 4.242640687119285)] // 軸向 3、徑向 3 → √18
    [InlineData(-1, -1, 2, 1, 1, 4, 0.0)]                // 在內部
    [InlineData(5, -1, 2, 8, 1, 4, 0.0)]                 // 側面貼合
    [InlineData(3, 4, 2, 6, 8, 4, 0.0)]                  // 方形角 (3,4) 恰在半徑 5 上
    [InlineData(4, 4, 2, 6, 8, 4, 0.6568542494923806)]   // 方形角 (4,4) 距軸心 √32，扣 5
    public void BoxCylinder_ZAxis(double x0, double y0, double z0, double x1, double y1, double z1, double expected)
    {
        var cyl = new AxisCylinder(Vec3.Zero, 5, 10, Axis.Z);
        var box = Box(x0, y0, z0, x1, y1, z1);
        Assert.Equal(expected, ShapeDistance.Between(box, cyl), 12);
        Assert.Equal(expected, ShapeDistance.Between(cyl, box), 12);
    }

    [Fact]
    public void BoxCylinder_XAxis()
    {
        var cyl = new AxisCylinder(Vec3.Zero, 2, 10, Axis.X);          // x 0..10
        Assert.Equal(1.0, ShapeDistance.Between(Box(11, -1, -1, 12, 1, 1), cyl), 12);   // 端面外
        Assert.Equal(3.0, ShapeDistance.Between(Box(2, 5, -1, 3, 6, 1), cyl), 12);      // 徑向 5 − 2
    }

    [Fact]
    public void RandomCases_MatchPointSampling()
    {
        // 取樣法：在方塊 A 內取格點，用 B 的「點到形狀距離」取最小。
        // 取樣最小值 ≥ 真值；且因距離函數 1-Lipschitz，真值 ≥ 取樣最小值 − 格點半對角線。
        var rng = new Random(2024);
        const int n = 16;
        for (int i = 0; i < 300; i++)
        {
            var min = new Vec3(rng.NextDouble() * 10, rng.NextDouble() * 10, rng.NextDouble() * 10);
            var a = new Aabb(min, min + new Vec3(1 + rng.NextDouble() * 6, 1 + rng.NextDouble() * 6, 1 + rng.NextDouble() * 6));

            double exact;
            Func<Vec3, double> pointDist;
            if (i % 2 == 0)
            {
                var m2 = new Vec3(rng.NextDouble() * 20 - 5, rng.NextDouble() * 20 - 5, rng.NextDouble() * 20 - 5);
                var b = new Aabb(m2, m2 + new Vec3(1 + rng.NextDouble() * 6, 1 + rng.NextDouble() * 6, 1 + rng.NextDouble() * 6));
                exact = ShapeDistance.Between(a, b);
                pointDist = b.DistanceTo;
            }
            else
            {
                var c = new AxisCylinder(new Vec3(rng.NextDouble() * 20 - 5, rng.NextDouble() * 20 - 5, rng.NextDouble() * 20 - 5),
                    1 + rng.NextDouble() * 4, 1 + rng.NextDouble() * 8, (Axis)rng.Next(3));
                exact = ShapeDistance.Between(a, c);
                pointDist = c.DistanceTo;
            }

            double sampled = double.MaxValue;
            for (int x = 0; x <= n; x++)
                for (int y = 0; y <= n; y++)
                    for (int z = 0; z <= n; z++)
                    {
                        var p = new Vec3(
                            a.Min.X + (a.Max.X - a.Min.X) * x / n,
                            a.Min.Y + (a.Max.Y - a.Min.Y) * y / n,
                            a.Min.Z + (a.Max.Z - a.Min.Z) * z / n);
                        sampled = Math.Min(sampled, pointDist(p));
                    }
            var s = a.Size;
            double halfDiag = 0.5 * new Vec3(s.X / n, s.Y / n, s.Z / n).Length;
            Assert.True(exact <= sampled + 1e-9, $"案例 {i}：exact={exact} > sampled={sampled}");
            Assert.True(exact >= sampled - halfDiag - 1e-9, $"案例 {i}：exact={exact} 遠小於 sampled={sampled}");
        }
    }
}
