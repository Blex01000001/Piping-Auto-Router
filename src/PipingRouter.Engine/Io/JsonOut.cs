using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PipingRouter.Engine.Geometry;

namespace PipingRouter.Engine.Io;

/// <summary>輸出用的有序 JSON 物件。欄位順序即寫出順序，確保輸出確定。</summary>
internal sealed class JObj : List<KeyValuePair<string, object?>>
{
    public JObj Add(string name, object? value)
    {
        Add(new KeyValuePair<string, object?>(name, value));
        return this;
    }
}

internal sealed class JArr : List<object?>
{
}

/// <summary>單行排版的數值陣列，例如 [1, 2, 3]。</summary>
internal sealed class JInline : List<double>
{
    public static JInline Of(Vec3 v) => new() { v.X, v.Y, v.Z };
}

/// <summary>極簡 JSON 輸出器：縮排 2 格、向量單行、保留中文不轉義、數字以可來回的最短格式輸出。</summary>
internal static class JsonOut
{
    private static readonly JsonSerializerOptions StringOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Write(JObj root)
    {
        var sb = new StringBuilder();
        WriteValue(sb, root, 0);
        sb.Append('\n');
        return sb.ToString();
    }

    public static string Num(double d)
    {
        if (!double.IsFinite(d)) throw new ArgumentException("無法輸出非有限數值。");
        return d.ToString("R", CultureInfo.InvariantCulture);
    }

    private static void WriteValue(StringBuilder sb, object? v, int level)
    {
        switch (v)
        {
            case null: sb.Append("null"); break;
            case string s: sb.Append(JsonSerializer.Serialize(s, StringOptions)); break;
            case bool b: sb.Append(b ? "true" : "false"); break;
            case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
            case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
            case double d: sb.Append(Num(d)); break;
            case JInline inl:
                sb.Append('[').Append(string.Join(", ", inl.Select(Num))).Append(']');
                break;
            case JArr arr:
                if (arr.Count == 0) { sb.Append("[]"); break; }
                sb.Append("[\n");
                for (int k = 0; k < arr.Count; k++)
                {
                    Indent(sb, level + 1);
                    WriteValue(sb, arr[k], level + 1);
                    sb.Append(k + 1 < arr.Count ? ",\n" : "\n");
                }
                Indent(sb, level);
                sb.Append(']');
                break;
            case JObj obj:
                if (obj.Count == 0) { sb.Append("{}"); break; }
                sb.Append("{\n");
                for (int k = 0; k < obj.Count; k++)
                {
                    Indent(sb, level + 1);
                    sb.Append(JsonSerializer.Serialize(obj[k].Key, StringOptions)).Append(": ");
                    WriteValue(sb, obj[k].Value, level + 1);
                    sb.Append(k + 1 < obj.Count ? ",\n" : "\n");
                }
                Indent(sb, level);
                sb.Append('}');
                break;
            default:
                throw new ArgumentException($"不支援的輸出型別 {v.GetType()}");
        }
    }

    private static void Indent(StringBuilder sb, int level) => sb.Append(' ', level * 2);
}
