using System.Runtime.CompilerServices;

// 測試專案可以直接測試內部的狀態編碼（StateLayout）。這只是一個組件屬性，不是專案參考。
[assembly: InternalsVisibleTo("PipingRouter.Tests")]

namespace PipingRouter.Engine;

/// <summary>Engine 組件標記，供測試與日後其他專案取得組件參考。</summary>
public static class EngineAssembly
{
    public const string Name = "PipingRouter.Engine";
}
