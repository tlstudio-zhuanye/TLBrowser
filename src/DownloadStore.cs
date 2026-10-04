using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace TLBrowser;

/// <summary>一条已完成 / 已取消 / 已中断的下载记录（落盘，供下次启动后仍能看到）。</summary>
internal sealed class DownloadItem
{
    public string FileName { get; set; } = "";
    public string Url { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string State { get; set; } = "已完成";
    public long TotalBytes { get; set; }
    public DateTime When { get; set; } = DateTime.Now;
}

/// <summary>一条正在进行的下载，持有操作句柄，能看进度、能取消。</summary>
internal sealed class ActiveDownload
{
    public CoreWebView2DownloadOperation Operation { get; }
    public string FileName { get; set; } = "";
    public string Url { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long BytesReceived { get; set; }
    public long TotalBytes { get; set; }
    public CoreWebView2DownloadState State { get; set; } = CoreWebView2DownloadState.InProgress;

    public ActiveDownload(CoreWebView2DownloadOperation op) => Operation = op;

    /// <summary>进度百分比；总大小未知时返回 -1（此时前端只显示已下多少）。</summary>
    public int Percent => TotalBytes > 0
        ? (int)Math.Clamp(BytesReceived * 100L / TotalBytes, 0, 100)
        : -1;

    public string StateText => State switch
    {
        CoreWebView2DownloadState.Completed => "已完成",
        CoreWebView2DownloadState.Interrupted => "已中断",
        _ => "下载中"
    };

    public void Cancel()
    {
        try { Operation.Cancel(); } catch { }
    }
}

/// <summary>
/// 下载中心。所有标签页的下载都汇总到这里：进行中的放在 Active，
/// 一旦结束（完成 / 取消 / 中断）就转成一条 DownloadItem 落盘进 downloads.json。
///
/// 写盘只在下载状态变化时发生，进度条那种每毫秒一次的 BytesReceivedChanged 只更新内存，
/// 不碰磁盘——下载一个大文件要是每次都落盘，等于把盘也下一遍。
/// </summary>
internal static class DownloadStore
{
    private const int MaxHistory = 500;

    private static readonly object Gate = new();
    private static readonly List<ActiveDownload> Active = new();
    private static List<DownloadItem>? _history;

    /// <summary>任何进度/状态变化都会触发。面板靠它刷新，但面板会自己节流。</summary>
    public static event Action? Changed;

    public static string FilePath => Path.Combine(Brand.AppDataDir, "downloads.json");

