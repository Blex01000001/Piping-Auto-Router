using PipingRouter.Engine;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Io;
using PipingRouter.Engine.Rules;
using PipingRouter.Engine.Scene;
using Xunit;

namespace PipingRouter.Tests.Rules;

internal static class RuleTestKit
{
    /// <summary>範例規則：間距 100、最小直管 300、手輪 900–1500 等（皆為範例值）。</summary>
    public static RuleConfig Example { get; } =
        RuleConfigJson.Load(RepoPaths.Combine("config", "rules.example.json")).ValueOrThrow();

    public static Vec3 V(double x, double y, double z) => new(x, y, z);

    public static Obstacle Box(string id, double x0, double y0, double z0, double x1, double y1, double z1,
        ObstacleKind kind = ObstacleKind.Equipment) =>
        new(id, kind, new BoxShape(new Aabb(V(x0, y0, z0), V(x1, y1, z1))));

    public static Obstacle Cyl(string id, double x, double y, double z, double r, double h, Axis axis = Axis.Z) =>
        new(id, ObstacleKind.Vessel, new CylinderShape(new AxisCylinder(V(x, y, z), r, h, axis)));

    public static ServiceZone Zone(string id, ServiceZoneKind kind, double x0, double y0, double z0, double x1, double y1, double z1) =>
        new(id, kind, new Aabb(V(x0, y0, z0), V(x1, y1, z1)));

    public static PipeDef Pipe(string id, double od, double ins, Vec3[] pts, params PlacedComponent[] comps) =>
        new(id, null, od, ins, pts, comps);

    public static PlacedComponent Comp(string id, string type, int seg, double dist) => new(id, type, seg, dist);

    public static SceneModel Scene(
        IEnumerable<Obstacle>? obstacles = null,
        IEnumerable<ServiceZone>? zones = null,
        IEnumerable<PipeDef>? pipes = null,
        double floorZ = 0) =>
        new(1, "test", true, floorZ,
            new Aabb(V(-10000, -10000, -10000), V(20000, 20000, 20000)),
            (obstacles ?? Array.Empty<Obstacle>()).ToList(),
            (zones ?? Array.Empty<ServiceZone>()).ToList(),
            Array.Empty<LineRequest>(),
            (pipes ?? Array.Empty<PipeDef>()).ToList());

    public static RuleReport Check(SceneModel scene, RuleConfig? rules = null) =>
        RuleChecker.Check(scene, rules ?? Example);

    public static List<RuleCheckResult> Of(RuleReport r, string ruleId) =>
        r.Results.Where(x => x.RuleId == ruleId).ToList();

    public static RuleCheckResult One(RuleReport r, string ruleId)
    {
        var list = Of(r, ruleId);
        Assert.True(list.Count == 1, $"規則 {ruleId} 應只有 1 筆，實際 {list.Count}：\n{Dump(r)}");
        return list[0];
    }

    public static string Dump(RuleReport r) => string.Join("\n", r.Results.Select(Line));

    public static string Line(RuleCheckResult x) =>
        $"{x.RuleId}|{x.IsViolation}|{x.Severity}|{string.Join(",", x.ObjectIds)}|{x.Location}|{x.Measured}|{x.Limit}|{x.Unit}|{x.Message}";
}
