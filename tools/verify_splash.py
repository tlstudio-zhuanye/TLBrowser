"""启动动画验收：等动画窗口真的出现再对时，按动画时间轴逐帧抓图，并测启动耗时。

用法: python tools/verify_splash.py
"""
import ctypes
import os
import subprocess
import sys
import time

from PIL import Image, ImageGrab

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.path.join(ROOT, "dist", "TLBrowser.exe")
SHOTS = os.path.join(ROOT, "_shots", "splash")

TITLE = "TL 浏览器"
SPLASH_TITLE = "TL 浏览器 · 启动中"
CROP_W, CROP_H = 860, 520

# 动画时间轴（秒，相对动画窗口出现）
MARKS = [
    ("01_0.25s_辉光渐起", 0.25),
    ("02_0.60s_logo浮现", 0.60),
    ("03_0.95s_完全显现", 0.95),
    ("04_1.15s_蓄力", 1.15),
    ("05_1.28s_投掷闪光", 1.28),
    ("06_1.55s_被丢进去", 1.55),
    ("07_1.80s_深渊深处", 1.80),
    ("08_2.15s_收尾", 2.15),
]

user32 = ctypes.windll.user32
try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    pass


def kill_all():
    subprocess.run(["taskkill", "/F", "/IM", "TLBrowser.exe"],
                   capture_output=True, text=True, encoding="utf-8", errors="replace")
    time.sleep(1.5)


class RECT(ctypes.Structure):
    _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                ("right", ctypes.c_long), ("bottom", ctypes.c_long)]


def snap(name):
    os.makedirs(SHOTS, exist_ok=True)
    # 只抓动画窗那块（它 TopMost，一定在最上面），比整屏抓快得多，
    # 抓得慢会把后面几帧的时间点整体拖后
    hwnd = user32.FindWindowW(None, SPLASH_TITLE) or user32.FindWindowW(None, TITLE)
    box = None
    if hwnd:
        r = RECT()
        user32.GetWindowRect(hwnd, ctypes.byref(r))
        if r.right > r.left and r.bottom > r.top:
            box = (max(0, r.left - 24), max(0, r.top - 24), r.right + 24, r.bottom + 24)

    img = ImageGrab.grab(bbox=box, all_screens=True) if box else ImageGrab.grab(all_screens=True)
    path = os.path.join(SHOTS, name + ".png")
    img.save(path)
    return path


def wait_for(title, timeout=20.0, interval=0.02):
    deadline = time.time() + timeout
    while time.time() < deadline:
        hwnd = user32.FindWindowW(None, title)
        if hwnd:
            return hwnd, time.time()
        time.sleep(interval)
    return 0, time.time()


def main():
    if not os.path.exists(EXE):
        print("exe 不存在:", EXE)
        return 1

    kill_all()
    launch_at = time.time()
    proc = subprocess.Popen([EXE], cwd=os.path.dirname(EXE))

    splash_hwnd, splash_at = wait_for(SPLASH_TITLE)
    t_splash = splash_at - launch_at
    lines = [f"启动到动画出现: {t_splash:.2f}s" + ("" if splash_hwnd else "  ← 没等到动画窗口!")]

    if not splash_hwnd:
        kill_all()
        print("\n".join(lines))
        return 1

    for name, at in MARKS:
        wait = at - (time.time() - splash_at)
        if wait > 0:
            time.sleep(wait)
        path = snap(name)
        lines.append(f"{name}: {os.path.basename(path)}")

    # 动画窗口应该在 ~2.6s 内自己消失，主窗口接上
    main_hwnd, main_at = wait_for(TITLE, timeout=8.0, interval=0.05)
    t_main = main_at - launch_at
    gone = user32.FindWindowW(None, SPLASH_TITLE) == 0
    lines.append(f"主窗口出现: {t_main:.2f}s (hwnd={main_hwnd})")
    lines.append(f"动画窗口已关闭: {gone}")
    lines.append(f"进程存活: {proc.poll() is None}")

    time.sleep(1.2)
    lines.append("主窗口图: " + os.path.basename(snap("09_主窗口")))

    kill_all()
    print("\n".join(lines))
    with open(os.path.join(ROOT, "_verify_splash.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
    return 0


if __name__ == "__main__":
    sys.exit(main())
