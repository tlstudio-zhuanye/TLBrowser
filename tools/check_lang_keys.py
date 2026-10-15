# -*- coding: utf-8 -*-
"""守门脚本：把「文案 key 对不上」这类编译器看不见的 bug 挡在发布之前。

为什么需要它：`Lang.T` 找不到 key 时**直接把 key 原样返回**
（Lang.cs：`if (!Pack.TryGetValue(key, out var pair)) return key;`），
所以漏一个 key 编译器一声不吭，界面上却会明晃晃地印出 `split.on` 这种裸 key。

真实案例：
  - v1.1.4 工具栏「分屏」按钮漏了 `split.on` / `split.off` ——
    编译 0 警告 0 错误、三套自检全绿，一跑起来按钮上写着 `split.on`（用户截图报的）
  - 同样的病在标签右键菜单里躺了很久：用了 `bm.addTab` / `bm.removeTab`，
    而表里定义的是 `bm.add` / `bm.remove`

两项检查：
  A. C# 里 `Lang.T("xxx")` 的字面量必须在 Lang.cs 的表里有定义
  B. 主页模板 `home.template.html` 里 JS 读的 `T.xxx` 必须由 `HomePage.Build()`
     注入（否则网页上会显示 undefined）

用法：
    python tools/check_lang_keys.py          # rc = 问题总数
    python tools/check_lang_keys.py --unused # 顺便列出「定义了但没人用」的 key
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src")
TEMPLATE = os.path.join(SRC, "assets", "home.template.html")

DEF_RE = re.compile(r'\["([^"]+)"\]\s*=\s*new\[\]')          # Lang.cs 的键定义
USE_RE = re.compile(r'Lang\.T\(\s*"([^"]+)"')                # C# 里的字面量调用
DYN_RE = re.compile(r'Lang\.T\(\s*[^")]')                    # 动态拼的调用
JS_USE_RE = re.compile(r'\bT\.([A-Za-z_][A-Za-z0-9_]*)')     # 模板 JS 里的 T.xxx
I18N_RE = re.compile(r'\["([^"]+)"\]\s*=\s*Lang\.T\(')       # HomePage 注入的 i18n 键

# 代码里「拼 key」的地方（正则抓不到实际目标），按名字列全代替人眼记着：
#   DownloadForm.cs  Lang.T(active > 0 ? "dl.sumActive" : "dl.sumIdle", ...)
#   DownloadStore.cs Lang.T("dl.state." + StateKey)   —— done/canceled/interrupted/active
DYNAMIC_KEYS = [
    "dl.sumActive", "dl.sumIdle",
    "dl.state.done", "dl.state.canceled", "dl.state.interrupted", "dl.state.active",
    # 工具箱：分类名与功能名是拿 id 拼出来的（toolbox.cat.<cat> / tool.<id>）
    "toolbox.cat.ops", "toolbox.cat.web", "toolbox.cat.text", "toolbox.cat.safe",
    # 工具箱：一格里二选一的那些
    "web.shotSave", "web.shotFullSave",
    "pageinfo.yes", "pageinfo.no",
    "web.styleOff", "web.readerOn", "web.darkOn",
    "verifyhash.match", "verifyhash.mismatch",
    "idcard.ok", "idcard.bad", "idcard.male", "idcard.female",
    "luhn.ok", "luhn.bad",
    "privacy.on", "privacy.off",
    "strength.weak", "strength.fair", "strength.good", "strength.strong",
    "entropy.instant", "entropy.sec", "entropy.min", "entropy.hour", "entropy.day", "entropy.year",
    "ipclass.private", "ipclass.loopback", "ipclass.linkLocal", "ipclass.multicast", "ipclass.public",
]


def read(path):
    with io.open(path, encoding="utf-8-sig") as f:
        return f.read()


def check_lang_table(problems):
    """A. Lang.T 字面量 vs Lang.cs 的表。"""
    lang_cs = os.path.join(SRC, "Lang.cs")
    defined = set(DEF_RE.findall(read(lang_cs)))

    used, dynamic = {}, []
    for name in sorted(os.listdir(SRC)):
        if not name.endswith(".cs") or name == "Lang.cs":
            continue
        for line_no, line in enumerate(read(os.path.join(SRC, name)).splitlines(), 1):
            for key in USE_RE.findall(line):
                if key.endswith("."):          # Lang.T("dl.state." + key) 这类前缀拼接
                    dynamic.append("%s:%d  前缀拼接 %r" % (name, line_no, key))
                    continue
                used.setdefault(key, []).append("%s:%d" % (name, line_no))
            if DYN_RE.search(line) and not USE_RE.search(line):
                dynamic.append("%s:%d  %s" % (name, line_no, line.strip()[:88]))

    missing = sorted(k for k in used if k not in defined)

    # 代码里是「拼出来」的 key，正则抓不到实际目标 —— 这里按名字列全，
    # 免得它们成为检查的盲区（新增动态站点时必须往这张表里补一行）
    for k in DYNAMIC_KEYS:
        if k not in defined:
            missing.append(k)
            print("   (动态) 缺失 %s" % k)

    print("A. C# 里的 Lang.T 字面量  ——  表里 %d 个，代码引用 %d 个"
          % (len(defined), len(used)))
    if missing:
        print("   !! 缺失 %d 个（界面上会直接显示这些裸 key）：" % len(missing))
        for k in missing:
            print("      %-26s <- %s" % (k, ", ".join(used[k])))
    else:
        print("   OK 没有缺失")
    if dynamic:
        print("   -- 动态拼 key（静态查不了，人眼过一下）：")
        for d in dynamic:
            print("      %s" % d)

    problems.extend(missing)
    return defined, used


def check_home_i18n(problems):
    """B. 主页模板 JS 读的 T.xxx vs HomePage.Build() 注入的 i18n。"""
    print()
    if not os.path.exists(TEMPLATE):
        print("B. 主页模板 i18n  ——  跳过（没找到 %s）" % TEMPLATE)
        return
    wanted = set(JS_USE_RE.findall(read(TEMPLATE)))
    provided = set(I18N_RE.findall(read(os.path.join(SRC, "HomePage.cs"))))
    missing = sorted(wanted - provided)

    print("B. 主页模板 JS 的 T.xxx  ——  模板用 %d 个，HomePage 注入 %d 个"
          % (len(wanted), len(provided)))
    if missing:
        print("   !! 缺失 %d 个（网页上会显示 undefined）：%s" % (len(missing), ", ".join(missing)))
    else:
        print("   OK 没有缺失")

    problems.extend("home:" + k for k in missing)


def main():
    if not os.path.exists(os.path.join(SRC, "Lang.cs")):
        print("!! 找不到 src/Lang.cs，是不是跑错目录了")
        return 1

    problems = []
    defined, used = check_lang_table(problems)
    check_home_i18n(problems)

    if "--unused" in sys.argv:
        unused = sorted(k for k in defined if k not in used)
        print()
        print("-- 定义了但没被 C# 引用（主页 home.* 那批走 %%I18N%% 注入，属正常）：%d 个"
              % len(unused))
        for k in unused:
            print("   %s" % k)

    print()
    print("=" * 62)
    print("问题 %d 个" % len(problems))
    return len(problems)


if __name__ == "__main__":
    sys.exit(main())
