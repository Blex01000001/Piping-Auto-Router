namespace PipingRouter.Engine.Geometry;

/// <summary>線段與軸對齊方塊的相交與距離。</summary>
public static class SegmentAabb
{
    /// <summary>
    /// 閉集合相交（slab 法）：線段與方塊（外擴 tolerance）有任何共同點即 true，
    /// 包含剛好接觸面／邊／角、線段落在面上、端點在內部。
    /// </summary>
    public static bool Intersects(Segment s, Aabb box, double tolerance = GeometryTolerance.Default)
    {
        var b = tolerance > 0 ? box.Inflate(tolerance) : box;
        double tMin = 0, tMax = 1;
        var d = s.Direction;
        for (int i = 0; i < 3; i++)
        {
            double p = s.A[i], di = d[i], lo = b.Min[i], hi = b.Max[i];
            if (di == 0)
            {
                if (p < lo || p > hi) return false;
                continue;
            }
            double t1 = (lo - p) / di, t2 = (hi - p) / di;
            if (t1 > t2) (t1, t2) = (t2, t1);
            if (t1 > tMin) tMin = t1;
            if (t2 < tMax) tMax = t2;
            if (tMin > tMax) return false;
        }
        return true;
    }

    /// <summary>
    /// 線段到方塊的最短距離（相交或接觸為 0）。精確解：沿線段的距離平方是分段二次式，
    /// 斷點為線段進出各軸 slab 的位置；各區間取二次式的極小點（夾在區間內）。
    /// </summary>
    public static double Distance(Segment s, Aabb box)
    {
        var d = s.Direction;
        var ts = new List<double>(8) { 0, 1 };
        for (int i = 0; i < 3; i++)
        {
            if (d[i] == 0) continue;
            AddBreak(ts, (box.Min[i] - s.A[i]) / d[i]);
            AddBreak(ts, (box.Max[i] - s.A[i]) / d[i]);
        }
        ts.Sort();

        double best = double.MaxValue;
        for (int k = 0; k + 1 < ts.Count; k++)
        {
            double t0 = ts[k], t1 = ts[k + 1];
            Consider(t0);
            Consider(t1);
            if (t1 <= t0) continue;

            // 區間內各軸「作用中」的邊界固定 → d²(t) = A t² + B t + C
            double tm = 0.5 * (t0 + t1);
            var pm = s.PointAt(tm);
            double qa = 0, qb = 0;
            for (int i = 0; i < 3; i++)
            {
                double c;
                if (pm[i] < box.Min[i]) c = box.Min[i];
                else if (pm[i] > box.Max[i]) c = box.Max[i];
                else continue;
                double off = s.A[i] - c;
                qa += d[i] * d[i];
                qb += 2 * off * d[i];
            }
            if (qa > 0) Consider(Math.Clamp(-qb / (2 * qa), t0, t1));
        }
        return Math.Sqrt(best);

        void Consider(double t)
        {
            double v = box.SquaredDistanceTo(s.PointAt(t));   // 直接以點計算，避免二次式相消誤差
            if (v < best) best = v;
        }
    }

    private static void AddBreak(List<double> ts, double t)
    {
        if (t > 0 && t < 1) ts.Add(t);
    }
}
