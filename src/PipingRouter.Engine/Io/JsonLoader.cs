using System.Text;
using System.Text.Json;

namespace PipingRouter.Engine.Io;

/// <summary>JSON 讀入的共用流程：解析、定位錯誤、呼叫各自的讀取器、彙整未知欄位警告。</summary>
internal static class JsonLoader
{
    // 設定檔允許註解與尾端逗號；輸出一律是嚴格 JSON。
    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static LoadResult<T> Parse<T>(string? json, Func<JsonObj, IssueList, T?> read) where T : class
    {
        var issues = new IssueList();
        if (json == null)
        {
            issues.Error("InvalidJson", "", "輸入為 null。");
            return new LoadResult<T>(null, issues.Items);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, Options);
        }
        catch (JsonException ex)
        {
            string where = ex.LineNumber.HasValue
                ? $"（第 {ex.LineNumber + 1} 行，第 {ex.BytePositionInLine + 1} 個位元組）"
                : "";
            issues.Error("InvalidJson", "", $"JSON 格式錯誤{where}：{ex.Message}");
            return new LoadResult<T>(null, issues.Items);
        }

        using (doc)
        {
            T? value = null;
            var root = JsonObj.From(doc.RootElement, "", issues);
            if (root != null) value = read(root, issues);
            issues.ReportUnknownFields();
            return new LoadResult<T>(value, issues.Items);
        }
    }

    public static LoadResult<T> Load<T>(string path, Func<JsonObj, IssueList, T?> read) where T : class
    {
        string text;
        try
        {
            if (!File.Exists(path))
            {
                var missing = new IssueList();
                missing.Error("FileNotFound", "", $"找不到檔案：{path}");
                return new LoadResult<T>(null, missing.Items);
            }
            text = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            var failed = new IssueList();
            failed.Error("FileReadError", "", $"無法讀取檔案 {path}：{ex.Message}");
            return new LoadResult<T>(null, failed.Items);
        }
        return Parse(text, read);
    }

    public static void Save(string path, string text)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }
}
