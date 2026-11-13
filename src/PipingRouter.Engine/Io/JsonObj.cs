using System.Text.Json;
using PipingRouter.Engine.Geometry;

namespace PipingRouter.Engine.Io;

/// <summary>
/// 讀 JSON 物件的輔助：每個存取都帶路徑，錯誤收集到 IssueList 而不是丟例外。
/// 必要欄位缺漏或型別錯誤時回傳預設值（0／空字串），讓呼叫端可以繼續讀其他欄位、一次回報所有問題。
/// </summary>
internal sealed class JsonObj
{
    private readonly Dictionary<string, JsonElement> _props = new(StringComparer.Ordinal);
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);
    private readonly IssueList _issues;

    public string Path { get; }

    private JsonObj(string path, IssueList issues)
    {
        Path = path;
        _issues = issues;
    }

    public static JsonObj? From(JsonElement el, string path, IssueList issues)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            issues.Error("WrongType", path, $"應為物件，實際為 {Describe(el)}");
            return null;
        }
        var o = new JsonObj(path, issues);
        foreach (var p in el.EnumerateObject())
        {
            if (!o._props.TryAdd(p.Name, p.Value))
                issues.Error("DuplicateField", o.P(p.Name), "欄位重複出現");
        }
        issues.Track(o);
        return o;
    }

    public string P(string name) => Path.Length == 0 ? name : Path + "." + name;

    public void Error(string name, string code, string message) => _issues.Error(code, P(name), message);
    public void Warn(string name, string code, string message) => _issues.Warn(code, P(name), message);

    internal void ReportUnused()
    {
        if (_used.Count == 0) return;
        foreach (var name in _props.Keys)
            if (!_used.Contains(name))
                _issues.Warn("UnknownField", P(name), "未知欄位，已忽略（是否拼錯？）");
    }

    public bool Has(string name)
    {
        _used.Add(name);
        return _props.TryGetValue(name, out var el) && el.ValueKind != JsonValueKind.Null;
    }

    private bool TryGet(string name, bool required, out JsonElement el)
    {
        _used.Add(name);
        if (_props.TryGetValue(name, out el) && el.ValueKind != JsonValueKind.Null) return true;
        if (required) _issues.Error("MissingField", P(name), "缺少必要欄位");
        return false;
    }

    // ---- 字串 ----
    private bool TryStr(string name, bool required, out string v)
    {
        v = "";
        if (!TryGet(name, required, out var el)) return false;
        if (el.ValueKind != JsonValueKind.String)
        {
            _issues.Error("WrongType", P(name), $"應為字串，實際為 {Describe(el)}");
            return false;
        }
        v = el.GetString() ?? "";
        return true;
    }

    public string ReqStr(string name) => TryStr(name, true, out var v) ? v : "";
    public string OptStr(string name, string def) => TryStr(name, false, out var v) ? v : def;

    // ---- 數值 ----
    private bool TryNum(string name, bool required, out double v)
    {
        v = 0;
        if (!TryGet(name, required, out var el)) return false;
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetDouble(out v) || !double.IsFinite(v))
        {
            _issues.Error("WrongType", P(name), $"應為有限數值，實際為 {Describe(el)}");
            v = 0;
            return false;
        }
        return true;
    }

    public double ReqNum(string name) => TryNum(name, true, out var v) ? v : 0;
    public double OptNum(string name, double def) => TryNum(name, false, out var v) ? v : def;

    private bool TryLong(string name, bool required, out long v)
    {
        v = 0;
        if (!TryGet(name, required, out var el)) return false;
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt64(out v))
        {
            _issues.Error("WrongType", P(name), $"應為整數，實際為 {Describe(el)}");
            v = 0;
            return false;
        }
        return true;
    }

    public long ReqLong(string name) => TryLong(name, true, out var v) ? v : 0;
    public long OptLong(string name, long def) => TryLong(name, false, out var v) ? v : def;

    public int ReqInt(string name)
    {
        long v = ReqLong(name);
        if (v < int.MinValue || v > int.MaxValue)
        {
            _issues.Error("OutOfRange", P(name), "整數超出範圍");
            return 0;
        }
        return (int)v;
    }

    // ---- 布林 ----
    public bool OptBool(string name, bool def)
    {
        if (!TryGet(name, false, out var el)) return def;
        if (el.ValueKind is JsonValueKind.True) return true;
        if (el.ValueKind is JsonValueKind.False) return false;
        _issues.Error("WrongType", P(name), $"應為 true/false，實際為 {Describe(el)}");
        return def;
    }

    // ---- 列舉（字串對應表）----
    public T ReqEnum<T>(string name, IReadOnlyDictionary<string, T> map) where T : struct
    {
        if (!TryStr(name, true, out var s)) return default;
        return MapEnum(name, s, map, default);
    }

    public T OptEnum<T>(string name, IReadOnlyDictionary<string, T> map, T def) where T : struct
    {
        if (!TryStr(name, false, out var s)) return def;
        return MapEnum(name, s, map, def);
    }

    private T MapEnum<T>(string name, string s, IReadOnlyDictionary<string, T> map, T fallback) where T : struct
    {
        foreach (var kv in map)
            if (string.Equals(kv.Key, s, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        _issues.Error("InvalidValue", P(name), $"不認得的值 \"{s}\"，可用：{string.Join(", ", map.Keys)}");
        return fallback;
    }

    public AxisDirection ReqDirection(string name)
    {
        if (!TryStr(name, true, out var s)) return AxisDirection.PosX;
        if (AxisDirections.TryParse(s, out var d)) return d;
        _issues.Error("InvalidValue", P(name), $"方向 \"{s}\" 無效，可用：+X, -X, +Y, -Y, +Z, -Z");
        return AxisDirection.PosX;
    }

    // ---- 向量 [x,y,z] ----
    private bool TryVec(JsonElement el, string path, out Vec3 v)
    {
        v = Vec3.Zero;
        if (el.ValueKind != JsonValueKind.Array || el.GetArrayLength() != 3)
        {
            _issues.Error("WrongType", path, "應為 [x, y, z] 三個數值的陣列");
            return false;
        }
        var c = new double[3];
        int i = 0;
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out c[i]) || !double.IsFinite(c[i]))
            {
                _issues.Error("WrongType", path + $"[{i}]", "應為有限數值");
                return false;
            }
            i++;
        }
        v = new Vec3(c[0], c[1], c[2]);
        return true;
    }

    public Vec3 ReqVec(string name) =>
        TryGet(name, true, out var el) && TryVec(el, P(name), out var v) ? v : Vec3.Zero;

    public Vec3 OptVec(string name, Vec3 def) =>
        TryGet(name, false, out var el) && TryVec(el, P(name), out var v) ? v : def;

    public List<Vec3> VecArray(string name, bool required)
    {
        var list = new List<Vec3>();
        if (!TryGet(name, required, out var el)) return list;
        if (el.ValueKind != JsonValueKind.Array)
        {
            _issues.Error("WrongType", P(name), "應為陣列");
            return list;
        }
        int i = 0;
        foreach (var item in el.EnumerateArray())
        {
            if (TryVec(item, $"{P(name)}[{i}]", out var v)) list.Add(v);
            i++;
        }
        return list;
    }

    /// <summary>整數陣列（必要欄位）。</summary>
    public List<int> VecArrayOfInts(string name)
    {
        var list = new List<int>();
        if (!TryGet(name, true, out var el)) return list;
        if (el.ValueKind != JsonValueKind.Array)
        {
            _issues.Error("WrongType", P(name), "應為陣列");
            return list;
        }
        int i = 0;
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int v)) list.Add(v);
            else _issues.Error("WrongType", $"{P(name)}[{i}]", "應為整數");
            i++;
        }
        return list;
    }

    public List<string> StrArray(string name)
    {
        var list = new List<string>();
        if (!TryGet(name, false, out var el)) return list;
        if (el.ValueKind != JsonValueKind.Array)
        {
            _issues.Error("WrongType", P(name), "應為陣列");
            return list;
        }
        int i = 0;
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString() ?? "");
            else _issues.Error("WrongType", $"{P(name)}[{i}]", "應為字串");
            i++;
        }
        return list;
    }

    // ---- 物件與物件陣列 ----
    public JsonObj? Obj(string name, bool required) =>
        TryGet(name, required, out var el) ? From(el, P(name), _issues) : null;

    public List<JsonObj> ObjArray(string name, bool required)
    {
        var list = new List<JsonObj>();
        if (!TryGet(name, required, out var el)) return list;
        if (el.ValueKind != JsonValueKind.Array)
        {
            _issues.Error("WrongType", P(name), "應為陣列");
            return list;
        }
        int i = 0;
        foreach (var item in el.EnumerateArray())
        {
            var o = From(item, $"{P(name)}[{i}]", _issues);
            if (o != null) list.Add(o);
            i++;
        }
        return list;
    }

    /// <summary>物件的所有成員名稱（用於讀取「名稱 → 規格」形式的字典）。</summary>
    public IEnumerable<string> Keys
    {
        get
        {
            foreach (var k in _props.Keys) _used.Add(k);
            return _props.Keys;
        }
    }

    private static string Describe(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Object => "物件",
        JsonValueKind.Array => "陣列",
        JsonValueKind.String => "字串",
        JsonValueKind.Number => "數值（非有限或超出範圍）",
        JsonValueKind.True or JsonValueKind.False => "布林",
        JsonValueKind.Null => "null",
        _ => el.ValueKind.ToString(),
    };
}
