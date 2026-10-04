"""量一量 tlstudio.cn / tldoublerstudio.cn 的复杂度，
判断「自己写一个渲染内核来显示它们」到底要付出什么代价。

用法：python tools/site_complexity.py <site1.html> <site2.html>
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

FEATURES = {
    "display:flex": r"display\s*:\s*flex",
    "display:grid": r"display\s*:\s*grid",
    "position:absolute/fixed": r"position\s*:\s*(?:absolute|fixed)",
    "transform:": r"transform\s*:",
    "transition:": r"transition\s*:",
    "@keyframes": r"@keyframes",
    "@media": r"@media",
    "var(--) 自定义属性": r"var\(--",
    "gradient() 渐变": r"gradient\(",
    "calc()": r"calc\(",
    "box-shadow": r"box-shadow",
    "::before/::after": r"::(?:before|after)",
    "border-radius": r"border-radius",
    "overflow:": r"overflow\s*:",
    "z-index": r"z-index",
    "规则块 {": r"\{",
}

DATA_URI = re.compile(r"""data:[^"')]{200,}""")


def analyse(path: Path) -> None:
    text = path.read_text(encoding="utf-8", errors="replace")

    m = re.search(r"<style[^>]*>(.*?)</style>", text, re.S | re.I)
    css = m.group(1) if m else ""

    print(f"  {'总字节':<24} {len(text):>10,}")
    print(f"  {'其中 <style> CSS':<24} {len(css):>10,}  ({len(css) * 100 // max(1, len(text))}%)")
    print(f"  {'<script> 个数':<24} {len(re.findall(r'<script', text, re.I)):>10}")
    print(f"  {'外链 http 资源':<24} "
          f"{len(re.findall(chr(34) + r'?(?:src|href)=.https?:', text)):>10}")

    blobs = DATA_URI.findall(text)
    print(f"  {'内联 data: 大块':<24} {len(blobs):>10}  共 {sum(len(b) for b in blobs):,} 字节")

    print("  ── CSS 特性出现次数 ──")
    for label, pattern in FEATURES.items():
        print(f"  {label:<24} {len(re.findall(pattern, css)):>10}")


def main() -> int:
    files = [Path(p) for p in sys.argv[1:]]
    if not files:
        print(__doc__)
        return 2
    for f in files:
        print("═" * 60)
        print(f"  {f.name}")
        print("═" * 60)
        if not f.exists():
            print("   找不到文件")
            continue
        analyse(f)
        print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
