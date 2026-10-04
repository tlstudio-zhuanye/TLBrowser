"""主页守护的界面截图：状态条上的盾牌，以及守护面板本身。

用法：python tools/shoot_guard.py

为了让截图里的盾牌是「已拦下」而不是「正常」，
启动时会故意带一个外部网址参数（就是真实劫持的手法），
这样截出来的就是它真的在工作时的样子。

注意：找窗口用「按进程枚举 + 标题包含」而不是 FindWindowW，
后者要求标题逐字符完全一致，且默认返回值是 32 位，容易踩坑。
"""

from __future__ import annotations

import ctypes
import os
import subprocess
import sys
import time
from ctypes import wintypes

from PIL import ImageGrab

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.path.join(ROOT, "dist", "TLBrowser.exe")
SHOTS = os.path.join(ROOT, "_shots")
MAIN_TITLE = "TL 浏览器"
GUARD_TITLE = "主页守护"
HIJACK = "http://hao.360.cn"

user32 = ctypes.windll.user32
user32.GetWindowTextLengthW.restype = ctypes.c_int
user32.GetWindowTextW.restype = ctypes.c_int
user32.GetWindowThreadProcessId.restype = wintypes.DWORD
user32.IsWindowVisible.restype = wintypes.BOOL

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


EnumProc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)


def windows_of(pid: int) -> list[tuple[int, str, bool]]:
    out: list[tuple[int, str, bool]] = []

    def cb(hwnd, _):
        p = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(p))
        if p.value == pid:
            n = user32.GetWindowTextLengthW(hwnd)
            buf = ctypes.create_unicode_buffer(n + 2)
            user32.GetWindowTextW(hwnd, buf, n + 2)
            out.append((hwnd, buf.value, bool(user32.IsWindowVisible(hwnd))))
        return True

    user32.EnumWindows(EnumProc(cb), 0)
    return out


def wait_window(pid: int, needle: str, timeout: float = 30.0) -> int:
    deadline = time.time() + timeout
    while time.time() < deadline:
        for hwnd, title, vis in windows_of(pid):
            if vis and needle in title:
                return hwnd
        time.sleep(0.4)
    return 0


def kill_all(timeout: float = 12.0) -> None:
    """杀掉所有实例，并等到真的都退干净。

    不等干净会出鬼：上一个实例还活着就持有单实例互斥体，
    下一次启动会被它「激活已有窗口」然后直接 return，
    新窗口永远不出来——看起来像功能坏了，其实是测试自己没清干净。
    """
    def remaining() -> list[str]:
        out = subprocess.run(
            ["powershell", "-NoProfile", "-Command",
             "@(Get-Process TLBrowser -ErrorAction SilentlyContinue).Count"],
            capture_output=True, text=True, encoding="utf-8", errors="replace").stdout.strip()
        return [] if out in ("", "0") else [out]

    subprocess.run(["taskkill", "/F", "/IM", "TLBrowser.exe"],
                   capture_output=True, encoding="utf-8", errors="replace")
    deadline = time.time() + timeout
    while time.time() < deadline:
        if not remaining():
            time.sleep(0.6)          # 再等一拍，让互斥体句柄真正释放
            return
        subprocess.run(["taskkill", "/F", "/IM", "TLBrowser.exe"],
                       capture_output=True, encoding="utf-8", errors="replace")
        time.sleep(0.7)
    print("  !! 警告：还有 TLBrowser 进程没退掉")


def focus(hwnd: int) -> bool:
    """AttachThreadInput 借线程抢前台，普通 SetForegroundWindow 会被前台锁挡住。"""
    for _ in range(12):
        if user32.GetForegroundWindow() == hwnd:
            time.sleep(0.5)
            return True
        fg = user32.GetForegroundWindow()
        fg_tid = user32.GetWindowThreadProcessId(fg, None)
        my_tid = ctypes.windll.kernel32.GetCurrentThreadId()
        attached = bool(user32.AttachThreadInput(my_tid, fg_tid, True)) if fg_tid and fg_tid != my_tid else False
        try:
            user32.ShowWindow(hwnd, 9)
            user32.BringWindowToTop(hwnd)
            user32.SetForegroundWindow(hwnd)
            user32.SetActiveWindow(hwnd)
        finally:
            if attached:
                user32.AttachThreadInput(my_tid, fg_tid, False)
        time.sleep(0.6)
    return user32.GetForegroundWindow() == hwnd


def grab(hwnd: int, name: str, crop_bottom: int | None = None) -> str:
    r = RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(r))
    box = (max(0, r.left), max(0, r.top), r.right, r.bottom)
    img = ImageGrab.grab(bbox=box, all_screens=True)
    if crop_bottom:
        img = img.crop((0, max(0, img.height - crop_bottom), img.width, img.height))
    path = os.path.join(SHOTS, name + ".png")
    img.save(path)
    print(f"  {name}: hwnd={hwnd} box={box} foreground={user32.GetForegroundWindow() == hwnd}")
    return path


def main() -> int:
    os.makedirs(SHOTS, exist_ok=True)
    if not os.path.exists(EXE):
        print("找不到 exe，先跑 tools/build.ps1")
        return 2

    # 1) 主窗口：故意带劫持参数，让盾牌显示「已拦下」
    kill_all()
    p = subprocess.Popen([EXE, "--nosplash", HIJACK], cwd=os.path.dirname(EXE))
    hwnd = wait_window(p.pid, MAIN_TITLE)
    if not hwnd:
        print("  !! 主窗口没出来")
        return 1
    time.sleep(6)
    focus(hwnd)
    grab(hwnd, "guard_shield_full")
    grab(hwnd, "guard_statusbar", crop_bottom=60)

    # 2) 守护面板
    kill_all()
    p = subprocess.Popen([EXE, "--nosplash", "--guard", HIJACK], cwd=os.path.dirname(EXE))
    hwnd = wait_window(p.pid, GUARD_TITLE)
    if not hwnd:
        print("  !! 守护面板没出来，诊断信息：")
        print(f"     pid={p.pid} alive={p.poll() is None} rc={p.returncode}")
        print(f"     本进程窗口: {windows_of(p.pid)}")
        others = subprocess.run(
            ["powershell", "-NoProfile", "-Command",
             "Get-Process TLBrowser -ErrorAction SilentlyContinue | "
             "ForEach-Object { \"$($_.Id) $($_.MainWindowTitle)\" }"],
            capture_output=True, text=True, encoding="utf-8", errors="replace")
        print(f"     其它 TLBrowser 进程: {others.stdout.strip()!r}")
        return 1
    time.sleep(1.5)
    focus(hwnd)
    grab(hwnd, "guard_dialog")

    kill_all()
    print("完成")
    return 0


if __name__ == "__main__":
    sys.exit(main())
