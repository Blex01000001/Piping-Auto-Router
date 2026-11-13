namespace PipingRouter.Engine.Routing;

/// <summary>
/// 決定多條管線的佈線順序。策略自己決定要評估哪些順序、評估幾次（evaluate 會記住算過的順序，重複評估不花成本），
/// 最後回傳選定的順序。MCTS、遺傳演算法、模擬退火日後各自實作這個介面即可——
/// <see cref="RoutePlanner"/> 與佈線器（<see cref="IRouter"/>）都不需要修改。
/// </summary>
public interface IRouteOrderStrategy
{
    IReadOnlyList<int> Choose(int lineCount, Func<IReadOnlyList<int>, PlanEvaluation> evaluate);
}

/// <summary>照場景給定的順序（0, 1, 2…）。不做任何搜尋。</summary>
public sealed class GivenOrderStrategy : IRouteOrderStrategy
{
    public IReadOnlyList<int> Choose(int lineCount, Func<IReadOnlyList<int>, PlanEvaluation> evaluate) =>
        Enumerable.Range(0, lineCount).ToArray();
}

/// <summary>指定一個固定順序。</summary>
public sealed class FixedOrderStrategy : IRouteOrderStrategy
{
    private readonly int[] _order;

    public FixedOrderStrategy(IEnumerable<int> order) => _order = order.ToArray();

    public IReadOnlyList<int> Choose(int lineCount, Func<IReadOnlyList<int>, PlanEvaluation> evaluate)
    {
        if (_order.Length != lineCount)
            throw new ArgumentException($"固定順序有 {_order.Length} 條，但場景有 {lineCount} 條管線。");
        return _order;
    }
}

/// <summary>
/// 窮舉所有順序，取 Score 最小者（同分取字典序最前者）。只適合管線很少的情況：n! 次評估。
/// 超過 maxLines 條管線時丟 ArgumentException（由程式員選用的策略，不是使用者輸入）。
/// 也當作之後各種啟發式搜尋的「正確答案」參考。
/// </summary>
public sealed class ExhaustiveOrderStrategy : IRouteOrderStrategy
{
    private readonly int _maxLines;

    public ExhaustiveOrderStrategy(int maxLines = 7) => _maxLines = maxLines;

    public IReadOnlyList<int> Choose(int lineCount, Func<IReadOnlyList<int>, PlanEvaluation> evaluate)
    {
        if (lineCount > _maxLines)
            throw new ArgumentException($"窮舉 {lineCount}! 種順序太多（上限 {_maxLines} 條管線）；請改用其他策略。");

        var perm = Enumerable.Range(0, lineCount).ToArray();
        int[] best = (int[])perm.Clone();
        long bestScore = long.MaxValue;
        do
        {
            long score = evaluate(perm).Score;
            if (score < bestScore)
            {
                bestScore = score;
                best = (int[])perm.Clone();
            }
        } while (NextPermutation(perm));
        return best;
    }

    /// <summary>字典序的下一個排列；已是最後一個回傳 false。</summary>
    private static bool NextPermutation(int[] a)
    {
        int i = a.Length - 2;
        while (i >= 0 && a[i] >= a[i + 1]) i--;
        if (i < 0) return false;
        int j = a.Length - 1;
        while (a[j] <= a[i]) j--;
        (a[i], a[j]) = (a[j], a[i]);
        Array.Reverse(a, i + 1, a.Length - i - 1);
        return true;
    }
}
