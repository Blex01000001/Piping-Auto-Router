namespace PipingRouter.Tests;

internal static class RepoPaths
{
    /// <summary>從測試輸出目錄往上找到含 PipingAutoRouter.sln 的 repo 根目錄。</summary>
    public static string Root { get; } = FindRoot();

    public static string Combine(params string[] parts) =>
        Path.Combine(new[] { Root }.Concat(parts).ToArray());

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PipingAutoRouter.sln")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("找不到 PipingAutoRouter.sln，無法定位 repo 根目錄。");
    }
}
