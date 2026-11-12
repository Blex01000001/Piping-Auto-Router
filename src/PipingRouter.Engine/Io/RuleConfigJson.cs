using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Rules;

namespace PipingRouter.Engine.Io;

public static class RuleConfigJson
{
    private static readonly Dictionary<string, ZonePolicy> Policies = new()
    {
        ["forbid"] = ZonePolicy.Forbid,
        ["penalize"] = ZonePolicy.Penalize,
    };

    private static readonly Dictionary<string, Severity> Severities = new()
    {
        ["error"] = Severity.Error,
        ["warning"] = Severity.Warning,
    };

    public static LoadResult<RuleConfig> Parse(string? json) => JsonLoader.Parse(json, Read);
    public static LoadResult<RuleConfig> Load(string path) => JsonLoader.Load(path, Read);
    public static void Save(string path, RuleConfig config) => JsonLoader.Save(path, Serialize(config));

    // =====================================================================
    private static RuleConfig? Read(JsonObj root, IssueList issues)
    {
        int version = root.ReqInt("schemaVersion");
        if (root.Has("schemaVersion") && version != RuleConfig.CurrentSchemaVersion)
            root.Error("schemaVersion", "UnsupportedSchemaVersion",
                $"不支援的 schemaVersion {version}（目前支援 {RuleConfig.CurrentSchemaVersion}）");

        string profile = root.ReqStr("profile");
        string disclaimer = root.OptStr("disclaimer", "");

        double eps = 1e-6;
        var geo = root.Obj("geometry", true);
        if (geo != null)
        {
            eps = geo.ReqNum("epsilonMm");
            if (geo.Has("epsilonMm") && !(eps > 0)) geo.Error("epsilonMm", "OutOfRange", "epsilonMm 必須 > 0");
        }

        ClearanceConfig clearance = new(0, 0);
        var cl = root.Obj("clearance", true);
        if (cl != null)
        {
            double obst = cl.ReqNum("pipeToObstacleMinMm");
            double pipe = cl.ReqNum("pipeToPipeMinMm");
            NonNegative(cl, "pipeToObstacleMinMm", obst);
            NonNegative(cl, "pipeToPipeMinMm", pipe);
            clearance = new ClearanceConfig(obst, pipe);
        }

        double minStraight = 0;
        var sr = root.Obj("straightRun", true);
        if (sr != null)
        {
            minStraight = sr.ReqNum("minBetweenBendsMm");
            NonNegative(sr, "minBetweenBendsMm", minStraight);
        }

        var policy = new ServiceZonePolicyConfig(ZonePolicy.Forbid, ZonePolicy.Forbid);
        var zp = root.Obj("serviceZonePolicy", false);
        if (zp != null)
            policy = new ServiceZonePolicyConfig(
                zp.OptEnum("maintenance", Policies, ZonePolicy.Forbid),
                zp.OptEnum("operation", Policies, ZonePolicy.Forbid));

        var severities = new SortedDictionary<string, Severity>(StringComparer.Ordinal);
        foreach (var id in RuleIds.All) severities[id] = RuleIds.DefaultSeverity(id);
        var sev = root.Obj("severities", false);
        if (sev != null)
        {
            foreach (var key in sev.Keys.ToList())
            {
                if (!RuleIds.All.Contains(key))
                {
                    sev.Error(key, "InvalidValue", $"未知的規則 id \"{key}\"，可用：{string.Join(", ", RuleIds.All)}");
                    continue;
                }
                severities[key] = sev.ReqEnum(key, Severities);
            }
        }

        var types = new SortedDictionary<string, ComponentTypeSpec>(StringComparer.Ordinal);
        var ct = root.Obj("componentTypes", true);
        if (ct != null)
        {
            foreach (var key in ct.Keys.ToList())
            {
                var spec = ct.Obj(key, true);
                if (spec != null) types[key] = ReadComponentType(spec);
            }
        }

        return new RuleConfig(version, profile, disclaimer, eps, clearance, minStraight, policy, severities, types);
    }

    private static void NonNegative(JsonObj o, string name, double v)
    {
        if (o.Has(name) && v < 0) o.Error(name, "OutOfRange", $"{name} 必須 ≥ 0");
    }

