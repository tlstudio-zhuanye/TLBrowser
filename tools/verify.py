"""TL 浏览器的真机验收：启动 exe、等页面加载、截窗口、记录进程存活与崩溃日志。

用法:
    python tools/verify.py            # 跑全部用例
    python tools/verify.py home       # 只跑某一个
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
CRASH = os.path.join(os.environ["LOCALAPPDATA"], "TLSTUDIO", "TLBrowser", "crash.log")

user32 = ctypes.windll.user32
TITLE = "TL 浏览器"

# 让 Python 也按物理像素取窗口坐标，避免缩放对不上
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


def kill_all() -> None:
    subprocess.run(["taskkill", "/F", "/IM", "TLBrowser.exe"],
                   capture_output=True, text=True,
                   encoding="utf-8", errors="replace")
    time.sleep(1.5)


def launch(args: list[str]) -> subprocess.Popen:
    return subprocess.Popen([EXE] + args, cwd=os.path.dirname(EXE))


kernel32 = ctypes.windll.kernel32


def focus_window() -> int:
    """把窗口抢到最前。普通 SetForegroundWindow 会被前台锁拦，得 AttachThreadInput 借一下。"""
    hwnd = user32.FindWindowW(None, TITLE)
    if not hwnd:
        return 0

    for _ in range(12):
        if user32.GetForegroundWindow() == hwnd:
            time.sleep(0.6)
            return hwnd

        fg = user32.GetForegroundWindow()
        fg_tid = user32.GetWindowThreadProcessId(fg, None)
        my_tid = kernel32.GetCurrentThreadId()
        attached = False
        if fg_tid and fg_tid != my_tid:
            attached = bool(user32.AttachThreadInput(my_tid, fg_tid, True))
        try:
            user32.ShowWindow(hwnd, 9)          # SW_RESTORE
            user32.BringWindowToTop(hwnd)
            user32.SetForegroundWindow(hwnd)
            user32.SetActiveWindow(hwnd)
        finally:
            if attached:
                user32.AttachThreadInput(my_tid, fg_tid, False)
        time.sleep(0.7)

    return hwnd


def grab_window(name: str) -> str:
    hwnd = focus_window()
    path = os.path.join(SHOTS, name + ".png")
    if not hwnd:
        ImageGrab.grab().save(path)
        return path

    foreground = user32.GetForegroundWindow() == hwnd
    r = RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(r))
    box = (max(0, r.left), max(0, r.top), r.right, r.bottom)
    img = ImageGrab.grab(bbox=box, all_screens=True)
    img.save(path)

    note = "" if foreground else "  ← 警告：此窗口没抢到前台，截图可能被别的窗口盖住"
    with open(os.path.join(ROOT, "_verify_focus.txt"), "a", encoding="utf-8") as f:
        f.write(f"{name}: hwnd={hwnd} foreground={foreground} box={box}{note}\n")
    return path + note


CASES = {
    "home": ([], 15),
    "tl": (["https://tlstudio.cn"], 20),
    "doubler": (["https://tldoublerstudio.cn"], 20),
    "dual": (["--dual"], 24),
}


def run(names: list[str], extra: list[str]) -> int:
    os.makedirs(SHOTS, exist_ok=True)
    if os.path.exists(CRASH):
        os.remove(CRASH)
    focus_log = os.path.join(ROOT, "_verify_focus.txt")
    if os.path.exists(focus_log):
        os.remove(focus_log)

    lines: list[str] = []
    ok = True

    for name in names:
        args, wait = CASES[name]
        args = list(args) + extra
        kill_all()
        proc = launch(args)
        lines.append(f"[{name}] pid={proc.pid} args={args}")

        alive_at = None
        deadline = time.time() + wait
        while time.time() < deadline:
            if proc.poll() is not None:
                alive_at = f"提前退出 rc={proc.returncode}"
                break
            time.sleep(1)
        if alive_at is None:
            alive_at = "存活"

        shot = grab_window(f"{name}")
        lines.append(f"[{name}] 进程: {alive_at} | 截图: {shot}")

        if proc.poll() is not None:
            ok = False

    kill_all()

    if os.path.exists(CRASH):
        ok = False
        lines.append("!! 出现 crash.log:")
        lines.append(open(CRASH, encoding="utf-8", errors="ignore").read()[:2000])
    else:
        lines.append("无 crash.log")

    report = os.path.join(ROOT, "_verify_result.txt")
    with open(report, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
    print("\n".join(lines))
    return 0 if ok else 1


if __name__ == "__main__":
    argv = sys.argv[1:]
    # 以 - 开头的是给 exe 的附加参数（比如 --nosplash），其余是用例名
    extra = [a for a in argv if a.startswith("-")]
    wanted = [a for a in argv if not a.startswith("-")] or list(CASES)
    sys.exit(run(wanted, extra))
