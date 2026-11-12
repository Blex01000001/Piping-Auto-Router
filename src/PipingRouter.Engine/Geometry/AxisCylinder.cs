namespace PipingRouter.Engine.Geometry;

/// <summary>
/// 軸向圓柱（實心、含端面）。BaseCenter 是底面圓心，圓柱沿 +Axis 方向延伸 Height。
/// </summary>
public readonly record struct AxisCylinder
{
    public Vec3 BaseCenter { get; }
    public double Radius { get; }
    public double Height { get; }
    public Axis Axis { get; }

    public AxisCylinder(Vec3 baseCenter, double radius, double height, Axis axis)
    {
        if (!baseCenter.IsFinite) throw new ArgumentException("圓柱底面圓心必須是有限數值。");
        if (!(radius > 0) || !double.IsFinite(radius)) throw new ArgumentException("圓柱半徑必須 > 0。");
        if (!(height > 0) || !double.IsFinite(height)) throw new ArgumentException("圓柱高度必須 > 0。");
        if (!Enum.IsDefined(axis)) throw new ArgumentException("圓柱軸向無效。");
        BaseCenter = baseCenter;
        Radius = radius;
        Height = height;
        Axis = axis;
    }

    public double AxialMin => BaseCenter.Get(Axis);
    public double AxialMax => AxialMin + Height;

    public Vec3 TopCenter => BaseCenter.With(Axis, AxialMax);

    /// <summary>圓柱的外接方塊。</summary>
    public Aabb Bounds
    {
        get
        {
            var min = new Vec3(BaseCenter.X - Radius, BaseCenter.Y - Radius, BaseCenter.Z - Radius);
            var max = new Vec3(BaseCenter.X + Radius, BaseCenter.Y + Radius, BaseCenter.Z + Radius);
            return new Aabb(min.With(Axis, AxialMin), max.With(Axis, AxialMax));
        }
    }

    /// <summary>點到實心圓柱的距離；點在圓柱內或表面上為 0。</summary>
    public double DistanceTo(Vec3 p)
    {
        int ax = (int)Axis;
        double a = p[ax];
        double da = Math.Max(0, Math.Max(AxialMin - a, a - AxialMax));
        double rho = RadialDistance(p);
        double dr = Math.Max(0, rho - Radius);
        return Math.Sqrt(dr * dr + da * da);
    }

    /// <summary>點到圓柱軸線（無限長）的距離。</summary>
    public double RadialDistance(Vec3 p)
    {
        int ax = (int)Axis;
        double s = 0;
        for (int i = 0; i < 3; i++)
        {
            if (i == ax) continue;
            double d = p[i] - BaseCenter[i];
            s += d * d;
        }
        return Math.Sqrt(s);
    }
}