    /// <summary>系统的「下载」文件夹。找不到就退回用户主目录，总之给个能打开的地方。</summary>
    public static string DownloadsFolder
    {
        get
        {
            var p = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return Directory.Exists(p)
                ? p
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }

    private static List<DownloadItem> History
    {
        get
        {
            lock (Gate)
            {
                if (_history is not null) return _history;
                _history = new List<DownloadItem>();
                try
                {
                    if (File.Exists(FilePath))
                    {
                        var loaded = JsonSerializer.Deserialize<List<DownloadItem>>(
                            File.ReadAllText(FilePath, Encoding.UTF8));
                        if (loaded is not null)
                            foreach (var d in loaded)
                                if (!string.IsNullOrWhiteSpace(d.FileName)) _history.Add(d);
                    }
                }
                catch { _history = new List<DownloadItem>(); }
                return _history;
            }
        }
    }

    public static IReadOnlyList<ActiveDownload> ActiveList
    {
        get { lock (Gate) return Active.ToList(); }
    }

    public static int ActiveCount
    {
        get { lock (Gate) return Active.Count; }
    }

    public static List<DownloadItem> Recent(int max)
    {
        lock (Gate) return History.Take(max).ToList();
    }

    public static int HistoryCount
    {
        get { lock (Gate) return History.Count; }
    }

    /// <summary>注册一条下载。进度/状态变化都会触发 Changed，结束时自动转入历史并落盘。</summary>
    public static void Track(CoreWebView2DownloadOperation op, string fileName, string url)
    {
        var item = new ActiveDownload(op) { FileName = fileName, Url = url };
        try { item.FilePath = op.ResultFilePath ?? ""; } catch { }

        lock (Gate) Active.Add(item);

        op.BytesReceivedChanged += (_, _) =>
        {
            try
            {
                item.BytesReceived = (long)op.BytesReceived;
                item.TotalBytes = (long)(op.TotalBytesToReceive ?? 0);
                var path = op.ResultFilePath;
                if (!string.IsNullOrEmpty(path))
                {
                    item.FilePath = path;
                    var n = System.IO.Path.GetFileName(path);
                    if (!string.IsNullOrEmpty(n)) item.FileName = n;
                }
            }
            catch { }
            Changed?.Invoke();
        };

        op.StateChanged += (_, _) =>
        {
            try
            {
                item.State = op.State;
                item.BytesReceived = (long)op.BytesReceived;
                item.TotalBytes = (long)(op.TotalBytesToReceive ?? 0);
                var path = op.ResultFilePath;
                if (!string.IsNullOrEmpty(path))
                {
                    item.FilePath = path;
                    var n = System.IO.Path.GetFileName(path);
                    if (!string.IsNullOrEmpty(n)) item.FileName = n;
                }
            }
            catch { }

            if (op.State != CoreWebView2DownloadState.InProgress)
            {
                lock (Gate) Active.Remove(item);
                Record(item, op);
            }

            Changed?.Invoke();
        };

        Changed?.Invoke();
    }

    private static void Record(ActiveDownload d, CoreWebView2DownloadOperation op)
    {
        var state = "已中断";
        try
        {
            if (op.State == CoreWebView2DownloadState.Completed) state = "已完成";
            else if (op.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled) state = "已取消";
            else if (op.InterruptReason == CoreWebView2DownloadInterruptReason.UserShutdown) state = "已取消";
        }
        catch { }

        lock (Gate)
        {
            History.Insert(0, new DownloadItem
            {
                FileName = string.IsNullOrWhiteSpace(d.FileName) ? "(未知文件)" : d.FileName,
                Url = d.Url,
                FilePath = d.FilePath,
                State = state,
                TotalBytes = d.BytesReceived,
                When = DateTime.Now
            });
            while (History.Count > MaxHistory) History.RemoveAt(History.Count - 1);
            SaveLocked();
        }
    }

    /// <summary>删掉一条历史记录。</summary>
    public static void Remove(DownloadItem item)
    {
        lock (Gate)
        {
            History.Remove(item);
            SaveLocked();
        }
        Changed?.Invoke();
    }

    /// <summary>清空全部下载记录。只删记录，不删已经下好的文件。</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            History.Clear();
            SaveLocked();
        }
        Changed?.Invoke();
    }

    private static void SaveLocked()
    {
        try
        {
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(History, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }
        catch { }
    }

    // ────────────────────────────── 动作 ──────────────────────────────

    /// <summary>用系统关联程序打开已下载的文件。</summary>
    public static bool OpenFile(DownloadItem item)
    {
        if (string.IsNullOrWhiteSpace(item.FilePath)) return false;
        if (!File.Exists(item.FilePath)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    /// <summary>在资源管理器里定位到这个文件（文件已删则退到所在文件夹）。</summary>
    public static bool OpenFolder(DownloadItem item)
    {
        try
        {
            if (File.Exists(item.FilePath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FilePath}\"")
                { UseShellExecute = false });
                return true;
            }

            var dir = string.IsNullOrWhiteSpace(item.FilePath)
                ? DownloadsFolder
                : System.IO.Path.GetDirectoryName(item.FilePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = DownloadsFolder;
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    /// <summary>打开系统的下载文件夹本体。</summary>
    public static bool OpenDownloadsFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(DownloadsFolder) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }
}
