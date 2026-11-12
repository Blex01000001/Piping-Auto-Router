namespace PipingRouter.Engine.Geometry;

/// <summary>
/// 實體形狀之間的最短距離（相交或接觸為 0）。體素化用它判斷「格子是否太靠近障礙」。
/// 兩者都是精確解：方塊與軸向圓柱都是「軸向區間 × 垂直平面上的二維形狀」的乘積集合，
/// 乘積集合的距離平方 = 各分量距離平方之和。
/// </summary>
public static class ShapeDistance
{
    public static double Between(Aabb a, Aabb b)
    {
        double sq = 0;
        for (int i = 0; i < 3; i++)
        {
            double g = IntervalGap(a.Min[i], a.Max[i], b.Min[i], b.Max[i]);
            sq += g * g;
        }
        return Math.Sqrt(sq);
    }

    public static double Between(Aabb box, AxisCylinder cyl)
    {
        int a = (int)cyl.Axis;
        double axial = IntervalGap(box.Min[a], box.Max[a], cyl.AxialMin, cyl.AxialMax);

        // 垂直平面：圓盤（圓心＝軸心、半徑 R）到方形的距離 = max(0, 軸心到方形距離 − R)
        double sq = 0;
        for (int i = 0; i < 3; i++)
        {
            if (i == a) continue;
            double c = cyl.BaseCenter[i];
            double d = Math.Max(0, Math.Max(box.Min[i] - c, c - box.Max[i]));
            sq += d * d;
        }
        double radial = Math.Max(0, Math.Sqrt(sq) - cyl.Radius);
        return Math.Sqrt(axial * axial + radial * radial);
    }

    public static double Between(AxisCylinder cyl, Aabb box) => Between(box, cyl);

    private static double IntervalGap(double aLo, double aHi, double bLo, double bHi) =>
        Math.Max(0, Math.Max(aLo - bHi, bLo - aHi));
}
