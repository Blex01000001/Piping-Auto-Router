namespace PipingRouter.Engine.Geometry;

/// <summary>
/// 膠囊體：線段加半徑，用來表示管線（半徑 = 外徑/2 + 保溫厚度）。
/// ClearanceTo* 回傳「表面到表面」的間隙（mm）：≥ 0 為不穿透（0 表示剛好接觸），
/// &lt; 0 表示穿透；穿透時的數值只代表「中心線距離 − 半徑」，不是精確穿透深度。
/// </summary>
public readonly record struct Capsule
{
    public Segment Axis { get; }
    public double Radius { get; }

    public Capsule(Segment axis, double radius)
    {
        if (!(radius >= 0) || !double.IsFinite(radius)) throw new ArgumentException("膠囊半徑必須 ≥ 0。");
        Axis = axis;
        Radius = radius;
    }

    public double ClearanceTo(Aabb box) => SegmentAabb.Distance(Axis, box) - Radius;
    public double ClearanceTo(AxisCylinder cyl) => SegmentCylinder.Distance(Axis, cyl) - Radius;
    public double ClearanceTo(Capsule other) => SegmentSegment.Distance(Axis, other.Axis) - Radius - other.Radius;
}
