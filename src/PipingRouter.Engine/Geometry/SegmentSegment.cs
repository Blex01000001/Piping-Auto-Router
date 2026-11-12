namespace PipingRouter.Engine.Geometry;

/// <summary>線段與線段的最短距離（管對管淨距用）。</summary>
public static class SegmentSegment
{
    private const double DegenerateLenSq = 1e-18;

    public static double Distance(Segment s1, Segment s2)
    {
        var (t1, t2) = ClosestParameters(s1, s2);
        return s1.PointAt(t1).DistanceTo(s2.PointAt(t2));
    }

    public static (double T1, double T2) ClosestParameters(Segment s1, Segment s2)
    {
        var d1 = s1.Direction;
        var d2 = s2.Direction;
        var r = s1.A - s2.A;
        double a = d1.Dot(d1), e = d2.Dot(d2), f = d2.Dot(r);
        double s, t;

        if (a <= DegenerateLenSq && e <= DegenerateLenSq) return (0, 0);
        if (a <= DegenerateLenSq)
        {
            s = 0;
            t = Math.Clamp(f / e, 0, 1);
        }
        else
        {
            double c = d1.Dot(r);
            if (e <= DegenerateLenSq)
            {
                t = 0;
                s = Math.Clamp(-c / a, 0, 1);
            }
            else
            {
                double b = d1.Dot(d2);
                double denom = a * e - b * b;
                s = denom > 1e-12 * a * e ? Math.Clamp((b * f - c * e) / denom, 0, 1) : 0;
                t = (b * s + f) / e;
                if (t < 0)
                {
                    t = 0;
                    s = Math.Clamp(-c / a, 0, 1);
                }
                else if (t > 1)
                {
                    t = 1;
                    s = Math.Clamp((b - c) / a, 0, 1);
                }
            }
        }
        return (s, t);
    }
}
