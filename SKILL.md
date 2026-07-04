# SKILL: 基于 Avalonia NativeWebView 构建统一 MarkdownView 控件

## 目标

构建跨平台 `MarkdownView` 控件，封装 `NativeWebView`，支持 Desktop (WebView2)、Browser (WASM iframe)、Android、iOS。核心原则：**写一次控件，各平台直接引用**。

---

## 1. 项目结构

```
AvalonMarkdown.slnx
├── AvalonMarkdown/                          # 共享库 (net10.0)
│   ├── AvalonMarkdown.csproj
│   ├── Views/
│   │   ├── MarkdownView.axaml + .cs         # ← 核心控件（无头）
│   ├── Services/
│   │   ├── IWebViewSourceProvider.cs
│   │   └── EmbeddedHtmlSourceProvider.cs    # 内联 HTML/CSS/JS
│   └── Assets/web/
│       ├── index.html                       # 模板（CDN 外链由代码替换为内联）
│       ├── renderer.css
│       └── renderer.js                      # 导出 setTheme / renderMarkdown 等
├── AvalonMarkdown.Test.Shared/              # 测试共享 (App + 演示 UI)
├── AvalonMarkdown.Test.Desktop/
├── AvalonMarkdown.Test.Browser/
├── AvalonMarkdown.Test.Android/
└── AvalonMarkdown.Test.iOS/
```

---

## 2. 核心架构

### 2.1 三路加载策略

唯一平台分支发生在 `InitializeWebViewAsync()` 和 `RestartPreviewAsync()` 中：

```csharp
if (IsDesktop)
    // file:/// 临时文件 — CDN 完整执行，WebView2 最佳兼容
else if (OperatingSystem.IsBrowser())
    // about:blank → document.write — WASM iframe 兼容
else
    // data:text/html;base64 — Android/iOS 原生 WebView（无注入长度限制）
```

**关键 API：**
```csharp
private static bool IsDesktop =>
    Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime;
```

### 2.2 EmbeddedHtmlSourceProvider

读取嵌入式资源 → 内联 CSS/JS → 注入系统主题：

```csharp
public string GetHtmlContent()
{
    var html = _html.Value;  // Lazy<string> 构建时读 AssetLoader
    var themeClass = GetCurrentTheme() == "light" ? "theme-light" : "theme-dark";
    return html.Replace("class=\"theme-dark\"", $"class=\"{themeClass}\"");
}

private static string BuildHtml()
{
    var html = ReadAsset("avares://AvalonMarkdown/Assets/web/index.html");
    // 1. 替换 <link href="renderer.css"> → <style>{css}</style>
    // 2. 替换 <script src="renderer.js"> → <script>{js}</script>
    // 3. CDN <script src> 和 <link href> 保留（按需网络加载，仅 KaTeX 字体）
    return html;
}
```

`AvaloniaResource` 通配 `Assets/**`，`AssetLoader.Open(new Uri(uri))` 运行时读取。

### 2.3 MarkdownView.axaml 布局（无头）

```xml
<DockPanel>
  <Border x:Name="ErrorPanel" DockPanel.Dock="Bottom" IsVisible="False">
    ⚠ ErrorTitle + ErrorMessage + ✕ Dismiss
  </Border>
  <Grid x:Name="WebViewHost">
    <NativeWebView />
  </Grid>
</DockPanel>
```

- 无工具栏，纯预览
- ErrorPanel 初始隐藏，通过 `ShowError()` / `HideError()` 控制

### 2.4 构造流程

```
MarkdownView(sourceProvider)
  ├── InitializeComponent()
  ├── CreateWebView()            → new NativeWebView { Background = 主题色 }
  ├── WireEvents()               → NavigationCompleted + WebViewMessages + Click + Viewport
  ├── RegisterInstance()         → 加入静态 WeakReference 列表，首次实例化时订阅主题事件
  ├── ApplyThemeColors(theme)    → 设置 WebViewHost / _webView 背景色
  └── InitializeWebViewAsync()   → 按平台加载 HTML
```

### 2.5 平台跳转与就绪态

