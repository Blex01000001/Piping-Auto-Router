using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Rules;

namespace PipingRouter.Engine.Io;

/// <summary>
/// 規則檢查報告的 JSON 讀寫。每一筆檢查結果完整保留：ruleId、isViolation、severity、objectIds、location、
/// measured、limit（min／max，null 表示該側無限制）、unit、message；另附 errorCount／warningCount。
/// 獨立檔案多一個 schemaVersion；內嵌在佈線結果或整體計畫裡時是 "ruleReport" 物件（不含 schemaVersion）。
///
/// 報告是給人看的：量測值、限值、位置輸出時四捨五入到 1e-6 mm，避免精確公式裡約 1e-12 的數值誤差
/// （例如 -49.99999999999909）出現在檔案裡。這只影響輸出，不影響檢查本身的判斷；
/// 輸出的 JSON 解析後再輸出會逐位元相同。
/// </summary>
public static class RuleReportJson
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>報告數值的輸出精度：小數 6 位（mm）。</summary>
    public const int Decimals = 6;

    private static double? R(double? v) => v is { } x ? R(x) : null;

    private static double R(double v)
    {
        double r = Math.Round(v, Decimals, MidpointRounding.AwayFromZero);
        return r == 0 ? 0 : r;                                     // 避免輸出 -0
    }

    private static Vec3 R(Vec3 v) => new(R(v.X), R(v.Y), R(v.Z));

    private static readonly Dictionary<string, Severity> Severities = new()
    {
        ["error"] = Severity.Error,
        ["warning"] = Severity.Warning,
    };

    public static string Serialize(RuleReport report)
    {
        var root = new JObj().Add("schemaVersion", CurrentSchemaVersion);
        foreach (var kv in ToObj(report)) root.Add(kv.Key, kv.Value);
        return JsonOut.Write(root);
    }

    public static LoadResult<RuleReport> Parse(string? json) => JsonLoader.Parse(json, ReadDocument);
    public static LoadResult<RuleReport> Load(string path) => JsonLoader.Load(path, ReadDocument);
    public static void Save(string path, RuleReport report) => JsonLoader.Save(path, Serialize(report));

    // =====================================================================
    internal static JObj ToObj(RuleReport report)
    {
        var results = new JArr();
        foreach (var r in report.Results)
        {
            var ids = new JArr();
            ids.AddRange(r.ObjectIds);
            results.Add(new JObj()
                .Add("ruleId", r.RuleId)
                .Add("isViolation", r.IsViolation)
                .Add("severity", r.Severity == Severity.Error ? "error" : "warning")
                .Add("objectIds", ids)
                .Add("location", JInline.Of(R(r.Location)))
                .Add("measured", R(r.Measured))
                .Add("limit", r.Limit == null ? null : new JObj().Add("min", R(r.Limit.Min)).Add("max", R(r.Limit.Max)))
                .Add("unit", r.Unit)
                .Add("message", r.Message));
        }
        return new JObj()
            .Add("errorCount", report.ErrorCount)
            .Add("warningCount", report.WarningCount)
            .Add("results", results);
    }

    private static RuleReport? ReadDocument(JsonObj root, IssueList issues)
    {
        int version = root.ReqInt("schemaVersion");
        if (root.Has("schemaVersion") && version != CurrentSchemaVersion)
            root.Error("schemaVersion", "UnsupportedSchemaVersion", $"不支援的 schemaVersion {version}（目前支援 {CurrentSchemaVersion}）");
        return ReadReport(root);
    }

    internal static RuleReport ReadReport(JsonObj o)
    {
        int errors = o.ReqInt("errorCount");
        int warnings = o.ReqInt("warningCount");
        var list = new List<RuleCheckResult>();
        foreach (var x in o.ObjArray("results", true))
        {
            string ruleId = x.ReqStr("ruleId");
            bool violation = x.OptBool("isViolation", false);
            var severity = x.ReqEnum("severity", Severities);
            var ids = x.StrArray("objectIds");
            var location = x.ReqVec("location");
            double? measured = x.Has("measured") ? x.ReqNum("measured") : null;
            RuleLimit? limit = null;
            var lo = x.Obj("limit", false);
            if (lo != null)
                limit = new RuleLimit(lo.Has("min") ? lo.ReqNum("min") : null, lo.Has("max") ? lo.ReqNum("max") : null);
            list.Add(new RuleCheckResult(ruleId, violation, severity, ids, location, measured, limit,
                x.OptStr("unit", ""), x.ReqStr("message")));
        }

        var report = new RuleReport(list);
        if (o.Has("errorCount") && errors != report.ErrorCount)
            o.Error("errorCount", "InvalidValue", $"errorCount ({errors}) 與 results 中的違規數 ({report.ErrorCount}) 不一致");
        if (o.Has("warningCount") && warnings != report.WarningCount)
            o.Error("warningCount", "InvalidValue", $"warningCount ({warnings}) 與 results 中的提醒數 ({report.WarningCount}) 不一致");
        return report;
    }
}
