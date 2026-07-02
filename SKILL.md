# AvalonMarkdown — 统一 Markdown 预览控件完全指南

## 概述

本文档详细说明如何基于 AvaloniaUI 的 NativeWebView 构建一个跨平台的 MarkdownView 控件。
核心目标：**写一次控件，各平台直接引用**，消除条件编译。

---

## 1. 项目搭建

### 1.1 解决方案结构

`
AvalonMarkdown.slnx
├── AvalonMarkdown/                          # 共享主项目 (net10.0)
│   ├── AvalonMarkdown.csproj
│   ├── App.axaml / App.axaml.cs
│   ├── ViewLocator.cs
│   ├── Views/
│   │   ├── MarkdownView.axaml + .cs         # 核心控件
│   │   ├── MainView.axaml + .cs             # 统一主视图
│   │   └── MainWindow.axaml + .cs           # Desktop 窗口
│   ├── ViewModels/
│   │   ├── MainViewModel.cs
│   │   └── PreviewConfigViewModel.cs
│   ├── Services/
│   │   ├── IWebViewSourceProvider.cs
│   │   └── EmbeddedHtmlSourceProvider.cs
│   └── Assets/web/
│       ├── index.html
│       ├── renderer.css
│       └── renderer.js
│
├── AvalonMarkdown.Desktop/                  # Desktop 启动项目
│   ├── AvalonMarkdown.Desktop.csproj
│   └── Program.cs
│
├── AvalonMarkdown.Browser/                  # Browser 启动项目 (WASM)
│   ├── AvalonMarkdown.Browser.csproj
│   ├── Program.cs
│   └── wwwroot/
│       ├── index.html
│       ├── main.js
│       └── app.css
│
├── AvalonMarkdown.Android/
├── AvalonMarkdown.iOS/
└── Directory.Packages.props
`

### 1.2 共享项目 csproj

添加 Avalonia.Controls.WebView 包引用，web 资源标记为 AvaloniaResource 和 Content。

`xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <AvaloniaResource Include="Assets\**" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" />
    <PackageReference Include="Avalonia.Themes.Fluent" />
    <PackageReference Include="Avalonia.Controls.WebView" />
    <PackageReference Include="CommunityToolkit.Mvvm" />
  </ItemGroup>
  <ItemGroup>
    <Content Include="Assets\web\index.html" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="Assets\web\renderer.css" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="Assets\web\renderer.js" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
`

AvaloniaResource 使文件可通过 AssetLoader 在运行时读取（全平台通用）。
Content + CopyToOutputDirectory 使文件在 Desktop 发布时复制到输出目录。

### 1.3 Desktop 项目 csproj

`xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia.Desktop" />
    <PackageReference Include="Avalonia.Controls.WebView" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\AvalonMarkdown\AvalonMarkdown.csproj" />
  </ItemGroup>
</Project>
`

### 1.4 Browser 项目 csproj

`xml
<Project Sdk="Microsoft.NET.Sdk.WebAssembly">
  <PropertyGroup>
    <TargetFramework>net10.0-browser</TargetFramework>
    <OutputType>Exe</OutputType>
    <Nullable>enable</Nullable>
    <WasmBuildNative>true</WasmBuildNative>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia.Browser" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\AvalonMarkdown\AvalonMarkdown.csproj" />
  </ItemGroup>
</Project>
`

不需要 #if BROWSER 或 DefineConstants——所有平台共享同一份代码。

---

## 2. HTML 渲染引擎

### 2.1 index.html

`html
<!DOCTYPE html>
<html class="theme-dark">
<head>
  <meta charset="UTF-8">
  <title>Markdown Preview</title>
  <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/katex@.../katex.min.css">
  <link rel="stylesheet" href="renderer.css">
  <style>
    html.theme-dark #preview .loading-placeholder { color: #888; }
    html.theme-light #preview .loading-placeholder { color: #999; }
  </style>
</head>
<body>
  <div id="preview"><p class="loading-placeholder">Loading Previewer...</p></div>
  <div id="error-overlay"><!-- --></div>
  <script src="https://cdn.jsdelivr.net/npm/markdown-it@..."></script>
  <script src="renderer.js"></script>
</body>
</html>
`

