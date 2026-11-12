using System.Globalization;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Scene;

namespace PipingRouter.Engine.Rules;

/// <summary>
/// 規則檢查器：獨立於佈線，可檢查人工給定或佈線產生的折線管線。
/// 全部使用連續座標與精確幾何公式（不經過體素格子）。
///
/// 約定（皆已與使用者確認）：
///  • 管徑指外徑；管線碰撞半徑 = 外徑/2 + 保溫厚度；淨距從保溫外表面算起。
///  • 剛好等於限值算通過（以 epsilon 處理浮點誤差）。
///  • 流向 = 折線第一點到最後一點。
///  • 直管段必須是純直管：被彎頭、其他元件的本體、或管線端點截斷（不假設管線端點之外還是直管）。
///  • 兩彎頭間最小直管長度以彎頭頂點中心到中心量測；只約束兩個彎頭之間，不含接口段。
///  • 手輪：水平管上的閥，手輪中心 = 元件中心 + 型錄的世界座標偏移；垂直（或傾斜）管上的閥回報 UnsupportedComponent。
///  • 手輪淨空區不與「該元件自己的管線」比較。
///
/// 輸出：違規的每一項，加上各規則「最緊」那一筆的通過紀錄（供檢視器顯示餘裕）。
/// 同一輸入的輸出順序固定。
/// </summary>
public static class RuleChecker
{
    public static RuleReport Check(SceneModel scene, RuleConfig rules, IReadOnlyList<PipeDef>? pipes = null) =>
        new Run(scene, rules, pipes ?? scene.Pipes).Execute();

    private sealed class Run
    {
        private readonly SceneModel _scene;
        private readonly RuleConfig _rules;
        private readonly IReadOnlyList<PipeDef> _pipes;
        private readonly List<RuleCheckResult> _results = new();
        private readonly double _eps;

        public Run(SceneModel scene, RuleConfig rules, IReadOnlyList<PipeDef> pipes)
        {
            _scene = scene;
            _rules = rules;
            _pipes = pipes;
            _eps = rules.EpsilonMm;
        }

        public RuleReport Execute()
        {
            var infos = _pipes.Select(p => new PolylineInfo(p)).ToList();
            foreach (var info in infos)
            {
                if (info.SegmentCount < 1) continue;
                PipeVsObstacles(info);
                PipeVsZones(info);
                Bends(info);
                Components(info);
            }
            PipePairs(infos);
            return new RuleReport(_results);
        }

