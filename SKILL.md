# SKILL: 基于 Avalonia NativeWebView 构建统一 MarkdownView 控件

## 目标

构建一个跨平台的 `MarkdownView` 控件，封装 `NativeWebView`，支持 Desktop (WebView2)、Browser (WASM iframe)、Android、iOS。核心原则：**写一次控件，各平台直接引用**。

---

## 1. 项目结构

```
AvalonMarkdown.slnx
├── AvalonMarkdown/                          # 共享主项目 (net10.0)
│   ├── AvalonMarkdown.csproj
│   ├── App.axaml / App.axaml.cs
│   ├── Views/
│   │   ├── MarkdownView.axaml + .cs         # ← 核心控件
│   │   ├── MainView.axaml + .cs             # 演示主视图
│   │   └── MainWindow.axaml + .cs           # Desktop 演示窗口
│   ├── Services/
│   │   ├── IWebViewSourceProvider.cs
│   │   └── EmbeddedHtmlSourceProvider.cs
│   └── Assets/web/
│       ├── index.html
│       ├── renderer.css
│       ├── renderer.js
│       └── lib/                              # 内联的 CDN 库
│           ├── katex.min.css / katex.min.js
│           ├── markdown-it.min.js + 插件
│           ├── highlight.min.js
│           └── mermaid.min.js
├── AvalonMarkdown.Desktop/
├── AvalonMarkdown.Browser/
├── AvalonMarkdown.Android/
└── AvalonMarkdown.iOS/
```

---

## 2. 关键文件与职责

### 2.1 EmbeddedHtmlSourceProvider

读取嵌入式资源 → 内联所有 CSS/JS → 注入系统主题 class。

```csharp
public class EmbeddedHtmlSourceProvider : IWebViewSourceProvider
{
    private readonly Lazy<string> _html;

    public string GetHtmlContent()
    {
        var html = _html.Value;
        var theme = GetCurrentTheme() == "light" ? "theme-light" : "theme-dark";
        return html.Replace("class=\"theme-dark\"", $"class=\"{theme}\"");
    }

    private static string BuildHtml()
    {
        var html = ReadAsset("avares://AvalonMarkdown/Assets/web/index.html");
        // 内联 renderer.css / renderer.js
        // 内联 lib/ 下全部 CDN 库（katex, markdown-it, highlight, mermaid）
        // 剔除所有 CDN <script src> 和 <link href>，
        // 替换为内联 <style> / <script>
        return html;  // ← 完全自包含，零网络请求
    }
}
```

关键：`AvaloniaResource` 在 csproj 中通配包含所有 Assets，文件通过 `AssetLoader` 在运行时读取，不依赖文件系统路径。

### 2.2 MarkdownView.axaml 布局

```
DockPanel
  ├── ToolbarPanel (DockPanel.Dock="Top")
  │     ├── TextBlock "Markdown"
  │     ├── Button "Reload"
  │     ├── Button "Hide" / "Show"
  │     └── StatusText
  ├── ErrorPanel (DockPanel.Dock="Bottom", 初始隐藏)
  │     ├── ⚠ + ErrorTitle + ErrorMessage
  │     └── Button "✕"
  └── WebViewHost (Grid, 背景由代码控制)
        └── NativeWebView (Stretch)
```

工具栏/错误面板的 Background/BorderBrush 不由 XAML 硬编码，由 `ApplyThemeColors()` 运行时设置。

### 2.3 MarkdownView.axaml.cs 核心逻辑

**构造流程：**

```
构造函数
  ├── InitializeComponent()
  ├── CreateWebView()              ─ ThemeVariant 决定 _webView.Background
  ├── WireEvents()                 ─ 绑定 NativeWebView 事件 + 主题监听
  ├── InitializeWebViewAsync()     ─ 加载 HTML
  ├── ApplyThemeColors()           ─ 工具栏/WebViewHost/StatusText 配色
  └── StatusText = "Loading…"
```

**双路径加载策略（唯一平台分支）：**

```csharp
private static bool IsDesktop =>
    Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime;

async Task InitializeWebViewAsync()
{
    _htmlContent = _sourceProvider.GetHtmlContent();
    if (IsDesktop)
    {
        // Desktop: 写临时文件 → file:/// 导航
        var file = WriteTempHtmlFile(_htmlContent);
        _webView.Source = new Uri("file:///" + file.Replace('\\', '/'));
    }
    else
    {
        // Browser/Android: about:blank → document.write 注入
        _webView.Source = new Uri("about:blank");
    }
    5秒安全网: if (!_ready) { 强制 InjectViaDocumentWriteAsync / SetReady }
}
```

