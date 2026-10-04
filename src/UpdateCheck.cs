using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace TLBrowser;

/// <summary>一次更新检查的结果。没网络 / 没新版本时 HasUpdate 为 false，其余字段留空。</summary>
internal sealed record UpdateInfo
{
    public bool HasUpdate { get; init; }
    public string LatestVersion { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public string Notes { get; init; } = "";
}

/// <summary>
/// 启动时从 GitHub 上的 version.json 拉最新版本号，和当前版本比对，
/// 有新版本就把直链与更新说明带回来。全程不抛异常：断网、被墙、
/// JSON 格式不对都当作「没更新」，静默返回，绝不拖慢或卡死启动。
/// </summary>
internal static class UpdateCheck
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    /// <summary>记下「已经为哪个版本弹过通知」，避免每次启动都弹同一个提示。</summary>
    private static string _lastNotified = "";

    public static string StatePath => Path.Combine(Brand.AppDataDir, "update_state.txt");

    /// <summary>启动时把上次已通知的版本读进来（读不到就当从没弹过）。</summary>
    public static void LoadState()
    {
        try { _lastNotified = File.ReadAllText(StatePath).Trim(); }
        catch { _lastNotified = ""; }
    }

    /// <summary>返回 true 表示这个版本还没通知过（本次可以弹），并把状态落盘。</summary>
    public static bool MarkNotified(string version)
    {
        if (string.Equals(_lastNotified, version, StringComparison.Ordinal)) return false;
        _lastNotified = version;
        try { File.WriteAllText(StatePath, version, new UTF8Encoding(false)); }
        catch { }
        return true;
    }

    /// <summary>
    /// 首选 api.tlstudio.cn 上的版本清单，打不开再退 GitHub raw。
    /// 两个都读不到就返回 null，由调用方按「没更新」处理。
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        var json = await FetchAsync(Brand.UpdateManifestUrl)
                ?? await FetchAsync(Brand.UpdateManifestUrlAlt);
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var version = GetStr(root, "version");
            if (string.IsNullOrWhiteSpace(version)) return null;

            if (CompareVersion(version, Brand.AppVersion) <= 0)
                return new UpdateInfo { HasUpdate = false, LatestVersion = Brand.AppVersion };

            return new UpdateInfo
            {
                HasUpdate = true,
                LatestVersion = version,
                DownloadUrl = GetStr(root, "url"),
                Notes = GetStr(root, "notes")
            };
        }
        catch { return null; }
    }

    private static async Task<string?> FetchAsync(string url)
    {
        try
        {
            using var resp = await Http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync();
        }
        catch { return null; }
    }

    private static string GetStr(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
            return el.GetString() ?? "";
        return "";
    }

    /// <summary>按数字段比较两个「x.y.z」版本号，1.10 大于 1.9，而不是字符串序。</summary>
    public static int CompareVersion(string a, string b)
    {
        var pa = Parts(a);
        var pb = Parts(b);
        var n = Math.Max(pa.Length, pb.Length);
        for (var i = 0; i < n; i++)
        {
            var x = i < pa.Length ? pa[i] : 0;
            var y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x > y ? 1 : -1;
        }
        return 0;
    }

    private static int[] Parts(string v)
    {
        var segs = (v ?? "").Split('.');
        var arr = new int[segs.Length];
        for (var i = 0; i < segs.Length; i++)
            int.TryParse(segs[i], out arr[i]);
        return arr;
    }
}
