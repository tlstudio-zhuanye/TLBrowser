"""主页守护验收：真的去改一遍，看它能不能自己发现并还原。

三个场景，都跑真的 exe、读真的文件、验真的结果：

  A. 启动参数注入   —— 模拟「360 往快捷方式里塞网址」的核心手法，
                       直接给 exe 传一个外部网址，看它会不会照单打开。
  B. 快捷方式被改   —— 把桌面快捷方式的参数改成外部网址，启动后看有没有被清掉。
                       会先按字节备份原文件，跑完自动还原。
  C. 首页文件被改   —— 往 home\\index.html 里塞一段跳转到其它站的脚本，
                       启动后看有没有被还原成内嵌版本。

用法：python verify_guard.py
"""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EXE = ROOT / "dist" / "TLBrowser.exe"
LNK_PS1 = ROOT / "tools" / "lnk.ps1"
DESKTOP = Path(os.path.expanduser("~")) / "Desktop"
LNKS = ["TL 浏览器.lnk", "TL Browser.lnk"]
APP_DIR = Path(os.environ["LOCALAPPDATA"]) / "TLSTUDIO" / "TLBrowser"
HOME_HTML = APP_DIR / "home" / "index.html"
GUARD_LOG = APP_DIR / "guard.log"
BACKUP_DIR = ROOT / "_guard_backup"

HIJACK = "http://hao.360.cn"

report: list[str] = []
failures: list[str] = []


def say(line: str = "") -> None:
    print(line, flush=True)
    report.append(line)


def ps(script: str, *args: str) -> str:
    cmd = ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(script), *args]
    out = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    return (out.stdout or "").strip()


def log_lines() -> list[str]:
    if not GUARD_LOG.exists():
        return []
    return GUARD_LOG.read_text(encoding="utf-8", errors="replace").splitlines()


def find_lnk() -> Path | None:
    for name in LNKS:
        p = DESKTOP / name
        if p.exists():
            return p
    return None


def launch(*extra: str, wait: float = 4.0) -> subprocess.Popen:
    p = subprocess.Popen([str(EXE), "--nosplash", *extra])
    time.sleep(wait)
    return p


def kill(p: subprocess.Popen) -> None:
    subprocess.run(["taskkill", "/PID", str(p.pid), "/F"],
                   capture_output=True, encoding="utf-8", errors="replace")
    time.sleep(0.4)


def check(ok: bool, label: str, detail: str = "") -> None:
    mark = "PASS" if ok else "FAIL"
    say(f"  [{mark}] {label}" + (f" —— {detail}" if detail else ""))
    if not ok:
        failures.append(label)


# ────────────────────────────── A. 启动参数注入 ──────────────────────────────

def case_args() -> None:
    say()
    say("A. 启动参数注入：直接给 exe 传一个外部网址")
    before = len(log_lines())

    p = launch(HIJACK)
    alive = p.poll() is None
    check(alive, "进程存活")
    kill(p)

    new = log_lines()[before:]
    say(f"  guard.log 新增 {len(new)} 行：")
    for line in new:
        say("    " + line)

    check(any("启动参数" in l and "拦截" in l for l in new),
          "把外部网址识别成启动页劫持并拦下")
    check(not any(HIJACK in l and "已处理" not in l for l in new),
          "拦截动作已被记录")


# ────────────────────────────── B. 快捷方式被改 ──────────────────────────────

def case_shortcut() -> None:
    say()
    say("B. 快捷方式被追加外部网址")

    lnk = find_lnk()
    if lnk is None:
        check(False, "找到桌面快捷方式", f"{DESKTOP} 下没有 TL 浏览器.lnk")
        return
    say(f"  目标快捷方式：{lnk}")

    BACKUP_DIR.mkdir(parents=True, exist_ok=True)
    backup = BACKUP_DIR / ("lnk_" + datetime.now().strftime("%Y%m%d_%H%M%S") + ".lnk")
    shutil.copy2(lnk, backup)
    say(f"  已按字节备份到：{backup}")

    try:
        ps(LNK_PS1, str(lnk), "set", HIJACK)
        tampered = ps(LNK_PS1, str(lnk), "get")
        check(tampered == HIJACK, "已把网址写进快捷方式参数", f"args={tampered!r}")

        before = len(log_lines())
        p = launch(wait=5.0)
        alive = p.poll() is None
        kill(p)

        after = ps(LNK_PS1, str(lnk), "get")
        check(after == "", "快捷方式参数已被清空", f"args={after!r}")

        target = ps(LNK_PS1, str(lnk), "target")
        check(target.lower() == str(EXE).lower(), "快捷方式目标仍然指向本程序", target)

        new = log_lines()[before:]
        say(f"  guard.log 新增 {len(new)} 行：")
        for line in new:
            say("    " + line)
        check(any("快捷方式" in l for l in new), "还原动作已被记录")
        check(alive, "进程存活")
    finally:
        shutil.copy2(backup, lnk)
        say(f"  已从备份还原快捷方式：args={ps(LNK_PS1, str(lnk), 'get')!r}")