        private static string F(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

        private void Add(string ruleId, bool violation, IReadOnlyList<string> ids, Vec3 loc,
            double? measured, RuleLimit? limit, string unit, string message, Severity? severity = null) =>
            _results.Add(new RuleCheckResult(ruleId, violation, severity ?? _rules.SeverityOf(ruleId),
                ids, loc, measured, limit, unit, message));

        // =====================================================================
        // 管線 vs 障礙物：干涉與最小淨距
        // =====================================================================
        private void PipeVsObstacles(PolylineInfo info)
        {
            if (_scene.Obstacles.Count == 0) return;
            var pipe = info.Pipe;
            double minClr = _rules.Clearance.PipeToObstacleMinMm;
            bool anyCollision = false, anyClearance = false;
            double tightGap = double.MaxValue;
            Vec3 tightLoc = default;
            string tightId = "";

            foreach (var ob in _scene.Obstacles)
            {
                var (gap, loc) = Tightest(info, seg => ClosestTo(seg, ob.Shape));
                if (gap < tightGap) { tightGap = gap; tightLoc = loc; tightId = ob.Id; }

                if (gap < -_eps)
                {
                    anyCollision = true;
                    Add(RuleIds.Collision, true, new[] { pipe.Id, ob.Id }, loc, gap, new RuleLimit(0, null), "mm",
                        $"管線 {pipe.Id} 與 {ob.Id} 干涉（間隙 {F(gap)} mm，負值為穿透；穿透深度僅為估計）");
                }
                else if (gap < minClr - _eps)
                {
                    anyClearance = true;
                    Add(RuleIds.MinClearance, true, new[] { pipe.Id, ob.Id }, loc, gap, new RuleLimit(minClr, null), "mm",
                        $"管線 {pipe.Id} 與 {ob.Id} 的淨距 {F(gap)} mm，小於最小淨距 {F(minClr)} mm");
                }
            }

            var ids = new[] { pipe.Id, tightId };
            if (!anyCollision)
                Add(RuleIds.Collision, false, ids, tightLoc, tightGap, new RuleLimit(0, null), "mm",
                    $"管線 {pipe.Id} 與障礙物無干涉（最近為 {tightId}，間隙 {F(tightGap)} mm）");
            if (!anyCollision && !anyClearance)
                Add(RuleIds.MinClearance, false, ids, tightLoc, tightGap, new RuleLimit(minClr, null), "mm",
                    $"管線 {pipe.Id} 淨距符合（最緊為 {tightId}，{F(tightGap)} mm ≥ {F(minClr)} mm）");
        }

        private static (double Distance, double T) ClosestTo(Segment seg, Shape shape) => shape switch
        {
            BoxShape b => SegmentAabb.Closest(seg, b.Box),
            CylinderShape c => SegmentCylinder.Closest(seg, c.Cylinder),
            _ => throw new ArgumentException($"未知形狀 {shape.GetType()}"),
        };

        /// <summary>管線所有線段中最緊的間隙（中心線距離 − 管半徑）與對應的中心線位置。同值取先出現者。</summary>
        private static (double Gap, Vec3 Location) Tightest(PolylineInfo info, Func<Segment, (double D, double T)> closest)
        {
            double r = info.Pipe.CollisionRadiusMm;
            double best = double.MaxValue;
            Vec3 loc = default;
            for (int i = 0; i < info.SegmentCount; i++)
            {
                var seg = info.Seg(i);
                var (d, t) = closest(seg);
                double gap = d - r;
                if (gap < best) { best = gap; loc = seg.PointAt(t); }
            }
            return (best, loc);
        }

        // =====================================================================
        // 管線 vs 維修／操作區
        // =====================================================================
        private void PipeVsZones(PolylineInfo info)
        {
            if (_scene.ServiceZones.Count == 0) return;
            var pipe = info.Pipe;
            bool any = false;
            double tightGap = double.MaxValue;
            Vec3 tightLoc = default;
            string tightId = "";

            foreach (var zone in _scene.ServiceZones)
            {
                var policy = zone.Kind == ServiceZoneKind.Maintenance
                    ? _rules.ServiceZonePolicy.Maintenance
                    : _rules.ServiceZonePolicy.Operation;
                string kindText = zone.Kind == ServiceZoneKind.Maintenance ? "維修區" : "操作區";

                var (gap, loc) = Tightest(info, seg => SegmentAabb.Closest(seg, zone.Box));
                if (gap < tightGap) { tightGap = gap; tightLoc = loc; tightId = zone.Id; }

                if (gap < -_eps)
                {
                    any = true;
                    // Forbid：依設定的嚴重度（預設 error）；Penalize：只提醒（warning）
                    var sev = policy == ZonePolicy.Forbid ? _rules.SeverityOf(RuleIds.ServiceZoneIntrusion) : Severity.Warning;
                    Add(RuleIds.ServiceZoneIntrusion, true, new[] { pipe.Id, zone.Id }, loc, gap, new RuleLimit(0, null), "mm",
                        $"管線 {pipe.Id} 穿越{kindText} {zone.Id}（間隙 {F(gap)} mm，負值為侵入）", sev);
                }
            }

            if (!any)
                Add(RuleIds.ServiceZoneIntrusion, false, new[] { pipe.Id, tightId }, tightLoc, tightGap, new RuleLimit(0, null), "mm",
                    $"管線 {pipe.Id} 未穿越維修／操作區（最近為 {tightId}，間隙 {F(tightGap)} mm）");
        }

        // =====================================================================
        // 兩彎頭之間的最小直管長度
        // =====================================================================
        private void Bends(PolylineInfo info)
        {
            if (info.BendS.Count < 2) return;
            var pipe = info.Pipe;
            double min = _rules.MinStraightBetweenBendsMm;
            bool any = false;
            double tight = double.MaxValue;
            Vec3 tightLoc = default;

            for (int k = 0; k + 1 < info.BendS.Count; k++)
            {
                double len = info.BendS[k + 1] - info.BendS[k];
                var mid = info.PointAtS((info.BendS[k] + info.BendS[k + 1]) / 2);
                if (len < tight) { tight = len; tightLoc = mid; }
                if (len < min - _eps)
                {
                    any = true;
                    Add(RuleIds.MinStraightBetweenBends, true, new[] { pipe.Id }, mid, len, new RuleLimit(min, null), "mm",
                        $"管線 {pipe.Id} 第 {k + 1} 與第 {k + 2} 個彎頭之間直管 {F(len)} mm，小於最小 {F(min)} mm");
                }
            }
            if (!any)
                Add(RuleIds.MinStraightBetweenBends, false, new[] { pipe.Id }, tightLoc, tight, new RuleLimit(min, null), "mm",
                    $"管線 {pipe.Id} 彎頭間直管符合（最短 {F(tight)} mm ≥ {F(min)} mm）");
        }

        // =====================================================================
        // 元件：前後直管段、手輪高度、手輪淨空
        // =====================================================================
        private void Components(PolylineInfo info)
        {
            var pipe = info.Pipe;
            int n = pipe.Components.Count;
            var s = new double[n];
            var half = new double[n];
            var spec = new ComponentTypeSpec?[n];
            var valid = new bool[n];

            for (int i = 0; i < n; i++)
            {
                var c = pipe.Components[i];
                _rules.ComponentTypes.TryGetValue(c.Type, out spec[i]);
                half[i] = (spec[i]?.Size?.LengthMm ?? 0) / 2;
                valid[i] = c.SegmentIndex >= 0 && c.SegmentIndex < info.SegmentCount;
                s[i] = valid[i] ? info.S[c.SegmentIndex] + c.DistanceFromStartMm : 0;
            }

            for (int i = 0; i < n; i++)
            {
                var c = pipe.Components[i];
                if (!valid[i])
                {
                    Add(RuleIds.UnsupportedComponent, true, new[] { pipe.Id, c.Id }, Vec3.Zero, null, null, "",
                        $"元件 {c.Id} 的位置無效（segmentIndex={c.SegmentIndex}），略過檢查");
                    continue;
                }
                if (spec[i] == null)
                {
                    Add(RuleIds.UnsupportedComponent, true, new[] { pipe.Id, c.Id }, info.PointAtS(s[i]), null, null, "",
                        $"元件 {c.Id} 的類型 \"{c.Type}\" 不在規則設定的型錄中，略過檢查");
                    continue;
                }
                if (spec[i]!.StraightRun is { } run)
                    StraightRuns(info, i, s, half, run);
                if (spec[i]!.Handwheel is { } hw)
                    Handwheel(info, c, hw);
            }
        }

        private static string StraightRuleId(string type) => type switch
        {
            "flowMeter" => RuleIds.FlowMeterStraightRun,
            "controlValve" => RuleIds.ControlValveStraightRun,
            _ => RuleIds.ComponentStraightRun,
        };

        private void StraightRuns(PolylineInfo info, int idx, double[] s, double[] half, StraightRunSpec run)
        {
            var pipe = info.Pipe;
            var comp = pipe.Components[idx];
            string ruleId = StraightRuleId(comp.Type);
            double d = pipe.OuterDiameterMm;
            double faceUp = s[idx] - half[idx];
            double faceDown = s[idx] + half[idx];

            // 上游：從管線起點、彎頭、上游元件本體末端中取最靠近者
            double upLimit = 0;
            string? upBy = null;
            foreach (double b in info.BendS)
                if (b <= s[idx] && b > upLimit) { upLimit = b; upBy = null; }
            // 下游：管線終點、彎頭、下游元件本體起端
            double downLimit = info.Total;
            string? downBy = null;
            foreach (double b in info.BendS)
                if (b >= s[idx] && b < downLimit) { downLimit = b; downBy = null; }

            for (int o = 0; o < pipe.Components.Count; o++)
            {
                if (o == idx) continue;
                bool upstream = s[o] < s[idx] || (s[o] == s[idx] && o < idx);
                if (upstream)
                {
                    double end = s[o] + half[o];
                    if (end > upLimit) { upLimit = end; upBy = pipe.Components[o].Id; }
                }
                else
                {
                    double start = s[o] - half[o];
                    if (start < downLimit) { downLimit = start; downBy = pipe.Components[o].Id; }
                }
            }

            EmitRun(info, comp, ruleId, "上游", faceUp - upLimit, run.UpstreamDiameters, d, (upLimit + faceUp) / 2, upBy);
            EmitRun(info, comp, ruleId, "下游", downLimit - faceDown, run.DownstreamDiameters, d, (faceDown + downLimit) / 2, downBy);
        }

        private void EmitRun(PolylineInfo info, PlacedComponent comp, string ruleId, string side,
            double measured, double multiple, double d, double midS, string? limitedBy)
        {
            double required = multiple * d;
            bool bad = measured < required - _eps;
            var ids = new List<string> { info.Pipe.Id, comp.Id };
            if (limitedBy != null) ids.Add(limitedBy);
            string by = limitedBy != null ? $"（被元件 {limitedBy} 截斷）" : "";
            Add(ruleId, bad, ids, info.PointAtS(midS), measured, new RuleLimit(required, null), "mm",
                bad
                    ? $"元件 {comp.Id}（{comp.Type}）{side}直管段 {F(measured)} mm{by}，需要 ≥ {F(required)} mm（{F(multiple)}D，D={F(d)}）"
                    : $"元件 {comp.Id}（{comp.Type}）{side}直管段符合：{F(measured)} mm ≥ {F(required)} mm（{F(multiple)}D）");
        }

        private void Handwheel(PolylineInfo info, PlacedComponent comp, HandwheelSpec hw)
        {
            var pipe = info.Pipe;
            var seg = info.Seg(comp.SegmentIndex);
            var dir = seg.Direction;
            double len = dir.Length;
            var p = seg.PointAt(len == 0 ? 0 : comp.DistanceFromStartMm / len);

            if (Math.Abs(dir.Z) > 1e-9 * len)
            {
                Add(RuleIds.UnsupportedComponent, true, new[] { pipe.Id, comp.Id }, p, null, null, "",
                    $"元件 {comp.Id}（{comp.Type}）位於非水平管段，手輪高度與淨空目前不支援，略過檢查");
                return;
            }

            var center = p + hw.CenterOffsetFromAxisMm;
            double height = center.Z - _scene.FloorZ;
            var range = hw.HeightAboveFloorMm;
            bool badHeight = height < range.Min - _eps || height > range.Max + _eps;
            Add(RuleIds.HandwheelHeight, badHeight, new[] { pipe.Id, comp.Id }, center, height,
                new RuleLimit(range.Min, range.Max), "mm",
                badHeight
                    ? $"元件 {comp.Id} 手輪中心離地 {F(height)} mm，超出範圍 {F(range.Min)}–{F(range.Max)} mm"
                    : $"元件 {comp.Id} 手輪中心離地 {F(height)} mm，在範圍 {F(range.Min)}–{F(range.Max)} mm 內");

            // ---- 淨空區 ----
            Aabb? zoneBox = null;
            AxisCylinder? zoneCyl = null;
            switch (hw.ClearanceZone)
            {
                case CylinderClearanceZone cz:
                    zoneCyl = new AxisCylinder(center.With(Axis.Z, center.Z - cz.HeightMm / 2), cz.RadiusMm, cz.HeightMm, Axis.Z);
                    break;
                case BoxClearanceZone bz:
                    var half = bz.SizeMm * 0.5;
                    zoneBox = new Aabb(center - half, center + half);
                    break;
            }

            var ids = new List<string> { pipe.Id, comp.Id };
            int intruders = 0;

            foreach (var ob in _scene.Obstacles)
            {
                if (ZoneOverlapsShape(zoneBox, zoneCyl, ob.Shape))
                {
                    intruders++;
                    ids.Add(ob.Id);
                }
            }
            foreach (var other in _pipes)
            {
                if (other.Id == pipe.Id) continue;          // 排除該元件自己的管線
                if (PipeIntrudesZone(other, zoneBox, zoneCyl))
                {
                    intruders++;
                    ids.Add(other.Id);
                }
            }

            bool bad = intruders > 0;
            Add(RuleIds.HandwheelClearance, bad, ids, center, intruders, new RuleLimit(null, 0), "個",
                bad
                    ? $"元件 {comp.Id} 手輪淨空區內有 {intruders} 個物件侵入：{string.Join("、", ids.Skip(2))}"
                    : $"元件 {comp.Id} 手輪淨空區內無侵入物件");
        }

        private bool ZoneOverlapsShape(Aabb? zoneBox, AxisCylinder? zoneCyl, Shape shape) => (zoneBox, zoneCyl, shape) switch
        {
            ({ } zb, _, BoxShape b) => ShapeOverlap.Overlaps(zb, b.Box, _eps),
            ({ } zb, _, CylinderShape c) => ShapeOverlap.Overlaps(zb, c.Cylinder, _eps),
            (_, { } zc, BoxShape b) => ShapeOverlap.Overlaps(zc, b.Box, _eps),
            (_, { } zc, CylinderShape c) => ShapeOverlap.Overlaps(zc, c.Cylinder, _eps),
            _ => false,
        };

        private bool PipeIntrudesZone(PipeDef other, Aabb? zoneBox, AxisCylinder? zoneCyl)
        {
            double r = other.CollisionRadiusMm;
            for (int i = 0; i + 1 < other.Points.Count; i++)
            {
                var seg = new Segment(other.Points[i], other.Points[i + 1]);
                double dist = zoneBox is { } zb ? SegmentAabb.Distance(seg, zb)
                            : zoneCyl is { } zc ? SegmentCylinder.Distance(seg, zc)
                            : double.MaxValue;
                if (dist < r - _eps) return true;           // 膠囊侵入深度 > eps
            }
            return false;
        }

        // =====================================================================
        // 管線 vs 管線
        // =====================================================================
        private void PipePairs(IReadOnlyList<PolylineInfo> infos)
        {
            if (infos.Count < 2) return;
            double minClr = _rules.Clearance.PipeToPipeMinMm;
            bool anyCollision = false, anyClearance = false;
            double tightGap = double.MaxValue;
            Vec3 tightLoc = default;
            string[] tightIds = Array.Empty<string>();

            for (int i = 0; i < infos.Count; i++)
            {
                for (int j = i + 1; j < infos.Count; j++)
                {
                    var a = infos[i];
                    var b = infos[j];
                    if (a.SegmentCount < 1 || b.SegmentCount < 1) continue;
                    double rr = a.Pipe.CollisionRadiusMm + b.Pipe.CollisionRadiusMm;
                    double gap = double.MaxValue;
                    Vec3 loc = default;
                    for (int si = 0; si < a.SegmentCount; si++)
                    {
                        for (int sj = 0; sj < b.SegmentCount; sj++)
                        {
                            var sa = a.Seg(si);
                            var sb = b.Seg(sj);
                            var (t1, t2) = SegmentSegment.ClosestParameters(sa, sb);
                            var pa = sa.PointAt(t1);
                            var pb = sb.PointAt(t2);
                            double g = pa.DistanceTo(pb) - rr;
                            if (g < gap) { gap = g; loc = (pa + pb) * 0.5; }
                        }
                    }
                    var ids = new[] { a.Pipe.Id, b.Pipe.Id };
                    if (gap < tightGap) { tightGap = gap; tightLoc = loc; tightIds = ids; }

                    if (gap < -_eps)
                    {
                        anyCollision = true;
                        Add(RuleIds.Collision, true, ids, loc, gap, new RuleLimit(0, null), "mm",
                            $"管線 {ids[0]} 與 {ids[1]} 干涉（間隙 {F(gap)} mm，負值為穿透；穿透深度僅為估計）");
                    }
                    else if (gap < minClr - _eps)
                    {
                        anyClearance = true;
                        Add(RuleIds.MinClearance, true, ids, loc, gap, new RuleLimit(minClr, null), "mm",
                            $"管線 {ids[0]} 與 {ids[1]} 的淨距 {F(gap)} mm，小於最小淨距 {F(minClr)} mm");
                    }
                }
            }

            if (tightIds.Length == 0) return;
            if (!anyCollision)
                Add(RuleIds.Collision, false, tightIds, tightLoc, tightGap, new RuleLimit(0, null), "mm",
                    $"管線之間無干涉（最近為 {tightIds[0]} 與 {tightIds[1]}，間隙 {F(tightGap)} mm）");
            if (!anyCollision && !anyClearance)
                Add(RuleIds.MinClearance, false, tightIds, tightLoc, tightGap, new RuleLimit(minClr, null), "mm",
                    $"管線之間淨距符合（最緊 {F(tightGap)} mm ≥ {F(minClr)} mm）");
        }
    }
}
