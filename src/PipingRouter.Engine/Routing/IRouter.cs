using PipingRouter.Engine.Scene;

namespace PipingRouter.Engine.Routing;

/// <summary>
/// 單條管線的佈線器。grid 由呼叫端準備（可帶入已佈管線的佔用），
/// 日後多條管線的整體搜尋（MCTS／GA／SA）只需要決定順序並反覆呼叫它。
/// </summary>
public interface IRouter
{
    RouteResult Route(VoxelGrid grid, LineRequest line, RoutingConfig config);
}
