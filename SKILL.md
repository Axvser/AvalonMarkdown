# Markdown 预览控件实现指南

## 概述

AvalonMarkdown 基于 AvaloniaUI 的 `NativeWebView` 封装了一个统一的 `MarkdownView` 控件，支持 Desktop（WebView2）、Browser（WASM iframe）、Android、iOS 全平台。核心设计原则：**写一次控件，各平台直接引用**，消除条件编译和平台分叉。

---

## 1. 项目结构

```
AvalonMarkdown.slnx
├── AvalonMarkdown/                          # 共享主项目 (net10.0)
│   ├── App.axaml / App.axaml.cs             # 入口，运行时 ApplicationLifetime 分发
│   ├── ViewLocator.cs
│   ├── Views/
│   │   ├── MarkdownView.axaml + .cs         # ← 统一控件，写一次
│   │   ├── MainView.axaml + .cs             # 统一主视图（编辑器 + MarkdownView）
│   │   └── MainWindow.axaml + .cs           # Desktop 窗口容器
│   ├── ViewModels/
│   │   ├── MainViewModel.cs
│   │   └── PreviewConfigViewModel.cs
│   ├── Services/
│   │   ├── IWebViewSourceProvider.cs        # 接口：提供完整 HTML
│   │   └── EmbeddedHtmlSourceProvider.cs    # 实现：通过 AssetLoader 读取+内联
│   └── Assets/web/
│       ├── index.html                       # 预览页面模板
│       ├── renderer.css                     # 共享样式（双主题）
│       └── renderer.js                      # 共享渲染引擎（IIFE）
│
├── AvalonMarkdown.Desktop/                  # Desktop 启动项目
│   └── Program.cs
│
├── AvalonMarkdown.Browser/                  # Browser 启动项目 (WASM)
│   ├── Program.cs
│   └── wwwroot/
│       ├── index.html                       # WASM 宿主页
│       ├── main.js                          # WASM 启动脚本
│       └── app.css
│
├── AvalonMarkdown.Android/                  # Android 启动项目
├── AvalonMarkdown.iOS/                      # iOS 启动项目
└── Directory.Packages.props                 # 中心包版本管理
```

**关键原则**：所有平台引用同一个 `MarkdownView` 控件，无 `#if` 条件编译。

---

## 2. 架构设计

### 2.1 依赖注入

```
IWebViewSourceProvider (接口)
    └── EmbeddedHtmlSourceProvider (唯一实现)
            ├── 通过 AssetLoader 读取 avares:// 嵌入式资源
            ├── 内联 renderer.css → <style>
            ├── 内联 renderer.js → <script>
            └── 返回完整 HTML 字符串
```

### 2.2 视图层次

```
App.OnFrameworkInitializationCompleted()
    ├── IClassicDesktopStyleApplicationLifetime → MainWindow
    │       └── MainView (编辑器 + MarkdownView)
    └── ISingleViewApplicationLifetime → MainView
            └── MarkdownView (工具栏 + NativeWebView + 错误面板)
```

### 2.3 加载策略（唯一平台分支）

`MarkdownView` 中唯一的平台判断：

| 平台              | 加载方式                               | 原理                                                                                                                                |
| ----------------- | -------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| **Desktop** | `file:///` 临时文件                  | EmbeddedHtmlSourceProvider 生成 HTML → 写入`%TEMP%\AvalonMarkdown\preview_{timestamp}.html` → WebView2 导航到 `file:///` 路径 |
| **Browser** | `about:blank` + `document.write()` | 先导航到`about:blank`（触发 NavigationCompleted）→ 通过 InvokeScript("document.write(...)") 注入完整 HTML                        |

判断代码（仅 3 行）：

```csharp
private static bool IsDesktop =>
    Avalonia.Application.Current?.ApplicationLifetime
        is IClassicDesktopStyleApplicationLifetime;
```

---

## 3. 统一 HTML 来源

### 3.1 EmbeddedHtmlSourceProvider

```csharp
private static string BuildHtml()
{
    var html = ReadAsset("avares://AvalonMarkdown/Assets/web/index.html");
    var css  = ReadAsset("avares://AvalonMarkdown/Assets/web/renderer.css");
    var js   = ReadAsset("avares://AvalonMarkdown/Assets/web/renderer.js");

    html = html.Replace("<link rel=\"stylesheet\" href=\"renderer.css\">", $"<style>{css}</style>");
    html = html.Replace("<script src=\"renderer.js\"></script>", $"<script>{js}</script>");
    return html;
}
```

