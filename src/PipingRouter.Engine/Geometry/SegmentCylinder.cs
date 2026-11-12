namespace PipingRouter.Engine.Geometry;

/// <summary>線段與軸向圓柱（實心）的相交與距離。</summary>
public static class SegmentCylinder
{
    private const int Iterations = 120;

    /// <summary>閉集合相交：距離 ≤ tolerance（含剛好相切、貼端面、端點在內部）。</summary>
    public static bool Intersects(Segment s, AxisCylinder cyl, double tolerance = GeometryTolerance.Default) =>
        Distance(s, cyl) <= tolerance;

    /// <summary>
    /// 線段到圓柱的最短距離。沿線段的「點到圓柱距離」是凸函數
    /// （軸向項與徑向項皆為凸且非負），故以三分搜尋找最小值；
    /// 直接對距離（非距離平方）搜尋，使相交時的結果誤差在 1e-12 量級。
    /// </summary>
    public static double Distance(Segment s, AxisCylinder cyl) => Closest(s, cyl).Distance;

    /// <summary>同 <see cref="Distance"/>，並回傳線段上最近點的參數 t ∈ [0,1]。</summary>
    public static (double Distance, double T) Closest(Segment s, AxisCylinder cyl)
    {
        double lo = 0, hi = 1;
        for (int i = 0; i < Iterations; i++)
        {
            double m1 = lo + (hi - lo) / 3, m2 = hi - (hi - lo) / 3;
            if (cyl.DistanceTo(s.PointAt(m1)) <= cyl.DistanceTo(s.PointAt(m2))) hi = m2;
            else lo = m1;
        }
        double tMid = 0.5 * (lo + hi);
        double best = cyl.DistanceTo(s.PointAt(tMid));
        double bestT = tMid;
        double dA = cyl.DistanceTo(s.A);
        if (dA < best) { best = dA; bestT = 0; }
        double dB = cyl.DistanceTo(s.B);
        if (dB < best) { best = dB; bestT = 1; }
        return (best, bestT);
    }
}
