using PipingRouter.Engine.Geometry;
using Xunit;

namespace PipingRouter.Tests.Geometry;

public class ShapeOverlapTests
{
    private const double Eps = 1e-6;

    private static Aabb Box(double x0, double y0, double z0, double x1, double y1, double z1) =>
        new(new Vec3(x0, y0, z0), new Vec3(x1, y1, z1));

    private static AxisCylinder Cyl(double x, double y, double z, double r, double h, Axis a) =>
        new(new Vec3(x, y, z), r, h, a);

    // ---- 方塊 vs 方塊 ----
    [Theory]
    [InlineData(5, 5, 5, 15, 15, 15, true)]     // 部分重疊
    [InlineData(10, 0, 0, 20, 10, 10, false)]   // 面貼合：不算重疊
    [InlineData(9, 0, 0, 20, 10, 10, true)]     // 重疊 1
    [InlineData(10.001, 0, 0, 20, 10, 10, false)]
    [InlineData(2, 2, 2, 3, 3, 3, true)]        // 包含
    [InlineData(10, 10, 10, 20, 20, 20, false)] // 角貼合
    public void BoxBox(double x0, double y0, double z0, double x1, double y1, double z1, bool expected)
    {
        var a = Box(0, 0, 0, 10, 10, 10);
        var b = Box(x0, y0, z0, x1, y1, z1);
        Assert.Equal(expected, ShapeOverlap.Overlaps(a, b, Eps));
        Assert.Equal(expected, ShapeOverlap.Overlaps(b, a, Eps));
    }

    [Fact]
    public void BoxBox_OverlapThinnerThanTwoEps_IsNotOverlap()
    {
        var a = Box(0, 0, 0, 10, 10, 10);
        Assert.False(ShapeOverlap.Overlaps(a, Box(10 - 1e-7, 0, 0, 20, 10, 10), Eps));
        Assert.True(ShapeOverlap.Overlaps(a, Box(10 - 1e-5, 0, 0, 20, 10, 10), Eps));
    }

    [Fact]
    public void BoxBox_ShrunkShapesExactlyTouching_CountsAsOverlap()
    {
        // eps=0.5（二進位可精確表示）：內縮後 a 為 [0.5,9.5]、b 為 [9.5,…]，剛好接觸 → 閉集合相交
        var a = Box(0, 0, 0, 10, 10, 10);
        Assert.True(ShapeOverlap.Overlaps(a, Box(9, 0, 0, 20, 10, 10), 0.5));
        Assert.False(ShapeOverlap.Overlaps(a, Box(9.25, 0, 0, 20, 10, 10), 0.5));
    }

    // ---- 方塊 vs 圓柱（Z 軸向，底面圓心原點，R=5，H=10）----
    [Theory]
    [InlineData(-1, -1, 2, 1, 1, 4, true)]       // 在內部
    [InlineData(5, -1, 2, 8, 1, 4, false)]       // 側面貼合
    [InlineData(4.9, -1, 2, 8, 1, 4, true)]      // 側面重疊 0.1
    [InlineData(-1, -1, 10, 1, 1, 12, false)]    // 頂面貼合
    [InlineData(-1, -1, 9.9, 1, 1, 12, true)]    // 頂面重疊 0.1
    [InlineData(-1, -1, -3, 1, 1, 0, false)]     // 底面貼合
    [InlineData(4, 4, 0, 8, 8, 10, false)]       // 方塊角到軸 √32 > 5
    [InlineData(3, 3, 0, 8, 8, 10, true)]        // 方塊角到軸 √18 < 5
    public void BoxCylinder_ZAxis(double x0, double y0, double z0, double x1, double y1, double z1, bool expected)
    {
        var cyl = Cyl(0, 0, 0, 5, 10, Axis.Z);
        var box = Box(x0, y0, z0, x1, y1, z1);
        Assert.Equal(expected, ShapeOverlap.Overlaps(box, cyl, Eps));
        Assert.Equal(expected, ShapeOverlap.Overlaps(cyl, box, Eps));
    }

    [Fact]
    public void BoxCylinder_XAxis()
    {
        var cyl = Cyl(0, 0, 0, 5, 10, Axis.X);                         // x 從 0 到 10
        Assert.False(ShapeOverlap.Overlaps(Box(2, 4, 4, 3, 6, 6), cyl, Eps));   // YZ 角到軸 √32
        Assert.True(ShapeOverlap.Overlaps(Box(2, 3, 3, 3, 6, 6), cyl, Eps));    // √18
        Assert.False(ShapeOverlap.Overlaps(Box(10, -1, -1, 12, 1, 1), cyl, Eps)); // 端面貼合
        Assert.True(ShapeOverlap.Overlaps(Box(9.5, -1, -1, 12, 1, 1), cyl, Eps));
    }

