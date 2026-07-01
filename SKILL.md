# AvaloniaUI — Web Markdown 实现指南

## 架构总览

```
Assets/web/
├── renderer.css     ← 共享样式（主题、代码块、任务列表等）
├── renderer.js      ← 共享脚本（markdown-it、KaTeX、Mermaid 等）
└── index.html       ← Desktop 加载 renderer.css + renderer.js 的壳

AvalonMarkdown.Browser/wwwroot/
├── index.html       ← textarea 编辑器 + preview 预览（纯 HTML/JS）
└── web/renderer.css  ← 从共享项目复制
└── web/renderer.js   ← 从共享项目复制
```

---

## 共享渲染引擎

### renderer.css

CSS 变量深色主题（`html.theme-dark`），作用于：

| 用途 | 选择器 |
|------|--------|
| 代码块边框/背景 | `.code-block-wrapper` / `.code-header` / `pre.hljs` |
| 任务列表复选框 | `.task-list-item input[type="checkbox"]` — `appearance:none` + SVG checkmark |
| 消除外层边框 | `pre:has(.code-block-wrapper) { border:none; }` |
| 代码高亮 | `html.theme-dark .hljs-*` — VS 风格配色 |
| 滚动条 | `::-webkit-scrollbar` |

### renderer.js（IIFE 包裹，以下导出到 window）

| 导出 | 说明 |
|------|------|
| `renderMarkdown(text)` | 渲染到 `#preview` 元素 |
| `setTheme("dark"/"light")` | 切换 CSS 变量主题 |
| `setPreviewConfig({fontSize,lineHeight,showCodeLanguage,showCopyButton,maxCodeBlockHeight})` | 预览配置 |
| `copyCode(btn)` | 复制代码 |
| `increaseCodeHeight(btn)` / `decreaseCodeHeight(btn)` | 单个代码块高度 +/- 80px |
| `md` | markdown-it 实例 |
| `escapeHtml(str)` | HTML 转义 |

### 渲染能力

- markdown-it + footnote + task-lists 插件
- KaTeX 行内 `$...$` / 块级 `$$...$$`
- highlight.js VS 风格高亮（190+ 语言）
- Mermaid 流程图/时序图
- 代码块：语言标签 + 复制按钮 + 高度 +/- 按钮 + 垂直滚动条(`max-height:480px`)
- 任务列表自定义复选框（`appearance:none` + SVG dataURI checkmark）
- 表格、引用、列表、脚注、删除线

---

## Desktop 端

### 原理

Avalonia `NativeWebView` 加载 `file:///Assets/web/index.html`，C# 通过 `InvokeScript()` 调用 JS。

### 关键文件

| 文件 | 内容 |
|------|------|
| `Views/MainWindow.axaml` | `TextBox`(左) + `NativeWebView`(右) 分栏 |
| `Views/MainWindow.axaml.cs` | 导航 → 推送配置 → 实时推送 Markdown |
| `Assets/web/index.html` | `<link href="renderer.css">` + `<script src="renderer.js">` |
| `App.axaml.cs` | `#if !BROWSER` → `new MainWindow()` |

### MainWindow.axaml.cs 核心逻辑

```csharp
// 加载
MarkdownPreview.Source = new Uri("file:///" + indexPath.Replace('\\', '/'));

// 导航完成 → 推送配置 + Markdown
MarkdownPreview.NavigationCompleted += async (_, _) => {
    MarkdownPreview.InvokeScript("setTheme('dark')");
    MarkdownPreview.InvokeScript(config.ToJsCallExpression());
    await SendMarkdownAsync(MarkdownEditor.Text);
};

// 编辑器变化 → 实时推送
async Task SendMarkdownAsync(string? md) {
    var escaped = md.Replace("\\","\\\\").Replace("'","\\'").Replace("\n","\\n").Replace("\r","\\r");
    MarkdownPreview.InvokeScript($"renderMarkdown('{escaped}')");
}
```

### csproj 依赖

```xml
<!-- Directory.Packages.props -->
<PackageVersion Include="Avalonia.Controls.WebView" Version="12.0.1" />

<!-- 共享 csproj -->
<PackageReference Include="Avalonia.Controls.WebView" />
<Content Include="Assets\web\index.html" CopyToOutputDirectory="PreserveNewest" />
<Content Include="Assets\web\renderer.css" CopyToOutputDirectory="PreserveNewest" />
<Content Include="Assets\web\renderer.js" CopyToOutputDirectory="PreserveNewest" />

<!-- Desktop csproj -->
<PackageReference Include="Avalonia.Controls.WebView" />
```

