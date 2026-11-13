using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Routing;

namespace PipingRouter.Engine.Io;

/// <summary>
/// 佈線結果的 JSON 讀寫。輸出是確定的（欄位順序固定、數字用最短可還原格式、不含耗時），
/// 同一輸入兩次佈線的輸出逐位元相同；Dashboard 也讀這個格式。
/// 欄位：schemaVersion、lineId、status、failure（含 diagnosis）、polyline、cells、metrics、gridSnap、warnings、search、
/// ruleReport（選填；null 表示沒有做規則檢查或佈線失敗；舊檔沒有這個欄位也能讀，schemaVersion 維持 1）。
/// 成本是 ×1000 定點整數（見 <see cref="CostUnits.Scale"/>）。
/// </summary>
public static class RouteResultJson
{
    public const int CurrentSchemaVersion = 1;

    private static readonly Dictionary<string, RouteStatus> Statuses = new()
    {
        ["Success"] = RouteStatus.Success,
        ["NoPath"] = RouteStatus.NoPath,
    };

    public static LoadResult<RouteResult> Parse(string? json) => JsonLoader.Parse(json, Read);
    public static LoadResult<RouteResult> Load(string path) => JsonLoader.Load(path, Read);
    public static void Save(string path, RouteResult result) => JsonLoader.Save(path, Serialize(result));

    // =====================================================================
    // 寫出
    // =====================================================================
    public static string Serialize(RouteResult r) => JsonOut.Write(ToObj(r, withSchemaVersion: true));

    /// <summary>withSchemaVersion = false 用於內嵌在整體計畫（<see cref="PlanJson"/>）裡的結果。</summary>
    internal static JObj ToObj(RouteResult r, bool withSchemaVersion)
    {
        var root = new JObj();
        if (withSchemaVersion) root.Add("schemaVersion", CurrentSchemaVersion);
        return root
            .Add("lineId", r.LineId)
            .Add("status", r.Status == RouteStatus.Success ? "Success" : "NoPath")
            .Add("failure", r.Failure == null ? null : FailureObj(r.Failure))
            .Add("polyline", Arr(r.Polyline.Select(p => (object?)JInline.Of(p))))
            .Add("cells", Arr(r.Cells.Select(c => (object?)Cell(c))))
            .Add("metrics", r.Metrics == null ? null : new JObj()
                .Add("lengthMm", r.Metrics.LengthMm)
                .Add("bendCount", r.Metrics.BendCount)
                .Add("cost", new JObj()
                    .Add("length", r.Metrics.Cost.Length)
                    .Add("bend", r.Metrics.Cost.Bend)
                    .Add("zone", r.Metrics.Cost.Zone)
                    .Add("total", r.Metrics.Cost.Total)))
            .Add("gridSnap", r.Snap == null ? null : new JObj()
                .Add("startPort", JInline.Of(r.Snap.StartPort))
                .Add("startCell", Cell(r.Snap.StartCell))
                .Add("startOffsetMm", r.Snap.StartOffsetMm)
                .Add("endPort", JInline.Of(r.Snap.EndPort))
                .Add("endCell", Cell(r.Snap.EndCell))
                .Add("endOffsetMm", r.Snap.EndOffsetMm))
            .Add("warnings", Arr(r.Warnings.Select(w => (object?)new JObj().Add("code", w.Code).Add("message", w.Message))))
            .Add("search", new JObj()
                .Add("expanded", r.Search.Expanded)
                .Add("seed", r.Search.Seed)
                .Add("voxelSizeMm", r.Search.VoxelSizeMm)
                .Add("minStraightCells", r.Search.MinStraightCells))
            .Add("ruleReport", r.RuleReport == null ? null : RuleReportJson.ToObj(r.RuleReport));
    }

