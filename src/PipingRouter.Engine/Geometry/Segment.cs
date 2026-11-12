namespace PipingRouter.Engine.Geometry;

/// <summary>線段 AB（管線中心線的一段）。</summary>
public readonly record struct Segment(Vec3 A, Vec3 B)
{
    public Vec3 Direction => B - A;
    public double Length => Direction.Length;
    public bool IsDegenerate => A == B;

    public Vec3 PointAt(double t) => A + Direction * t;

    /// <summary>線段是否沿 X/Y/Z 其中一軸（其他兩軸差異 ≤ tolerance）。退化線段回傳 true。</summary>
    public bool IsAxisAligned(double tolerance = GeometryTolerance.Default)
    {
        var d = Direction;
        int nonZero = 0;
        for (int i = 0; i < 3; i++)
            if (Math.Abs(d[i]) > tolerance) nonZero++;
        return nonZero <= 1;
    }

    /// <summary>線段上離 p 最近點的參數 t ∈ [0,1]。</summary>
    public double ClosestT(Vec3 p)
    {
        var d = Direction;
        double len2 = d.LengthSquared;
        if (len2 == 0) return 0;
        return Math.Clamp((p - A).Dot(d) / len2, 0, 1);
    }

    public double DistanceTo(Vec3 p) => PointAt(ClosestT(p)).DistanceTo(p);
}
