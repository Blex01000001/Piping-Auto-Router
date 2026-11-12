using PipingRouter.Engine.Geometry;

namespace PipingRouter.Engine.Scene;

public enum ObstacleKind
{
    Equipment,
    Steel,
    Wall,
    Vessel,
}

public enum ServiceZoneKind
{
    Maintenance,
    Operation,
}

/// <summary>障礙物形狀：方塊或軸向圓柱。</summary>
public abstract record Shape
{
    public abstract Aabb Bounds { get; }
}

public sealed record BoxShape(Aabb Box) : Shape
{
    public override Aabb Bounds => Box;
}

public sealed record CylinderShape(AxisCylinder Cylinder) : Shape
{
    public override Aabb Bounds => Cylinder.Bounds;
}

public sealed record Obstacle(string Id, ObstacleKind Kind, Shape Shape);

/// <summary>維修／操作空間：虛擬方塊，只檢查干涉，不是實體。</summary>
public sealed record ServiceZone(string Id, ServiceZoneKind Kind, Aabb Box);

/// <summary>
/// 管線接口：位置與指定方向。Direction 是接口的外向法線，也就是「管線從接口出發時行進的方向」。
/// 例如起點 +X、終點 -X，表示管線離開起點時朝 +X 走，並從終點的 -X 側接近終點（抵達時朝 +X 行進）。
/// </summary>
public sealed record PortSpec(Vec3 Position, AxisDirection Direction);

/// <summary>一條待佈的管線需求。ComponentSequence 為預留（第一階段須為空，佈線器會回報 NotSupported）。</summary>
public sealed record LineRequest(
    string Id,
    double OuterDiameterMm,
    double InsulationThicknessMm,
    PortSpec Start,
    PortSpec End,
    IReadOnlyList<string> ComponentSequence)
{
    /// <summary>碰撞用膠囊半徑：外徑/2 + 保溫厚度。</summary>
    public double CollisionRadiusMm => OuterDiameterMm / 2 + InsulationThicknessMm;
}

/// <summary>人工給定的管線上的元件：位置以「第幾段、離該段起點多遠」表示。</summary>
public sealed record PlacedComponent(string Id, string Type, int SegmentIndex, double DistanceFromStartMm);

/// <summary>一條已有路徑的管線（折線），供規則檢查器檢查。</summary>
public sealed record PipeDef(
    string Id,
    string? LineId,
    double OuterDiameterMm,
    double InsulationThicknessMm,
    IReadOnlyList<Vec3> Points,
    IReadOnlyList<PlacedComponent> Components)
{
    public double CollisionRadiusMm => OuterDiameterMm / 2 + InsulationThicknessMm;
}

public sealed record SceneModel(
    int SchemaVersion,
    string Name,
    bool IsSynthetic,
    double FloorZ,
    Aabb Bounds,
    IReadOnlyList<Obstacle> Obstacles,
    IReadOnlyList<ServiceZone> ServiceZones,
    IReadOnlyList<LineRequest> Lines,
    IReadOnlyList<PipeDef> Pipes)
{
    public const int CurrentSchemaVersion = 1;
}