```csharp
// 初始加载
InitializeWebViewAsync()
  └─ 设 _webView.Source = uri  // 按平台选 file:// / about:blank / data:base64
        └─ NavigationCompleted 事件异步触发
              └─ OnNavigationCompleted()
                    ├─ Desktop/原生Mobile → _htmlInjected=true → ForceLayout() → SetReady()
                    └─ Browser (WASM)     → InjectViaDocumentWriteAsync() → SetReady()

// 重启
RestartPreviewAsync()
  ├─ _ready=false, _htmlInjected=false
  ├─ Desktop     → 写新临时文件 → 设 _webView.Source
  ├─ Browser     → InjectViaDocumentWriteAsync()
  └─ Android/iOS → InvokeScript("location.href='data:...'")
```

### 2.6 document.write 注入（仅 Browser/WASM）

```csharp
async Task InjectViaDocumentWriteAsync()
{
    escaped = html.Replace("\\","\\\\").Replace("'","\\'")
                  .Replace("\r\n","\\n").Replace("\n","\\n")
    script = $"document.open();document.write('{escaped}');document.close();"
    var result = _webView.InvokeScript(script);
    if (result is Task t) await t.WaitAsync(5s);
    ForceLayout();
    await Task.Delay(2000);
    SetReady();
}
```

### 2.7 布局修正 — 事件驱动

```csharp
WebViewHost.EffectiveViewportChanged += OnHostViewportChanged;

void OnHostViewportChanged(object?, EffectiveViewportChangedEventArgs e)
{
    if (e.EffectiveViewport.Width <= 0 || e.EffectiveViewport.Height <= 0) return;
    WebViewHost.EffectiveViewportChanged -= OnHostViewportChanged;  // 一次性
    _webView.InvalidateMeasure(); _webView.InvalidateArrange();
}
```

关键：不使用 `Task.Delay` 等布局，而是等待布局系统真正计算出尺寸。

---

## 3. 主题系统 — WeakReference 响应式推送

### 3.1 静态管理

```csharp
private static readonly List<WeakReference<MarkdownView>> _instances = new();
private static readonly object _lock = new();
private static bool _themeSubscribed;
```

### 3.2 实例注册

```csharp
private void RegisterInstance()
{
    lock (_lock)
    {
        _instances.RemoveAll(wr => !wr.TryGetTarget(out _));
        _instances.Add(new WeakReference<MarkdownView>(this));
        if (!_themeSubscribed && Application.Current is { } app)
        {
            app.ActualThemeVariantChanged += OnGlobalThemeChanged;
            _themeSubscribed = true;
        }
    }
}
```

### 3.3 全局主题推送

```csharp
private static void OnGlobalThemeChanged(object? sender, EventArgs e)
{
    var theme = GetCurrentTheme();
    lock (_lock)
    {
        for (int i = _instances.Count - 1; i >= 0; i--)
            if (_instances[i].TryGetTarget(out var view))
                _ = Dispatcher.UIThread.InvokeAsync(() => view.ApplyThemePush(theme));
            else
                _instances.RemoveAt(i);
    }
}
```

### 3.4 实例级推送

```csharp
private async void ApplyThemePush(string theme)
{
    ApplyThemeColors(theme);
    if (_ready) await InvokeScriptSafeAsync($"setTheme('{theme}')");
}
```

### 3.5 主题查询（必须用 ActualThemeVariant）

```csharp
private static string GetCurrentTheme()
{
    var app = Application.Current;
    if (app == null) return "dark";

    // ❌ 不可用 RequestedThemeVariant — App.axaml 设了 "Default"
    //    即使系统是 Light 也会返回 Default，导致误判
    var variant = app.ActualThemeVariant;  // ✅ 返回解析后的最终主题
    if (variant == ThemeVariant.Light) return "light";
    if (variant == ThemeVariant.Dark) return "dark";
    return "dark";
}
```

### 3.6 全链路时序

```
1. EmbeddedHtmlSourceProvider.GetHtmlContent()
   → GetCurrentTheme() → 替换 HTML class → 首次加载即匹配系统主题

2. MarkdownView 构造函数
   → ApplyThemeColors(GetCurrentTheme()) → WebViewHost / _webView 背景色

3. SetReady()
   → PushThemeToWebView(GetCurrentTheme()) → JS setTheme() + Mermaid

4. 系统主题变化
   → ActualThemeVariantChanged → OnGlobalThemeChanged
   → 遍历所有活动 WeakReference → Dispatcher.UIThread.InvokeAsync
   → 每个实例 ApplyThemePush(theme) → 背景色 + JS setTheme
```

