"""守住一条规矩：这台机器上的 .ps1 必须纯 ASCII。

Windows PowerShell 5.1 在没有 BOM 时按 ANSI(GBK) 解码 .ps1 文件，
中文注释会乱码，而且多字节序列可能吃掉后面的字符，
导致「表达式或语句中包含意外的标记」这种莫名其妙的解析错误。

用法：python check_ps1_ascii.py
"""

from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def main() -> int:
    bad = []
    for f in sorted(ROOT.rglob("*.ps1")):
        data = f.read_bytes()
        if data.startswith(b"\xef\xbb\xbf"):
            continue                      # 有 BOM 就没事
        offenders = [(i, b) for i, b in enumerate(data) if b > 0x7F]
        if offenders:
            bad.append((f, offenders))

    if not bad:
        print("OK: all .ps1 files are pure ASCII (or have a BOM)")
        return 0

    print("FAIL: these .ps1 files contain non-ASCII bytes and will break under PowerShell 5.1")
    for f, offenders in bad:
        line = f.read_bytes()[: offenders[0][0]].count(b"\n") + 1
        print(f"  {f.relative_to(ROOT)}  -> {len(offenders)} non-ASCII bytes, first at line {line}")
    print()
    print("Fix: either keep them ASCII-only (use [char]0xXXXX for CJK), or save with a UTF-8 BOM.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
