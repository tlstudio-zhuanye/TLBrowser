using System.Text;
using System.Text.Json;

namespace TLBrowser;

/// <summary>一条访问记录。</summary>
internal sealed class HistoryItem
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime When { get; set; } = DateTime.Now;
    public int Visits { get; set; } = 1;
}

/// <summary>
/// 浏览历史。只保留最近 600 条，同一个地址重复访问只更新时间并把访问次数加一。
///
/// 写盘做了节流：浏览时每跳一次页面都落盘既没必要也伤盘，
/// 所以两秒内的连续写入合并成一次，另外退出前一定会 Flush。
/// 落盘失败一律静默——历史记录丢失不该影响浏览。
/// </summary>
internal static class HistoryStore
{
    private const int MaxItems = 600;
    private const int SaveThrottleMs = 2000;

    private static readonly object Gate = new();
    private static List<HistoryItem>? _items;
    private static bool _dirty;
    private static long _lastSave;

    public static string FilePath => Path.Combine(Brand.AppDataDir, "history.json");

    /// <summary>本地首页、about:、data: 之类不进历史——那不是「访问过的网站」。</summary>
    private static bool IsRecordable(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static List<HistoryItem> Items
    {
        get
        {
            lock (Gate)
            {
                if (_items is not null) return _items;

                _items = new List<HistoryItem>();
                try
                {
                    if (File.Exists(FilePath))
                    {
                        var json = File.ReadAllText(FilePath, Encoding.UTF8);
                        var loaded = JsonSerializer.Deserialize<List<HistoryItem>>(json);
                        if (loaded is not null)
                        {
                            foreach (var it in loaded)
                                if (IsRecordable(it.Url)) _items.Add(it);
                        }
                    }
                }
                catch { _items = new List<HistoryItem>(); }

                return _items;
            }
        }
    }

    public static void Add(string? url, string? title)
    {
        if (!IsRecordable(url)) return;
        var u = url!.Trim();

        lock (Gate)
        {
            var list = Items;
            var idx = list.FindIndex(x => string.Equals(x.Url, u, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                var hit = list[idx];
                list.RemoveAt(idx);
                hit.When = DateTime.Now;
                hit.Visits++;
                if (!string.IsNullOrWhiteSpace(title)) hit.Title = title!.Trim();
                list.Insert(0, hit);
            }
            else
            {
                list.Insert(0, new HistoryItem
                {
                    Url = u,
                    Title = string.IsNullOrWhiteSpace(title) ? "" : title!.Trim(),
                    When = DateTime.Now
                });
                while (list.Count > MaxItems) list.RemoveAt(list.Count - 1);
            }

            _dirty = true;
            if (Environment.TickCount64 - _lastSave >= SaveThrottleMs) SaveLocked();
        }
    }

    public static List<HistoryItem> Recent(int max)
    {
        lock (Gate) return Items.Take(max).ToList();
    }

    /// <summary>按地址或标题模糊匹配，最近的排前面。用于地址栏联想。</summary>
    public static List<HistoryItem> Match(string query, int max)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<HistoryItem>();
        var q = query.Trim();

        lock (Gate)
        {
            return Items
                .Where(x => x.Url.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            x.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Url.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.When)
                .Take(max)
                .ToList();
        }
    }

    public static int Count
    {
        get { lock (Gate) return Items.Count; }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Items.Clear();
            SaveLocked();
        }
    }

    /// <summary>退出前把还没落盘的改动写下去。</summary>
    public static void Flush()
    {
        lock (Gate)
        {
            if (!_dirty) return;
            SaveLocked();
        }
    }

    private static void SaveLocked()
    {
        try
        {
            var json = JsonSerializer.Serialize(Items,
                new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(FilePath, json, new UTF8Encoding(false));
            _dirty = false;
            _lastSave = Environment.TickCount64;
        }
        catch { }
    }
}