---

## 4. 公开 API

| 成员 | 说明 |
|------|------|
| `OnReady` (event) | 控件完全就绪 |
| `ErrorOccurred` (event) | 内部可恢复错误 |
| `RenderMarkdownAsync(string?)` | 渲染 Markdown |
| `RestartPreviewAsync()` | 重启预览器（重新加载 HTML+JS） |
| `ApplyConfigAsync(string)` | 执行 JS (如 setPreviewConfig) |
| `InvokeScriptAsync(string)` | 执行任意 JS |

---

## 5. 错误处理

双层面板：
- **C# 底部面板**：`InvokeScriptSafeAsync` 捕获异常 → `ShowError()` → `ErrorPanel`
- **JS 页面浮层**：`window.onerror` / `unhandledrejection` → `showErrorOverlay()` → `postMessage()` → C# `OnWebViewMessage`

所有 `InvokeScript` 经 `InvokeScriptSafeAsync` 包装，5 秒超时 + try-catch。

---

## 6. HTML 与 JS 渲染引擎

### 6.1 index.html

```html
<html class="theme-dark">
<head>
  <link href="katex.min.css">        <!-- CDN，保留 -->
  <link rel="stylesheet" href="renderer.css">  <!-- 被内联 -->
  <style>/* loading-placeholder + error-overlay */</style>
</head>
<body>
  <div id="preview"><p class="loading-placeholder">Loading Previewer…</p></div>
  <div id="error-overlay">...</div>
  <script src="renderer.js"></script>  <!-- 被内联 -->
</body>
</html>
```

### 6.2 renderer.js 导出接口

| 函数 | 说明 | C# 调用 |
|------|------|---------|
| `renderMarkdown(text)` | 渲染 Markdown | `InvokeScript` |
| `setTheme(theme)` | 切换主题 + 重建 Mermaid | `InvokeScript` |
| `setPreviewConfig(config)` | 更新预览配置 | `InvokeScript` |
| `showPreviewError(detail)` | 显示错误浮层 | `InvokeScript` |

关键实现：
- 从 `<html class>` 读取初始主题，非硬编码
- Mermaid 使用 `theme: 'base'` + 双套 `themeVariables`（深/浅）
- 主题切换时 `reRenderMarkdown()` 从 `preview.dataset.source` 重建全文

---

## 7. csproj 关键配置

```xml
<AvaloniaResource Include="Assets\**" />                       <!-- AssetLoader -->
<Content Include="Assets\web\*.html" CopyToOutputDirectory="PreserveNewest" />
<Content Include="Assets\web\*.css"  CopyToOutputDirectory="PreserveNewest" />
<Content Include="Assets\web\*.js"   CopyToOutputDirectory="PreserveNewest" />

<PackageReference Include="Avalonia.Controls.WebView" />       <!-- NativeWebView -->
<GeneratePackageOnBuild>True</GeneratePackageOnBuild>
<PackageId>AvalonMarkdown</PackageId>
```

---

## 8. 入口点分发

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

## 9. 已知问题与解决

| 问题 | 解决 |
|------|------|
| Desktop data: URI 导致 CDN 不加载 | Desktop 用 file:/// 临时文件 |
| Browser data: URI NavigationCompleted 不触发 | Browser 用 about:blank + document.write |
| iframe 居于左上角 | EffectiveViewportChanged 事件驱动 ForceLayout |
| Desktop document.write 导致 CDN 不执行 | Desktop 不走 document.write |
| Mermaid 主题切换 SVG 不刷新 | reRenderMarkdown() 重建全文 |
| Android CDN 被屏蔽 | 全部 CDN 库内联到 HTML |
| Android document.write 大 HTML 注入失败 | Android 用 data:text/html;base64 直接加载 |
| RequestedThemeVariant=Default 导致主题永远 dark | 用 ActualThemeVariant 获取解析后最终主题 |
