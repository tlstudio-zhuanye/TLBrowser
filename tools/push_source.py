# -*- coding: utf-8 -*-
"""把工作区的源码推到 GitHub 主仓库 —— 走 GitHub API，不依赖 git push。

为什么不用 `git push`：本机的 git 走 HTTPS 代理，**时通时不通**
（反复报 `CONNECT tunnel failed, response 502`）。`gh` 走 api.github.com 则一直通。
所以推源码这条路做成 API 版，任何时候都能用。

它做的事（镜像文档里那套手搓步骤）：
    1. 读远端 main 的 commit / tree
    2. 逐文件算 git blob sha（文本按 LF 归一化，和仓库既有风格一致），只挑出真正变了的
    3. POST git/blobs 上传新内容
    4. POST git/trees（base_tree = 远端 tree）→ 只改这些 path，其余原样保留
    5. POST git/commits（parent = 远端 main）→ PATCH git/refs/heads/main

维护范围：`src/`（排除 bin/obj）和 `tools/`。
这两个是**镜像**目录 —— 仓库里有、本地没有的文件会被删掉（用于清理废弃文件）。
`README.md` / `index.html` 只在仓库里存在，不在维护范围内，不会被碰。

**`version.json` 不归这里管** —— 它由 `publish.py` 用 Contents API 提交（内容是
`json.dumps` 出来的、结尾不带换行）。两边都推会互相打架，所以划清界限。

用法：
    python tools/push_source.py --dry-run            # 只看会改哪些文件
    python tools/push_source.py -m "feat: xxx"       # 真推
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = "tlstudio-zhuanye/TLBrowser"
BRANCH = "main"
GH = r"C:\Program Files\GitHub CLI\gh.exe"

# 这些扩展名按文本处理：提交前把 CRLF 归一化成 LF
# （仓库里存的是 LF，本地工作区是 CRLF，不归一化会把每个文件都标成改动）
TEXT_EXT = {".cs", ".csproj", ".user", ".py", ".ps1", ".md", ".json", ".txt",
            ".html", ".manifest", ".gitignore", ".yml", ".yaml", ".config"}
SKIP_DIRS = {"bin", "obj", ".vs", ".git"}


def log(msg: str = "") -> None:
    print(msg, flush=True)


def gh(*args: str) -> dict:
    exe = GH if os.path.exists(GH) else "gh"
    r = subprocess.run([exe] + list(args), capture_output=True)
    if r.returncode != 0:
        raise SystemExit("gh 调用失败：%s\n%s"
                         % (" ".join(args), r.stderr.decode("utf-8", "replace")[:500]))
    out = r.stdout.decode("utf-8", "replace").strip()
    return json.loads(out) if out else {}


def gh_post(path: str, body: dict) -> dict:
    tmp = os.path.join(ROOT, "logs", "_push_body.json")
    os.makedirs(os.path.dirname(tmp), exist_ok=True)
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(body, f)
    try:
        return gh("api", "--method", "POST", "repos/%s/%s" % (REPO, path), "--input", tmp)
    finally:
        try:
            os.remove(tmp)
        except OSError:
            pass


def blob_sha(data: bytes) -> str:
    """git 的 blob 摘要：sha1("blob <len>\\0" + content)，和 git hash-object 一致。"""
    return hashlib.sha1(b"blob %d\0" % len(data) + data).hexdigest()


def collect() -> dict[str, bytes]:
    """要维护的文件：相对仓库根的路径 -> 内容（文本已归一化成 LF）。"""
    files: dict[str, bytes] = {}

    def add(path: str, rel: str) -> None:
        with open(path, "rb") as f:
            data = f.read()
        if os.path.splitext(rel)[1].lower() in TEXT_EXT:
            data = data.replace(b"\r\n", b"\n")
        files[rel.replace("\\", "/")] = data

    for sub in ("src", "tools"):
        base = os.path.join(ROOT, sub)
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
            for name in filenames:
                full = os.path.join(dirpath, name)
                rel = os.path.relpath(full, ROOT)
                add(full, rel)

    return files


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("-m", "--message", default="chore: 同步源码")
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()

    head = gh("api", "repos/%s/commits/%s" % (REPO, BRANCH))
    parent = head["sha"]
    base_tree = head["commit"]["tree"]["sha"]
    log("远端 %s = %s  %s" % (BRANCH, parent[:12], head["commit"]["message"].splitlines()[0]))

    remote = {}
    tree = gh("api", "repos/%s/git/trees/%s?recursive=1" % (REPO, base_tree))
    for e in tree["tree"]:
        if e["type"] == "blob":
            remote[e["path"]] = e["sha"]

    local = collect()

    # 只看维护范围内的远端文件，用来判断删除
    def managed(p: str) -> bool:
        return p.startswith("src/") or p.startswith("tools/")

    changed, deleted = [], []
    for path, data in sorted(local.items()):
        if remote.get(path) != blob_sha(data):
            changed.append(path)
    for path in sorted(remote):
        if managed(path) and path not in local:
            deleted.append(path)

    log("本地维护 %d 个文件；改动 %d 个、删除 %d 个" % (len(local), len(changed), len(deleted)))
    for p in changed:
        log("  M %s" % p)
    for p in deleted:
        log("  D %s" % p)

    if not changed and not deleted:
        log("已经是最新，无需提交")
        return 0
    if a.dry_run:
        log("[dry] 不做任何写操作")
        return 0

    entries = []
    for path in changed:
        blob = gh_post("git/blobs", {
            "content": base64.b64encode(local[path]).decode(),
            "encoding": "base64",
        })
        entries.append({"path": path, "mode": "100644", "type": "blob", "sha": blob["sha"]})
    for path in deleted:
        entries.append({"path": path, "mode": "100644", "type": "blob", "sha": None})

    new_tree = gh_post("git/trees", {"base_tree": base_tree, "tree": entries})
    commit = gh_post("git/commits", {
        "message": a.message,
        "tree": new_tree["sha"],
        "parents": [parent],
    })
    gh("api", "--method", "PATCH", "repos/%s/git/refs/heads/%s" % (REPO, BRANCH),
       "-f", "sha=" + commit["sha"])

    now = gh("api", "repos/%s/commits/%s" % (REPO, BRANCH))
    log("已提交 %s → 远端 %s = %s" % (commit["sha"][:12], BRANCH, now["sha"][:12]))
    if now["sha"] != commit["sha"]:
        log("!! 远端 sha 与刚才的提交不一致，可能有人同时推了东西，检查一下")
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