关键点：
- class="theme-dark" 是默认值，EmbeddedHtmlSourceProvider 构建时替换为当前系统主题
- 加载占位文字使用 CSS 类而非行内样式，以便主题切换时自动更新颜色
- CDN 脚本在 Desktop file:/// 和 Browser document.write() 中均能正常加载

### 2.2 renderer.js 核心 API

| 导出全局函数 | 用途 | C# 调用方式 |
|-------------|------|------------|
| window.renderMarkdown(text) | 渲染 Markdown | InvokeScript("renderMarkdown('...')") |
| window.setTheme(theme) | 切换 dark/light | InvokeScript("setTheme('dark')") |
| window.setPreviewConfig(config) | 更新配置 | InvokeScript("setPreviewConfig({...})") |
| window.showPreviewError(detail) | 显示错误浮层 | InvokeScript("showPreviewError('...')") |

### 2.3 renderer.css 主题系统

使用 CSS 变量 + html.theme-dark / html.theme-light 选择器实现双主题。
深色主题采用 VS Code 风格配色，包含完整的 highlight.js 语法高亮颜色。

---

## 3. MarkdownView 控件

### 3.1 XAML 布局

`xml
<UserControl ...>
  <DockPanel>
    <Border x:Name="ToolbarPanel" DockPanel.Dock="Top">
      <Grid ColumnDefinitions="Auto,Auto,Auto,*,Auto">
        <TextBlock Text="Markdown" />
        <Button x:Name="RestartButton" Content="Reload" />
        <Button x:Name="ToggleToolbarButton" Content="Hide" />
        <TextBlock x:Name="StatusText" />
      </Grid>
    </Border>
    <Border x:Name="ErrorPanel" DockPanel.Dock="Bottom" IsVisible="False">
      <Grid ColumnDefinitions="Auto,*,Auto">
        <TextBlock Text="⚠" />
        <StackPanel>
          <TextBlock x:Name="ErrorTitle" />
          <TextBlock x:Name="ErrorMessage" />
        </StackPanel>
        <Button x:Name="DismissErrorButton" Content="x" />
      </Grid>
    </Border>
    <Grid x:Name="WebViewHost" />
  </DockPanel>
</UserControl>
`

工具栏的 Background 和 BorderBrush 不在 XAML 中硬编码，由代码根据当前主题设置。

### 3.2 公开 API

| 成员 | 类型 | 说明 |
|------|------|------|
| ShowToolbar | bool property | 有头/无头模式切换 |
| OnReady | event | WebView 完全就绪（含 CDN 脚本加载完成） |
| ErrorOccurred | event | 内部可恢复错误 |
| RenderMarkdownAsync(string?) | Task | 渲染 Markdown 内容 |
| RestartPreviewAsync() | Task | 重启预览器 |
| ApplyConfigAsync(string) | Task | 应用预览配置 |
| InvokeScriptAsync(string) | Task<string?> | 执行自定义 JS |

### 3.3 核心构造流程

`
构造函数
  ├── InitializeComponent()          ← XAML 解析
  ├── CreateWebView()                ← 创建 NativeWebView，背景色跟随主题
  ├── WireEvents()                   ← 绑定事件 + 订阅主题变更
  ├── InitializeWebViewAsync()       ← 双路径加载
  │     ├── IsDesktop? → 写临时文件 → file:/// 导航
  │     └── !IsDesktop → about:blank 导航（5s 安全网）
  ├── ApplyThemeColors()             ← 设置工具栏/WebView 背景/状态文字颜色
  └── StatusText = "Loading..."
`

### 3.4 双路径加载策略