**临时文件管理（Desktop 专用）：**

```csharp
static string WriteTempHtmlFile(string html)
{
    Dir.Create(dir = %TEMP%\AvalonMarkdown)
    path = dir / preview_{HHmmssfff}.html
    File.WriteAllText(path, html)
    清理 30 秒前的旧文件
    return path  // 时间戳防 WebView2 缓存
}
```

**document.write 注入（Browser/Android 专用）：**

```csharp
async Task InjectViaDocumentWriteAsync()
{
    escaped = html.Replace("\\","\\\\").Replace("'","\\'")
                  .Replace("\r\n","\\n").Replace("\n","\\n").Replace("\r","\\n")
    script = $"document.open();document.write('{escaped}');document.close();"
    result = _webView.InvokeScript(script)
    if (result is Task t) await t.WaitAsync(5s)  // Desktop: Task; Browser: null
    ForceLayout()
    await Task.Delay(2000)  // 等 CDN 脚本（已内联，等待可缩短）
    SetReady()
}
```

**导航完成处理：**

```csharp
void OnNavigationCompleted(object?, EventArgs)
{
    if (_htmlInjected) return;
    if (IsDesktop)
    {
        ForceLayout();
        Task.Run(async { await 2s; UIThread.SetReady(); });
    }
    else  // Browser/Android → 执行 inject
        _ = InjectViaDocumentWriteAsync();
}
```

**SetReady & 安全网：**

```csharp
void SetReady()
{
    _ready = true; StatusText = "Ready";
    _ = ApplySystemThemeAsync();  // 同步主题到 WebView
    OnReady?.Invoke(this, EventArgs.Empty);
    if (_pendingMarkdown != null) 处理积压内容
}

// 初始化/重启各有独立的 5s 安全网
async Task StartRestartSafetyNetAsync(int seq)
{
    await 5s;
    if (!_ready && seq == _loadSequence)  // 防过期安全网
        UIThread.InvokeAsync(InjectViaDocumentWriteAsync / SetReady)
}
```

**布局修正（关键！Browser iframe 尺寸问题）：**

```csharp
// 事件驱动：WebViewHost 首次获得有效尺寸时触发一次
WebViewHost.EffectiveViewportChanged += OnHostViewportChanged;

void OnHostViewportChanged(object?, EffectiveViewportChangedEventArgs e)
{
    if (e.EffectiveViewport.Width <= 0 || e.EffectiveViewport.Height <= 0) return;
    WebViewHost.EffectiveViewportChanged -= OnHostViewportChanged;  // 一次性
    _webView.InvalidateMeasure();
    _webView.InvalidateArrange();
}
```

不使用 `Task.Delay` 等待布局，而是等布局系统真正计算出尺寸后再修正。

### 2.4 公开 API

| 成员 | 说明 |
|------|------|
| `ShowToolbar` (property) | 显示/隐藏工具栏 |
| `OnReady` (event) | 控件完全就绪 |
| `ErrorOccurred` (event) | 内部可恢复错误 |
| `RenderMarkdownAsync(string?)` | 渲染 Markdown |
| `RestartPreviewAsync()` | 重启预览器 |
| `ApplyConfigAsync(string)` | 执行 JS (如 setPreviewConfig) |
| `InvokeScriptAsync(string)` | 执行任意 JS |

### 2.5 主题系统

**全链路：**

```
1. EmbeddedHtmlSourceProvider.GetHtmlContent()
   → 读取 Application.Current.ActualThemeVariant
   → 替换 HTML 中 class="theme-dark" → class="theme-{light|dark}"
   → 使 WebView 首次加载就与系统主题一致（加载文字颜色/背景）

2. MarkdownView 构造函数
   → ApplyThemeColors(GetCurrentTheme())
   → 设置 ToolbarPanel / WebViewHost / _webView / StatusText 背景/前景色

3. SetReady()
   → ApplySystemThemeAsync()
   → 调用 setTheme('dark'|'light') 到 WebView JS

4. Application.Current!.ActualThemeVariantChanged
   → 自动触发 ApplySystemThemeAsync()
```

**C# 端 ApplyThemeColors：**

