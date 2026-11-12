namespace PipingRouter.Engine.Geometry;

/// <summary>六個軸向方向。順序固定（+X,-X,+Y,-Y,+Z,-Z），搜尋展開順序依此，以確保確定性。</summary>
public enum AxisDirection
{
    PosX = 0,
    NegX = 1,
    PosY = 2,
    NegY = 3,
    PosZ = 4,
    NegZ = 5,
}

public static class AxisDirections
{
    public static readonly IReadOnlyList<AxisDirection> All = new[]
    {
        AxisDirection.PosX, AxisDirection.NegX, AxisDirection.PosY,
        AxisDirection.NegY, AxisDirection.PosZ, AxisDirection.NegZ,
    };

    public static Vec3 ToVector(this AxisDirection d) => d switch
    {
        AxisDirection.PosX => new Vec3(1, 0, 0),
        AxisDirection.NegX => new Vec3(-1, 0, 0),
        AxisDirection.PosY => new Vec3(0, 1, 0),
        AxisDirection.NegY => new Vec3(0, -1, 0),
        AxisDirection.PosZ => new Vec3(0, 0, 1),
        AxisDirection.NegZ => new Vec3(0, 0, -1),
        _ => throw new ArgumentOutOfRangeException(nameof(d)),
    };

    public static AxisDirection Opposite(this AxisDirection d) => (AxisDirection)((int)d ^ 1);

    public static Axis GetAxis(this AxisDirection d) => (Axis)((int)d / 2);

    /// <summary>"+X"、"-Y" 之類的文字表示（JSON 用）。</summary>
    public static string ToText(this AxisDirection d) => d switch
    {
        AxisDirection.PosX => "+X",
        AxisDirection.NegX => "-X",
        AxisDirection.PosY => "+Y",
        AxisDirection.NegY => "-Y",
        AxisDirection.PosZ => "+Z",
        AxisDirection.NegZ => "-Z",
        _ => throw new ArgumentOutOfRangeException(nameof(d)),
    };

    public static bool TryParse(string? text, out AxisDirection direction)
    {
        foreach (var d in All)
        {
            if (string.Equals(d.ToText(), text, StringComparison.OrdinalIgnoreCase))
            {
                direction = d;
                return true;
            }
        }
        direction = default;
        return false;
    }
}
