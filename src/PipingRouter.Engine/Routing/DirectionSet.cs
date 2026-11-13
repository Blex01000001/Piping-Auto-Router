using PipingRouter.Engine.Geometry;

namespace PipingRouter.Engine.Routing;

/// <summary>
/// 搜尋可用的方向表。目前只有 Axis6（±X、±Y、±Z，順序同 <see cref="AxisDirection"/>），所以只會有 90 度彎頭。
/// 45 度是預留的選用功能：日後新增一個方向集合（例如 18 或 26 個方向）並讓 BendClass 區分 45／90 度即可，
/// 搜尋主體不需要改——它只透過這張表取得「方向數、每個方向的步進、反方向、轉彎類別」。
/// </summary>
public sealed class DirectionSet
{
    private readonly int[] _dx, _dy, _dz, _opposite;

    private DirectionSet(int[] dx, int[] dy, int[] dz, int[] opposite)
    {
        _dx = dx;
        _dy = dy;
        _dz = dz;
        _opposite = opposite;
    }

    public static DirectionSet Axis6 { get; } = new(
        dx: new[] { 1, -1, 0, 0, 0, 0 },
        dy: new[] { 0, 0, 1, -1, 0, 0 },
        dz: new[] { 0, 0, 0, 0, 1, -1 },
        opposite: new[] { 1, 0, 3, 2, 5, 4 });

    public static DirectionSet For(DirectionSetKind kind) => kind switch
    {
        DirectionSetKind.Axis6 => Axis6,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), $"未實作的方向集合 {kind}"),
    };

    public int Count => _dx.Length;

    public int StepX(int dir) => _dx[dir];
    public int StepY(int dir) => _dy[dir];
    public int StepZ(int dir) => _dz[dir];

    public int Opposite(int dir) => _opposite[dir];

    /// <summary>從方向 from 換到方向 to 的轉彎類別：0 = 直行（不算彎頭）、1 = 90 度彎頭。回頭（180 度）不是合法的轉彎。</summary>
    public int BendClass(int from, int to) => from == to ? 0 : 1;
}
