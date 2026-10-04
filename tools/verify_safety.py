"""安全与拦截这一层的验收。

A. --blocktest   24 条判定样本，重点是那几条「看着像广告但绝不能拦」的对照项：
                 eviltlstudio.cn / notdoubleclick.net / adobe.com / img.alicdn.com。
                 后缀匹配一旦写成 Contains，这几条必然翻车。
B. 规则文件      内置规则条数 > 0；用户规则模板已生成；设置文件已落盘。
C. 审计日志      blocked.log 的路径可读（没有拦截时允许不存在）。

用法：python verify_safety.py
"""

from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EXE = ROOT / "dist" / "TLBrowser.exe"
APP_DIR = Path(os.environ["LOCALAPPDATA"]) / "TLSTUDIO" / "TLBrowser"

report: list[str] = []
fails = 0


def say(line: str = "") -> None:
    print(line, flush=True)
    report.append(line)


def check(ok: bool, label: str, detail: str = "") -> None:
    global fails
    if not ok:
        fails += 1
    say(f"  [{'PASS' if ok else 'FAIL'}] {label}" + (f" —— {detail}" if detail else ""))


def run(*args: str) -> subprocess.CompletedProcess:
    return subprocess.run([str(EXE), *args], capture_output=True, text=True,
                          encoding="utf-8", errors="replace", timeout=120)


def case_blocktest() -> None:
    say()
    say("A. 拦截判定自检（--blocktest）")
    out = run("--blocktest")
    text = ""
    report_path = APP_DIR / "blocktest.txt"
    if report_path.exists():
        text = report_path.read_text(encoding="utf-8", errors="replace")

    check(out.returncode == 0, "自检退出码为 0", f"rc={out.returncode}")
    for line in text.splitlines():
        say("    " + line)
    check("全部通过" in text, "样本全部通过")
    check("FAIL" not in text, "没有 FAIL 项")

    # 单独把几条最容易被误拦的挑出来再确认一次
    for host in ("eviltlstudio.cn", "notdoubleclick.net", "adobe.com",
                 "img.alicdn.com", "api.tlstudio.cn"):
        line = next((l for l in text.splitlines() if host in l), "")
        check("放" in line and "PASS" in line, f"{host} 没有被误拦")


def case_files() -> None:
    say()
    say("B. 规则与设置文件")

    rules = APP_DIR / "adblock_user.txt"
    check(rules.exists(), "用户规则模板已生成", str(rules))
    if rules.exists():
        body = rules.read_text(encoding="utf-8", errors="replace")
        check("+example.com" in body and "=ad" in body, "模板里有写法说明")

    settings = APP_DIR / "settings.json"
    check(settings.exists(), "设置文件已落盘", str(settings))
    if settings.exists():
        body = settings.read_text(encoding="utf-8", errors="replace").replace(" ", "").replace("\n", "")
        check('"AdBlock":true' in body or '"AdBlock":false' in body,
              "设置里有广告拦截开关", body[:120])

    # 内置规则条数写在 blocktest 报告里
    text = ""
    report_path = APP_DIR / "blocktest.txt"
    if report_path.exists():
        text = report_path.read_text(encoding="utf-8", errors="replace")
    line = next((l for l in text.splitlines() if "规则条数" in l), "规则条数：0")
    n = 0
    for token in line.replace("：", " ").replace("　", " ").split():
        if token.isdigit():
            n = int(token)
            break
    check(n >= 100, "内置规则条数够用", f"{n} 条")


def case_audit() -> None:
    say()
    say("C. 审计日志")
    log = APP_DIR / "blocked.log"
    if log.exists():
        lines = log.read_text(encoding="utf-8", errors="replace").splitlines()
        check(True, "blocked.log 存在", f"{len(lines)} 行")
        for line in lines[-5:]:
            say("    " + line)
    else:
        say("  [INFO] blocked.log 还不存在——这次会话没拦到东西，属于正常")


def main() -> int:
    say("TL 浏览器 · 安全与拦截验收")
    say(f"程序：{EXE}")
    if EXE.exists():
        say(f"大小：{EXE.stat().st_size / 1024 / 1024:.2f} MB")
    say("=" * 62)

    if not EXE.exists():
        say("找不到 exe，先跑 tools/build.ps1")
        return 2

    subprocess.run(["taskkill", "/IM", "TLBrowser.exe", "/F"],
                   capture_output=True, encoding="utf-8", errors="replace")

    case_blocktest()
    case_files()
    case_audit()

    say()
    say("━" * 12 + " 结果 " + "━" * 12)
    say(f"失败 {fails} 条")
    say("全部通过 ✓" if fails == 0 else "有失败项 ✗")

    (ROOT / "_verify_safety.txt").write_text("\n".join(report), encoding="utf-8")
    return fails


if __name__ == "__main__":
    sys.exit(main())