CDN 脚本（markdown-it、KaTeX、Mermaid 等）保持外部引用，在 Desktop `file:///` 和 Browser `document.write()` 中均能正常加载。

### 3.2 Desktop 路径

```
InitializeWebViewAsync()
  └── IsDesktop == true
      ├── WriteTempHtmlFile(html) → preview_143022001.html
      ├── _webView.Source = file:///C:/Users/.../preview_143022001.html
      ├── NavigationCompleted 触发
      ├── 等待 2s（CDN 脚本就绪）
      └── SetReady()
```

临时文件带时间戳防缓存，自动清理 30 秒前的旧文件。

### 3.3 Browser 路径

```
InitializeWebViewAsync()
  └── IsDesktop == false
      ├── _webView.Source = about:blank
      ├── NavigationCompleted 触发（about:blank 加载完成）
      ├── OnNavigationCompleted()
      │     └── InjectViaDocumentWriteAsync()
      │           ├── 转义 HTML（处理 \\ ' \n）
      │           ├── InvokeScript("document.open();document.write('...');document.close();")
      │           ├── ForceLayout()  ← 修正 iframe 尺寸
      │           ├── 等待 2s
      │           └── SetReady()
      └── 安全网：5 秒后强制注入
```

### 3.4 安全网机制

两个场景都有 5 秒后备定时器：

```csharp
_ = Task.Run(async () =>
{
    await Task.Delay(5000);
    if (!_ready)
        await Dispatcher.UIThread.InvokeAsync(/* 注入或 SetReady */);
});
```

重启时使用 `_loadSequence` 序列号防止过期的安全网干扰新的重启。

---

## 4. MarkdownView 控件

### 4.1 XAML 布局

```
DockPanel
  ├── ToolbarPanel
  │     ├── "Markdown 预览" 标题
  │     ├── "⟳ 重启预览" 按钮
  │     ├── "⊟ 隐藏工具栏" / "⊞ 显示工具栏" 按钮
  │     └── StatusText（初始化… / 就绪 / 已渲染）
  ├── ErrorPanel（初始隐藏）
  │     ├── ⚠ 图标 + 错误标题 + 详情
  │     └── "✕ 关闭" 按钮
  └── WebViewHost → NativeWebView
```

### 4.2 公开 API

| 成员                             | 类型          | 说明                                |
| -------------------------------- | ------------- | ----------------------------------- |
| `NavigationCompleted`          | event         | WebView 就绪（含 CDN 脚本加载完成） |
| `ErrorOccurred`                | event         | 内部可恢复错误                      |
| `ShowToolbar`                  | bool property | 有头/无头模式切换                   |
| `RenderMarkdownAsync(string?)` | Task          | 渲染 Markdown 内容                  |
| `RestartPreviewAsync()`        | Task          | 重启预览器                          |
| `ApplyConfigAsync(string)`     | Task          | 应用预览配置                        |
| `InvokeScriptAsync(string)`    | Task<string?> | 执行自定义 JS                       |

### 4.3 有头/无头模式

```csharp
markdownView.ShowToolbar = false;  // 代码控制
// UI 切换：工具栏右上角 "⊟ 隐藏工具栏" 按钮
```

---

## 5. 渲染引擎 (renderer.js)

### 5.1 架构

单一 IIFE，通过 `window` 导出接口：

```javascript
(function() {
    // Console → C# bridge (chrome.webview.postMessage)
    // window.onerror + unhandledrejection 全局捕获
    // markdown-it + 插件
    // KaTeX 内联/块级公式
    // Mermaid 图表
    // highlight.js VS-style 高亮
    // 代码块: 语言标签 + 复制 + 高度调节
    // 主题管理

    window.renderMarkdown = function(text) { ... };
    window.setTheme = function(theme) { ... };
    window.setPreviewConfig = function(config) { ... };
    window.showPreviewError = function(detail) { ... };
    window.dismissErrorOverlay = function() { ... };
})();
```

### 5.2 导出接口

