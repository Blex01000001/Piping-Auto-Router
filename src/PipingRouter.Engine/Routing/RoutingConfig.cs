namespace PipingRouter.Engine.Routing;

/// <summary>可用的方向集合。Axis6 = ±X/±Y/±Z；45 度（Axis6Diag45）預留，尚未實作。</summary>
public enum DirectionSetKind
{
    Axis6,
}

/// <summary>成本權重。ZonePenaltyPerCell 只在區域策略為 Penalize 時使用。</summary>
public sealed record RoutingWeights(double Length, double Bend, double ZonePenaltyPerCell);

public sealed record SearchSettings(long MaxExpandedNodes, long Seed);

public sealed record RoutingConfig(
    int SchemaVersion,
    double VoxelSizeMm,
    RoutingWeights Weights,
    DirectionSetKind Directions,
    SearchSettings Search,
    bool DiagnoseOnFailure)
{
    public const int CurrentSchemaVersion = 1;
}
