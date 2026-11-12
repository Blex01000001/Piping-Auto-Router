using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Scene;

namespace PipingRouter.Engine.Io;

/// <summary>場景 JSON 的讀寫。讀入時一次回報所有問題，不丟例外。</summary>
public static class SceneJson
{
    private const double Eps = 1e-9;

    private static readonly Dictionary<string, ObstacleKind> ObstacleKinds = new()
    {
        ["equipment"] = ObstacleKind.Equipment,
        ["steel"] = ObstacleKind.Steel,
        ["wall"] = ObstacleKind.Wall,
        ["vessel"] = ObstacleKind.Vessel,
    };

    private static readonly Dictionary<string, ServiceZoneKind> ZoneKinds = new()
    {
        ["maintenance"] = ServiceZoneKind.Maintenance,
        ["operation"] = ServiceZoneKind.Operation,
    };

    private static readonly Dictionary<string, Axis> Axes = new()
    {
        ["X"] = Axis.X,
        ["Y"] = Axis.Y,
        ["Z"] = Axis.Z,
    };

    public static LoadResult<SceneModel> Parse(string? json) => JsonLoader.Parse(json, Read);
    public static LoadResult<SceneModel> Load(string path) => JsonLoader.Load(path, Read);
    public static void Save(string path, SceneModel scene) => JsonLoader.Save(path, Serialize(scene));

