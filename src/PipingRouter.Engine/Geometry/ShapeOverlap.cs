namespace PipingRouter.Engine.Geometry;

/// <summary>
/// 實體形狀之間的「重疊」判斷（手輪淨空區等體積對體積的檢查用）。
/// 語意：把兩個形狀各自往內縮 eps，再做閉集合相交。
/// 因此「剛好貼合／相切」不算重疊（淨空區貼著牆是允許的），重疊需超過約 2·eps 才成立。
/// 全部是精確解（方塊與軸向圓柱的組合）：
///   方塊–方塊：逐軸區間；方塊–圓柱：軸向區間 + 端面方形到軸心的距離；
///   同軸圓柱：軸向區間 + 圓心距；垂直軸向圓柱：消去一個變數後的閉式條件。
/// </summary>
public static class ShapeOverlap
{
    public static bool Overlaps(Aabb a, Aabb b, double eps)
    {
        for (int i = 0; i < 3; i++)
        {
            double aLo = a.Min[i] + eps, aHi = a.Max[i] - eps;
            double bLo = b.Min[i] + eps, bHi = b.Max[i] - eps;
            if (aLo > aHi || bLo > bHi) return false;      // 內縮後為空
            if (aLo > bHi || bLo > aHi) return false;
        }
        return true;
    }

    public static bool Overlaps(Aabb box, AxisCylinder cyl, double eps)
    {
        int a = (int)cyl.Axis;
        double r = cyl.Radius - eps;
        double cLo = cyl.AxialMin + eps, cHi = cyl.AxialMax - eps;
        if (r < 0 || cLo > cHi) return false;

        double[] lo = { box.Min.X + eps, box.Min.Y + eps, box.Min.Z + eps };
        double[] hi = { box.Max.X - eps, box.Max.Y - eps, box.Max.Z - eps };
        for (int i = 0; i < 3; i++)
            if (lo[i] > hi[i]) return false;

        if (lo[a] > cHi || cLo > hi[a]) return false;      // 軸向區間不重疊

        double sq = 0;
        for (int i = 0; i < 3; i++)
        {
            if (i == a) continue;
            double c = cyl.BaseCenter[i];
            double d = Math.Max(0, Math.Max(lo[i] - c, c - hi[i]));
            sq += d * d;
        }
        return sq <= r * r;
    }

    public static bool Overlaps(AxisCylinder cyl, Aabb box, double eps) => Overlaps(box, cyl, eps);

    public static bool Overlaps(AxisCylinder p, AxisCylinder q, double eps)
    {
        double rp = p.Radius - eps, rq = q.Radius - eps;
        double pLo = p.AxialMin + eps, pHi = p.AxialMax - eps;
        double qLo = q.AxialMin + eps, qHi = q.AxialMax - eps;
        if (rp < 0 || rq < 0 || pLo > pHi || qLo > qHi) return false;

        int a = (int)p.Axis, b = (int)q.Axis;
        if (a == b)
        {
            if (pLo > qHi || qLo > pHi) return false;
            double sq = 0;
            for (int i = 0; i < 3; i++)
            {
                if (i == a) continue;
                double d = p.BaseCenter[i] - q.BaseCenter[i];
                sq += d * d;
            }
            double sum = rp + rq;
            return sq <= sum * sum;
        }

        // 垂直軸向：p 沿 a 軸、q 沿 b 軸，c 為第三軸。
        //   p：a ∈ [pLo,pHi]，(b,c) 平面圓盤（圓心 p.b, p.c，半徑 rp）
        //   q：b ∈ [qLo,qHi]，(a,c) 平面圓盤（圓心 q.a, q.c，半徑 rq）
        // 固定 c：p 需要某個 b ∈ [qLo,qHi] 落在圓盤內 ⇒ |c − p.c| ≤ w；
        //        q 需要某個 a ∈ [pLo,pHi] 落在圓盤內 ⇒ |c − q.c| ≤ v。
        // 存在這樣的 c ⇔ |p.c − q.c| ≤ w + v。
        int c3 = 3 - a - b;
        double need = DistToInterval(p.BaseCenter[b], qLo, qHi);
        if (need > rp) return false;
        double w = Math.Sqrt(rp * rp - need * need);
        double dz = DistToInterval(q.BaseCenter[a], pLo, pHi);
        if (dz > rq) return false;
        double v = Math.Sqrt(rq * rq - dz * dz);
        return Math.Abs(p.BaseCenter[c3] - q.BaseCenter[c3]) <= w + v;
    }

    private static double DistToInterval(double x, double lo, double hi) =>
        Math.Max(0, Math.Max(lo - x, x - hi));
}
