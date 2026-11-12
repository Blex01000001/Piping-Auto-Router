using PipingRouter.Engine;
using PipingRouter.Engine.Io;
using Xunit;

namespace PipingRouter.Tests.Io;

internal static class IssueAssert
{
    public static string Dump<T>(LoadResult<T> r) where T : class =>
        r.Issues.Count == 0 ? "(無問題)" : string.Join("\n", r.Issues);

    public static void HasError<T>(LoadResult<T> r, string code, string path) where T : class
    {
        Assert.False(r.Ok, "預期讀入失敗，但成功了");
        Assert.Null(r.Value);
        bool hit = r.Errors.Any(e => e.Code == code && e.Path == path);
        Assert.True(hit, $"找不到 error {code} @ {path}。實際問題：\n{Dump(r)}");
    }

    public static void HasWarning<T>(LoadResult<T> r, string code, string path) where T : class
    {
        bool hit = r.Warnings.Any(e => e.Code == code && e.Path == path);
        Assert.True(hit, $"找不到 warning {code} @ {path}。實際問題：\n{Dump(r)}");
    }

    public static void NoIssues<T>(LoadResult<T> r) where T : class
    {
        Assert.True(r.Ok, Dump(r));
        Assert.True(r.Issues.Count == 0, Dump(r));
    }

    public static string Mutate(string json, string from, string to)
    {
        Assert.Contains(from, json);
        return json.Replace(from, to);
    }
}
