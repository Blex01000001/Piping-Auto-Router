using Xunit;

namespace PipingRouter.Tests.Routing;

/// <summary>
/// Golden 檔：把佈線結果的 JSON 鎖在 tests/PipingRouter.Tests/Golden/ 底下，任何排序、成本或格式的變動都會被抓到。
/// 有意改變輸出格式或演算法時，以環境變數 UPDATE_GOLDEN=1 重新產生，並檢視 git diff 後再提交。
/// </summary>
internal static class GoldenFiles
{
    public static void Match(string name, string actual)
    {
        string path = RepoPaths.Combine("tests", "PipingRouter.Tests", "Golden", name);
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual, new System.Text.UTF8Encoding(false));
            return;
        }
        Assert.True(File.Exists(path), $"缺少 golden 檔 {name}。若是新增，請用 UPDATE_GOLDEN=1 產生並檢視後提交。");
        string expected = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.Equal(expected, actual);
    }
}
