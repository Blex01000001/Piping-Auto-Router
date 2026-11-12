namespace PipingRouter.Engine.Geometry;

/// <summary>軸對齊方塊（最小角點與最大角點）。閉集合：邊界上的點屬於方塊。</summary>
public readonly record struct Aabb
{
    public Vec3 Min { get; }
    public Vec3 Max { get; }

    public Aabb(Vec3 min, Vec3 max)
    {
        if (!min.IsFinite || !max.IsFinite)
            throw new ArgumentException("Aabb 角點必須是有限數值。");
        if (!(min.X <= max.X && min.Y <= max.Y && min.Z <= max.Z))
            throw new ArgumentException($"Aabb 最小角點必須 ≤ 最大角點：min={min}, max={max}");
        Min = min;
        Max = max;
    }

    public Vec3 Center => (Min + Max) * 0.5;
    public Vec3 Size => Max - Min;

    /// <summary>各方向外擴 margin（負值為內縮，內縮過頭會丟例外）。</summary>
    public Aabb Inflate(double margin) =>
        new(new Vec3(Min.X - margin, Min.Y - margin, Min.Z - margin),
            new Vec3(Max.X + margin, Max.Y + margin, Max.Z + margin));

    public bool Contains(Vec3 p) =>
        p.X >= Min.X && p.X <= Max.X &&
        p.Y >= Min.Y && p.Y <= Max.Y &&
        p.Z >= Min.Z && p.Z <= Max.Z;

    /// <summary>閉集合相交：只有面／邊／角接觸也算相交。</summary>
    public bool Intersects(Aabb o) =>
        Min.X <= o.Max.X && Max.X >= o.Min.X &&
        Min.Y <= o.Max.Y && Max.Y >= o.Min.Y &&
        Min.Z <= o.Max.Z && Max.Z >= o.Min.Z;

    public double SquaredDistanceTo(Vec3 p)
    {
        double dx = Math.Max(0, Math.Max(Min.X - p.X, p.X - Max.X));
        double dy = Math.Max(0, Math.Max(Min.Y - p.Y, p.Y - Max.Y));
        double dz = Math.Max(0, Math.Max(Min.Z - p.Z, p.Z - Max.Z));
        return dx * dx + dy * dy + dz * dz;
    }

    /// <summary>點到方塊的距離；點在方塊內或邊界上為 0。</summary>
    public double DistanceTo(Vec3 p) => Math.Sqrt(SquaredDistanceTo(p));
}