    private static JObj FailureObj(RouteFailure f)
    {
        var o = new JObj()
            .Add("code", f.Code)
            .Add("message", f.Message)
            .Add("expanded", f.Expanded)
            .Add("closestCell", f.ClosestCell is { } c ? Cell(c) : null)
            .Add("closestDistanceMm", f.ClosestDistanceMm)
            .Add("diagnosis", f.Diagnosis == null ? null : new JObj()
                .Add("summary", f.Diagnosis.Summary)
                .Add("relaxations", Arr(f.Diagnosis.Relaxations.Select(x => (object?)new JObj()
                    .Add("id", x.Id)
                    .Add("name", x.Name)
                    .Add("succeeds", x.Succeeds)
                    .Add("bendCount", x.BendCount)
                    .Add("lengthMm", x.LengthMm)
                    .Add("expanded", x.Expanded)
                    .Add("failureCode", x.FailureCode)))));
        return o;
    }

    private static JInline Cell(CellIndex c) => new() { c.X, c.Y, c.Z };

    private static JArr Arr(IEnumerable<object?> items)
    {
        var a = new JArr();
        a.AddRange(items);
        return a;
    }

    // =====================================================================
    // 讀入
    // =====================================================================
    private static RouteResult? Read(JsonObj root, IssueList issues) => ReadCore(root, issues, topLevel: true);

    internal static RouteResult? ReadEmbedded(JsonObj root, IssueList issues) => ReadCore(root, issues, topLevel: false);

    private static RouteResult? ReadCore(JsonObj root, IssueList issues, bool topLevel)
    {
        if (topLevel)
        {
            int version = root.ReqInt("schemaVersion");
            if (root.Has("schemaVersion") && version != CurrentSchemaVersion)
                root.Error("schemaVersion", "UnsupportedSchemaVersion", $"不支援的 schemaVersion {version}（目前支援 {CurrentSchemaVersion}）");
        }

        string lineId = root.ReqStr("lineId");
        var status = root.ReqEnum("status", Statuses);

        RouteFailure? failure = null;
        var fo = root.Obj("failure", false);
        if (fo != null) failure = ReadFailure(fo);

        var polyline = root.VecArray("polyline", true);
        var cells = ReadCells(root, "cells");

        RouteMetrics? metrics = null;
        var mo = root.Obj("metrics", false);
        if (mo != null)
        {
            var co = mo.Obj("cost", true);
            RouteCost cost = new(0, 0, 0);
            if (co != null)
            {
                long l = co.ReqLong("length"), b = co.ReqLong("bend"), z = co.ReqLong("zone"), t = co.ReqLong("total");
                if (co.Has("total") && t != l + b + z) co.Error("total", "InvalidValue", $"total ({t}) 必須等於 length+bend+zone ({l + b + z})");
                cost = new RouteCost(l, b, z);
            }
            metrics = new RouteMetrics(mo.ReqNum("lengthMm"), mo.ReqInt("bendCount"), cost);
        }

        GridSnap? snap = null;
        var so = root.Obj("gridSnap", false);
        if (so != null)
        {
            var sp = so.ReqVec("startPort");
            var sc = ReadCell(so, "startCell", true) ?? default;
            double so1 = so.ReqNum("startOffsetMm");
            var ep = so.ReqVec("endPort");
            var ec = ReadCell(so, "endCell", true) ?? default;
            double eo1 = so.ReqNum("endOffsetMm");
            snap = new GridSnap(sp, sc, so1, ep, ec, eo1);
        }

        var warnings = new List<RouteWarning>();
        foreach (var w in root.ObjArray("warnings", false))
            warnings.Add(new RouteWarning(w.ReqStr("code"), w.ReqStr("message")));

        RouteSearchInfo search = new(0, 0, 0);
        var sr = root.Obj("search", true);
        if (sr != null)
            search = new RouteSearchInfo(sr.ReqLong("expanded"), sr.ReqLong("seed"), sr.ReqNum("voxelSizeMm"), sr.ReqInt("minStraightCells"));

        Engine.Rules.RuleReport? ruleReport = null;
        var rr = root.Obj("ruleReport", false);
        if (rr != null) ruleReport = RuleReportJson.ReadReport(rr);

        // ---- 一致性 ----
        if (status == RouteStatus.Success)
        {
            if (failure != null) root.Error("failure", "InvalidValue", "status 為 Success 時不應有 failure");
            if (metrics == null) root.Error("metrics", "MissingField", "status 為 Success 時必須有 metrics");
            if (polyline.Count < 2) root.Error("polyline", "OutOfRange", "status 為 Success 時折線至少需要 2 個點");
            if (cells.Count < 2) root.Error("cells", "OutOfRange", "status 為 Success 時格子序列至少需要 2 格");
        }
        else
        {
            if (failure == null) root.Error("failure", "MissingField", "status 為 NoPath 時必須有 failure");
            if (polyline.Count > 0) root.Error("polyline", "InvalidValue", "status 為 NoPath 時折線必須為空");
            if (metrics != null) root.Error("metrics", "InvalidValue", "status 為 NoPath 時不應有 metrics");
            if (ruleReport != null) root.Error("ruleReport", "InvalidValue", "status 為 NoPath 時不應有 ruleReport（沒有折線可檢查）");
        }

        return new RouteResult(lineId, status, failure, polyline, cells, metrics, snap, search)
        {
            Warnings = warnings,
            RuleReport = ruleReport,
        };
    }

