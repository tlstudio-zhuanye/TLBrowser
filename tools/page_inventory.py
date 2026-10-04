"""盘一盘页面里到底有什么元素，给「自研渲染内核」算真实工作量。

用法：python tools/page_inventory.py <page.html> [...]
"""

from __future__ import annotations

import re
import sys
from collections import Counter
from pathlib import Path

TAGS = [
    "html", "head", "body", "div", "span", "p", "a", "img", "svg", "path", "circle",
    "rect", "g", "button", "input", "form", "ul", "li", "h1", "h2", "h3", "h4",
    "section", "header", "footer", "nav", "main", "article", "aside", "style",
    "script", "link", "meta", "br", "hr", "video", "audio", "canvas", "iframe",
    "table", "tr", "td", "th", "label", "select", "textarea", "strong", "em", "code",
    "pre", "blockquote", "figure", "figcaption", "picture", "source", "template",
]


def probe(path: Path) -> None:
    text = path.read_text(encoding="utf-8", errors="replace")
    print("=" * 66)
    print(path.name)
    print("=" * 66)

    counts = Counter()
    for tag in TAGS:
        n = len(re.findall(r"<" + tag + r"[\s/>]", text, re.I))
        if n:
            counts[tag] = n
    total = sum(counts.values())
    print(f"  元素总数（本表口径） {total}")
    for tag, n in counts.most_common(24):
        print(f"     <{tag}>{'':<12} {n}")

    # 内联图片
    data_imgs = re.findall(r'data:image/([a-z+]+);base64,', text, re.I)
    print(f"  内联 base64 图 {len(data_imgs)} 张: " +
          ", ".join(f"{k}×{v}" for k, v in Counter(data_imgs).most_common()))

    # 外链资源
    ext = re.findall(r'(?:src|href)\s*=\s*"?(https?://[^"\s>]+)', text, re.I)
    print(f"  外链资源 {len(ext)} 个:")
    seen = set()
    for u in ext:
        host = re.sub(r"^(https?://[^/]+).*", r"\1", u)
        if host not in seen:
            seen.add(host)
            print(f"     {host}")

    # script 里有没有逻辑
    scripts = re.findall(r"<script[^>]*>(.*?)</script>", text, re.S | re.I)
    for i, s in enumerate(scripts, 1):
        body = s.strip()
        print(f"  <script> #{i}: {len(body)} 字节"
              + ("" if not body else f" | 首行: {body.splitlines()[0][:70]}"))

    # 文本量
    plain = re.sub(r"<(script|style)[^>]*>.*?</\1>", " ", text, flags=re.S | re.I)
    plain = re.sub(r"<[^>]+>", " ", plain)
    plain = re.sub(r"&[a-z#0-9]+;", " ", plain)
    plain = re.sub(r"\s+", " ", plain).strip()
    cjk = len(re.findall(r"[\u4e00-\u9fff]", plain))
    print(f"  可见文本 {len(plain):,} 字符（其中汉字 {cjk:,}）")

    print("  字体族声明：")
    for fam in set(re.findall(r"font-family\s*:\s*([^;}]+)", text, re.I)):
        print(f"     {fam.strip()[:90]}")
    print()


def main() -> int:
    files = [Path(p) for p in sys.argv[1:]]
    if not files:
        print(__doc__)
        return 2
    for f in files:
        if not f.exists():
            print(f"{f}: 找不到")
            continue
        probe(f)
    return 0


if __name__ == "__main__":
    sys.exit(main())
