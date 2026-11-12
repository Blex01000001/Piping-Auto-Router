using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Scene;

namespace PipingRouter.Engine.Rules;

/// <summary>折線的沿程參數化：頂點累積長度、彎頭位置，以及「沿程距離 → 座標」。</summary>
internal sealed class PolylineInfo
{
    private const double BendCrossTolerance = 1e-9;

    public PipeDef Pipe { get; }
    public double[] S { get; }                 // 各頂點的累積長度
    public double Total { get; }
    public IReadOnlyList<int> BendVertices { get; }
    public IReadOnlyList<double> BendS { get; }

    public PolylineInfo(PipeDef pipe)
    {
        Pipe = pipe;
        var pts = pipe.Points;
        S = new double[pts.Count];
        for (int i = 1; i < pts.Count; i++)
            S[i] = S[i - 1] + pts[i].DistanceTo(pts[i - 1]);
        Total = pts.Count > 0 ? S[^1] : 0;

        var bends = new List<int>();
        for (int i = 1; i + 1 < pts.Count; i++)
            if (IsBend(pts[i - 1], pts[i], pts[i + 1])) bends.Add(i);
        BendVertices = bends;
        BendS = bends.Select(i => S[i]).ToList();
    }

    public int SegmentCount => Math.Max(0, Pipe.Points.Count - 1);

    public Segment Seg(int i) => new(Pipe.Points[i], Pipe.Points[i + 1]);

    /// <summary>方向有變化（含 180 度折返）的內部頂點才算彎頭；共線的中間點不算。</summary>
    private static bool IsBend(Vec3 a, Vec3 b, Vec3 c)
    {
        var d1 = b - a;
        var d2 = c - b;
        double l1 = d1.Length, l2 = d2.Length;
        if (l1 == 0 || l2 == 0) return false;
        var cross = new Vec3(
            d1.Y * d2.Z - d1.Z * d2.Y,
            d1.Z * d2.X - d1.X * d2.Z,
            d1.X * d2.Y - d1.Y * d2.X);
        if (cross.Length / (l1 * l2) > BendCrossTolerance) return true;
        return d1.Dot(d2) < 0;
    }

    public Vec3 PointAtS(double s)
    {
        var pts = Pipe.Points;
        if (pts.Count == 0) return Vec3.Zero;
        if (s <= 0) return pts[0];
        if (s >= Total) return pts[^1];
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            if (s <= S[i + 1])
            {
                double len = S[i + 1] - S[i];
                double t = len == 0 ? 0 : (s - S[i]) / len;
                return Seg(i).PointAt(t);
            }
        }
        return pts[^1];
    }
}