    private static RouteFailure ReadFailure(JsonObj o)
    {
        string code = o.ReqStr("code");
        string message = o.ReqStr("message");
        long expanded = o.OptLong("expanded", 0);
        var closest = ReadCell(o, "closestCell", false);
        double? dist = o.Has("closestDistanceMm") ? o.ReqNum("closestDistanceMm") : null;

        RouteDiagnosis? diagnosis = null;
        var d = o.Obj("diagnosis", false);
        if (d != null)
        {
            string summary = d.ReqStr("summary");
            var list = new List<RelaxationOutcome>();
            foreach (var x in d.ObjArray("relaxations", true))
            {
                list.Add(new RelaxationOutcome(
                    x.ReqStr("id"), x.ReqStr("name"), x.OptBool("succeeds", false),
                    x.Has("bendCount") ? x.ReqInt("bendCount") : null,
                    x.Has("lengthMm") ? x.ReqNum("lengthMm") : null,
                    x.OptLong("expanded", 0),
                    x.Has("failureCode") ? x.ReqStr("failureCode") : null));
            }
            diagnosis = new RouteDiagnosis(list, summary);
        }
        return new RouteFailure(code, message, expanded, closest, dist) { Diagnosis = diagnosis };
    }

    private static CellIndex? ReadCell(JsonObj o, string name, bool required)
    {
        if (!o.Has(name))
        {
            if (required) o.Error(name, "MissingField", "缺少必要欄位");
            return null;
        }
        var v = o.ReqVec(name);
        return ToCell(v, o, name) ?? default(CellIndex);
    }

    private static CellIndex? ToCell(Vec3 v, JsonObj o, string path)
    {
        if (v.X != Math.Floor(v.X) || v.Y != Math.Floor(v.Y) || v.Z != Math.Floor(v.Z) ||
            Math.Abs(v.X) > int.MaxValue || Math.Abs(v.Y) > int.MaxValue || Math.Abs(v.Z) > int.MaxValue)
        {
            o.Error(path, "WrongType", "格子索引必須是整數");
            return null;
        }
        return new CellIndex((int)v.X, (int)v.Y, (int)v.Z);
    }

    private static List<CellIndex> ReadCells(JsonObj o, string name)
    {
        var list = new List<CellIndex>();
        foreach (var v in o.VecArray(name, true))
            list.Add(ToCell(v, o, name) ?? default);
        return list;
    }
}
