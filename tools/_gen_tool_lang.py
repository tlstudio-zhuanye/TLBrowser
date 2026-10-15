# -*- coding: utf-8 -*-
"""生成工具箱的 100 条文案键（中英），插进 Lang.cs。
生成一次就够了，之后文案要改直接改 Lang.cs。"""
import io, os, re

ROOT = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(os.path.dirname(ROOT), "src")

# id -> (中文, English)
T = {
    # 一、运维 / IT
    "ops.subnet": ("子网掩码计算", "Subnet calculator"),
    "ops.ip2int": ("IP 转整数", "IP to integer"),
    "ops.int2ip": ("整数转 IP", "Integer to IP"),
    "ops.port": ("端口对照查询", "Port lookup"),
    "ops.ipclass": ("IP 地址分类", "IP address class"),
    "ops.cidr": ("网段包含检查", "CIDR overlap check"),
    "ops.mac": ("MAC 地址格式化", "Format MAC address"),
    "ops.ts2date": ("时间戳转日期", "Timestamp to date"),
    "ops.date2ts": ("日期转时间戳", "Date to timestamp"),
    "ops.utc": ("UTC / 本地时间", "UTC / local time"),
    "ops.hex": ("十六进制 ↔ 文本", "Hex ↔ text"),
    "ops.b64enc": ("Base64 编码", "Base64 encode"),
    "ops.b64dec": ("Base64 解码", "Base64 decode"),
    "ops.urlenc": ("URL 编码", "URL encode"),
    "ops.urldec": ("URL 解码", "URL decode"),
    "ops.md5": ("MD5 计算", "MD5 hash"),
    "ops.sha1": ("SHA-1 计算", "SHA-1 hash"),
    "ops.sha256": ("SHA-256 计算", "SHA-256 hash"),
    "ops.regex": ("正则测试", "Regex tester"),
    "ops.jsonfmt": ("JSON 格式化", "Format JSON"),
    "ops.jsonmin": ("JSON 压缩", "Minify JSON"),
    "ops.machine": ("本机信息", "System info"),
    "ops.myip": ("本机 IP 地址", "Local IP addresses"),
    "ops.disk": ("磁盘剩余空间", "Disk space"),
    "ops.newline": ("换行符统一", "Normalize line endings"),

    # 二、网页 / 浏览
    "web.shot": ("网页截图", "Screenshot"),
    "web.shotfull": ("整页截图", "Full-page screenshot"),
    "web.print": ("打印当前页", "Print page"),
    "web.pdf": ("导出为 PDF", "Export as PDF"),
    "web.info": ("页面信息", "Page info"),
    "web.links": ("提取所有链接", "Extract links"),
    "web.images": ("提取所有图片", "Extract images"),
    "web.copytitle": ("复制页面标题", "Copy page title"),
    "web.copyurl": ("复制页面地址", "Copy page URL"),
    "web.copymd": ("复制 Markdown 链接", "Copy as Markdown link"),
    "web.copytext": ("复制正文纯文本", "Copy page text"),
    "web.find": ("页内查找", "Find on page"),
    "web.source": ("查看网页源代码", "View page source"),
    "web.headers": ("查看响应头", "View response headers"),
    "web.reader": ("阅读模式", "Reader mode"),
    "web.dark": ("夜间模式", "Dark mode"),
    "web.nojs": ("禁止网页脚本", "Disable scripts"),
    "web.noimg": ("禁止网页图片", "Disable images"),
    "web.translate": ("翻译当前页", "Translate page"),
    "web.zoom100": ("缩放复位 100%", "Reset zoom to 100%"),
    "web.zoomin": ("放大 10%", "Zoom in 10%"),
    "web.zoomout": ("缩小 10%", "Zoom out 10%"),
    "web.seltext": ("复制选中文字", "Copy selection"),
    "web.sysopen": ("用系统浏览器打开", "Open in system browser"),
    "web.clearsite": ("清除本页站点数据", "Clear this site's data"),

    # 三、文本 / 日常
    "text.count": ("字数统计", "Count characters"),
    "text.upper": ("转大写", "UPPERCASE"),
    "text.lower": ("转小写", "lowercase"),
    "text.title": ("首字母大写", "Title Case"),
    "text.swap": ("大小写互换", "Swap case"),
    "text.camel2snake": ("驼峰转下划线", "camelCase → snake_case"),
    "text.snake2camel": ("下划线转驼峰", "snake_case → camelCase"),
    "text.noblank": ("去除空行", "Remove blank lines"),
    "text.trim": ("去除行首尾空白", "Trim each line"),
    "text.nospace": ("去除所有空格", "Remove all spaces"),
    "text.dedup": ("行去重", "Remove duplicate lines"),
    "text.sort": ("行排序", "Sort lines"),
    "text.reverse": ("行倒序", "Reverse line order"),
    "text.alpha": ("按字母排序", "Sort lines A-Z"),
    "text.bylen": ("按长度排序", "Sort lines by length"),
    "text.shuffle": ("行洗牌", "Shuffle lines"),
    "text.addnum": ("加行号", "Add line numbers"),
    "text.delnum": ("去行号", "Remove line numbers"),
    "text.prefix": ("加前缀", "Add prefix"),
    "text.suffix": ("加后缀", "Add suffix"),
    "text.reversechars": ("文本反转", "Reverse text"),
    "text.wide2narrow": ("全角转半角", "Full-width → half-width"),
    "text.narrow2wide": ("半角转全角", "Half-width → full-width"),
    "text.t2s": ("繁简转换", "Traditional ↔ Simplified"),
    "text.replace": ("查找替换", "Find and replace"),

    # 四、安全 / 隐私
    "safe.genpass": ("密码生成器", "Password generator"),
    "safe.pass16": ("生成 16 位强密码", "Generate 16-char password"),
    "safe.pass24": ("生成 24 位强密码", "Generate 24-char password"),
    "safe.strength": ("密码强度检测", "Password strength"),
    "safe.entropy": ("密码熵值估算", "Password entropy"),
    "safe.uuid": ("生成 UUID v4", "Generate UUID v4"),
    "safe.uuid10": ("批量生成 10 个 UUID", "Generate 10 UUIDs"),
    "safe.hexrand": ("随机十六进制串", "Random hex string"),
    "safe.verifyhash": ("哈希校验", "Verify hash"),
    "safe.filemd5": ("文件 MD5", "File MD5"),
    "safe.filesha256": ("文件 SHA-256", "File SHA-256"),
    "safe.pii": ("敏感信息检测", "Detect sensitive data"),
    "safe.idcard": ("身份证号校验", "Validate ID number"),
    "safe.luhn": ("银行卡号校验", "Validate card number"),
    "safe.urldanger": ("URL 安全检查", "URL safety check"),
    "safe.weakpass": ("弱口令匹配", "Weak password check"),
    "safe.clearcookie": ("清除 Cookie", "Clear cookies"),
    "safe.clearhistory": ("清除浏览历史", "Clear history"),
    "safe.clearcache": ("清除缓存", "Clear cache"),
    "safe.adblock": ("广告拦截开关", "Toggle ad blocking"),
    "safe.adstats": ("拦截统计", "Blocking stats"),
    "safe.adlog": ("拦截日志", "Blocking log"),
    "safe.clearsites": ("清除全部站点数据", "Clear all site data"),
    "safe.privacyreport": ("隐私报告", "Privacy report"),
    "safe.cleanup": ("一键隐私清理", "One-click privacy cleanup"),
}