    // ---- 圓柱 vs 圓柱 ----
    [Theory]
    [InlineData(9, true)]       // 圓心距 9 < 10
    [InlineData(10, false)]     // 外切
    [InlineData(10.1, false)]
    [InlineData(0, true)]
    public void CylinderCylinder_SameAxis_Radial(double dx, bool expected)
    {
        var a = Cyl(0, 0, 0, 5, 10, Axis.Z);
        var b = Cyl(dx, 0, 5, 5, 10, Axis.Z);
        Assert.Equal(expected, ShapeOverlap.Overlaps(a, b, Eps));
        Assert.Equal(expected, ShapeOverlap.Overlaps(b, a, Eps));
    }

    [Theory]
    [InlineData(9.9, true)]
    [InlineData(10, false)]     // 端面貼合
    [InlineData(11, false)]
    public void CylinderCylinder_SameAxis_Axial(double baseZ, bool expected)
    {
        Assert.Equal(expected, ShapeOverlap.Overlaps(Cyl(0, 0, 0, 5, 10, Axis.Z), Cyl(0, 0, baseZ, 5, 10, Axis.Z), Eps));
    }

    // Z 軸圓柱（R=5，z 0..10）與沿 X 的細桿（R=2，長 40，中心 z=5）
    [Theory]
    [InlineData(0, true)]       // 十字交叉
    [InlineData(6.9, true)]     // 偏 y：|d| < 5+2
    [InlineData(7, false)]      // 剛好相切
    [InlineData(7.1, false)]
    [InlineData(-6.9, true)]
    public void CylinderCylinder_Perpendicular_OffsetY(double yOffset, bool expected)
    {
        var zone = Cyl(0, 0, 0, 5, 10, Axis.Z);
        var rod = Cyl(-20, yOffset, 5, 2, 40, Axis.X);
        Assert.Equal(expected, ShapeOverlap.Overlaps(zone, rod, Eps));
        Assert.Equal(expected, ShapeOverlap.Overlaps(rod, zone, Eps));
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(11.9, true)]    // 桿 z 範圍 [9.9,13.9]，與 0..10 重疊 0.1
    [InlineData(12, false)]     // 剛好貼頂面
    [InlineData(-1.9, true)]
    [InlineData(-2, false)]     // 剛好貼底面
    public void CylinderCylinder_Perpendicular_OffsetZ(double rodCenterZ, bool expected)
    {
        var zone = Cyl(0, 0, 0, 5, 10, Axis.Z);
        var rod = Cyl(-20, 0, rodCenterZ, 2, 40, Axis.X);
        Assert.Equal(expected, ShapeOverlap.Overlaps(zone, rod, Eps));
    }

    [Theory]
    [InlineData(4, true)]       // 桿從 x=4 開始，進入半徑 5
    [InlineData(5, false)]      // 剛好從側面外切開始
    [InlineData(6, false)]
    public void CylinderCylinder_Perpendicular_ShortRod(double rodStartX, bool expected)
    {
        var zone = Cyl(0, 0, 0, 5, 10, Axis.Z);
        var rod = Cyl(rodStartX, 0, 5, 2, 14, Axis.X);
        Assert.Equal(expected, ShapeOverlap.Overlaps(zone, rod, Eps));
    }

    [Theory]
    [InlineData(6.9, true)]
    [InlineData(7.1, false)]
    public void CylinderCylinder_Perpendicular_YAxisRod(double xOffset, bool expected)
    {
        var zone = Cyl(0, 0, 0, 5, 10, Axis.Z);
        var rod = Cyl(xOffset, -20, 5, 2, 40, Axis.Y);
        Assert.Equal(expected, ShapeOverlap.Overlaps(zone, rod, Eps));
    }

    [Fact]
    public void CylinderCylinder_PerpendicularXandY()
    {
        var a = Cyl(-20, 0, 0, 2, 40, Axis.X);
        Assert.True(ShapeOverlap.Overlaps(a, Cyl(0, -20, 0, 2, 40, Axis.Y), Eps));      // 同高度十字
        Assert.True(ShapeOverlap.Overlaps(a, Cyl(0, -20, 3.9, 2, 40, Axis.Y), Eps));    // z 差 3.9 < 4
        Assert.False(ShapeOverlap.Overlaps(a, Cyl(0, -20, 4, 2, 40, Axis.Y), Eps));     // z 差 4 = 外切
    }

