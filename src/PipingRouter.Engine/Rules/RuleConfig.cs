using PipingRouter.Engine.Geometry;

namespace PipingRouter.Engine.Rules;

/// <summary>維修／操作區的處理方式。預設為 Forbid（禁止穿越）；Penalize 為日後可選。</summary>
public enum ZonePolicy
{
    Forbid,
    Penalize,
}

public sealed record ClearanceConfig(double PipeToObstacleMinMm, double PipeToPipeMinMm);

public sealed record ServiceZonePolicyConfig(ZonePolicy Maintenance, ZonePolicy Operation);

public sealed record ValueRange(double Min, double Max);

public sealed record ComponentSize(double LengthMm, double WidthMm, double HeightMm);

/// <summary>手輪淨空區：以手輪中心為基準的圓柱（軸向 Z）或方塊。</summary>
public abstract record ClearanceZoneSpec;

public sealed record CylinderClearanceZone(double RadiusMm, double HeightMm) : ClearanceZoneSpec;

public sealed record BoxClearanceZone(Vec3 SizeMm) : ClearanceZoneSpec;

public sealed record HandwheelSpec(ValueRange HeightAboveFloorMm, Vec3 CenterOffsetFromAxisMm, ClearanceZoneSpec ClearanceZone);

/// <summary>前後所需的直管段，以管外徑的倍數表示。</summary>
public sealed record StraightRunSpec(double UpstreamDiameters, double DownstreamDiameters);

public sealed record ComponentTypeSpec(ComponentSize? Size, HandwheelSpec? Handwheel, StraightRunSpec? StraightRun);

/// <summary>
/// 規則設定。所有數值來自 JSON；範例檔的 Profile 為 "EXAMPLE"，數值僅供示範，非任何規範依據。
/// </summary>
public sealed record RuleConfig(
    int SchemaVersion,
    string Profile,
    string Disclaimer,
    double EpsilonMm,
    ClearanceConfig Clearance,
    double MinStraightBetweenBendsMm,
    ServiceZonePolicyConfig ServiceZonePolicy,
    IReadOnlyDictionary<string, Severity> Severities,
    IReadOnlyDictionary<string, ComponentTypeSpec> ComponentTypes)
{
    public const int CurrentSchemaVersion = 1;
    public const string ExampleProfile = "EXAMPLE";

    public bool IsExample => Profile == ExampleProfile;

    public Severity SeverityOf(string ruleId) =>
        Severities.TryGetValue(ruleId, out var s) ? s : RuleIds.DefaultSeverity(ruleId);
}
