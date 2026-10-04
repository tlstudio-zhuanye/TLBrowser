# TL 浏览器

内置 TLSTUDIO 双站入口的桌面浏览器 —— 功能多、更安全、无广告。

- 官网（下载页）：https://tlstudio-zhuanye.github.io/TLBrowser/
- 更新清单：https://raw.githubusercontent.com/tlstudio-zhuanye/TLBrowser/main/version.json
- 更新通知后端：`https://api.tlstudio.cn/tlbrowser/version.json`（Cloudflare）

## 特性

- **广告与跟踪拦截**：内置 115+ 规则，可一键开关
- **主页守护**：起始页永远是指定官网，外部改主页自动还原
- **下载中心**：下载进度 + 历史记录，Ctrl+J 调出
- **双站对照**：tlstudio.cn 与 tldoublerstudio.cn 并排显示
- **自动更新通知**：启动时检查 `version.json`，有新版本提示下载

## 下载

最新版本请前往 [Releases](https://github.com/tlstudio-zhuanye/TLBrowser/releases) 下载 `TLBrowser.exe`（单文件免安装）。

## 版本发布流程

1. 打包新 exe
2. 在「TL 管理后台」里「推送新版本更新通知」，填写版本号 / 下载直链 / 更新说明
3. 客户端启动时自动检测到更新并提示

## version.json 格式

```json
{
  "version": "1.0.0",
  "url": "https://github.com/tlstudio-zhuanye/TLBrowser/releases/download/v1.0.0/TLBrowser.exe",
  "notes": "更新说明",
  "publishedAt": "2026-10-04"
}
```
