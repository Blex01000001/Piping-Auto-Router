using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Scene;

namespace PipingRouter.Engine.Routing;

/// <summary>
/// 軸向 A*（±X、±Y、±Z 六個方向，只會有 90 度彎頭）。
///
/// 搜尋狀態 = (格子, 進入此格的方向)。起點狀態的方向是起點接口方向，且第一步必須沿該方向；
/// 抵達終點格時必須沿終點接口方向的反向（管線從終點接口外側直進）。禁止 180 度回頭。
/// 起點格與終點格是「端點專用」：不可回到起點格，也不可穿越終點格（只能以抵達方向進入一次）。
/// 其餘的自交（格子重複經過）無法在 (格子, 方向) 狀態空間中禁止，會以 SelfOverlap 警告標示。
/// 第 7 步會在狀態加入「上次轉彎後的連續直管格數」與元件序列索引；現在還沒有。
///
/// 成本（整數定點）= 長度權重 × 每步毫米數 + 彎頭權重 × 彎頭數 + 區域懲罰 × 進入 Penalized 格的次數。
/// 啟發函數 = 曼哈頓距離 × 每步成本（可採納且一致）。
///
/// 確定性：鄰居展開順序固定；優先佇列的鍵 (f, 較大的 g 優先, 插入序號)。
/// 種子只影響「同一個狀態有多個等成本前驅」時留哪一個：seed = 0 先發現者勝；
/// seed ≠ 0 取 splitmix64(seed, 前驅, 狀態) 最小者。前驅的 g 一定嚴格小於該狀態的 g，
/// 所以換前驅不會產生環、也不改變最佳成本，只會在多條等成本路徑之間做選擇——可用來產生備選方案。
/// </summary>
public sealed class AStarRouter : IRouter
{
    private const int DirCount = 6;
    private const long MaxStatesForArrays = 16_000_000;

    private static readonly int[] Dx = { 1, -1, 0, 0, 0, 0 };
    private static readonly int[] Dy = { 0, 0, 1, -1, 0, 0 };
    private static readonly int[] Dz = { 0, 0, 0, 0, 1, -1 };

    private readonly record struct OpenItem(int State, long G);

    private readonly record struct OpenKey(long F, long NegG, long Seq) : IComparable<OpenKey>
    {
        public int CompareTo(OpenKey o)
        {
            int c = F.CompareTo(o.F);
            if (c != 0) return c;
            c = NegG.CompareTo(o.NegG);
            return c != 0 ? c : Seq.CompareTo(o.Seq);
        }
    }

