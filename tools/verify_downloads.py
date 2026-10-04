"""下载面板验收：
1. 往数据目录写一份假的 downloads.json（两条记录：已完成 + 已取消）
2. 启动 exe，等主窗口就绪
3. 发 Ctrl+J 打开下载面板
4. 断言「TL 浏览器 · 下载」窗口存在、进程存活、无 crash.log，并截图
"""
import ctypes
import json
import os
import subprocess
import time

from PIL import ImageGrab

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.path.join(ROOT, "dist", "TLBrowser.exe")
SHOTS = os.path.join(ROOT, "_shots")
APPDATA = os.path.join(os.environ["LOCALAPPDATA"], "TLSTUDIO", "TLBrowser")
DLJSON = os.path.join(APPDATA, "downloads.json")
CRASH = os.path.join(APPDATA, "crash.log")

user32 = ctypes.windll.user32
kernel32 = ctypes.windll.kernel32
MAIN_TITLE = "TL 浏览器"
DL_TITLE = "TL 浏览器 · 下载"

try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    try:
        user32.SetProcessDPIAware()
    except Exception:
        pass


class RECT(ctypes.Structure):
    _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                ("right", ctypes.c_long), ("bottom", ctypes.c_long)]


def kill_all():
    subprocess.run(["taskkill", "/F", "/IM", "TLBrowser.exe"],
                   capture_output=True, text=True,
                   encoding="utf-8", errors="replace")
    time.sleep(1.5)


def write_fake_history():
    os.makedirs(APPDATA, exist_ok=True)
    rows = [
        {
            "FileName": "TL验收-已完成样本.pdf",
            "Url": "https://example.com/files/TL验收-已完成样本.pdf",
            "FilePath": os.path.join(os.environ["USERPROFILE"], "Downloads", "TL验收-已完成样本.pdf"),
            "State": "已完成",
            "TotalBytes": 1048576,
            "When": "2026-10-04T12:00:00",
        },
        {
            "FileName": "TL验收-已取消样本.zip",
            "Url": "https://example.com/files/TL验收-已取消样本.zip",
            "FilePath": os.path.join(os.environ["USERPROFILE"], "Downloads", "TL验收-已取消样本.zip"),
            "State": "已取消",
            "TotalBytes": 204800,
            "When": "2026-10-04T11:30:00",
        },
    ]
    with open(DLJSON, "w", encoding="utf-8") as f:
        json.dump(rows, f, ensure_ascii=False)


def focus_main():
    hwnd = user32.FindWindowW(None, MAIN_TITLE)
    if not hwnd:
        return 0
    for _ in range(12):
        if user32.GetForegroundWindow() == hwnd:
            time.sleep(0.5)
            return hwnd
        fg = user32.GetForegroundWindow()
        fg_tid = user32.GetWindowThreadProcessId(fg, None)
        my_tid = kernel32.GetCurrentThreadId()
        attached = False
        if fg_tid and fg_tid != my_tid:
            attached = bool(user32.AttachThreadInput(my_tid, fg_tid, True))
        try:
            user32.ShowWindow(hwnd, 9)
            user32.BringWindowToTop(hwnd)
            user32.SetForegroundWindow(hwnd)
            user32.SetActiveWindow(hwnd)
        finally:
            if attached:
                user32.AttachThreadInput(my_tid, fg_tid, False)
        time.sleep(0.6)
    return hwnd


def send_ctrl_j():
    # keybd_event: keydown/up, KEYEVENTF_KEYUP=2
    user32.keybd_event(0x11, 0, 0, 0)      # Ctrl down
    user32.keybd_event(0x4A, 0, 0, 0)      # J down
    time.sleep(0.05)
    user32.keybd_event(0x4A, 0, 2, 0)      # J up
    user32.keybd_event(0x11, 0, 2, 0)      # Ctrl up


def grab(name):
    path = os.path.join(SHOTS, name + ".png")
    ImageGrab.grab(all_screens=True).save(path)
    return path


def main() -> int:
    os.makedirs(SHOTS, exist_ok=True)
    checks: list[tuple[bool, str]] = []

    kill_all()
    if os.path.exists(CRASH):
        os.remove(CRASH)
    write_fake_history()
    checks.append((os.path.exists(DLJSON), "伪造的 downloads.json 已写入数据目录"))

    proc = subprocess.Popen([EXE, "--nosplash"], cwd=os.path.dirname(EXE))
    time.sleep(10)
    hwnd = focus_main()
    checks.append((hwnd != 0, "主窗口已出现并拿到前台"))
    checks.append((proc.poll() is None, "进程存活"))

    send_ctrl_j()
    time.sleep(2.0)

    dl_hwnd = user32.FindWindowW(None, DL_TITLE)
    checks.append((dl_hwnd != 0, "Ctrl+J 打开了下载面板（找到「TL 浏览器 · 下载」窗口）"))

    if dl_hwnd:
        r = RECT()
        user32.GetWindowRect(dl_hwnd, ctypes.byref(r))
        user32.SetForegroundWindow(dl_hwnd)
        time.sleep(0.8)
        user32.GetWindowRect(dl_hwnd, ctypes.byref(r))
        img = ImageGrab.grab(bbox=(max(0, r.left), max(0, r.top), r.right, r.bottom),
                             all_screens=True)
        shot = os.path.join(SHOTS, "downloads.png")
        img.save(shot)
        checks.append((True, f"下载面板截图: {shot}"))
    else:
        checks.append((False, "没找到下载面板窗口，截全屏留证"))
        grab("downloads_fail")

    # 关掉面板再退出，保持环境干净
    if dl_hwnd:
        user32.PostMessageW(dl_hwnd, 0x0010, 0, 0)   # WM_CLOSE
        time.sleep(0.5)
    kill_all()

    if os.path.exists(CRASH):
        checks.append((False, "出现 crash.log: " +
                       open(CRASH, encoding="utf-8", errors="ignore").read()[:800]))
    else:
        checks.append((True, "无 crash.log"))

    print("TL 浏览器 · 下载面板验收")
    print("=" * 60)
    bad = 0
    for ok, text in checks:
        print(f"  [{'PASS' if ok else 'FAIL'}] {text}")
        if not ok:
            bad += 1
    print("=" * 60)
    print(f"断言 {len(checks)} 条通过，{bad} 条失败")
    return bad


if __name__ == "__main__":
    import sys
    sys.exit(main())