```csharp
void ApplyThemeColors(string theme)
{
    if (theme == "light") {
        ToolbarPanel.Background = #f0f0f0;  ToolbarPanel.BorderBrush = #d4d4d4;
        StatusText.Foreground = #666;
        WebViewHost.Background = #ffffff;  _webView.Background = #ffffff;
    } else {
        ToolbarPanel.Background = #252526;  ToolbarPanel.BorderBrush = #3c3c3c;
        StatusText.Foreground = #888;
        WebViewHost.Background = #1e1e1e;  _webView.Background = #1e1e1e;
    }
}
```

**JS 端 setTheme：**

```javascript
function setTheme(theme) {
    document.documentElement.className = 'theme-' + theme;
    mermaid.initialize(getMermaidThemeVars(theme));
    reRenderMarkdown();  // 重建所有 Mermaid 图表（之前渲染的 SVG 需重建）
}
// 两套 themeVariables 分别匹配深色/浅色 CSS 变量
```

### 2.6 错误处理

双层面板：

- **C# 底部面板**：`InvokeScriptSafeAsync` 捕获异常 → `ShowError()` → `ErrorPanel`
- **JS 页面浮层**：`window.onerror` / `unhandledrejection` → `showErrorOverlay()` → `postMessage()` → C#

所有 `InvokeScript` 经过 `InvokeScriptSafeAsync` 包装，5 秒超时 + try-catch。

---

## 3. HTML 与 JS 渲染引擎

### 3.1 index.html

```html
<html class="theme-dark">  <!-- class 由 EmbeddedHtmlSourceProvider 替换 -->
<head>
  <link href="katex.min.css">  <!-- 被 EmbeddedHtmlSourceProvider 内联 -->
  <link href="renderer.css">   <!-- 同上 -->
  <style>...</style>
</head>
<body>
  <div id="preview"><p class="loading-placeholder">Loading Previewer…</p></div>
  <div id="error-overlay">...</div>
  <!-- 所有 CDN script 被 EmbeddedHtmlSourceProvider 替换为内联 -->
  <script src="renderer.js"></script>  <!-- 内联 -->
</body>
</html>
```

### 3.2 renderer.js 导出接口

| 函数 | 说明 | C# 调用 |
|------|------|---------|
| `renderMarkdown(text)` | 渲染 Markdown | `InvokeScript` |
| `setTheme(theme)` | 切换主题 + 重建 Mermaid | `InvokeScript` |
| `setPreviewConfig(config)` | 更新配置 | `InvokeScript` |
| `showPreviewError(detail)` | 显示错误浮层 | `InvokeScript` |

关键实现要点：
- 从 `<html class>` 读取初始主题，而非硬编码 `'dark'`
- Mermaid 使用 `theme: 'base'` + 自定义 `themeVariables` 两套配色（深/浅）
- 主题切换时调用 `reRenderMarkdown()` 从 `preview.dataset.source` 重建全文

---

## 4. 入口点分发

```csharp
// App.axaml.cs — 无 #if 条件编译
void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        desktop.MainWindow = new MainWindow();
    else if (ApplicationLifetime is ISingleViewApplicationLifetime sv)
        sv.MainView = new MainView();
}
```

---

## 5. csproj 关键配置

```xml
<AvaloniaResource Include="Assets\**" />           <!-- AssetLoader 读取 -->
<Content Include="Assets\web\*.html" CopyToOutputDirectory="PreserveNewest" />
<Content Include="Assets\web\*.css" CopyToOutputDirectory="PreserveNewest" />
<Content Include="Assets\web\*.js" CopyToOutputDirectory="PreserveNewest" />
<!-- NuGet -->
<GeneratePackageOnBuild>True</GeneratePackageOnBuild>
<PackageId>AvalonMarkdown.Controls</PackageId>
```

---

## 6. 已知问题与解决

| 问题 | 解决 |
|------|------|
| Desktop data: URI CDN 不加载 | Desktop 用 file:/// 临时文件 |
| Browser data: URI 不触发 NavigationCompleted | 用 about:blank + document.write |
| iframe 居于左上角 | EffectiveViewportChanged 事件驱动 ForceLayout |
| Desktop document.write CDN 不执行 | Desktop 不走 document.write |
| Mermaid 主题切换不更新 | reRenderMarkdown() 重建 SVG |
| Android CDN 被屏蔽 | 全部 CDN 库内联到 HTML |
