using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Rules;
using PipingRouter.Engine.Scene;

namespace PipingRouter.Engine.Routing;

/// <summary>
/// 多條管線依序佈線：每條管線從乾淨的基底格子複製出來，蓋上「前面已成功的管線」（管半徑和 + 管間最小淨距），
/// 再交給 <see cref="IRouter"/> 佈線。佈線器一次只佈一條，順序由 <see cref="IRouteOrderStrategy"/> 決定。
///
/// 約定：
///  • 前面失敗的管線不佔位置（沒有路徑可蓋）；後面的管線只會被「成功」的管線擋住。
///  • 對同一個順序的結果有快取（Evaluate 重複呼叫不重算），EvaluationCount 是實際佈線過的不同順序數。
///  • 失敗診斷（RoutingConfig.DiagnoseOnFailure）沿用設定；做順序搜尋時建議關掉以節省時間。
///    這裡的診斷只有同一格子上的放寬（最小直管、抵達方向），不含需要重新體素化的淨距放寬。
///  • 不是執行緒安全的；同一個 planner 一次只給一個執行緒用。
/// </summary>
public sealed class RoutePlanner
{
    private readonly SceneModel _scene;
    private readonly RuleConfig _rules;
    private readonly RoutingConfig _config;
    private readonly IRouter _router;
    private readonly VoxelizeOptions? _options;
    private readonly RouteConstraints _constraints;
    private readonly long _penaltyUnits;
    private readonly Dictionary<double, VoxelizeResult> _baseGrids = new();
    private readonly Dictionary<string, PlanEvaluation> _memo = new(StringComparer.Ordinal);

    public RoutePlanner(SceneModel scene, RuleConfig rules, RoutingConfig config,
        IRouter? router = null, VoxelizeOptions? options = null)
    {
        _scene = scene;
        _rules = rules;
        _config = config;
        _router = router ?? new AStarRouter();
        _options = options;
        _constraints = RouteConstraints.From(rules);
        _penaltyUnits = (long)Math.Round(config.Planning.FailurePenalty * CostUnits.Scale);
    }

    public int LineCount => _scene.Lines.Count;

    /// <summary>實際佈線過的不同順序數（快取命中不算）。</summary>
    public int EvaluationCount => _memo.Count;

    /// <summary>讓策略挑順序，回傳該順序的評估結果。</summary>
    public PlanEvaluation Plan(IRouteOrderStrategy strategy)
    {
        var order = strategy.Choose(LineCount, Evaluate);
        return Evaluate(order);
    }

    /// <summary>
    /// 依指定順序佈線並評估。order 必須是 0..LineCount-1 的排列，否則丟 ArgumentException
    /// （順序是程式產生的，不是使用者輸入）。
    /// </summary>
    public PlanEvaluation Evaluate(IReadOnlyList<int> order)
    {
        ValidateOrder(order);
        string key = string.Join(",", order);
        if (_memo.TryGetValue(key, out var cached)) return cached;

        var lines = _scene.Lines;
        var results = new RouteResult[lines.Count];
        var routed = new List<(RouteResult Result, double Radius)>();
        int failed = 0;
        long routeCost = 0;

        foreach (int idx in order)
        {
            var line = lines[idx];
            var result = RouteOne(line, routed);
            results[idx] = result;
            if (result.Success)
            {
                routed.Add((result, line.CollisionRadiusMm));
                routeCost += result.Metrics!.Cost.Total;
            }
            else
            {
                failed++;
            }
        }

        var evaluation = new PlanEvaluation(order.ToArray(), results, failed, routeCost, failed * _penaltyUnits);
        _memo[key] = evaluation;
        return evaluation;
    }

    /// <summary>
    /// 對整個計畫做規則檢查並掛在評估結果上：所有成功的管線一起檢查（含管對管淨距與干涉）。
    /// 失敗的管線沒有折線，不納入。管線順序依場景順序，所以報告的順序確定。
    /// </summary>
    public PlanEvaluation AttachRuleReport(PlanEvaluation evaluation)
    {
        var pipes = new List<PipeDef>();
        for (int i = 0; i < _scene.Lines.Count; i++)
            if (evaluation.Results[i].Success)
                pipes.Add(RouteResultExtensions.ToPipe(_scene.Lines[i], evaluation.Results[i]));
        return evaluation with { RuleReport = RuleChecker.Check(_scene, _rules, pipes) };
    }

    private RouteResult RouteOne(LineRequest line, List<(RouteResult Result, double Radius)> routed)
    {
        double radius = line.CollisionRadiusMm;
        if (!_baseGrids.TryGetValue(radius, out var built))
            _baseGrids[radius] = built = Voxelizer.Build(_scene, _rules, _config.VoxelSizeMm, radius, _options);

        if (built.Grid == null)
        {
            return RouteResult.Fail(line.Id,
                new RouteFailure(built.ErrorCode ?? "InvalidInput", built.Message ?? "無法建立格子"),
                new RouteSearchInfo(0, _config.Search.Seed, _config.VoxelSizeMm));
        }

        var grid = built.Grid.Clone();
        foreach (var (prev, prevRadius) in routed)
        {
            double margin = prevRadius + radius + _rules.Clearance.PipeToPipeMinMm;
            for (int i = 0; i + 1 < prev.Polyline.Count; i++)
                grid.StampCapsule(new Segment(prev.Polyline[i], prev.Polyline[i + 1]), margin);
        }
        return _router.Route(grid, line, _config, _constraints);
    }

    private void ValidateOrder(IReadOnlyList<int> order)
    {
        int n = LineCount;
        if (order.Count != n)
            throw new ArgumentException($"佈線順序有 {order.Count} 項，但場景有 {n} 條管線。");
        var seen = new bool[n];
        foreach (int i in order)
        {
            if (i < 0 || i >= n) throw new ArgumentException($"佈線順序含有不存在的管線索引 {i}（有效範圍 0..{n - 1}）。");
            if (seen[i]) throw new ArgumentException($"佈線順序重複出現管線索引 {i}。");
            seen[i] = true;
        }
    }
}
