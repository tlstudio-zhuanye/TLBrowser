"""把站点 CSS 里用到的属性/选择器统计出来，
给「自研渲染内核」圈定必须实现的属性集合。

用法：python tools/css_surface.py <site.html> [...]
"""

from __future__ import annotations

import re
import sys
from collections import Counter
from pathlib import Path

BLOCK = re.compile(r"([^{}]+)\{([^{}]*)\}", re.S)


def strip_at_rules(css: str) -> tuple[str, list[str]]:
    """把 @media / @keyframes 拆出来单独看，主体只留普通规则。"""
    ats: list[str] = []
    out = []
    i = 0
    while i < len(css):
        m = re.compile(r"@[a-z-]+", re.I).search(css, i)
        if not m:
            out.append(css[i:])
            break
        out.append(css[i:m.start()])
        # 从 @ 开始找配对的 { }
        j = css.find("{", m.end())
        if j < 0:
            ats.append(css[m.start():])
            break
        depth = 1
        k = j + 1
        while k < len(css) and depth:
            if css[k] == "{":
                depth += 1
            elif css[k] == "}":
                depth -= 1
            k += 1
        ats.append(css[m.start():k])
        i = k
    return "".join(out), ats


def main() -> int:
    files = [Path(p) for p in sys.argv[1:]]
    if not files:
        print(__doc__)
        return 2

    total_props: Counter[str] = Counter()
    total_units: Counter[str] = Counter()
    total_pseudo: Counter[str] = Counter()
    total_at: Counter[str] = Counter()

    for f in files:
        raw = f.read_text(encoding="utf-8", errors="replace")
        m = re.search(r"<style[^>]*>(.*?)</style>", raw, re.S | re.I)
        if not m:
            print(f"{f.name}: 没有内联 <style>")
            continue
        css = m.group(1)
        body, ats = strip_at_rules(css)

        props: Counter[str] = Counter()
        for _sel, decls in BLOCK.findall(body):
            for d in decls.split(";"):
                if ":" not in d:
                    continue
                name = d.split(":", 1)[0].strip().lower()
                if name and not name.startswith("--"):
                    props[name] += 1

        # 自定义属性
        customs = set(re.findall(r"(--[a-zA-Z0-9-]+)\s*:", css))

        # 值里的单位
        for unit in ["px", "%", "rem", "em", "vw", "vh", "vmin", "vmax", "fr", "deg", "s", "ms", "ch"]:
            n = len(re.findall(r"[\d.]+" + unit + r"\b", body))
            if n:
                total_units[unit] += n

        pseudos = Counter(re.findall(r"::?(hover|before|after|focus|active|first-child|last-child|nth-child)", body))
        total_pseudo.update(pseudos)

        print("=" * 66)
        print(f"{f.name}")
        print(f"  CSS {len(css):,} 字节 | 普通规则属性 {len(props)} 种 | 自定义属性 {len(customs)} 个")
        print(f"  @ 规则 {len(ats)} 个: " + ", ".join(sorted({a.split()[0] for a in ats})))
        print("  ── 用到的属性（按次数） ──")
        for name, n in props.most_common():
            print(f"     {name:<26} {n}")
        print("  ── 伪类/伪元素 ──")
        print("     " + (", ".join(f"{k}×{v}" for k, v in pseudos.most_common()) or "无"))
        print()
        total_props.update(props)
        for a in ats:
            total_at[a.split()[0].lower()] += 1

    print("=" * 66)
    print("合并：两个站一共要用到的属性种类")
    print("  " + ", ".join(sorted(total_props)))
    print()
    print("合并：数值单位用量")
    print("  " + ", ".join(f"{k}={v}" for k, v in total_units.most_common()))
    print()
    print("合并：伪类/伪元素")
    print("  " + ", ".join(f"{k}×{v}" for k, v in total_pseudo.most_common()))
    print()
    print("合并：@ 规则")
    print("  " + ", ".join(f"{k}×{v}" for k, v in total_at.most_common()))
    return 0


if __name__ == "__main__":
    sys.exit(main())
