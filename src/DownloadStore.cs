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
    /// <summary>
    /// 存机器可读的 key：done / canceled / interrupted。
    /// v1.1.2 之前落盘的是中文（"已完成"），读取时由 NormalizeState 兼容映射，
    /// 显示统一交给 Lang.T("dl.state." + StateKey)，这样切英文才不会冒出中文状态。
    /// </summary>
    public string State { get; set; } = "done";

    public string StateKey => DownloadStore.NormalizeState(State);

    public string StateText => Lang.T("dl.state." + StateKey);

    public bool IsDone => StateKey == "done";

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

    /// <summary>机器可读的状态 key（done / canceled / interrupted），显示时再翻成当前语言。</summary>
    public string StateKey => State switch
    {
        CoreWebView2DownloadState.Completed => "done",
        CoreWebView2DownloadState.Interrupted => "interrupted",
        _ => "active"
    };

    public string StateText => Lang.T("dl.state." + StateKey);

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

    /// <summary>
    /// 用户自己指定的下载位置，一行路径，纯文本。
    /// 单独放一个文件而不是塞进 settings.json，是为了让用户出问题时不装任何工具、
    /// 直接用记事本改回来（跟 adblock_user.txt 一个思路）。
    /// 文件不存在或为空 = 用系统「下载」文件夹。
    /// </summary>
    public static string FolderSettingPath => Path.Combine(Brand.AppDataDir, "download_folder.txt");

    /// <summary>当前生效的下载目录（已保证存在；建不出来就退回系统下载文件夹）。</summary>
    public static string DownloadFolder
    {
        get
        {
            var custom = ReadFolderSetting();
            if (!string.IsNullOrWhiteSpace(custom) && EnsureDir(custom)) return custom;
            return DownloadsFolder;
        }
    }

    /// <summary>用户有没有自己指定过位置（用来决定界面上要不要显示「恢复默认」）。</summary>
    public static bool IsCustomFolder
    {
        get
        {
            var custom = ReadFolderSetting();
            return !string.IsNullOrWhiteSpace(custom);
        }
    }

    /// <summary>保存自定义下载位置。传空 / 空白 = 恢复系统「下载」文件夹。</summary>
    public static void SetDownloadFolder(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                if (File.Exists(FolderSettingPath)) File.Delete(FolderSettingPath);
            }
            else
            {
                if (!EnsureDir(path))
                    throw new IOException("cannot create directory: " + path);
                File.WriteAllText(FolderSettingPath, path.Trim(), new UTF8Encoding(false));
            }
        }
        catch { throw; }
        Changed?.Invoke();
    }

    /// <summary>恢复默认的「下载」文件夹。</summary>
    public static void ResetDownloadFolder() => SetDownloadFolder(null);

    private static string ReadFolderSetting()
    {
        try
        {
            if (!File.Exists(FolderSettingPath)) return "";
            return File.ReadAllText(FolderSettingPath, Encoding.UTF8).Trim();
        }
        catch { return ""; }
    }

    /// <summary>目录在就用，不在就建；建不出来返回 false（调用方负责回退）。</summary>
    private static bool EnsureDir(string path)
    {
        try
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            return Directory.Exists(path);
        }
        catch { return false; }
    }

    /// <summary>
    /// 目标文件已存在就往后加 (1) (2)，避免把同名旧文件覆盖掉。
    /// 只能在本方法里拼路径，别在调用处手写。
    /// </summary>
    public static string UniquePath(string folder, string fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "download" : fileName.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        if (name.Length == 0) name = "download";

        var full = Path.Combine(folder, name);
        if (!File.Exists(full)) return full;

        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(folder, $"{stem} ({DateTime.Now:yyyyMMddHHmmss}){ext}");
    }

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

    /// <summary>
    /// 把落盘的状态统一成机器可读 key。
    /// 老版本写的是中文，这里一并认下来，免得升级后历史记录变成一堆「-」。
    /// </summary>
    public static string NormalizeState(string? raw) => raw switch
    {
        "done" => "done",
        "canceled" => "canceled",
        "interrupted" => "interrupted",
        "已完成" => "done",
        "已取消" => "canceled",
        "已中断" => "interrupted",
        _ => "interrupted"
    };

    private static void Record(ActiveDownload d, CoreWebView2DownloadOperation op)
    {
        var state = "interrupted";
        try
        {
            if (op.State == CoreWebView2DownloadState.Completed) state = "done";
            else if (op.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled) state = "canceled";
            else if (op.InterruptReason == CoreWebView2DownloadInterruptReason.UserShutdown) state = "canceled";
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
