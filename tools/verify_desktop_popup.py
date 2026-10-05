"""实测桌面那份 exe：双击后会不会弹出更新公告，并读出它自报的当前版本。

只读验证 —— 不覆盖、不修改桌面文件，也**不会去关用户正在用的浏览器窗口**。

单实例陷阱（这个脚本第一版就栽在这）：
    程序带单实例互斥。如果用户此刻已经开着浏览器，我们再启动一个副本，
    新进程会在 `TryActivateExisting()` 里发现已有窗口、把旧窗口提到前台然后**直接退出** ——
    它根本不会走到「检查更新」那一步，于是脚本报「没找到弹窗」，看起来像功能坏了，其实不是。
    所以：启动前先探测有没有活着的实例，有就如实报告并退出，别硬测。
"""
import argparse
import ctypes
import os
import re
import subprocess
import sys
import time

from PIL import ImageGrab

DESKTOP = os.path.join(os.environ["USERPROFILE"], "Desktop", "TL 浏览器.exe")
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHOTS = os.path.join(ROOT, "_shots")
LOGS = os.path.join(ROOT, "logs")
os.makedirs(LOGS, exist_ok=True)
REPORT = os.path.join(LOGS, "desktop_popup.txt")

# 桌面那份叫「TL 浏览器.exe」，进程名是「TL 浏览器」；
# dist 那份叫「TLBrowser.exe」，进程名是「TLBrowser」。两个都要认，
# 但**清理时只认自己启动的那个 PID**，绝不按镜像名批量杀 —— 那会把用户开着的窗口一起干掉。
PROC_NAMES = ["TL 浏览器", "TLBrowser"]

user32 = ctypes.windll.user32
try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    pass


class RECT(ctypes.Structure):
    _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                ("right", ctypes.c_long), ("bottom", ctypes.c_long)]


WNDENUMPROC = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)


def texts_of(hwnd):
    """窗口自己 + 所有子控件的文字（MessageBox 正文在 Static 子控件里）。"""
    out = []

    def cb(h, lp):
        buf = ctypes.create_unicode_buffer(2048)
        user32.GetWindowTextW(h, buf, 2048)
        if buf.value.strip():
            out.append(buf.value.strip())
        return True

    user32.EnumChildWindows(hwnd, WNDENUMPROC(cb), 0)
    return out


def enum_windows():
    found = []

    def cb(h, lp):
        buf = ctypes.create_unicode_buffer(512)
        user32.GetWindowTextW(h, buf, 512)
        if buf.value:
            found.append((h, buf.value))
        return True

    user32.EnumWindows(WNDENUMPROC(cb), 0)
    return found


def live_instances():
    """返回 [(pid, path)]，只看本程序自己，不看 WebView2 子进程。"""
    import json
    import subprocess as sp
    # 用 CIM 拿路径（tasklist 不给路径）。必须强制 UTF-8 输出，
    # 否则中文路径会按 ANSI 解码成乱码（`TL 浏览器.exe` -> `TL ?????.exe`）。
    ps = ("$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8; "
          "Get-CimInstance Win32_Process "
          "-Filter \"Name='TL 浏览器.exe' or Name='TLBrowser.exe'\" | "
          "Select-Object ProcessId,ExecutablePath | ConvertTo-Json -Compress")
    try:
        p = sp.run(["powershell", "-NoProfile", "-Command", ps],
                   capture_output=True, timeout=60)
        raw = p.stdout.decode("utf-8", "replace").strip()
        if not raw:
            return []
        data = json.loads(raw)
        if isinstance(data, dict):
            data = [data]
        return [(int(d["ProcessId"]), d.get("ExecutablePath") or "") for d in data]
    except Exception as e:
        print("  (探测已有实例失败：%s)" % e)
        return []


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--keep-open", action="store_true",
                    help="测完不关掉自己启动的那个实例（默认关）")
    a = ap.parse_args()

    log = []
    log.append("桌面 exe: " + DESKTOP)
    log.append("存在: %s" % os.path.exists(DESKTOP))
    if not os.path.exists(DESKTOP):
        print("\n".join(log))
        return 1

    st = os.stat(DESKTOP)
    log.append("大小: %d  修改时间: %s" % (
        st.st_size, time.strftime("%m-%d %H:%M:%S", time.localtime(st.st_mtime))))

    # —— 启动前先看有没有活着的实例。有就别测了，否则结论一定是错的。 ——
    before = live_instances()
    if before:
        log.append("")
        log.append("!! 检测到已有 %d 个实例在运行，本次不测：" % len(before))
        for pid, path in before:
            log.append("     pid=%d  %s" % (pid, path))
        log.append("   原因：程序带单实例互斥，新副本会直接激活已有窗口后退出，")
        log.append("         走不到「检查更新」，硬测只会得到假的失败结论。")
        log.append("   想测的话：先手动关掉那个窗口，再重跑本脚本。")
        out = "\n".join(log)
        print(out)
        with open(REPORT, "w", encoding="utf-8") as f:
            f.write(out)
        return 3

    os.makedirs(SHOTS, exist_ok=True)
    p = subprocess.Popen([DESKTOP, "--nosplash"])
    log.append("已启动 pid=%d，等 12 秒让公告弹出来…" % p.pid)
    time.sleep(12)

    if p.poll() is not None:
        log.append("!! 进程已退出（rc=%s）—— 多半又是抢到了别的实例的互斥" % p.returncode)

    dlg = 0
    log.append("--- 可见窗口 ---")
    for h, t in enum_windows():
        if "TL" in t or "版本" in t or "version" in t.lower():
            log.append("   %s  %s" % (h, t))
            if "新版本" in t or "New version" in t:
                dlg = h

    shot = os.path.join(SHOTS, "desktop_popup.png")
    if dlg:
        txt = texts_of(dlg)
        log.append("--- 弹窗文字 ---")
        for s in txt:
            log.append("   | " + s)
        m = re.search(r"v?(\d+\.\d+\.\d+)", " ".join(txt))
        r = RECT()
        user32.GetWindowRect(dlg, ctypes.byref(r))
        ImageGrab.grab(bbox=(r.left, r.top, r.right, r.bottom)).save(shot)
        log.append("弹窗截图: %s" % shot)
        log.append("公告版本号: %s" % (m.group(0) if m else "?"))
    else:
        log.append("!! 没找到「发现新版本」弹窗")
        ImageGrab.grab().save(shot)
        log.append("全屏截图: %s" % shot)

    # 只关自己启动的那个，不按镜像名批量杀
    if not a.keep_open:
        subprocess.run(["taskkill", "/F", "/PID", str(p.pid)], capture_output=True)
        time.sleep(1.0)
        log.append("已关闭本次启动的 pid=%d" % p.pid)

    out = "\n".join(log)
    print(out)
    with open(REPORT, "w", encoding="utf-8") as f:
        f.write(out)
    return 0 if dlg else 1


if __name__ == "__main__":
    sys.exit(main())
