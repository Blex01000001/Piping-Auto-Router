namespace PipingRouter.Engine.Geometry;

/// <summary>三維向量／點，單位 mm。</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);

    public double this[int index] => index switch
    {
        0 => X,
        1 => Y,
        2 => Z,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public double Get(Axis axis) => this[(int)axis];

    public Vec3 With(Axis axis, double value) => axis switch
    {
        Axis.X => this with { X = value },
        Axis.Y => this with { Y = value },
        _ => this with { Z = value },
    };

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => a * s;

    public double Dot(Vec3 o) => X * o.X + Y * o.Y + Z * o.Z;
    public double LengthSquared => Dot(this);
    public double Length => Math.Sqrt(LengthSquared);
    public double DistanceTo(Vec3 o) => (this - o).Length;

    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);

    public static Vec3 Min(Vec3 a, Vec3 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    public static Vec3 Max(Vec3 a, Vec3 b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
}
