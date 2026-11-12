using PipingRouter.Engine.Routing;

namespace PipingRouter.Engine.Io;

public static class RoutingConfigJson
{
    public static LoadResult<RoutingConfig> Parse(string? json) => JsonLoader.Parse(json, Read);
    public static LoadResult<RoutingConfig> Load(string path) => JsonLoader.Load(path, Read);
    public static void Save(string path, RoutingConfig config) => JsonLoader.Save(path, Serialize(config));

    private static RoutingConfig? Read(JsonObj root, IssueList issues)
    {
        int version = root.ReqInt("schemaVersion");
        if (root.Has("schemaVersion") && version != RoutingConfig.CurrentSchemaVersion)
            root.Error("schemaVersion", "UnsupportedSchemaVersion",
                $"不支援的 schemaVersion {version}（目前支援 {RoutingConfig.CurrentSchemaVersion}）");

        double voxel = root.ReqNum("voxelSizeMm");
        if (root.Has("voxelSizeMm") && !(voxel > 0)) root.Error("voxelSizeMm", "OutOfRange", "voxelSizeMm 必須 > 0");

        RoutingWeights weights = new(1, 0, 0);
        var w = root.Obj("weights", true);
        if (w != null)
        {
            double len = w.ReqNum("length");
            double bend = w.ReqNum("bend");
            double zone = w.OptNum("zonePenaltyPerCell", 0);
            if (w.Has("length") && !(len > 0)) w.Error("length", "OutOfRange", "length 權重必須 > 0（啟發函數以它為基準）");
            if (w.Has("bend") && bend < 0) w.Error("bend", "OutOfRange", "bend 權重必須 ≥ 0");
            if (zone < 0) w.Error("zonePenaltyPerCell", "OutOfRange", "zonePenaltyPerCell 必須 ≥ 0");
            weights = new RoutingWeights(len, bend, zone);
        }

        var directions = DirectionSetKind.Axis6;
        if (root.Has("directions"))
        {
            string d = root.ReqStr("directions");
            if (d == "axis6+diag45")
                root.Error("directions", "NotSupported", "45 度方向集合已預留但尚未實作，請使用 \"axis6\"");
            else if (d != "axis6")
                root.Error("directions", "InvalidValue", $"不認得的方向集合 \"{d}\"，可用：axis6");
        }

        SearchSettings search = new(5_000_000, 0);
        var s = root.Obj("search", false);
        if (s != null)
        {
            long max = s.OptLong("maxExpandedNodes", 5_000_000);
            long seed = s.OptLong("seed", 0);
            if (max <= 0) s.Error("maxExpandedNodes", "OutOfRange", "maxExpandedNodes 必須 > 0");
            search = new SearchSettings(max, seed);
        }

        bool diagnose = root.OptBool("diagnoseOnFailure", true);
        return new RoutingConfig(version, voxel, weights, directions, search, diagnose);
    }

    public static string Serialize(RoutingConfig c)
    {
        var root = new JObj()
            .Add("schemaVersion", c.SchemaVersion)
            .Add("voxelSizeMm", c.VoxelSizeMm)
            .Add("weights", new JObj()
                .Add("length", c.Weights.Length)
                .Add("bend", c.Weights.Bend)
                .Add("zonePenaltyPerCell", c.Weights.ZonePenaltyPerCell))
            .Add("directions", "axis6")
            .Add("search", new JObj()
                .Add("maxExpandedNodes", c.Search.MaxExpandedNodes)
                .Add("seed", c.Search.Seed))
            .Add("diagnoseOnFailure", c.DiagnoseOnFailure);
        return JsonOut.Write(root);
    }
}