    public RouteResult Route(VoxelGrid grid, LineRequest line, RoutingConfig config)
    {
        long seed = config.Search.Seed;
        var info0 = new RouteSearchInfo(0, seed, grid.CellSize);
        RouteResult Fail(string code, string msg, GridSnap? snap = null, long expanded = 0,
            CellIndex? closest = null, double? closestMm = null) =>
            RouteResult.Fail(line.Id, new RouteFailure(code, msg, expanded, closest, closestMm),
                info0 with { Expanded = expanded }, snap);

        // ---- 輸入檢查 ----
        if (line.ComponentSequence.Count > 0)
            return Fail("NotSupported", "元件序列佈線尚未實作；請讓 componentSequence 為空。");

        long step = Scaled(config.Weights.Length * grid.CellSize);
        long bend = Scaled(config.Weights.Bend);
        long zone = Scaled(config.Weights.ZonePenaltyPerCell);
        if (step <= 0 || bend < 0 || zone < 0 || step > long.MaxValue / 4 / Math.Max(1, grid.Nx + grid.Ny + grid.Nz))
            return Fail("InvalidInput", "成本權重換算後無效（長度成本必須為正，且數值不可過大）。");

        if (!grid.TrySnap(line.Start.Position, out var startCell, out double startOffset))
            return Fail("StartOutOfBounds", $"起點 {line.Start.Position} 在格子範圍之外。");
        if (!grid.TrySnap(line.End.Position, out var goalCell, out double goalOffset))
            return Fail("GoalOutOfBounds", $"終點 {line.End.Position} 在格子範圍之外。");
        var snap = new GridSnap(line.Start.Position, startCell, startOffset, line.End.Position, goalCell, goalOffset);

        if (grid.IsBlocked(startCell))
            return Fail("StartBlocked", $"起點格子 {startCell} 被佔用（{Describe(grid.FlagsAt(startCell))}）；接口離障礙物太近或在障礙物內。", snap);
        if (grid.IsBlocked(goalCell))
            return Fail("GoalBlocked", $"終點格子 {goalCell} 被佔用（{Describe(grid.FlagsAt(goalCell))}）；接口離障礙物太近或在障礙物內。", snap);
        if (startCell == goalCell)
            return Fail("InvalidInput", "起點與終點落在同一個格子；請加大距離或縮小格子。", snap);

        int startDir = (int)line.Start.Direction;
        int arriveDir = (int)line.End.Direction.Opposite();       // 抵達終點時行進的方向

        // ---- A* ----
        long stateCount = grid.CellCount * DirCount;
        IStateStore store = stateCount <= MaxStatesForArrays
            ? new ArrayStateStore((int)stateCount)
            : new DictionaryStateStore();

        int startState = grid.LinearIndex(startCell) * DirCount + startDir;
        int goalLinear = grid.LinearIndex(goalCell);
        var open = new PriorityQueue<OpenItem, OpenKey>();
        long seq = 0;

        long H(int x, int y, int z) =>
            (Math.Abs(x - goalCell.X) + Math.Abs(y - goalCell.Y) + Math.Abs(z - goalCell.Z)) * step;

        store.Set(startState, 0, -1);
        open.Enqueue(new OpenItem(startState, 0),
            new OpenKey(H(startCell.X, startCell.Y, startCell.Z), 0, seq++));

        long expanded = 0;
        int bestManhattan = int.MaxValue;
        CellIndex? closest = null;
        int goalState = -1;

        while (open.TryDequeue(out var item, out _))
        {
            int s = item.State;
            if (store.IsClosed(s) || item.G > store.G(s)) continue;

            if (expanded >= config.Search.MaxExpandedNodes)
                return Fail("SearchLimitExceeded",
                    $"已展開 {expanded} 個狀態仍未找到路徑，達到搜尋上限 {config.Search.MaxExpandedNodes}。",
                    snap, expanded, closest, closest is { } cc ? ManhattanMm(cc, goalCell, grid) : null);

            store.Close(s);
            expanded++;

            int linear = s / DirCount;
            int d = s % DirCount;
            int cx = linear % grid.Nx;
            int cy = linear / grid.Nx % grid.Ny;
            int cz = linear / (grid.Nx * grid.Ny);

            int man = Math.Abs(cx - goalCell.X) + Math.Abs(cy - goalCell.Y) + Math.Abs(cz - goalCell.Z);
            if (man < bestManhattan) { bestManhattan = man; closest = new CellIndex(cx, cy, cz); }

            if (linear == goalLinear && d == arriveDir)
            {
                goalState = s;
                break;
            }

            for (int nd = 0; nd < DirCount; nd++)
            {
                if (s == startState && nd != startDir) continue;     // 起點第一步必須沿接口方向
                if (nd == (d ^ 1)) continue;                          // 不可 180 度回頭

                var next = new CellIndex(cx + Dx[nd], cy + Dy[nd], cz + Dz[nd]);
                if (grid.IsBlocked(next)) continue;
                if (next == startCell) continue;                       // 起點格是端點專用
                if (next == goalCell && nd != arriveDir) continue;     // 終點格只能以抵達方向進入

                int ns = grid.LinearIndex(next) * DirCount + nd;

                long cost = step;
                if (nd != d) cost += bend;
                if (grid.IsPenalized(next)) cost += zone;

                long ng = item.G + cost;
                long old = store.G(ns);
                if (ng > old) continue;
                if (ng == old)
                {
                    // 等成本的另一個前驅：seed ≠ 0 時以雜湊決定留哪一個（seed = 0 先發現者勝）。
                    // 只換父指標、不改 g，也不重新入佇列（該狀態已在佇列中或已展開）。
                    if (seed != 0 && Tie(seed, s, ns) < Tie(seed, store.Parent(ns), ns))
                        store.Set(ns, old, s);
                    continue;
                }
                if (store.IsClosed(ns)) continue;
                store.Set(ns, ng, s);
                open.Enqueue(new OpenItem(ns, ng),
                    new OpenKey(ng + H(next.X, next.Y, next.Z), -ng, seq++));
            }
        }

        if (goalState < 0)
        {
            return Fail("NoPathExists",
                $"起點可達區域已全部展開（{expanded} 個狀態），沒有通往終點的路徑；最接近終點的格子為 {closest}。",
                snap, expanded, closest, closest is { } c2 ? ManhattanMm(c2, goalCell, grid) : null);
        }

        // ---- 還原路徑 ----
        var states = new List<int>();
        for (int s = goalState; s >= 0; s = store.Parent(s)) states.Add(s);
        states.Reverse();

        var cells = new List<CellIndex>(states.Count);
        var dirs = new List<int>(states.Count);
        foreach (int s in states)
        {
            int linear = s / DirCount;
            cells.Add(new CellIndex(linear % grid.Nx, linear / grid.Nx % grid.Ny, linear / (grid.Nx * grid.Ny)));
            dirs.Add(s % DirCount);
        }

        // 折線：起點中心、每個轉彎的格子中心、終點中心（共線點合併）
        var polyline = new List<Vec3> { grid.CellCenter(cells[0]) };
        int bends = 0;
        long penalized = 0;
        for (int i = 1; i < cells.Count; i++)
        {
            if (dirs[i] != dirs[i - 1])
            {
                bends++;
                polyline.Add(grid.CellCenter(cells[i - 1]));
            }
            if (grid.IsPenalized(cells[i])) penalized++;
        }
        polyline.Add(grid.CellCenter(cells[^1]));

        int steps = cells.Count - 1;
        var cost2 = new RouteCost(steps * step, bends * bend, penalized * zone);
        var metrics = new RouteMetrics(steps * grid.CellSize, bends, cost2);

        var warnings = new List<RouteWarning>();
        int repeated = cells.Count - cells.Distinct().Count();
        if (repeated > 0)
        {
            var again = cells.GroupBy(c => c).First(g => g.Count() > 1).Key;
            warnings.Add(new RouteWarning("SelfOverlap",
                $"路徑重複經過 {repeated} 個格子（例如 {again}），管線與自己重疊；實體上無效。請調整接口方向或降低彎頭成本。"));
        }

        return new RouteResult(line.Id, RouteStatus.Success, null, polyline, cells, metrics, snap,
            info0 with { Expanded = expanded }) { Warnings = warnings };
    }