    // ---- 與取樣法交叉驗證 ----
    private static bool Inside(Aabb b, Vec3 p, double eps) =>
        p.X >= b.Min.X + eps && p.X <= b.Max.X - eps &&
        p.Y >= b.Min.Y + eps && p.Y <= b.Max.Y - eps &&
        p.Z >= b.Min.Z + eps && p.Z <= b.Max.Z - eps;

    private static bool Inside(AxisCylinder c, Vec3 p, double eps)
    {
        double a = p.Get(c.Axis);
        return a >= c.AxialMin + eps && a <= c.AxialMax - eps && c.RadialDistance(p) <= c.Radius - eps;
    }

    private static Vec3 Rand(Random r, double lo, double hi) =>
        new(lo + r.NextDouble() * (hi - lo), lo + r.NextDouble() * (hi - lo), lo + r.NextDouble() * (hi - lo));

    private static Aabb RandBox(Random r)
    {
        var min = Rand(r, 0, 8);
        return new Aabb(min, min + new Vec3(2 + r.NextDouble() * 8, 2 + r.NextDouble() * 8, 2 + r.NextDouble() * 8));
    }

    private static bool AnySampleInBoth(Aabb region, Func<Vec3, bool> inA, Func<Vec3, bool> inB)
    {
        const int n = 28;
        for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++)
                for (int k = 0; k <= n; k++)
                {
                    var p = new Vec3(
                        region.Min.X + (region.Max.X - region.Min.X) * i / n,
                        region.Min.Y + (region.Max.Y - region.Min.Y) * j / n,
                        region.Min.Z + (region.Max.Z - region.Min.Z) * k / n);
                    if (inA(p) && inB(p)) return true;
                }
        return false;
    }

    [Fact]
    public void RandomShapes_FormulaAgreesWithSampling()
    {
        const double eps = 0.2;
        var rng = new Random(424242);
        int sound = 0, trueCases = 0, missed = 0, total = 0;

        for (int i = 0; i < 700; i++)
        {
            var mode = i % 3;
            Aabb region;
            bool formula;
            Func<Vec3, bool> inA, inB;

            if (mode == 0)
            {
                var a = RandBox(rng);
                var b = RandBox(rng);
                formula = ShapeOverlap.Overlaps(a, b, eps);
                inA = p => Inside(a, p, eps); inB = p => Inside(b, p, eps);
                region = a;
            }
            else if (mode == 1)
            {
                var box = RandBox(rng);
                var cyl = new AxisCylinder(Rand(rng, 0, 12), 1 + rng.NextDouble() * 5, 2 + rng.NextDouble() * 10, (Axis)rng.Next(3));
                formula = ShapeOverlap.Overlaps(box, cyl, eps);
                inA = p => Inside(box, p, eps); inB = p => Inside(cyl, p, eps);
                region = box;
            }
            else
            {
                var c1 = new AxisCylinder(Rand(rng, 0, 12), 1 + rng.NextDouble() * 5, 3 + rng.NextDouble() * 12, (Axis)rng.Next(3));
                var c2 = new AxisCylinder(Rand(rng, 0, 12), 1 + rng.NextDouble() * 5, 3 + rng.NextDouble() * 12, (Axis)rng.Next(3));
                formula = ShapeOverlap.Overlaps(c1, c2, eps);
                inA = p => Inside(c1, p, eps); inB = p => Inside(c2, p, eps);
                region = c1.Bounds;
            }

            bool sampled = AnySampleInBoth(region, inA, inB);
            total++;
            // 取樣找到共同點 ⇒ 公式必須為 true（完全不容許違反）
            if (sampled) { Assert.True(formula, $"第 {i} 案：取樣找到共同點但公式為 false"); sound++; }
            if (formula) { trueCases++; if (!sampled) missed++; }
        }

        Assert.True(sound > 50 && total - trueCases > 50, $"案例分布失衡 sound={sound} true={trueCases} total={total}");
        // 公式為 true 但取樣沒抓到：只應發生在很薄的重疊
        Assert.True(missed <= trueCases * 0.15, $"取樣漏掉 {missed}/{trueCases}，比例過高");
    }
}