def q(s):
    return s.replace("\\", "\\\\").replace('"', '\\"')


def main():
    lines = []
    lines.append("")
    lines.append("        // ── 工具箱：类别 ──")
    lines.append('        ["toolbox.menu"] = new[] { "工具箱", "Toolbox" },')
    lines.append('        ["toolbox.window"] = new[] { "{0} · 工具箱", "{0} · Toolbox" },')
    lines.append('        ["toolbox.title"] = new[] { "工具箱", "Toolbox" },')
    lines.append('        ["tool.title"] = new[] { "工具箱 · {0}", "Toolbox · {0}" },')
    lines.append('        ["toolbox.cat.ops"] = new[] { "运维 / IT", "Ops / IT" },')
    lines.append('        ["toolbox.cat.web"] = new[] { "网页 / 浏览", "Web / Browsing" },')
    lines.append('        ["toolbox.cat.text"] = new[] { "文本 / 日常", "Text / Everyday" },')
    lines.append('        ["toolbox.cat.safe"] = new[] { "安全 / 隐私", "Security / Privacy" },')
    lines.append('        ["toolbox.count"] = new[] { "共 {0} 项功能", "{0} tools" },')
    lines.append('        ["toolbox.doneTitle"] = new[] { "{0} · 完成", "{0} · Done" },')
    lines.append('        ["toolbox.confirmTitle"] = new[] { "{0} · 请确认", "{0} · Confirm" },')
    lines.append('        ["toolbox.inputTitle"] = new[] { "{0} · 输入", "{0} · Input" },')
    lines.append('        ["toolbox.resultTitle"] = new[] { "{0} · 结果", "{0} · Result" },')
    lines.append('        ["toolbox.run"] = new[] { "执行", "Run" },')
    lines.append('        ["toolbox.copy"] = new[] { "复制结果", "Copy result" },')
    lines.append('        ["toolbox.copied"] = new[] { "已复制到剪贴板。", "Copied to clipboard." },')
    lines.append('        ["toolbox.clear"] = new[] { "清空", "Clear" },')
    lines.append('        ["toolbox.close"] = new[] { "关闭", "Close" },')
    lines.append('        ["toolbox.empty"] = new[] { "请先填内容。", "Enter something first." },')
    lines.append("")
    lines.append("        // ── 工具箱：100 项功能 ──")
    for k, (zh, en) in T.items():
        lines.append(f'        ["tool.{k}"] = new[] {{ "{q(zh)}", "{q(en)}" }},')

    out = os.path.join(ROOT, "_lang_tools.txt")
    with io.open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print("wrote", out, len(T), "items")


if __name__ == "__main__":
    main()