| 全局 API                            | 用途                         | 调用方          |
| ----------------------------------- | ---------------------------- | --------------- |
| `window.renderMarkdown(text)`     | 渲染 Markdown 到`#preview` | C# InvokeScript |
| `window.setTheme(theme)`          | 切换 dark/light              | C#              |
| `window.setPreviewConfig(config)` | 更新配置                     | C#              |
| `window.showPreviewError(detail)` | 显示 JS 错误浮层             | C# / JS 内部    |
| `window.dismissErrorOverlay()`    | 关闭错误浮层                 | JS 内部         |

---

## 6. C# ↔ JS 通信

### 6.1 C# → JS: InvokeScript

```csharp
var result = _webView.InvokeScript("renderMarkdown('...')");
if (result is Task t)
    await t.WaitAsync(TimeSpan.FromSeconds(5));
```

所有调用经 `InvokeScriptSafeAsync` 包装，含异常捕获和 5 秒超时。

### 6.2 JS → C#: chrome.webview.postMessage

```javascript
try { window.chrome.webview.postMessage('[ERR] ...'); } catch(e) {}
```

C# 端通过反射订阅 `WebViewMessages` 事件（兼容不支持该事件的平台）。

---

## 7. 错误处理

### 7.1 双层面板

| 层面                  | 位置                         | 来源                               |
| --------------------- | ---------------------------- | ---------------------------------- |
| **C# 错误面板** | MarkdownView 底部 ErrorPanel | InvokeScript 异常、文件缺失、超时  |
| **JS 错误浮层** | HTML 页面底部#error-overlay  | window.onerror、unhandledrejection |

### 7.2 错误捕获链

```
JS 运行时异常 → window.onerror → HTML 错误浮层 → postMessage → C# 底部面板
C# InvokeScript 异常 → InvokeScriptSafeAsync try-catch → C# 底部面板
```

---

## 8. 重构前后对比

| 对比项          | 重构前 (OLD)                                  | 重构后 (NEW)                              |
| --------------- | --------------------------------------------- | ----------------------------------------- |
| 条件编译        | `#if BROWSER`                               | 无，运行时 ApplicationLifetime 分发       |
| 视图            | MainWindow + MainView(占位) + BrowserMainView | 统一 MainView + MainWindow(仅窗口)        |
| HTML 加载       | Desktop: file:/// Browser: standalone-md.html | 统一 EmbeddedHtmlSourceProvider 内联+注入 |
| Browser WebView | JS setTimeout 设置 iframe.src                 | C# about:blank + document.write()         |
| 服务接口        | IMarkdownPreviewService                       | IWebViewSourceProvider                    |
| Browser 本地库  | wwwroot/lib/ CDN 副本                         | CDN 在线引用                              |
| 布局修正        | 无，需手动调窗口                              | ForceLayout() 注入后立即执行              |
| 安全网          | 无                                            | 5 秒后备定时器 + _loadSequence 防串扰     |
| 错误处理        | 零散 try-catch                                | 双层面板 + 全局 onerror + 超时检测        |

### 已删除文件

| 文件                                      | 替代                       |
| ----------------------------------------- | -------------------------- |
| `Views/BrowserMainView.axaml` + `.cs` | 统一 MainView              |
| `Services/IMarkdownPreviewService.cs`   | 内聚到 MarkdownView        |
| `Services/WebViewPreviewService.cs`     | 同上                       |
| `Services/FileWebViewSourceProvider.cs` | EmbeddedHtmlSourceProvider |
| `Services/UrlWebViewSourceProvider.cs`  | 同上                       |
| `wwwroot/standalone-md.html`            | 同上                       |
| `wwwroot/web/` + `wwwroot/lib/`       | CDN 在线引用               |

---

## 9. 常见问题

| 问题                                         | 原因                                 | 解决                                |
| -------------------------------------------- | ------------------------------------ | ----------------------------------- |
| Browser data: URI 不触发 NavigationCompleted | iframe 中 data: URI 不触发 load 事件 | 用 about:blank + document.write()   |
| Desktop data: URI CDN 不加载                 | data: URI 中 script src 可能被阻止   | Desktop 用 file:/// 临时文件        |
| Desktop document.write() 黑屏                | WebView2 对注入式 CDN 处理不同       | Desktop 不走 document.write()       |
| iframe 居于左上角                            | 布局未及时更新 DOM 尺寸              | 注入后立即 ForceLayout()            |
| 重启不变                                     | 同名文件缓存/安全网过期              | 时间戳文件名 + _loadSequence 安全网 |
