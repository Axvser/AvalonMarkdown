# AvalonMarkdown

基于 **AvaloniaUI** 的 **NativeWebView** 构建的 Markdown 预览控件——支持数学公式（KaTeX）、代码高亮（highlight.js）、图表（Mermaid / PlantUML）与视频嵌入。

> 🌏 [English Documentation](README.md)

## 安装

```bash
dotnet add package AvalonMarkdown
```

## MarkdownView

即用型 Markdown 预览控件。

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:md="clr-namespace:AvalonMarkdown.Views;assembly=AvalonMarkdown">
    <md:MarkdownView x:Name="Preview" />
</Window>
```

### 渲染内容

推荐 —— 绑定 `Text` 属性（更新自动去重）：

```xml
<md:MarkdownView Text="{Binding MarkdownContent}" />
```

或等控件就绪后显式渲染：

```csharp
Preview.OnReady += async (_, _) =>
{
    await Preview.RenderMarkdownAsync("# Hello World\n\n**Bold** *Italic*");
};
```

### 链接导航

每个超链接点击都会触发 `LinkClicked` 事件。设置 `Handled = true` 可接管导航（例如应用内跳转）；否则默认在系统浏览器中打开。

```csharp
Preview.LinkClicked += (_, e) =>
{
    if (e.Url.StartsWith("app://"))
    {
        NavigateInternal(e.Url);
        e.Handled = true; // 阻止默认的系统浏览器打开
    }
};
```

### 预览配置

内容更新时自动滚动到底部以跟随最新内容，操作滚动条期间暂停。可通过 `ApplyConfigAsync` 调节：

```csharp
await Preview.ApplyConfigAsync("setPreviewConfig({ autoScrollOnUpdate: true, autoScrollCooldown: 2000 })");
```

### 主要 API

| 成员 | 类型 | 说明 |
| --- | --- | --- |
| `Text` | `string?` | Markdown 内容（双向绑定） |
| `OnReady` | `event` | 预览就绪时触发 |
| `LinkClicked` | `event` | 每个超链接点击时触发；`Handled = true` 可接管导航 |
| `ErrorOccurred` | `event` | 内部可恢复错误时触发 |
| `RenderMarkdownAsync(text)` | `Task` | 渲染 Markdown 内容 |
| `RestartPreviewAsync()` | `Task` | 重启预览 |
| `ApplyConfigAsync(js)` | `Task` | 应用预览配置 |
| `ApplyCustomCssAsync(css)` | `Task` | 注入自定义 CSS |
| `InvokeScriptAsync(js)` | `Task<string?>` | 执行自定义 JavaScript |

## MarkdownThemeView

内置主题编辑器，`Target` 绑定到 `MarkdownView`。提供颜色 RGB 滑块，以及排版、代码高亮、Mermaid/PlantUML 样式配置——修改自动应用。

```xml
<md:MarkdownView x:Name="Preview" />
<md:MarkdownThemeView Target="{Binding #Preview}" />
```

默认收起，点击标题栏展开。

## 许可证

[MIT](LICENSE.txt)