    // =====================================================================
    // 讀入
    // =====================================================================
    private static SceneModel? Read(JsonObj root, IssueList issues)
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);

        int version = root.ReqInt("schemaVersion");
        if (root.Has("schemaVersion") && version != SceneModel.CurrentSchemaVersion)
            root.Error("schemaVersion", "UnsupportedSchemaVersion",
                $"不支援的 schemaVersion {version}（目前支援 {SceneModel.CurrentSchemaVersion}）");

        string name = root.OptStr("name", "");
        bool synthetic = root.OptBool("isSynthetic", false);
        if (!synthetic)
            root.Warn("isSynthetic", "MissingSyntheticFlag", "場景未標示 isSynthetic=true；本專案只使用自編假資料。");

        string units = root.OptStr("units", "mm");
        if (units != "mm") root.Error("units", "InvalidValue", $"單位必須是 \"mm\"，實際為 \"{units}\"");

        double floorZ = root.OptNum("floorZ", 0);

        Aabb? bounds = null;
        var boundsObj = root.Obj("bounds", true);
        if (boundsObj != null) bounds = ReadBox(boundsObj);

        if (bounds is { } b && (floorZ < b.Min.Z - Eps || floorZ > b.Max.Z + Eps))
            root.Warn("floorZ", "OutsideBounds", $"floorZ={floorZ} 不在場景範圍的 Z 區間內");

        var obstacles = new List<Obstacle>();
        foreach (var o in root.ObjArray("obstacles", false))
        {
            string id = ReadId(o, ids);
            var kind = o.ReqEnum("kind", ObstacleKinds);
            var shapeObj = o.Obj("shape", true);
            var shape = shapeObj == null ? null : ReadShape(shapeObj);
            if (shape == null) continue;
            WarnIfOutside(o, "shape", shape.Bounds, bounds);
            obstacles.Add(new Obstacle(id, kind, shape));
        }

        var zones = new List<ServiceZone>();
        foreach (var z in root.ObjArray("serviceZones", false))
        {
            string id = ReadId(z, ids);
            var kind = z.ReqEnum("kind", ZoneKinds);
            var shapeObj = z.Obj("shape", true);
            var shape = shapeObj == null ? null : ReadShape(shapeObj);
            if (shape == null) continue;
            if (shape is not BoxShape box)
            {
                z.Error("shape", "InvalidValue", "維修／操作空間必須是方塊（type = \"box\"）");
                continue;
            }
            WarnIfOutside(z, "shape", box.Box, bounds);
            zones.Add(new ServiceZone(id, kind, box.Box));
        }

        var lines = new List<LineRequest>();
        foreach (var l in root.ObjArray("lines", false))
        {
            var line = ReadLine(l, ids, bounds);
            if (line != null) lines.Add(line);
        }

        var pipes = new List<PipeDef>();
        foreach (var p in root.ObjArray("pipes", false))
        {
            var pipe = ReadPipe(p, ids, bounds);
            if (pipe != null) pipes.Add(pipe);
        }

        foreach (var pipe in pipes)
        {
            if (pipe.LineId != null && lines.All(x => x.Id != pipe.LineId))
                issues.Error("UnknownReference", $"pipes[{pipes.IndexOf(pipe)}].lineId",
                    $"找不到 lineId \"{pipe.LineId}\" 對應的管線需求");
        }

        if (bounds is not { } bb) return null;
        return new SceneModel(version, name, synthetic, floorZ, bb, obstacles, zones, lines, pipes);
    }

    private static string ReadId(JsonObj o, Dictionary<string, string> ids)
    {
        string id = o.ReqStr("id");
        if (o.Has("id"))
        {
            if (id.Length == 0) o.Error("id", "InvalidValue", "id 不可為空字串");
            else if (!ids.TryAdd(id, o.P("id")))
                o.Error("id", "DuplicateId", $"id \"{id}\" 重複（另見 {ids[id]}）；所有物件 id 必須全域唯一");
        }
        return id;
    }

    private static Aabb? ReadBox(JsonObj o)
    {
        var min = o.ReqVec("min");
        var max = o.ReqVec("max");
        if (!o.Has("min") || !o.Has("max")) return null;
        if (!(min.X <= max.X && min.Y <= max.Y && min.Z <= max.Z))
        {
            o.Error("min", "OutOfRange", $"min 必須逐軸 ≤ max（min={min}, max={max}）");
            return null;
        }
        return new Aabb(min, max);
    }

    private static Shape? ReadShape(JsonObj s)
    {
        string type = s.ReqStr("type");
        switch (type)
        {
            case "box":
                var box = ReadBox(s);
                return box is { } bx ? new BoxShape(bx) : null;
            case "cylinder":
                var baseCenter = s.ReqVec("baseCenter");
                double radius = s.ReqNum("radius");
                double height = s.ReqNum("height");
                var axis = s.ReqEnum("axis", Axes);
                bool ok = s.Has("baseCenter") && s.Has("radius") && s.Has("height") && s.Has("axis");
                if (s.Has("radius") && !(radius > 0)) { s.Error("radius", "OutOfRange", "radius 必須 > 0"); ok = false; }
                if (s.Has("height") && !(height > 0)) { s.Error("height", "OutOfRange", "height 必須 > 0"); ok = false; }
                return ok ? new CylinderShape(new AxisCylinder(baseCenter, radius, height, axis)) : null;
            default:
                if (s.Has("type"))
                    s.Error("type", "InvalidValue", $"不認得的形狀 \"{type}\"，可用：box, cylinder");
                return null;
        }
    }

    private static void WarnIfOutside(JsonObj o, string field, Aabb item, Aabb? bounds)
    {
        if (bounds is not { } b) return;
        if (!(b.Contains(item.Min) && b.Contains(item.Max)))
            o.Warn(field, "OutsideBounds", "形狀超出場景範圍 bounds");
    }

    private static LineRequest? ReadLine(JsonObj l, Dictionary<string, string> ids, Aabb? bounds)
    {
        string id = ReadId(l, ids);
        double od = l.ReqNum("outerDiameterMm");
        double ins = l.OptNum("insulationThicknessMm", 0);
        if (l.Has("outerDiameterMm") && !(od > 0)) l.Error("outerDiameterMm", "OutOfRange", "管外徑必須 > 0");
        if (ins < 0) l.Error("insulationThicknessMm", "OutOfRange", "保溫厚度必須 ≥ 0");

        var start = ReadPort(l, "start", bounds);
        var end = ReadPort(l, "end", bounds);
        var seq = l.StrArray("componentSequence");
        for (int i = 0; i < seq.Count; i++)
            if (seq[i].Length == 0) l.Error($"componentSequence[{i}]", "InvalidValue", "元件類型不可為空字串");

        if (start != null && end != null && start.Position == end.Position)
            l.Error("end", "InvalidValue", "起點與終點位置相同");
        if (start == null || end == null) return null;
        return new LineRequest(id, od, ins, start, end, seq);
    }

    private static PortSpec? ReadPort(JsonObj parent, string name, Aabb? bounds)
    {
        var p = parent.Obj(name, true);
        if (p == null) return null;
        var pos = p.ReqVec("position");
        var dir = p.ReqDirection("direction");
        if (!p.Has("position") || !p.Has("direction")) return null;
        if (bounds is { } b && !b.Contains(pos))
            p.Error("position", "OutsideBounds", $"接口位置 {pos} 超出場景範圍");
        return new PortSpec(pos, dir);
    }

    private static PipeDef? ReadPipe(JsonObj p, Dictionary<string, string> ids, Aabb? bounds)
    {
        string id = ReadId(p, ids);
        string? lineId = p.Has("lineId") ? p.ReqStr("lineId") : null;
        double od = p.ReqNum("outerDiameterMm");
        double ins = p.OptNum("insulationThicknessMm", 0);
        if (p.Has("outerDiameterMm") && !(od > 0)) p.Error("outerDiameterMm", "OutOfRange", "管外徑必須 > 0");
        if (ins < 0) p.Error("insulationThicknessMm", "OutOfRange", "保溫厚度必須 ≥ 0");

        var points = p.VecArray("points", true);
        if (p.Has("points") && points.Count < 2)
            p.Error("points", "OutOfRange", "折線至少需要 2 個點");

        for (int i = 0; i + 1 < points.Count; i++)
        {
            var seg = new Segment(points[i], points[i + 1]);
            if (seg.IsDegenerate)
                p.Error($"points[{i + 1}]", "InvalidValue", "與前一點重複（長度為 0 的線段）");
            else if (!seg.IsAxisAligned())
                p.Warn($"points[{i}]", "NonAxialSegment", "此線段不沿 X/Y/Z 軸（佈線器只產生軸向線段；人工管線仍會被檢查）");
        }
        if (bounds is { } b)
            for (int i = 0; i < points.Count; i++)
                if (!b.Contains(points[i]))
                    p.Warn($"points[{i}]", "OutsideBounds", "點超出場景範圍");

        var comps = new List<PlacedComponent>();
        foreach (var c in p.ObjArray("components", false))
        {
            string cid = ReadId(c, ids);
            string type = c.ReqStr("type");
            var along = c.Obj("alongPolyline", true);
            if (along == null) continue;
            int segIndex = along.ReqInt("segmentIndex");
            double dist = along.ReqNum("distanceFromStartMm");
            if (!along.Has("segmentIndex") || !along.Has("distanceFromStartMm")) continue;

            if (points.Count >= 2)
            {
                if (segIndex < 0 || segIndex > points.Count - 2)
                {
                    along.Error("segmentIndex", "OutOfRange", $"segmentIndex 必須在 0..{points.Count - 2}");
                    continue;
                }
                double len = new Segment(points[segIndex], points[segIndex + 1]).Length;
                if (dist < 0 || dist > len + Eps)
                {
                    along.Error("distanceFromStartMm", "OutOfRange", $"距離必須在 0..{len}（該段長度）之間，實際 {dist}");
                    continue;
                }
            }
            comps.Add(new PlacedComponent(cid, type, segIndex, dist));
        }

        if (!p.Has("points") || points.Count < 2) return null;
        return new PipeDef(id, lineId, od, ins, points, comps);
    }

    // =====================================================================
    // 寫出
    // =====================================================================
    public static string Serialize(SceneModel scene)
    {
        var root = new JObj()
            .Add("schemaVersion", scene.SchemaVersion)
            .Add("name", scene.Name)
            .Add("isSynthetic", scene.IsSynthetic)
            .Add("units", "mm")
            .Add("floorZ", scene.FloorZ)
            .Add("bounds", BoxObj(scene.Bounds));

        root.Add("obstacles", Arr(scene.Obstacles.Select(o => (object?)new JObj()
            .Add("id", o.Id)
            .Add("kind", KindText(o.Kind))
            .Add("shape", ShapeObj(o.Shape)))));

        root.Add("serviceZones", Arr(scene.ServiceZones.Select(z => (object?)new JObj()
            .Add("id", z.Id)
            .Add("kind", z.Kind == ServiceZoneKind.Maintenance ? "maintenance" : "operation")
            .Add("shape", ShapeObj(new BoxShape(z.Box))))));

        root.Add("lines", Arr(scene.Lines.Select(l => (object?)new JObj()
            .Add("id", l.Id)
            .Add("outerDiameterMm", l.OuterDiameterMm)
            .Add("insulationThicknessMm", l.InsulationThicknessMm)
            .Add("start", PortObj(l.Start))
            .Add("end", PortObj(l.End))
            .Add("componentSequence", Arr(l.ComponentSequence.Select(s => (object?)s))))));

        root.Add("pipes", Arr(scene.Pipes.Select(p =>
        {
            var o = new JObj().Add("id", p.Id);
            if (p.LineId != null) o.Add("lineId", p.LineId);
            o.Add("outerDiameterMm", p.OuterDiameterMm)
             .Add("insulationThicknessMm", p.InsulationThicknessMm)
             .Add("points", Arr(p.Points.Select(v => (object?)JInline.Of(v))))
             .Add("components", Arr(p.Components.Select(c => (object?)new JObj()
                 .Add("id", c.Id)
                 .Add("type", c.Type)
                 .Add("alongPolyline", new JObj()
                     .Add("segmentIndex", c.SegmentIndex)
                     .Add("distanceFromStartMm", c.DistanceFromStartMm)))));
            return (object?)o;
        })));

        return JsonOut.Write(root);
    }

    private static JArr Arr(IEnumerable<object?> items)
    {
        var a = new JArr();
        a.AddRange(items);
        return a;
    }

    private static JObj BoxObj(Aabb b) => new JObj().Add("min", JInline.Of(b.Min)).Add("max", JInline.Of(b.Max));

    private static JObj ShapeObj(Shape s) => s switch
    {
        BoxShape bx => new JObj().Add("type", "box").Add("min", JInline.Of(bx.Box.Min)).Add("max", JInline.Of(bx.Box.Max)),
        CylinderShape cy => new JObj()
            .Add("type", "cylinder")
            .Add("baseCenter", JInline.Of(cy.Cylinder.BaseCenter))
            .Add("radius", cy.Cylinder.Radius)
            .Add("height", cy.Cylinder.Height)
            .Add("axis", cy.Cylinder.Axis.ToString()),
        _ => throw new ArgumentException($"未知形狀 {s.GetType()}"),
    };

    private static JObj PortObj(PortSpec p) =>
        new JObj().Add("position", JInline.Of(p.Position)).Add("direction", p.Direction.ToText());

    private static string KindText(ObstacleKind k) => k switch
    {
        ObstacleKind.Equipment => "equipment",
        ObstacleKind.Steel => "steel",
        ObstacleKind.Wall => "wall",
        _ => "vessel",
    };
}
