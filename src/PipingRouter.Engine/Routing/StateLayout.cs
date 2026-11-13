namespace PipingRouter.Engine.Routing;

/// <summary>
/// 搜尋狀態的編碼：(格子, 方向, 連續直管格數 run ∈ [1,N], 元件序列索引 comp ∈ [0,ComponentCount))。
/// 元件序列（逆止閥 → 手閥 → 流量計…）是後續階段：狀態裡先留好 comp 這一維，第一階段 ComponentCount = 1、comp 恆為 0，
/// 到時候只要把 ComponentCount 改成「元件數 + 1」並在放置元件時遞增 comp，其他編碼邏輯不用動。
/// </summary>
internal sealed class StateLayout
{
    public int DirectionCount { get; }
    public int MinRun { get; }
    public int ComponentCount { get; }

    public StateLayout(int directionCount, int minRun, int componentCount = 1)
    {
        if (directionCount < 1 || minRun < 1 || componentCount < 1) throw new ArgumentOutOfRangeException();
        DirectionCount = directionCount;
        MinRun = minRun;
        ComponentCount = componentCount;
    }

    /// <summary>每個格子的狀態數 = 方向數 × N × 元件數。</summary>
    public long StatesPerCell => (long)DirectionCount * MinRun * ComponentCount;

    public int Encode(int linear, int dir, int run, int comp = 0) =>
        (((linear * DirectionCount + dir) * MinRun) + (run - 1)) * ComponentCount + comp;

    public void Decode(int state, out int linear, out int dir, out int run, out int comp)
    {
        comp = state % ComponentCount;
        int rest = state / ComponentCount;
        run = rest % MinRun + 1;
        rest /= MinRun;
        dir = rest % DirectionCount;
        linear = rest / DirectionCount;
    }
}
