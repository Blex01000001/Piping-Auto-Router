namespace PipingRouter.Engine.Routing;

/// <summary>搜尋狀態（格子 × 方向）的 g 值、父狀態與關閉旗標。小場景用陣列，大場景改用字典，避免耗盡記憶體。</summary>
internal interface IStateStore
{
    long G(int state);
    int Parent(int state);
    bool IsClosed(int state);
    void Set(int state, long g, int parent);
    void Close(int state);
}

internal sealed class ArrayStateStore : IStateStore
{
    private readonly long[] _g;
    private readonly int[] _parent;
    private readonly bool[] _closed;

    public ArrayStateStore(int states)
    {
        _g = new long[states];
        Array.Fill(_g, long.MaxValue);
        _parent = new int[states];
        _closed = new bool[states];
    }

    public long G(int state) => _g[state];
    public int Parent(int state) => _parent[state];
    public bool IsClosed(int state) => _closed[state];
    public void Set(int state, long g, int parent) { _g[state] = g; _parent[state] = parent; }
    public void Close(int state) => _closed[state] = true;
}

internal sealed class DictionaryStateStore : IStateStore
{
    private struct Entry
    {
        public long G;
        public int Parent;
        public bool Closed;
    }

    private readonly Dictionary<int, Entry> _map = new();

    public long G(int state) => _map.TryGetValue(state, out var e) ? e.G : long.MaxValue;
    public int Parent(int state) => _map[state].Parent;
    public bool IsClosed(int state) => _map.TryGetValue(state, out var e) && e.Closed;

    public void Set(int state, long g, int parent)
    {
        _map.TryGetValue(state, out var e);
        e.G = g;
        e.Parent = parent;
        _map[state] = e;
    }

    public void Close(int state)
    {
        var e = _map[state];
        e.Closed = true;
        _map[state] = e;
    }
}
