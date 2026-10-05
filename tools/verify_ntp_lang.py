"""中英两种语言各渲染一次新标签页，确认主页跟着界面语言走。

会临时改 lang.txt，结束后**务必还原**。
"""
import os
import shutil
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.path.join(ROOT, "dist", "TLBrowser.exe")
SHOTS = os.path.join(ROOT, "_shots")
LANG = os.path.join(os.environ["LOCALAPPDATA"], "TLSTUDIO", "TLBrowser", "lang.txt")

user32 = ctypes = None
import ctypes  # noqa: E402
user32 = ctypes.windll.user32
try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    pass


def grab_tag(tag):
    """只截浏览器窗口本身。先把它提到前台 —— WorkBuddy 之类的窗口会盖在上面。"""
    WND = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
    hit = []

    def cb(h, lp):
        b = ctypes.create_unicode_buffer(512)
        user32.GetWindowTextW(h, b, 512)
        if b.value.strip() == "TL 浏览器":
            hit.append(h)
        return True

    user32.EnumWindows(WND(cb), 0)
    if not hit:
        return grab_full(tag)
    hwnd = hit[0]
    # ImageGrab 抓的是"屏幕上那块区域"，不是某个窗口 —— 有别的窗口盖着就拍到别的。
    # SetForegroundWindow 对后台进程常被系统拒绝，所以先用 TOPMOST 把浏览器顶到最上层。
    HWND_TOPMOST, SWP_NOMOVE, SWP_NOSIZE = -1, 0x0001, 0x0002
    user32.SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE)
    user32.ShowWindow(hwnd, 9)
    user32.SetForegroundWindow(hwnd)
    time.sleep(0.9)
    r = RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(r))
    p = os.path.join(SHOTS, f"ntp_{tag}.png")
    ImageGrab.grab(bbox=(r.left, r.top, r.right, r.bottom)).save(p)
    return p


def grab_full(tag):
    p = os.path.join(SHOTS, f"ntp_{tag}.png")
    ImageGrab.grab(bbox=None).save(p)
    return p


class RECT(ctypes.Structure):
    _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                ("right", ctypes.c_long), ("bottom", ctypes.c_long)]


from PIL import ImageGrab  # noqa: E402


def run(tag):
    subprocess.run(["taskkill", "/F", "/IM", "TLBrowser.exe"], capture_output=True)
    time.sleep(1.2)
    p = subprocess.Popen([EXE, "--nosplash"])
    time.sleep(6)
    shot = grab_tag(tag)
    alive = p.poll() is None
    subprocess.run(["taskkill", "/F", "/PID", str(p.pid)], capture_output=True)
    time.sleep(1.0)
    print(f"[{tag}] 存活={alive}  截图={shot}")
    return alive


def main():
    bak = LANG + ".bak"
    orig = None
    if os.path.exists(LANG):
        orig = open(LANG, encoding="utf-8-sig").read()
        shutil.copy2(LANG, bak)

    ok = True
    try:
        with open(LANG, "w", encoding="utf-8") as f:
            f.write("zh-CN")
        ok &= run("zh")
        with open(LANG, "w", encoding="utf-8") as f:
            f.write("en-US")
        ok &= run("en")
    finally:
        # 还原用户的语言设置
        if orig is not None:
            with open(LANG, "w", encoding="utf-8") as f:
                f.write(orig)
        elif os.path.exists(LANG):
            os.remove(LANG)
        if os.path.exists(bak):
            os.remove(bak)

    print("当前 lang.txt =", open(LANG, encoding="utf-8-sig").read() if os.path.exists(LANG) else "(无)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
