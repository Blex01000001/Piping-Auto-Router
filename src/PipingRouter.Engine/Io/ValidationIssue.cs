using System.Text;

namespace PipingRouter.Engine.Io;

/// <summary>讀入 JSON 時發現的問題。Path 例如 "obstacles[2].shape.radius"，根物件為空字串。</summary>
public sealed record ValidationIssue(Severity Severity, string Code, string Path, string Message)
{
    public override string ToString() =>
        $"[{(Severity == Severity.Error ? "error" : "warning")}] {(Path.Length == 0 ? "(root)" : Path)}: {Message} ({Code})";
}

/// <summary>讀入結果：沒有 error 才有 Value；warning 不影響 Value。</summary>
public sealed class LoadResult<T> where T : class
{
    public T? Value { get; }
    public IReadOnlyList<ValidationIssue> Issues { get; }

    internal LoadResult(T? value, IReadOnlyList<ValidationIssue> issues)
    {
        Issues = issues;
        Value = issues.Any(i => i.Severity == Severity.Error) ? null : value;
    }

    public bool Ok => Value != null;
    public IEnumerable<ValidationIssue> Errors => Issues.Where(i => i.Severity == Severity.Error);
    public IEnumerable<ValidationIssue> Warnings => Issues.Where(i => i.Severity == Severity.Warning);

    /// <summary>明確選擇用例外處理失敗（例如測試或命令列工具）。一般流程請檢查 Ok。</summary>
    public T ValueOrThrow()
    {
        if (Value != null) return Value;
        var sb = new StringBuilder("讀入失敗：");
        foreach (var e in Errors) sb.AppendLine().Append("  ").Append(e);
        throw new InvalidDataException(sb.ToString());
    }
}

internal sealed class IssueList
{
    private readonly List<ValidationIssue> _items = new();
    private readonly List<JsonObj> _objects = new();

    public IReadOnlyList<ValidationIssue> Items => _items;

    public void Error(string code, string path, string message) =>
        _items.Add(new ValidationIssue(Severity.Error, code, path, message));

    public void Warn(string code, string path, string message) =>
        _items.Add(new ValidationIssue(Severity.Warning, code, path, message));

    public void Track(JsonObj obj) => _objects.Add(obj);

    /// <summary>讀完後，對每個「有被讀過」的物件，把沒被用到的欄位回報為 warning（抓拼字錯誤）。</summary>
    public void ReportUnknownFields()
    {
        foreach (var o in _objects) o.ReportUnused();
    }
}
