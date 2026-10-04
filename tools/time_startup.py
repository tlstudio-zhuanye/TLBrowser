"""纯计时：冷启动多久出画面、动画窗多久关掉、主窗多久可见。不截图，避免截图本身拖慢时间线。

用法: python tools/time_startup.py [次数]
"""
import ctypes
import os
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.environ.get("TLBROWSER_EXE") or os.path.join(ROOT, "dist", "TLBrowser.exe")
TITLE = "TL 浏览器"
SPLASH_TITLE = "TL 浏览器 · 启动中"

user32 = ctypes.windll.user32


def kill_all():
    subprocess.run(["taskkill", "/F", "/IM", "TLBrowser.exe"],
                   capture_output=True, text=True, encoding="utf-8", errors="replace")
    time.sleep(1.2)


def wait_for(title, from_time, timeout=25.0):
    deadline = from_time + timeout
    while time.time() < deadline:
        hwnd = user32.FindWindowW(None, title)
        if hwnd:
            return time.time() - from_time, hwnd
        time.sleep(0.015)
    return None, 0


def once(args, label):
    kill_all()
    t0 = time.time()
    proc = subprocess.Popen([EXE] + args, cwd=os.path.dirname(EXE))

    t_splash, _ = wait_for(SPLASH_TITLE, t0) if "--nosplash" not in args else (0.0, 0)
    t_main, _ = wait_for(TITLE, t0)

    # 动画窗什么时候消失
    t_gone = None
    deadline = t0 + 25
    while time.time() < deadline:
        if user32.FindWindowW(None, SPLASH_TITLE) == 0:
            t_gone = time.time() - t0
            break
        time.sleep(0.03)

    alive = proc.poll() is None
    kill_all()

    return {
        "label": label,
        "splash": t_splash,
        "main": t_main,
        "splash_gone": t_gone,
        "alive": alive,
        "size_mb": round(os.path.getsize(EXE) / 1024 / 1024, 1),
    }


def main():
    rounds = int(sys.argv[1]) if len(sys.argv) > 1 else 2
    out = [f"exe: {EXE}", f"体积: {round(os.path.getsize(EXE)/1024/1024, 1)} MB", ""]
    for i in range(rounds):
        r = once([], f"带启动动画 #{i+1}")
        out.append(f"{r['label']}: 动画出现 {r['splash']:.2f}s | 动画关闭 {r['splash_gone']:.2f}s | "
                   f"主窗句柄就绪 {r['main']:.2f}s | 存活 {r['alive']}")
    for i in range(rounds):
        r = once(["--nosplash"], f"跳过动画 #{i+1}")
        out.append(f"{r['label']}: 主窗出现 {r['main']:.2f}s | 存活 {r['alive']}")

    print("\n".join(out))
    with open(os.path.join(ROOT, "_time_startup.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
