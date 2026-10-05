# -*- coding: utf-8 -*-
"""TL 浏览器 · 一键发布。

把「编译 → 发 GitHub Release → 更新公告 → 同步清单 → 终检」这一整条链路固化成
一个命令，避免每次手搓临时脚本容易漏步。

用法：
    python tools/publish.py                 # 全自动：编译 + 推包 + 发公告
    python tools/publish.py --skip-build    # 用 dist 里现成的 exe（调试通道用）
    python tools/publish.py --dry-run       # 只打印将要做什么，不产生任何写操作
    python tools/publish.py --no-announce   # 推包但不发公告

硬约束（写死在代码里，别绕）：
  * 绝不碰桌面那份 exe —— 用户拿它手工验收。想同步桌面请显式跑 build.ps1 -Desktop。
  * 发公告与推包默认同一个版本号，两者不会各说各话。
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = "tlstudio-zhuanye/TLBrowser"
API = "https://api.tlstudio.cn"
ADMIN_TOKEN = "tlstudioadmin"
UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 Edg/124.0.0.0")

DIST_EXE = os.path.join(ROOT, "dist", "TLBrowser.exe")
BRAND_CS = os.path.join(ROOT, "src", "Brand.cs")
# 两个文案来源，别混：
#   _release_notes.md —— GitHub Release 正文，可以很长，Markdown
#   _announce.txt     —— 弹窗里显示的那几行，必须短（弹窗放不下 Markdown）
NOTES_MD = os.path.join(ROOT, "_release_notes.md")
ANN_TXT = os.path.join(ROOT, "_announce.txt")
GH = r"C:\Program Files\GitHub CLI\gh.exe"
CURL = "curl"


def log(msg: str = "") -> None:
    print(msg, flush=True)


def run(args, **kw):
    return subprocess.run(args, capture_output=True, cwd=ROOT, timeout=kw.pop("timeout", 900), **kw)


# ---------------------------------------------------------------- 版本 / 构建

def read_version() -> str:
    with open(BRAND_CS, encoding="utf-8-sig") as f:
        m = re.search(r'AppVersion\s*=\s*"([\d.]+)"', f.read())
    if not m:
        raise SystemExit("读不到 Brand.cs 里的 AppVersion")
    return m.group(1)


def build() -> None:
    log("[1/6] 编译 dist\\TLBrowser.exe （不碰桌面）…")
    p = run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass",
             "-File", os.path.join(ROOT, "tools", "build.ps1")])
    tail = (p.stdout or b"").decode("utf-8", "replace")[-1500:]
    if p.returncode != 0:
        log(tail)
        raise SystemExit("编译失败 rc=%d" % p.returncode)
    if not os.path.exists(DIST_EXE):
        raise SystemExit("编译结束但找不到 " + DIST_EXE)
    log("      OK  %s" % tail.strip().splitlines()[-1] if tail.strip() else "      OK")


def sha256_of(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest().upper()


# ---------------------------------------------------------------- GitHub

def gh_token() -> str:
    tok = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")
    if tok:
        return tok.strip()
    p = run([GH, "auth", "token"])
    if p.returncode != 0 or not p.stdout.strip():
        raise SystemExit("拿不到 GitHub token：gh 没登录？")
    return p.stdout.decode().strip()


def gh_api(path: str, method="GET", body: dict | None = None, token: str = ""):
    import tempfile
    args = [GH, "api", "--method", method, path]
    tfp = None
    if body is not None:
        fd, tfp = tempfile.mkstemp(suffix=".json")
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            json.dump(body, f, ensure_ascii=False)
        args += ["--input", tfp]
    try:
        p = run(args)
    finally:
        if tfp:
            os.remove(tfp)
    txt = (p.stdout or b"").decode("utf-8", "replace")
    if p.returncode != 0:
        return None, (p.stderr or b"").decode("utf-8", "replace")
    return (json.loads(txt) if txt.strip() else {}), None


def release_exists(tag: str) -> dict | None:
    data, err = gh_api("repos/%s/releases/tags/%s" % (REPO, tag))
    return data if isinstance(data, dict) and data.get("id") else None


def ensure_release(tag: str, title: str, notes_file: str, dry: bool) -> dict:
    rel = release_exists(tag)
    if rel:
        log("      Release %s 已存在 (id=%s)，跳过创建" % (tag, rel["id"]))
        return rel
    if dry:
        log("      [dry] 会创建 Release %s" % tag)
        return {"id": 0, "upload_url": "https://uploads.github.com/repos/%s/releases/0/assets" % REPO}
    args = [GH, "release", "create", tag,
            "--repo", REPO, "--title", title, "--draft=false"]
    if os.path.exists(notes_file):
        args += ["--notes-file", notes_file]
    else:
        args += ["--notes", title]
    p = run(args)
    if p.returncode != 0:
        raise SystemExit("创建 Release 失败：" + (p.stderr or b"").decode("utf-8", "replace")[:600])
    rel = release_exists(tag)
    if not rel:
        raise SystemExit("Release 创建后读不回来")
    log("      Release %s 已创建 (id=%s)" % (tag, rel["id"]))
    return rel


def upload_asset(rel: dict, token: str, dry: bool) -> str:
    """大文件必须走 curl 单次 octet-stream —— gh release upload 对 40MB+ 必 408。"""
    name = "TLBrowser.exe"
    local_sha = sha256_of(DIST_EXE)
    local_size = os.path.getsize(DIST_EXE)

    # 只比大小是不够的：.NET 单文件发布不是逐字节可复现的，重新编译常常
    # 大小一样但内容不同。新版 GitHub 资产带 digest 字段，有就按摘要比。
    for a in rel.get("assets", []) or []:
        if a.get("name") != name:
            continue
        digest = (a.get("digest") or "").lower()
        if digest.startswith("sha256:"):
            if digest.split(":", 1)[1].upper() == local_sha:
                log("      资产 sha256 一致，跳过上传")
                return a["browser_download_url"]
            log("      资产 sha256 不一致，需要重传")
        elif a.get("state") == "uploaded" and a.get("size") == local_size:
            log("      资产大小一致且无摘要可比，跳过上传")
            return a["browser_download_url"]

    if dry:
        log("      [dry] 会删除旧资产并上传 dist\\TLBrowser.exe")
        return "https://github.com/%s/releases/download/%s/%s" % (REPO, rel.get("tag_name", "?"), name)

    for a in rel.get("assets", []) or []:
        if a.get("name") == name:
            subprocess.run([CURL, "-sS", "-X", "DELETE",
                            "-H", "Authorization: token %s" % token,
                            "https://api.github.com/repos/%s/releases/assets/%d" % (REPO, a["id"])],
                           capture_output=True, timeout=120)

    url = ("https://uploads.github.com/repos/%s/releases/%d/assets?name=%s"
           % (REPO, rel["id"], name))
    log("      上传 %s (%.1f MB)…" % (name, os.path.getsize(DIST_EXE) / 1048576))
    p = subprocess.run([CURL, "-sS", "-X", "POST",
                        "-H", "Authorization: token %s" % token,
                        "-H", "Content-Type: application/octet-stream",
                        "--data-binary", "@" + DIST_EXE.replace("\\", "/"),
                        url], capture_output=True, timeout=3600)
    body = (p.stdout or b"").decode("utf-8", "replace")
    if p.returncode != 0 or '"state":"uploaded"' not in body.replace(" ", ""):
        raise SystemExit("上传失败：%s" % body[:800])
    log("      上传完成")
    return json.loads(body)["browser_download_url"]


# ---------------------------------------------------------------- 公告 / 清单

def announce(version: str, url: str, notes: str, dry: bool) -> None:
    if dry:
        log("      [dry] 会 POST %s/admin set_update %s" % (API, version))
        return
    payload = {"action": "set_update", "version": version, "url": url,
               "page": "https://tlstudio.cn/llq.html", "notes": notes}
    req = urllib.request.Request(
        API + "/admin",
        data=json.dumps(payload, ensure_ascii=False).encode("utf-8"),
        headers={"Authorization": "Bearer " + ADMIN_TOKEN,
                 "Content-Type": "application/json; charset=utf-8",
                 "User-Agent": UA, "Accept": "application/json"},
        method="POST")
    try:
        with urllib.request.urlopen(req, timeout=40) as r:
            data = json.loads(r.read().decode("utf-8", "replace"))
        log("      公告已生效：version=%s" % data.get("update", {}).get("version"))
    except urllib.error.HTTPError as e:
        # 403 是 Cloudflare 的 Bot 防护（缺 UA 时才会），401 才是鉴权失败
        raise SystemExit("发公告失败 HTTP %s：%s" % (e.code, e.read().decode("utf-8", "replace")[:400]))


def push_manifest(version: str, url: str, notes: str, dry: bool) -> None:
    path = "version.json"
    manifest = {"version": version, "url": url, "page": "https://tlstudio.cn/llq.html",
                "notes": notes, "publishedAt": time.strftime("%Y-%m-%d")}
    local = os.path.join(ROOT, path)
    if not dry:
        with open(local, "w", encoding="utf-8") as f:
            json.dump(manifest, f, ensure_ascii=False, indent=2)
            f.write("\n")

    meta, err = gh_api("repos/%s/contents/%s" % (REPO, path))
    if err:
        log("      (读仓库 version.json 失败，跳过同步) %s" % err[:200])
        return
    old = json.loads(base64.b64decode(meta["content"]).decode("utf-8", "replace"))
    if old == manifest:
        log("      仓库 version.json 已一致，无需提交")
        return
    if dry:
        log("      [dry] 会提交 version.json")
        return
    body = {"message": "chore: version.json -> %s" % version,
            "content": base64.b64encode(json.dumps(manifest, ensure_ascii=False, indent=2).encode()).decode(),
            "sha": meta["sha"],
            "committer": {"name": "tlstudio-zhuanye",
                          "email": "tlstudio-zhuanye@users.noreply.github.com"}}
    res, err = gh_api("repos/%s/contents/%s" % (REPO, path), "PUT", body)
    if err:
        log("      提交失败：%s" % err[:300])
    else:
        log("      version.json 已提交 %s" % res["commit"]["sha"][:12])


# ---------------------------------------------------------------- 终检

def verify(version: str, url: str) -> int:
    bad = 0
    targets = [
        ("更新清单(API)", API + "/tlbrowser/version.json", False),
        ("更新清单(备)", "https://raw.githubusercontent.com/%s/main/version.json" % REPO, False),
        ("官网下载页", "https://tlstudio.cn/llq.html", False),
        ("exe 直链", url, True),
    ]
    for name, u, head in targets:
        try:
            req = urllib.request.Request(u, headers={"User-Agent": UA})
            if head:
                req.get_method = lambda: "HEAD"
            with urllib.request.urlopen(req, timeout=60) as r:
                extra = ""
                if head:
                    extra = " size=%s" % r.headers.get("Content-Length")
                else:
                    txt = r.read(4000).decode("utf-8", "replace")
                    if version not in txt and name.startswith("更新清单"):
                        extra = "  !! 清单里没有 %s" % version
                        bad += 1
                log("      [OK ] %-14s %s%s" % (name, r.status, extra))
        except Exception as e:
            log("      [ERR] %-14s %s" % (name, e))
            bad += 1
    return bad


def read_announcement(version: str) -> str:
    """弹窗里显示的那几行。优先 _announce.txt，没有就退回 Release 正文的第一段。"""
    rel_notes = ""
    if os.path.exists(NOTES_MD):
        rel_notes = open(NOTES_MD, encoding="utf-8").read().strip()

    ann = ""
    if os.path.exists(ANN_TXT):
        ann = open(ANN_TXT, encoding="utf-8").read().strip()
    if not ann:
        first = [b for b in rel_notes.split("\n\n") if b.strip()]
        ann = first[0] if first else "TL 浏览器 %s 更新。" % version
    if len(ann) > 600:
        raise SystemExit("_announce.txt 太长了（%d 字），弹窗放不下，压到 600 字以内" % len(ann))
    return ann


def resolve_download_url(tag: str, dry: bool) -> str:
    """只推消息时用：从已存在的 Release 里取资产直链；取不到就按约定拼一个。"""
    fallback = "https://github.com/%s/releases/download/%s/TLBrowser.exe" % (REPO, tag)
    if dry:
        return fallback
    rel = release_exists(tag)
    if not rel:
        log("      !! 找不到 Release %s，按约定拼直链" % tag)
        return fallback
    for a in rel.get("assets", []) or []:
        if a.get("name") == "TLBrowser.exe" and a.get("state") == "uploaded":
            return a["browser_download_url"]
    log("      !! Release %s 里没有已上传的 TLBrowser.exe，按约定拼直链" % tag)
    return fallback


# ---------------------------------------------------------------- main

def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--skip-build", action="store_true", help="用 dist 里现成的 exe")
    ap.add_argument("--dry-run", action="store_true", help="只打印计划")
    ap.add_argument("--no-announce", action="store_true", help="不发公告、不动清单")
    ap.add_argument("--announce-only", action="store_true",
                    help="只重新推送更新消息（编译、Release、上传全部跳过）")
    a = ap.parse_args()
    dry = a.dry_run

    version = read_version()
    tag = "v" + version
    log("TL 浏览器 发布 · 版本 %s" % version)
    log("=" * 58)

    # ---- 只推消息：编译 / Release / 上传都不碰，纯粹把公告再刷一遍 ----
    if a.announce_only:
        ann = read_announcement(version)
        log("[1/2] 推送更新公告（只推消息模式）")
        url = resolve_download_url(tag, dry)
        log("      url = %s" % url)
        announce(version, url, ann, dry)
        log("[2/2] 同步仓库 version.json")
        push_manifest(version, url, ann, dry)
        bad = 0 if dry else verify(version, url)
        log("=" * 58)
        log("干跑结束，什么都没改。" if dry else
            ("更新消息已推送。" if bad == 0 else "推送完成，但终检有 %d 项失败。" % bad))
        return 0 if bad == 0 else 2

    if not a.skip_build:
        if dry:
            log("[1/6] [dry] 会跑 tools\\build.ps1")
        else:
            build()
    else:
        log("[1/6] 跳过编译，用现有 dist\\TLBrowser.exe")
    if not os.path.exists(DIST_EXE):
        raise SystemExit("dist\\TLBrowser.exe 不存在")
    digest = sha256_of(DIST_EXE)
    log("      sha256 = %s" % digest)

    ann = read_announcement(version)

    token = "" if dry else gh_token()
    log("[2/6] GitHub Release %s" % tag)
    rel = ensure_release(tag, "TL 浏览器 " + tag, NOTES_MD, dry)

    log("[3/6] 上传资产")
    url = upload_asset(rel, token, dry)

    if a.no_announce:
        log("[4/6] --no-announce，跳过公告与清单")
    else:
        log("[4/6] 推送更新公告")
        announce(version, url, ann, dry)
        log("[5/6] 同步仓库 version.json")
        push_manifest(version, url, ann, dry)

    log("[6/6] 终检")
    bad = 0 if dry else verify(version, url)

    log("=" * 58)
    log(("干跑结束，什么都没改。" if dry else
         ("全部完成。" if bad == 0 else "完成，但有 %d 项终检失败，请人工看一眼。" % bad)))
    if not dry:
        log("桌面那份 exe 未被动过 —— 双击它会看到 %s 的更新公告，可用来验收。" % tag)
    return 0 if bad == 0 else 2


if __name__ == "__main__":
    sys.exit(main())