Desktop: WriteTempHtmlFile → file:/// → NavigationCompleted → 2s 等待 CDN → SetReady
Browser: about:blank → NavigationCompleted → document.write → ForceLayout → 2s 等待 → SetReady

安全网：5 秒后若未就绪，强制注入或 SetReady。

### 3.5 临时文件管理

`csharp
var path = Path.Combine(dir, $"preview_{DateTime.Now:HHmmssfff}.html");
`

时间戳确保每次重启生成唯一文件名，避免 WebView2 缓存。
自动清理 30 秒前的旧临时文件。

### 3.6 document.write 注入（Browser）

`csharp
var escaped = htmlContent
    .Replace("\\", "\\\\").Replace("'", "\\'")
    .Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");
var script = $"document.open();document.write('{escaped}');document.close();";
var result = _webView.InvokeScript(script);
if (result is Task t) await t.WaitAsync(TimeSpan.FromSeconds(5));
`

InvokeScript 在 Desktop 返回 Task<string?>（异步），在 Browser 返回 null（同步）。
result is Task t 判断兼容两者。

---

## 4. HTML 来源提供器

### 4.1 接口

`csharp
public interface IWebViewSourceProvider
{
    string GetHtmlContent();
}
`

### 4.2 EmbeddedHtmlSourceProvider

`csharp
public class EmbeddedHtmlSourceProvider : IWebViewSourceProvider
{
    private readonly Lazy<string> _html;

    public EmbeddedHtmlSourceProvider() { _html = new Lazy<string>(BuildHtml); }

    public string GetHtmlContent()
    {
        var html = _html.Value;
        // 注入当前系统主题 class
        var theme = GetCurrentTheme() == "light" ? "theme-light" : "theme-dark";
        return html.Replace("class=\"theme-dark\"", $"class=\"{theme}\"");
    }

    private static string BuildHtml()
    {
        // AssetLoader 全平台通用
        var html = ReadAsset("avares://AvalonMarkdown/Assets/web/index.html");
        var css  = ReadAsset("avares://AvalonMarkdown/Assets/web/renderer.css");
        var js   = ReadAsset("avares://AvalonMarkdown/Assets/web/renderer.js");
        // 内联 CSS/JS
        html = html.Replace("<link rel=\"stylesheet\" href=\"renderer.css\">", $"<style>{css}</style>");
        html = html.Replace("<script src=\"renderer.js\"></script>", $"<script>{js}</script>");
        return html;
    }

    private static string GetCurrentTheme()
    {
        var variant = Application.Current?.ActualThemeVariant;
        return variant == ThemeVariant.Light ? "light" : "dark";
    }
}
`

核心作用：
1. AssetLoader 读取嵌入式资源（全平台统一，不依赖文件系统）
2. 内联 renderer.css/renderer.js 消除外部文件依赖
3. 注入当前系统主题 class，使加载前的占位文字颜色与主题匹配

---

## 5. 统一主视图

### 5.1 MainView.axaml

`xml
<Grid ColumnDefinitions="1*, 4, 3*">
  <DockPanel Grid.Column="0">
    <TextBlock Text="Markdown Editor" />
    <TextBox x:Name="MarkdownEditor" />
  </DockPanel>
  <Rectangle Grid.Column="1" Fill="#3c3c3c" Width="4" />
  <Grid x:Name="PreviewHost" Grid.Column="2" />
</Grid>
`

PreviewHost 是占位 Grid，MarkdownView 在代码中创建后放入。

### 5.2 MainView.axaml.cs

