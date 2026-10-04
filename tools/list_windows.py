"""列出某个 exe 进程的所有顶层窗口标题，排查「窗口没出来是哪一步断了」。

用法：python tools/list_windows.py [--nosplash --guard ...]
不带参数就默认用 --nosplash --guard。
"""

from __future__ import annotations

import ctypes
import os
import subprocess
import sys
import time
from ctypes import wintypes

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.path.join(ROOT, "dist", "TLBrowser.exe")
ARGS = sys.argv[1:] or ["--nosplash", "--guard"]

user32 = ctypes.windll.user32
EnumWindowsProc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

user32.GetWindowTextLengthW.restype = ctypes.c_int
user32.GetWindowThreadProcessId.restype = wintypes.DWORD


def titles_for(pid: int) -> list[tuple[int, str, bool]]:
    found: list[tuple[int, str, bool]] = []

    def cb(hwnd, _):
        p = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(p))
        if p.value == pid:
            n = user32.GetWindowTextLengthW(hwnd)
            buf = ctypes.create_unicode_buffer(n + 2)
            user32.GetWindowTextW(hwnd, buf, n + 2)
            found.append((hwnd, buf.value, bool(user32.IsWindowVisible(hwnd))))
        return True

    user32.EnumWindows(EnumWindowsProc(cb), 0)
    return found


def main() -> int:
    subprocess.run(["taskkill", "/F", "/IM", "TLBrowser.exe"],
                   capture_output=True, encoding="utf-8", errors="replace")
    time.sleep(1.0)

    proc = subprocess.Popen([EXE] + ARGS, cwd=os.path.dirname(EXE))
    print(f"pid={proc.pid} args={ARGS}")

    for t in (2, 5, 9, 14, 20):
        time.sleep(t - (0 if t == 2 else 0))
        alive = proc.poll() is None
        print(f"\n--- t≈{t}s  alive={alive} rc={proc.returncode} ---")
        for hwnd, title, vis in titles_for(proc.pid):
            print(f"   hwnd={hwnd} visible={vis} title={title!r}")
        if not alive:
            break

    subprocess.run(["taskkill", "/F", "/PID", str(proc.pid)],
                   capture_output=True, encoding="utf-8", errors="replace")
    return 0


if __name__ == "__main__":
    sys.exit(main())
