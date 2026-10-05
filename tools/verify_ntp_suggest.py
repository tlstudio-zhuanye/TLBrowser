"""实测新标签页的联想下拉：启动 → 点搜索框 → 打字 → 截图。

主页搜索框虽然会自动 focus，但 WebView2 控件本身不一定持有键盘焦点，
直接发键盘事件会落空 —— 所以先真实点一下输入框。
"""
import ctypes
import os
import subprocess
import sys
import time

from PIL import ImageGrab

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.path.join(ROOT, "dist", "TLBrowser.exe")
SHOTS = os.path.join(ROOT, "_shots")

user32 = ctypes.windll.user32
try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    pass


class RECT(ctypes.Structure):
    _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                ("right", ctypes.c_long), ("bottom", ctypes.c_long)]


class _KI(ctypes.Structure):
    _fields_ = [("wVk", ctypes.c_ushort), ("wScan", ctypes.c_ushort),
                ("dwFlags", ctypes.c_ulong), ("time", ctypes.c_ulong),
                ("dwExtraInfo", ctypes.c_void_p)]


class _MOUSE(ctypes.Structure):
    _fields_ = [("dx", ctypes.c_long), ("dy", ctypes.c_long),
                ("mouseData", ctypes.c_ulong), ("dwFlags", ctypes.c_ulong),
                ("time", ctypes.c_ulong), ("dwExtraInfo", ctypes.c_void_p)]


class _INPUT(ctypes.Structure):
    class _U(ctypes.Union):
        _fields_ = [("mi", _MOUSE), ("ki", _KI)]
    _anonymous_ = ("u",)
    _fields_ = [("type", ctypes.c_ulong), ("u", _U)]


INPUT_MOUSE, INPUT_KEYBOARD = 0, 1
KEYUP, UNICODE = 0x0002, 0x0004
MOUSEEVENTF_MOVE, MOUSEEVENTF_ABSOLUTE = 0x0001, 0x8000


def _send(arr):
    user32.SendInput(len(arr), arr, ctypes.sizeof(_INPUT))


def send_text(s):
    for ch in s:
        code = ord(ch)
        d = (_INPUT * 1)(); d[0].type = INPUT_KEYBOARD
        d[0].ki = _KI(0, code, UNICODE, 0, None)
        _send(d)
        u = (_INPUT * 1)(); u[0].type = INPUT_KEYBOARD
        u[0].ki = _KI(0, code, UNICODE | KEYUP, 0, None)
        _send(u)
        time.sleep(0.05)


def click(x, y):
    """移动到绝对坐标并左键单击。用 SetCursorPos + mouse_event，
    比 SendInput 的 MOUSEINPUT 结构体省事（不用搭 union）。"""
    user32.SetCursorPos(int(x), int(y))
    time.sleep(0.06)
    user32.mouse_event(0x0002, 0, 0, 0, 0)   # LEFTDOWN
    time.sleep(0.05)
    user32.mouse_event(0x0004, 0, 0, 0, 0)   # LEFTUP


def find_window():
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
        return None
    global _hwnd
    _hwnd = hit[0]
    # 提到前台：屏幕上常有别的弹窗（音乐播放器、聊天窗）盖在浏览器中间，
    # 不提的话点击会落到别的窗口上，测试结果完全是假的。
    user32.ShowWindow(_hwnd, 9)            # SW_RESTORE
    user32.SetForegroundWindow(_hwnd)
    time.sleep(0.6)
    r = RECT()
    user32.GetWindowRect(_hwnd, ctypes.byref(r))
    return r


def shoot(name):
    p = os.path.join(SHOTS, name)
    ImageGrab.grab().save(p)
    return p


_hwnd = None


def shoot_window(name, hwnd=None):
    """只截浏览器窗口那块区域。全屏截图会把用户别的窗口一起拍进去，不留档。"""
    if hwnd is None:
        hwnd = _hwnd
    if hwnd is None:
        return ""
    r = RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(r))
    p = os.path.join(SHOTS, name)
    ImageGrab.grab(bbox=(r.left, r.top, r.right, r.bottom)).save(p)
    return p


def main():
    subprocess.run(["taskkill", "/F", "/IM", "TLBrowser.exe"], capture_output=True)
    time.sleep(1.2)
    os.makedirs(SHOTS, exist_ok=True)

    p = subprocess.Popen([EXE, "--nosplash"])
    time.sleep(6)

    r = find_window()
    if r is None:
        print("没找到浏览器窗口")
        subprocess.run(["taskkill", "/F", "/PID", str(p.pid)], capture_output=True)
        return 1
    print("窗口", r.left, r.top, r.right, r.bottom)

    # 搜索框在窗口里的相对位置（按 1456x939 的窗口量出来的）
    rel_x, rel_y = int((r.right - r.left) * 0.5), int((r.bottom - r.top) * 0.538)
    click(r.left + rel_x, r.top + rel_y)
    time.sleep(0.8)

    send_text("tl")
    time.sleep(1.5)
    a = shoot_window("ntp_suggest.png")
    print("输入 tl 后:", a)

    send_text("studio")
    time.sleep(1.5)
    b = shoot_window("ntp_suggest.png")
    print("输入 tlstudio 后:", b)

    subprocess.run(["taskkill", "/F", "/PID", str(p.pid)], capture_output=True)
    time.sleep(1.0)
    return 0


if __name__ == "__main__":
    sys.exit(main())
