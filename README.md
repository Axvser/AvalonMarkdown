# AvalonMarkdown.Controls

跨平台 Markdown 预览控件，基于 AvaloniaUI + NativeWebView。
写一次控件，支持 Desktop / Browser (WASM) / Android / iOS。

## 安装

```bash
dotnet add package AvalonMarkdown.Controls
```

## 快速开始

### 1. 在 XAML 中声明

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:md="clr-namespace:AvalonMarkdown.Views;assembly=AvalonMarkdown.Controls">
    <md:MarkdownView x:Name="Preview" />
</Window>
```

### 2. 在代码中渲染

```csharp
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Preview.OnReady += async (_, _) =>
        {
            await Preview.RenderMarkdownAsync("# Hello World\n\n**Bold** *Italic*");
        };
    }
}
```

## API 参考

### 属性

| 成员 | 类型 | 说明 |
|------|------|------|
| `ShowToolbar` | `bool` | 显示/隐藏工具栏 |

### 方法

| 方法 | 返回 | 说明 |
|------|------|------|
| `RenderMarkdownAsync(string?)` | `Task` | 渲染 Markdown 内容 |
| `RestartPreviewAsync()` | `Task` | 重启预览器 |
| `ApplyConfigAsync(string)` | `Task` | 执行 JS 配置调用 |
| `InvokeScriptAsync(string)` | `Task<string?>` | 执行自定义 JS |

### 事件

| 事件 | 参数 | 触发时机 |
|------|------|----------|
| `OnReady` | `EventHandler` | 控件完全就绪，可安全调用 `RenderMarkdownAsync` |
| `ErrorOccurred` | `EventHandler<MarkdownViewErrorEventArgs>` | 内部可恢复错误 |

## 渲染能力

- **Markdown** — markdown-it 14 + footnote / task-lists
- **数学公式** — KaTeX（行内 `$...$` / 块级 `$$...$$`）
- **代码高亮** — highlight.js 11，VS 风格配色
- **图表** — Mermaid 11（流程图、时序图、饼图、Git 图）
- **代码块** — 语言标签 · 复制按钮 · 高度调节
- **任务列表** — 自定义复选框
- **脚注 / 表格 / 引用 / 删除线**

## 技术栈

| 组件 | 版本 | 用途 |
|------|------|------|
| Avalonia | 12.0.1 | UI 框架 |
| CommunityToolkit.Mvvm | 8.4.0 | MVVM |
| markdown-it | 14.1.0 | Markdown 解析 |
| highlight.js | 11.10.0 | 代码高亮 |
| KaTeX | 0.16.11 | 数学公式 |
| Mermaid | 11.4.1 | 图表 |

所有 JS/CSS 依赖已内联，无需网络连接。

## 许可证

MIT
