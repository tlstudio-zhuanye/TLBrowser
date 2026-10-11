# -*- coding: utf-8 -*-
"""把官网下载页 `tlstudio.cn/llq.html` 的版本号刷到指定版本。

官网是独立仓库 `tlstudioznb/tlstudio-site`（Cloudflare Pages 自动部署），
`publish.py` 只「检查它是不是 200」，**不会改它** —— 所以版本号得单独推一次。

只用 gh 命令行（走 api.github.com，比 git push 稳），不依赖本地克隆。

用法：
    python tools/sync_site_version.py 1.1.5            # 拉最新 → 改版本号 → 推回去
    python tools/sync_site_version.py 1.1.5 --dry-run  # 只打印会改什么
    python tools/sync_site_version.py --check 1.1.5    # 只核对线上是不是这个版本

注意：仓库里存的是 LF，别用本地 CRLF 副本覆盖它。
"""
from __future__ import annotations

import argparse
import base64
import json
import os
import re
import subprocess
import sys
import urllib.request

SITE_REPO = "tlstudioznb/tlstudio-site"
SITE_PATH = "llq.html"
SITE_URL = "https://tlstudio.cn/llq.html"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DIST_EXE = os.path.join(ROOT, "dist", "TLBrowser.exe")
GH = r"C:\Program Files\GitHub CLI\gh.exe"

# 允许 1.1.5 / v1.1.5 两种写法
VER_RE = r"v?\d+\.\d+\.\d+"


def log(msg: str = "") -> None:
    print(msg, flush=True)


def gh(*args: str) -> str:
    exe = GH if os.path.exists(GH) else "gh"
    r = subprocess.run([exe] + list(args), capture_output=True)
    if r.returncode != 0:
        raise SystemExit("gh 调用失败：%s\n%s" % (" ".join(args),
                                                 r.stderr.decode("utf-8", "replace")[:400]))
    return r.stdout.decode("utf-8", "replace")


def fetch() -> tuple[bytes, str]:
    """返回 (文件内容, 当前 sha)。"""
    data = json.loads(gh("api", "repos/%s/contents/%s" % (SITE_REPO, SITE_PATH)))
    return base64.b64decode(data["content"]), data["sha"]


def exe_mb() -> float | None:
    try:
        return round(os.path.getsize(DIST_EXE) / 1024 / 1024, 1)
    except OSError:
        return None


def patch(text: str, ver: str) -> tuple[str, list[str]]:
    """把几处固定的版本号写法换掉，返回 (新文本, 改动说明)。

    只认这几种带上下文的写法，避免误伤页面上别的数字。
    """
    changes: list[str] = []

    def sub(pattern: str, repl: str, label: str) -> None:
        nonlocal text
        new, n = re.subn(pattern, repl, text)
        if n:
            changes.append("%s ×%d" % (label, n))
            text = new

    sub(r"v%s 已发布" % VER_RE, "v%s 已发布" % ver, "顶部徽标版本")
    sub(r"下载 TL 浏览器 v%s" % VER_RE, "下载 TL 浏览器 v%s" % ver, "下载区按钮")
    sub(r"最新版本 v%s" % VER_RE, "最新版本 v%s" % ver, "下载弹窗版本")
    sub(r"releases/download/v%s/" % VER_RE, "releases/download/v%s/" % ver, "GitHub 直链")

    mb = exe_mb()
    if mb is not None:
        sub(r"单文件 \d+(?:\.\d+)? MB", "单文件 %s MB" % mb, "单文件体积")

    return text, changes


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("version")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--check", action="store_true", help="只核对线上版本，不改")
    a = ap.parse_args()
    ver = a.version.lstrip("v")

    if a.check:
        ctx = __import__("ssl").create_default_context()
        ctx.check_hostname = False
        ctx.verify_mode = __import__("ssl").CERT_NONE
        req = urllib.request.Request(SITE_URL, headers={"User-Agent": "Mozilla/5.0"})
        body = urllib.request.urlopen(req, timeout=30, context=ctx).read().decode("utf-8", "replace")
        hits = len(re.findall(r"v%s(?![\d.])" % re.escape(ver), body))
        stale = sorted(set(re.findall(r"v(\d+\.\d+\.\d+)", body)) - {ver})
        log("线上 %s" % SITE_URL)
        log("  本版本出现 %d 次；页面上其它版本号：%s" % (hits, stale or "无"))
        return 0 if hits and not stale else 1

    raw, sha = fetch()
    old = raw.decode("utf-8")
    new, changes = patch(old, ver)

    log("官网下载页 → v%s（当前 sha %s）" % (ver, sha[:12]))
    if not changes:
        log("  没有需要改的地方（可能已经是这个版本）")
        return 0
    for c in changes:
        log("  · %s" % c)

    if a.dry_run:
        log("  [dry] 不改动")
        return 0

    body = {
        "message": "chore: 下载页版本号 → v%s" % ver,
        "content": base64.b64encode(new.encode("utf-8")).decode(),
        "sha": sha,
        "branch": "main",
    }
    tmp = os.path.join(ROOT, "logs", "_site_put.json")
    os.makedirs(os.path.dirname(tmp), exist_ok=True)
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(body, f)

    out = gh("api", "--method", "PUT",
             "repos/%s/contents/%s" % (SITE_REPO, SITE_PATH), "--input", tmp)
    try:
        os.remove(tmp)
    except OSError:
        pass
    log("  已提交，新 sha %s" % json.loads(out)["content"]["sha"][:12])
    log("  Cloudflare Pages 会自动部署，稍等一会儿再看")
    return 0


if __name__ == "__main__":
    sys.exit(main())
