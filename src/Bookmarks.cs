using System.Text;
using System.Text.Json;

namespace TLBrowser;

internal sealed class Bookmark
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime Added { get; set; } = DateTime.Now;
}

/// <summary>
/// 书签。跟历史一样落一个 JSON，区别是只有用户明确点了「添加」才会进。
/// 上限 500 条，同一个地址只留一条（再点一次是取消收藏）。
/// </summary>
internal static class Bookmarks
{
    private const int MaxItems = 500;
    private static readonly object Gate = new();
    private static List<Bookmark>? _items;

    public static string FilePath => Path.Combine(Brand.AppDataDir, "bookmarks.json");

    private static List<Bookmark> Items
    {
        get
        {
            lock (Gate)
            {
                if (_items is not null) return _items;
                _items = new List<Bookmark>();
                try
                {
                    if (File.Exists(FilePath))
                    {
                        var loaded = JsonSerializer.Deserialize<List<Bookmark>>(
                            File.ReadAllText(FilePath, Encoding.UTF8));
                        if (loaded is not null)
                            foreach (var b in loaded)
                                if (!string.IsNullOrWhiteSpace(b.Url)) _items.Add(b);
                    }
                }
                catch { _items = new List<Bookmark>(); }
                return _items;
            }
        }
    }

    public static int Count
    {
        get { lock (Gate) return Items.Count; }
    }

    public static List<Bookmark> All()
    {
        lock (Gate) return Items.ToList();
    }

    public static bool Contains(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        lock (Gate) return Items.Any(b => string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>加书签；已经存在就当成「取消收藏」，返回 false 表示这次是移除。</summary>
    public static bool Toggle(string url, string title)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        lock (Gate)
        {
            var idx = Items.FindIndex(b => string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                Items.RemoveAt(idx);
                Save();
                return false;
            }

            Items.Insert(0, new Bookmark
            {
                Url = url,
                Title = string.IsNullOrWhiteSpace(title) ? url : title.Trim(),
                Added = DateTime.Now
            });
            while (Items.Count > MaxItems) Items.RemoveAt(Items.Count - 1);
            Save();
            return true;
        }
    }

    public static void Remove(string url)
    {
        lock (Gate)
        {
            Items.RemoveAll(b => string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));
            Save();
        }
    }

    public static void Save()
    {
        try
        {
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(Items, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }
        catch { }
    }
}