    private static long Scaled(double v)
    {
        double x = Math.Round(v * CostUnits.Scale);
        return !double.IsFinite(x) || Math.Abs(x) > 1e15 ? -1 : (long)x;
    }

    private static double ManhattanMm(CellIndex a, CellIndex b, VoxelGrid g) =>
        (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z)) * g.CellSize;

    private static string Describe(CellFlags f)
    {
        var parts = new List<string>();
        if ((f & CellFlags.Obstacle) != 0) parts.Add("障礙物及其淨距範圍");
        if ((f & CellFlags.Zone) != 0) parts.Add("維修／操作區");
        if ((f & CellFlags.OutsideBounds) != 0) parts.Add("場景範圍外");
        return parts.Count == 0 ? "未知" : string.Join("、", parts);
    }

    /// <summary>splitmix64(seed, state)：決定性的雜湊，不依賴 System.Random 的版本行為。</summary>
    private static ulong Tie(long seed, int parent, int state)
    {
        ulong x = unchecked((ulong)seed + ((ulong)(uint)parent << 32 | (uint)state) * 0x9E3779B97F4A7C15UL);
        x = unchecked((x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL);
        x = unchecked((x ^ (x >> 27)) * 0x94D049BB133111EBUL);
        return x ^ (x >> 31);
    }
}