### 注意事项

- IIFE 内的函数需 `window.xxx = xxx` 才能被 HTML `onclick` 找到
- `InvokeScript` 返回 `Task` 兼容对象，可用 `await` 等待
- JS 在 `NavigationCompleted` 之后才可用

---

## Browser 端

### 原理

Avalonia WASM 无法使用 WebView（`[JSImport]` 不工作），改用纯 HTML/JS：`<textarea>` 编辑器 + `<div>` 预览。

### 关键文件

| 文件 | 内容 |
|------|------|
| `wwwroot/index.html` | `<textarea id="md-editor">`(左) + `<div id="preview-panel">`(右) flex 布局 |
| `wwwroot/web/renderer.css` | 从共享项目复制 |
| `wwwroot/web/renderer.js` | 从共享项目复制 |
| `App.axaml.cs` | `#if BROWSER` → `new BrowserMainView()` |

### wwwroot/index.html 核心逻辑

```html
<html class="theme-dark">  <!-- 必须加 class 使 CSS 变量生效 -->
<textarea id="md-editor"></textarea>
<div id="preview-panel"></div>

<script src="/web/renderer.js"></script>
<script>
(function() {
    var ed = document.getElementById("md-editor");
    var pv = document.getElementById("preview-panel");
    ed.value = "# Markdown 测试内容...";
    function render() {
        pv.innerHTML = window.md.render(ed.value);
        pv.querySelectorAll(".mermaid").forEach(function(el) {
            window.mermaid.run({ nodes: [el] });
        });
    }
    render();
    ed.addEventListener("input", render);
})();
</script>
```

### csproj 依赖

```xml
<!-- Browser csproj -->
<ProjectReference Include="..\AvalonMarkdown\AvalonMarkdown.csproj">
  <DefineConstants>BROWSER</DefineConstants>
</ProjectReference>
```

### 同步命令

```bash
Copy-Item "AvalonMarkdown/Assets/web/renderer.css" "AvalonMarkdown.Browser/wwwroot/web/" -Force
Copy-Item "AvalonMarkdown/Assets/web/renderer.js" "AvalonMarkdown.Browser/wwwroot/web/" -Force
```

### 注意事项

- `<html>` 必须有 `class="theme-dark"`
- 修改 `renderer.css`/`renderer.js` 后需手动复制到 `wwwroot/web/`
- 直接调用 `window.md.render()`，不覆写 `window.renderMarkdown`
- 所有依赖通过 CDN 加载，需联网

---

## App.axaml.cs 条件编译

```csharp
public override void OnFrameworkInitializationCompleted()
{
#if BROWSER
    if (ApplicationLifetime is ISingleViewApplicationLifetime sv)
        sv.MainView = new BrowserMainView();
#else
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        desktop.MainWindow = new MainWindow { DataContext = new MainViewModel() };
    else if (ApplicationLifetime is ISingleViewApplicationLifetime sv)
        sv.MainView = new MainView { DataContext = new MainViewModel() };
#endif
    base.OnFrameworkInitializationCompleted();
}
```

---

## 运行

```bash
# Desktop
dotnet run --project AvalonMarkdown.Desktop -c Debug

# Browser（访问 http://localhost:5235）
dotnet run --project AvalonMarkdown.Browser -c Debug
```

---

## 常见问题

| 问题 | 原因 | 解决 |
|------|------|------|
| 代码块双层边框 | markdown-it 的 highlight 返回值被 `<pre><code>` 包裹 | `pre:has(.code-block-wrapper) { border:none; }` |
| +/- 按钮无效 | IIFE 内函数未暴露到 window | `window.increaseCodeHeight = fn` |
| checkbox 自定义样式无效 | `<input>` 不支持 `::before`/`::after` | 用 `background-image: url("data:image/svg+xml,...")` |
| 浏览器端无样式 | `<html>` 缺少 `class="theme-dark"` | 添加 `class="theme-dark"` |
| 浏览器修改后不生效 | renderer.css/js 未同步 | 复制到 `wwwroot/web/` |
