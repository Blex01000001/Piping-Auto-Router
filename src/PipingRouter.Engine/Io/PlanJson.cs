using PipingRouter.Engine.Routing;

namespace PipingRouter.Engine.Io;

/// <summary>
/// 多條管線整體評估結果（<see cref="PlanEvaluation"/>）的 JSON 讀寫，Dashboard 可以直接顯示整個計畫。
/// 欄位：schemaVersion、order（佈線順序，場景 lines 的索引）、failedCount、routeCost、penaltyCost、score（皆 ×1000 定點）、
/// results（依場景順序，每條是一個內嵌的佈線結果，不含各自的 schemaVersion）、ruleReport（整體規則報告，含管對管檢查；選填）。
/// 同樣是確定的輸出：欄位順序固定、不含耗時。
/// </summary>
public static class PlanJson
{
    public const int CurrentSchemaVersion = 1;

    public static string Serialize(PlanEvaluation e)
    {
        var order = new JArr();
        order.AddRange(e.Order.Select(i => (object?)i));
        var results = new JArr();
        results.AddRange(e.Results.Select(r => (object?)RouteResultJson.ToObj(r, withSchemaVersion: false)));
        var root = new JObj()
            .Add("schemaVersion", CurrentSchemaVersion)
            .Add("order", order)
            .Add("failedCount", e.FailedCount)
            .Add("routeCost", e.RouteCost)
            .Add("penaltyCost", e.PenaltyCost)
            .Add("score", e.Score)
            .Add("results", results)
            .Add("ruleReport", e.RuleReport == null ? null : RuleReportJson.ToObj(e.RuleReport));
        return JsonOut.Write(root);
    }

    public static LoadResult<PlanEvaluation> Parse(string? json) => JsonLoader.Parse(json, Read);
    public static LoadResult<PlanEvaluation> Load(string path) => JsonLoader.Load(path, Read);
    public static void Save(string path, PlanEvaluation e) => JsonLoader.Save(path, Serialize(e));

    private static PlanEvaluation? Read(JsonObj root, IssueList issues)
    {
        int version = root.ReqInt("schemaVersion");
        if (root.Has("schemaVersion") && version != CurrentSchemaVersion)
            root.Error("schemaVersion", "UnsupportedSchemaVersion", $"不支援的 schemaVersion {version}（目前支援 {CurrentSchemaVersion}）");

        var results = new List<RouteResult>();
        foreach (var o in root.ObjArray("results", true))
        {
            var r = RouteResultJson.ReadEmbedded(o, issues);
            if (r != null) results.Add(r);
        }

        var vecOrder = new List<int>();
        var orderObj = root.VecArrayOfInts("order");
        vecOrder.AddRange(orderObj);

        int failed = root.ReqInt("failedCount");
        long routeCost = root.ReqLong("routeCost");
        long penaltyCost = root.ReqLong("penaltyCost");
        long score = root.ReqLong("score");

        Engine.Rules.RuleReport? report = null;
        var rr = root.Obj("ruleReport", false);
        if (rr != null) report = RuleReportJson.ReadReport(rr);

        // ---- 一致性 ----
        int n = results.Count;
        var sorted = vecOrder.OrderBy(x => x).ToList();
        if (!sorted.SequenceEqual(Enumerable.Range(0, n)))
            root.Error("order", "InvalidValue", $"order 必須是 0..{n - 1} 的排列（與 results 的條數一致）");

        int actualFailed = results.Count(r => !r.Success);
        if (root.Has("failedCount") && failed != actualFailed)
            root.Error("failedCount", "InvalidValue", $"failedCount ({failed}) 與 results 中失敗的條數 ({actualFailed}) 不一致");

        long actualCost = results.Where(r => r.Success && r.Metrics != null).Sum(r => r.Metrics!.Cost.Total);
        if (root.Has("routeCost") && routeCost != actualCost)
            root.Error("routeCost", "InvalidValue", $"routeCost ({routeCost}) 必須等於成功管線的成本總和 ({actualCost})");
        if (root.Has("penaltyCost") && penaltyCost < 0)
            root.Error("penaltyCost", "OutOfRange", "penaltyCost 必須 ≥ 0");
        if (root.Has("score") && score != routeCost + penaltyCost)
            root.Error("score", "InvalidValue", $"score ({score}) 必須等於 routeCost + penaltyCost ({routeCost + penaltyCost})");

        return new PlanEvaluation(vecOrder, results, failed, routeCost, penaltyCost) { RuleReport = report };
    }
}