# ────────────────────────────── C. 首页文件被改 ──────────────────────────────

def case_home() -> None:
    say()
    say("C. 首页文件被塞进跳转脚本")

    if not HOME_HTML.exists():
        # 先生成一份
        p = launch(wait=3.0)
        kill(p)
    if not HOME_HTML.exists():
        check(False, "首页文件存在", str(HOME_HTML))
        return

    pristine = HOME_HTML.read_text(encoding="utf-8", errors="replace")
    check("bing.com/search" in pristine, "原始首页里是写死的搜索地址")

    tampered = pristine.replace(
        "https://www.bing.com/search",
        "http://hao.360.cn/?src=360",
    ) + "\n<script>location.href='http://hao.360.cn'</script>\n"
    HOME_HTML.write_text(tampered, encoding="utf-8")
    say(f"  已篡改 {HOME_HTML}（把搜索地址换成 hao.360.cn 并追加跳转脚本）")

    before = len(log_lines())
    p = launch(wait=5.0)
    alive = p.poll() is None
    kill(p)

    now = HOME_HTML.read_text(encoding="utf-8", errors="replace")
    check(now == pristine, "首页已被还原成内嵌版本")
    check("hao.360.cn" not in now, "篡改内容已消失")

    new = log_lines()[before:]
    say(f"  guard.log 新增 {len(new)} 行：")
    for line in new:
        say("    " + line)
    check(any("首页文件" in l for l in new), "还原动作已被记录")
    check(alive, "进程存活")


# ────────────────────────────── D. 启发式自检 ──────────────────────────────

def case_selftest() -> None:
    say()
    say("D. 启发式自检（--selftest，检查网址识别和白名单没被改坏）")

    out = subprocess.run([str(EXE), "--selftest"],
                         capture_output=True, text=True,
                         encoding="utf-8", errors="replace")
    report = APP_DIR / "selftest.txt"
    text = report.read_text(encoding="utf-8", errors="replace") if report.exists() else ""

    check(out.returncode == 0, "自检退出码为 0", f"rc={out.returncode}")
    for line in text.splitlines():
        say("    " + line)

    check("全部通过" in text, "样本全部通过")
    check("FAIL" not in text, "没有 FAIL 项")


# ────────────────────────────── 主流程 ──────────────────────────────

def main() -> int:
    say("TL 浏览器 · 主页守护验收")
    say(f"时间：{datetime.now():%Y-%m-%d %H:%M:%S}")
    say(f"程序：{EXE}")
    say(f"大小：{EXE.stat().st_size / 1024 / 1024:.2f} MB" if EXE.exists() else "程序：不存在！")

    if not EXE.exists():
        say("找不到 exe，先跑 tools/build.ps1")
        return 2

    subprocess.run(["taskkill", "/IM", "TLBrowser.exe", "/F"],
                   capture_output=True, encoding="utf-8", errors="replace")
    time.sleep(0.5)

    case_args()
    case_shortcut()
    case_home()
    case_selftest()

    say()
    say("══════════ 结果 ══════════")
    passed = sum(1 for l in report if "[PASS]" in l)
    say(f"断言 {passed} 条通过，{len(failures)} 条失败")
    for f in failures:
        say("  失败：" + f)
    say("全部通过 ✔" if not failures else "有失败项 ✘")

    (ROOT / "_verify_guard.txt").write_text("\n".join(report), encoding="utf-8")
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