    private static ComponentTypeSpec ReadComponentType(JsonObj spec)
    {
        ComponentSize? size = null;
        var so = spec.Obj("size", false);
        if (so != null)
        {
            double l = so.ReqNum("lengthMm"), w = so.ReqNum("widthMm"), h = so.ReqNum("heightMm");
            foreach (var (n, v) in new[] { ("lengthMm", l), ("widthMm", w), ("heightMm", h) })
                if (so.Has(n) && !(v > 0)) so.Error(n, "OutOfRange", $"{n} 必須 > 0");
            size = new ComponentSize(l, w, h);
        }

        HandwheelSpec? hw = null;
        var ho = spec.Obj("handwheel", false);
        if (ho != null)
        {
            ValueRange range = new(0, 0);
            var ro = ho.Obj("heightAboveFloorMm", true);
            if (ro != null)
            {
                double min = ro.ReqNum("min"), max = ro.ReqNum("max");
                if (ro.Has("min") && ro.Has("max") && min > max)
                    ro.Error("min", "OutOfRange", $"min ({min}) 不可大於 max ({max})");
                range = new ValueRange(min, max);
            }
            var offset = ho.ReqVec("centerOffsetFromAxisMm");
            ClearanceZoneSpec zone = new CylinderClearanceZone(0, 0);
            var zo = ho.Obj("clearanceZone", true);
            if (zo != null) zone = ReadClearanceZone(zo);
            hw = new HandwheelSpec(range, offset, zone);
        }

        StraightRunSpec? run = null;
        var rn = spec.Obj("straightRunInDiameters", false);
        if (rn != null)
        {
            double up = rn.ReqNum("upstream"), down = rn.ReqNum("downstream");
            NonNegative(rn, "upstream", up);
            NonNegative(rn, "downstream", down);
            run = new StraightRunSpec(up, down);
        }

        return new ComponentTypeSpec(size, hw, run);
    }

    private static ClearanceZoneSpec ReadClearanceZone(JsonObj zo)
    {
        string type = zo.ReqStr("type");
        switch (type)
        {
            case "cylinder":
                double r = zo.ReqNum("radiusMm"), h = zo.ReqNum("heightMm");
                if (zo.Has("radiusMm") && !(r > 0)) zo.Error("radiusMm", "OutOfRange", "radiusMm 必須 > 0");
                if (zo.Has("heightMm") && !(h > 0)) zo.Error("heightMm", "OutOfRange", "heightMm 必須 > 0");
                return new CylinderClearanceZone(r, h);
            case "box":
                var size = zo.ReqVec("sizeMm");
                if (zo.Has("sizeMm") && !(size.X > 0 && size.Y > 0 && size.Z > 0))
                    zo.Error("sizeMm", "OutOfRange", "sizeMm 各軸必須 > 0");
                return new BoxClearanceZone(size);
            default:
                if (zo.Has("type")) zo.Error("type", "InvalidValue", $"不認得的淨空區形狀 \"{type}\"，可用：cylinder, box");
                return new CylinderClearanceZone(0, 0);
        }
    }

    // =====================================================================
    public static string Serialize(RuleConfig c)
    {
        var sev = new JObj();
        foreach (var kv in c.Severities.OrderBy(k => k.Key, StringComparer.Ordinal))
            sev.Add(kv.Key, kv.Value == Severity.Error ? "error" : "warning");

        var types = new JObj();
        foreach (var kv in c.ComponentTypes.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var o = new JObj();
            if (kv.Value.Size is { } s)
                o.Add("size", new JObj().Add("lengthMm", s.LengthMm).Add("widthMm", s.WidthMm).Add("heightMm", s.HeightMm));
            if (kv.Value.Handwheel is { } h)
                o.Add("handwheel", new JObj()
                    .Add("heightAboveFloorMm", new JObj().Add("min", h.HeightAboveFloorMm.Min).Add("max", h.HeightAboveFloorMm.Max))
                    .Add("centerOffsetFromAxisMm", JInline.Of(h.CenterOffsetFromAxisMm))
                    .Add("clearanceZone", h.ClearanceZone switch
                    {
                        CylinderClearanceZone cz => new JObj().Add("type", "cylinder").Add("radiusMm", cz.RadiusMm).Add("heightMm", cz.HeightMm),
                        BoxClearanceZone bz => new JObj().Add("type", "box").Add("sizeMm", JInline.Of(bz.SizeMm)),
                        _ => throw new ArgumentException("未知淨空區形狀"),
                    }));
            if (kv.Value.StraightRun is { } r)
                o.Add("straightRunInDiameters", new JObj().Add("upstream", r.UpstreamDiameters).Add("downstream", r.DownstreamDiameters));
            types.Add(kv.Key, o);
        }

        var root = new JObj()
            .Add("schemaVersion", c.SchemaVersion)
            .Add("profile", c.Profile)
            .Add("disclaimer", c.Disclaimer)
            .Add("geometry", new JObj().Add("epsilonMm", c.EpsilonMm))
            .Add("clearance", new JObj()
                .Add("pipeToObstacleMinMm", c.Clearance.PipeToObstacleMinMm)
                .Add("pipeToPipeMinMm", c.Clearance.PipeToPipeMinMm))
            .Add("straightRun", new JObj().Add("minBetweenBendsMm", c.MinStraightBetweenBendsMm))
            .Add("serviceZonePolicy", new JObj()
                .Add("maintenance", c.ServiceZonePolicy.Maintenance == ZonePolicy.Forbid ? "forbid" : "penalize")
                .Add("operation", c.ServiceZonePolicy.Operation == ZonePolicy.Forbid ? "forbid" : "penalize"))
            .Add("severities", sev)
            .Add("componentTypes", types);
        return JsonOut.Write(root);
    }
}
