using PipingRouter.Engine;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Routing;
using Xunit;
using static PipingRouter.Tests.Rules.RuleTestKit;
using static PipingRouter.Tests.Routing.RouteTestKit;

namespace PipingRouter.Tests.Routing;

/// <summary>找不到路徑或無法開始搜尋時，必須回傳明確的失敗代碼與原因，絕不丟例外。</summary>
public class AStarFailureTests
{
    private static void AssertFailure(RouteResult r, string code)
    {
        Assert.False(r.Success);
        Assert.Equal(RouteStatus.NoPath, r.Status);
        Assert.NotNull(r.Failure);
        Assert.Equal(code, r.Failure!.Code);
        Assert.False(string.IsNullOrWhiteSpace(r.Failure.Message));
        Assert.Empty(r.Polyline);
        Assert.Empty(r.Cells);
        Assert.Null(r.Metrics);
    }

    [Fact]
    public void S09_EnclosedGoal_NoPathExists_WithDiagnostics()
    {
        var s = LoadScene("s09_enclosed_nopath");
        var line = s.Lines[0];
        var grid = GridFor(s, line);
        // 終點格本身是自由的（封閉空間內部），所以不是 GoalBlocked
        Assert.True(grid.TrySnap(line.End.Position, out var goal, out _));
        Assert.False(grid.IsBlocked(goal));

        var r = Cached("s09_enclosed_nopath");
        AssertFailure(r, "NoPathExists");
        Assert.True(r.Failure!.Expanded > 100);
        Assert.NotNull(r.Failure.ClosestCell);
        Assert.True(r.Failure.ClosestDistanceMm > 0);
        Assert.Equal(r.Failure.Expanded, r.Search.Expanded);
        Assert.Null(ReferenceOptimum(grid, line, Config, MinCells(Example, Config)));                // 參考解也認為無解
        // 起訖吸附資訊在失敗時仍保留，方便檢視器標示
        Assert.NotNull(r.Snap);
    }

    [Fact]
    public void S10_StartInsideObstacle_IsStartBlocked()
    {
        var r = Route(LoadScene("s10_start_goal_blocked"), 0);
        AssertFailure(r, "StartBlocked");
        Assert.Contains("障礙物", r.Failure!.Message);
        Assert.Equal("L-START-INSIDE", r.LineId);
    }

    [Fact]
    public void S10_GoalInsideVessel_IsGoalBlocked()
    {
        var r = Route(LoadScene("s10_start_goal_blocked"), 1);
        AssertFailure(r, "GoalBlocked");
        Assert.Contains("障礙物", r.Failure!.Message);
    }

    [Fact]
    public void S10_StartTooCloseToObstacle_IsStartBlockedByClearance()
    {
        // 接口 (900,2000,1000) 離設備面 x=800 只有 100 mm（< 管半徑 50 + 淨距 100），不在障礙物內，但格子被淨距範圍擋住
        var s = LoadScene("s10_start_goal_blocked");
        var line = s.Lines[2];
        Assert.False(((Engine.Scene.BoxShape)s.Obstacles[0].Shape).Box.Contains(line.Start.Position));
        var r = Route(s, 2);
        AssertFailure(r, "StartBlocked");
    }

    [Fact]
    public void ComponentSequence_IsNotSupportedYet()
    {
        var s = LoadScene("s01_open_straight");
        var line = s.Lines[0] with { ComponentSequence = new[] { "handValve" } };
        var r = LineRouterRoute(s, line);
        AssertFailure(r, "NotSupported");
    }

    [Fact]
    public void PortsOutsideTheGrid_AreReported()
    {
        var s = LoadScene("s01_open_straight");
        var l0 = s.Lines[0];
        var startOut = l0 with { Start = l0.Start with { Position = V(-100, 2000, 1200) } };
        AssertFailure(LineRouterRoute(s, startOut), "StartOutOfBounds");
        var endOut = l0 with { End = l0.End with { Position = V(9000, 2000, 1200) } };
        AssertFailure(LineRouterRoute(s, endOut), "GoalOutOfBounds");
    }

    [Fact]
    public void PortsInTheSameCell_AreInvalidInput()
    {
        var s = LoadScene("s01_open_straight");
        var l0 = s.Lines[0];
        var same = l0 with { End = l0.End with { Position = V(520, 2020, 1210) } };    // 與起點 (500,2000,1200) 同一格
        AssertFailure(LineRouterRoute(s, same), "InvalidInput");
    }

    [Fact]
    public void SearchLimit_IsReportedNotCrashed()
    {
        var cfg = Config with { Search = Config.Search with { MaxExpandedNodes = 10 } };
        var r = Route(LoadScene("s01_open_straight"), config: cfg);
        AssertFailure(r, "SearchLimitExceeded");
        Assert.Equal(10, r.Failure!.Expanded);
        Assert.NotNull(r.Failure.ClosestCell);
    }

    [Fact]
    public void LimitJustEnough_Succeeds()
    {
        // s01 直線：50 步 + 抵達狀態，展開數剛好足夠時成功
        var ok = Route(LoadScene("s01_open_straight"));
        long needed = ok.Search.Expanded;
        var exact = Config with { Search = Config.Search with { MaxExpandedNodes = needed } };
        Assert.True(Route(LoadScene("s01_open_straight"), config: exact).Success);
        var short1 = Config with { Search = Config.Search with { MaxExpandedNodes = needed - 1 } };
        AssertFailure(Route(LoadScene("s01_open_straight"), config: short1), "SearchLimitExceeded");
    }

    [Fact]
    public void ZeroLengthCost_IsInvalidInput()
    {
        var cfg = Config with { Weights = Config.Weights with { Length = 1e-9 } };
        AssertFailure(Route(LoadScene("s01_open_straight"), config: cfg), "InvalidInput");
    }

    [Fact]
    public void GridErrors_SurfaceAsFailures()
    {
        var tiny = Config with { VoxelSizeMm = 0.001 };
        AssertFailure(Route(LoadScene("s01_open_straight"), config: tiny), "GridTooLarge");
        var bad = Config with { VoxelSizeMm = 0 };
        AssertFailure(Route(LoadScene("s01_open_straight"), config: bad), "InvalidVoxelSize");
    }

    [Fact]
    public void StartPortBoxedIn_NoFirstStep_IsNoPath()
    {
        // 起點接口方向 +X，但第一步的格子被障礙物擋住 → 無解（不能改走別的方向）
        var s = LoadScene("s01_open_straight");
        var scene = s with { Obstacles = new[] { Box("PLUG", 800, 1700, 900, 900, 2300, 1500, Engine.Scene.ObstacleKind.Wall) } };
        var line = scene.Lines[0];
        var grid = GridFor(scene, line);
        Assert.True(grid.TrySnap(line.Start.Position, out var sc, out _));
        Assert.False(grid.IsBlocked(sc));
        Assert.True(grid.IsBlocked(new CellIndex(sc.X + 1, sc.Y, sc.Z)));
        var r = Route(scene);
        AssertFailure(r, "NoPathExists");
        Assert.Null(ReferenceOptimum(grid, line, Config, MinCells(Example, Config)));
    }

    private static RouteResult LineRouterRoute(Engine.Scene.SceneModel s, Engine.Scene.LineRequest line) =>
        LineRouter.Route(s, line, Example, Config);
}
