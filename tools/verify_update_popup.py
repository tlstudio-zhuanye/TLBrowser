"""临时脚本：拿一个「比线上低一版」的 exe 跑起来，截图看更新公告弹窗。

只给验收用，不进 dist、不覆盖桌面。
"""
import ctypes
import os
import subprocess
import sys
import time

from PIL import ImageGrab

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
# 普通 dotnet build（不带 -r）落在 net9.0-windows；publish 才落 win-x64
_CANDIDATES = [
    ("src", "bin", "Release", "net9.0-windows", "TLBrowser.exe"),
    ("src", "bin", "Release", "net9.0-windows", "win-x64", "TLBrowser.exe"),
]
EXE = ""
for _parts in _CANDIDATES:
    _p = os.path.join(ROOT, *_parts)
    if os.path.exists(_p):
        EXE = _p
        break
SHOTS = os.path.join(ROOT, "_shots")

user32 = ctypes.windll.user32
TITLE = "TL 浏览器"

try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    pass


class RECT(ctypes.Structure):
    _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                ("right", ctypes.c_long), ("bottom", ctypes.c_long)]


def kill_all():
    subprocess.run(["taskkill", "/F", "/IM", "TLBrowser.exe"], capture_output=True)
    time.sleep(1.2)


def main():
    if not os.path.exists(EXE):
        print("找不到:", EXE)
        return 1

    kill_all()
    os.makedirs(SHOTS, exist_ok=True)

    # 公告是启动后异步查的，要等一会儿
    p = subprocess.Popen([EXE, "--nosplash"])
    time.sleep(9)

    hwnd = user32.FindWindowW(None, TITLE)
    print("main hwnd =", hwnd, "alive =", p.poll() is None)

    # 公告是模态对话框，标题是「TL浏览器 · 发现新版本」
    dlg = 0
    data = []

    @ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
    def enum_proc(h, lp):
        buf = ctypes.create_unicode_buffer(512)
        user32.GetWindowTextW(h, buf, 512)
        if buf.value:
            data.append((h, buf.value))
        return True

    user32.EnumWindows(enum_proc, 0)
    print("窗口列表:")
    for h, t in data:
        if "TL" in t or "版本" in t:
            print("   ", h, t)
            if "新版本" in t:
                dlg = h

    out = os.path.join(SHOTS, "update_popup.png")
    if dlg:
        r = RECT()
        user32.GetWindowRect(dlg, ctypes.byref(r))
        img = ImageGrab.grab(bbox=(r.left, r.top, r.right, r.bottom))
        img.save(out)
        print("弹窗截图:", out, img.size)
    else:
        ImageGrab.grab().save(out)
        print("没找到弹窗，截全屏:", out)

    subprocess.run(["taskkill", "/F", "/PID", str(p.pid)], capture_output=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
