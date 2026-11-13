namespace PipingRouter.Engine.Routing;

/// <summary>可用的方向集合。Axis6 = ±X/±Y/±Z；45 度（Axis6Diag45）預留，尚未實作。</summary>
public enum DirectionSetKind
{
    Axis6,
}

/// <summary>成本權重。ZonePenaltyPerCell 只在區域策略為 Penalize 時使用。</summary>
public sealed record RoutingWeights(double Length, double Bend, double ZonePenaltyPerCell);

public sealed record SearchSettings(long MaxExpandedNodes, long Seed);

/// <summary>
/// 多條管線整體評估的設定。FailurePenalty 是每條佈線失敗的管線加上的固定懲罰（成本單位，與長度權重同一個尺度），
/// 要大到讓「少失敗」永遠優先於「少成本」；範例值僅供示範。
/// </summary>
public sealed record PlanningSettings(double FailurePenalty)
{
    public static PlanningSettings Default { get; } = new(1_000_000);
}

public sealed record RoutingConfig(
    int SchemaVersion,
    double VoxelSizeMm,
    RoutingWeights Weights,
    DirectionSetKind Directions,
    SearchSettings Search,
    bool DiagnoseOnFailure)
{
    public const int CurrentSchemaVersion = 1;

    public PlanningSettings Planning { get; init; } = PlanningSettings.Default;
}