`csharp
public partial class MainView : UserControl
{
    private MarkdownView MarkdownPreview { get; set; } = null!;

    public MainView()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        InitPreview(new EmbeddedHtmlSourceProvider());
    }

    private void InitPreview(IWebViewSourceProvider sp)
    {
        MarkdownPreview = new MarkdownView(sp);
        MarkdownPreview.OnReady += OnNavComplete;
        MarkdownEditor.TextChanged += OnMarkdownChanged;
        PreviewHost.Children.Add(MarkdownPreview);
    }

    private async void OnNavComplete(object? s, EventArgs e)
    {
        await Task.Delay(500);
        await ApplyConfigAsync();
        await MarkdownPreview.RenderMarkdownAsync(MarkdownEditor.Text);
    }

    private async Task ApplyConfigAsync()
    {
        var config = GetVm().PreviewConfig;
        config.IsDarkTheme = Application.Current?.ActualThemeVariant != ThemeVariant.Light;
        await MarkdownPreview.ApplyConfigAsync(config.ToJsCallExpression());
    }
}
`

ApplyConfigAsync 不调用 setTheme——主题由 MarkdownView 的 ApplySystemThemeAsync 自动管理。

---

## 6. 入口点分发

### 6.1 App.axaml.cs

`csharp
public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        desktop.MainWindow = new MainWindow();
    else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        singleView.MainView = new MainView();
    base.OnFrameworkInitializationCompleted();
}
`

运行时 ApplicationLifetime 类型判断，无 #if 条件编译。

### 6.2 Desktop Program.cs

`csharp
public static void Main(string[] args) => BuildAvaloniaApp()
    .StartWithClassicDesktopLifetime(args);
public static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
`

### 6.3 Browser Program.cs

`csharp
private static Task Main(string[] args) => BuildAvaloniaApp()
    .WithInterFont().StartBrowserAppAsync("out");
public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>();
`

---

## 7. 主题系统

### 7.1 全链路

`
EmbeddedHtmlSourceProvider (HTML 构建时)
  └── 读取 ActualThemeVariant → 注入 theme-dark/light class

MarkdownView 构造函数
  └── ApplyThemeColors(GetCurrentTheme())
        ├── ToolbarPanel.Background
        ├── WebViewHost.Background
        ├── _webView.Background
        └── StatusText.Foreground

SetReady() / 主题切换时
  └── ApplySystemThemeAsync()
        ├── ApplyThemeColors(theme)  ← C# 端控件
        └── InvokeScript("setTheme('...')")  ← JS 端 WebView

ActualThemeVariantChanged 事件
  └── ApplySystemThemeAsync()  ← 系统主题变化时自动跟随
`

### 7.2 关键代码

`csharp
private void SubscribeThemeChanges()
{
    if (_themeMonitored) return;
    _themeMonitored = true;
    Application.Current!.ActualThemeVariantChanged += OnActualThemeChanged;
}

private async Task ApplySystemThemeAsync()
{
    var theme = GetCurrentTheme();
    if (theme == _currentTheme && _ready) return;
    _currentTheme = theme;
    ApplyThemeColors(theme);
    if (_ready)
        await InvokeScriptSafeAsync($"setTheme('{theme}')");
}
`

JS 端 setTheme 内部调用 reRenderMarkdown() 重建所有 Mermaid 图表，使新主题生效。

---

## 8. 错误处理

### 8.1 双层面板

C# 底部面板：InvokeScriptSafeAsync 捕获异常 → ShowError → ErrorPanel
JS 页面浮层：window.onerror / unhandledrejection → showErrorOverlay → postMessage → C#

### 8.2 安全网

初始化 5 秒后若未就绪，强制注入或 SetReady，防止永久卡死。
重启时使用 _loadSequence 序列号防止过期安全网干扰。

---

## 9. 常见问题

| 问题 | 原因 | 解决 |
|------|------|------|
| Desktop data: URI CDN 不加载 | data: URI 中 script 被阻止 | Desktop 用 file:/// 临时文件 |
| Browser data: URI 不触发事件 | iframe 中 data: URI 无 load 事件 | about:blank + document.write |
| iframe 居于左上角 | 布局未更新 DOM 尺寸 | 注入后 ForceLayout() |
| 重启不变 | 同名文件缓存/安全网过期 | 时间戳文件名 + _loadSequence |
| Mermaid 主题不更新 | SVG 已渲染不受 initialize 影响 | reRenderMarkdown() 重建 |
