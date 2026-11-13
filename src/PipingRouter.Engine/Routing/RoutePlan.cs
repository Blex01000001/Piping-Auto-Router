namespace PipingRouter.Engine.Routing;

/// <summary>
/// 評估一個佈線順序的結果。Order 是佈線順序（場景 lines 的索引）；Results[i] 是 scene.Lines[i] 的結果（不是依佈線順序排列）。
/// Score = 成功管線的成本總和 + 失敗管線數 × 失敗懲罰（皆 ×1000 定點），越小越好。
/// 失敗懲罰要大到讓「少失敗」永遠優先於「少成本」，所以日後 MCTS／遺傳演算法／模擬退火可以直接拿 Score 當目標函數。
/// </summary>
public sealed record PlanEvaluation(
    IReadOnlyList<int> Order,
    IReadOnlyList<RouteResult> Results,
    int FailedCount,
    long RouteCost,
    long PenaltyCost)
{
    public long Score => RouteCost + PenaltyCost;

    public bool AllSucceeded => FailedCount == 0;

    public string OrderKey => string.Join(",", Order);
}
