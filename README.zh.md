# AvalonMarkdown

面向 **AvaloniaUI** 的 Markdown 预览控件——支持数学公式（KaTeX）、语法高亮（highlight.js）、图表（Mermaid / PlantUML）与视频嵌入。

> 🌏 [English Documentation](README.md)

## 安装

```bash
dotnet add package AvalonMarkdown
```

你只需要认识两个类型：

- **`MarkdownView`** —— 展示 Markdown 文档并响应交互（链接、滚动、标题）。
- **`MarkdownThemeViewModel`** —— 控制文档长什么样。

---

## MarkdownView —— 渲染与交互

把控件加进窗口，喂给它 Markdown 内容即可。

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:md="clr-namespace:AvalonMarkdown.Views;assembly=AvalonMarkdown">
    <md:MarkdownView x:Name="Preview" />
</Window>
```

### 展示内容

绑定 `Text` 属性（推荐方式，自动更新）：

```xml
<md:MarkdownView Text="{Binding MarkdownContent}" />
```

或等控件就绪后手动渲染：

```csharp
Preview.OnReady += async (_, _) =>
{
    await Preview.RenderMarkdownAsync("# Hello World\n\n**Bold** *Italic*");
};
```

内容为空时可设置 `Placeholder` 显示提示文字（默认空白）：

```xml
<md:MarkdownView Text="{Binding MarkdownContent}" Placeholder="Type something…" />
```

### 处理链接点击

每次点击超链接都会触发 `LinkClicked`。设置 `Handled = true` 即可自己接管导航；否则默认在系统浏览器中打开。

```csharp
Preview.LinkClicked += (_, e) =>
{
    if (e.Url.StartsWith("app://"))
    {
        OpenInternalPage(e.Url);  // 你的应用内路由
        e.Handled = true;         // 阻止默认的系统浏览器打开
    }
};
```

### 页内跳转（锚点 & 目录）

Markdown 标题支持同页锚点跳转，`[跳转到 §2](#2-text-formatting)` 这类写法可直接生效。

想在自己界面上做可点击的目录，可用：

```csharp
var headings = await Preview.GetHeadingsAsync();           // [{ Text, Level }, …]
var ok       = await Preview.NavigateToHeadingAsync("Chapter 2"); // 滚动到指定标题
```

### 编辑器与预览同步（滚动进度）

用于编辑区 ⇄ 预览区的双向滚动定位：

```csharp
double? progress = await Preview.GetScrollProgressAsync(); // 0–100
await Preview.ScrollToProgressAsync(50);
```

### 行为配置与诊断

内容更新时默认自动滚动跟随最新内容，操作滚动条期间暂停；可用 `ApplyConfigAsync` 调节：

```csharp
await Preview.ApplyConfigAsync("setPreviewConfig({ autoScrollOnUpdate: true, autoScrollCooldown: 2000 })");
```

`ErrorOccurred` 与 `ConsoleMessage` 事件可把渲染/脚本消息展示出来或记日志。

### MarkdownView 参考

| 成员 | 类型 | 说明 |
| --- | --- | --- |
| `Text` | `string?` | Markdown 内容（双向绑定） |
| `Placeholder` | `string?` | 内容为空时显示的提示文字 |
| `OnReady` | `event` | 预览就绪、可以渲染时触发 |
| `LinkClicked` | `event` | 点击超链接时触发；`Handled = true` 接管导航 |
| `ErrorOccurred` | `event` | 内部可恢复错误时触发 |
| `ConsoleMessage` | `event` | 每条网页控制台消息（log/warn/error）触发 |
| `RenderMarkdownAsync(text)` | `Task` | 渲染 Markdown 内容 |
| `GetHeadingsAsync()` | `Task<IReadOnlyList<MarkdownHeading>>` | 列出所有标题（`Text`、`Level`） |
| `NavigateToHeadingAsync(heading)` | `Task<bool>` | 滚动到指定标题；返回是否找到 |
| `GetScrollProgressAsync()` | `Task<double?>` | 当前垂直滚动进度（0–100） |
| `ScrollToProgressAsync(percent)` | `Task` | 滚动到垂直进度位置（0–100） |
| `ApplyConfigAsync(expression)` | `Task` | 应用预览行为配置 |
| `ApplyCustomCssAsync(css)` | `Task` | 应用整份自定义样式表 |
| `RestartPreviewAsync()` | `Task` | 重建预览页面 |

---

## MarkdownThemeViewModel —— 样式

配置文档外观：颜色、排版、代码高亮、图表样式。每个属性都带 `[Description]`，在 IntelliSense 里悬停即可看到它控制什么。

### 方案 A：使用内置主题编辑器

`MarkdownThemeView` 是针对上述属性的现成编辑器面板。指向你的预览即可，任何修改即时应用。

```xml
<md:MarkdownView x:Name="Preview" />
<md:MarkdownThemeView Target="{Binding #Preview}" />
```

面板默认收起，点击其标题栏展开。

### 方案 B：代码驱动

创建 `MarkdownThemeViewModel`，改你需要动的属性，再让它把变更推给一个或多个预览：

```csharp
var theme = new MarkdownThemeViewModel
{
    BgR = 30, BgG = 30, BgB = 30,        // 页面背景
    TextR = 212, TextG = 212, TextB = 212, // 正文文字
    HljsKeyword = "#569cd6",
    MermaidContainerPadding = 8,
};

theme.RegisterRenderer(myPreview);
theme.StartAutoApply();   // 之后任意属性改动都会自动应用
```

只想应用一次？

```csharp
await myPreview.ApplyCustomCssAsync(theme.GenerateCss());
```

> 无论当前应用用的是浅色还是深色主题，样式都会生效。

### 可配置项一览

| 分组 | 覆盖范围 |
| --- | --- |
| 核心颜色 | 页面背景、正文文字、链接、标题、行内代码、边框 |
| 表面颜色 | 次级背景（如代码头）、次级文字、行内代码背景、代码块背景、表头背景 |
| 排版 | 代码字号；代码块 / pre 圆角 |
| 代码高亮 | 每种语法 token 的颜色（关键字、字符串、数字、注释……） |
| 图表 | Mermaid 与 PlantUML 容器背景 / 内边距 / 外边距 / 圆角；PlantUML 暗色反色强度 |

### 支持的样式属性

**颜色** —— 每种颜色都是三个 `int` 通道 `R` / `G` / `B`（0–255）；对应的 `XxxHex` 是只读的 `#RRGGBB` 预览。

| 表面 | 通道属性 | 预览 |
| --- | --- | --- |
| 背景 | `BgR` `BgG` `BgB` | `BgHex` |
| 正文文字 | `TextR` `TextG` `TextB` | `TextHex` |
| 链接 | `LinkR` `LinkG` `LinkB` | `LinkHex` |
| 标题 | `HeadingR` `HeadingG` `HeadingB` | `HeadingHex` |
| 行内代码文字 | `CodeR` `CodeG` `CodeB` | `CodeHex` |
| 边框 | `BorderR` `BorderG` `BorderB` | `BorderHex` |
| 次级背景 | `BgSecR` `BgSecG` `BgSecB` | `BgSecHex` |
| 次级文字 | `TextSecR` `TextSecG` `TextSecB` | `TextSecHex` |
| 行内代码背景 | `CodeBgR` `CodeBgG` `CodeBgB` | `CodeBgHex` |
| 代码块背景 | `PreBgR` `PreBgG` `PreBgB` | `PreBgHex` |
| 表头背景 | `TableBgR` `TableBgG` `TableBgB` | `TableBgHex` |

**排版 / 布局**（`double` / `int`，如未注明均以 px 计）

| 属性 | 含义 |
| --- | --- |
| `CodeFontSize` | 代码字号 |
| `BorderRadius` | 代码块 / pre 圆角 |
| `MermaidBgR` `MermaidBgG` `MermaidBgB` | Mermaid 容器背景（0–255） |
| `MermaidContainerPadding` · `MermaidContainerMargin` · `MermaidBorderRadius` | Mermaid 容器内边距 / 外边距 / 圆角 |
| `PumlBgR` `PumlBgG` `PumlBgB` | PlantUML 容器背景（0–255） |
| `PumlContainerPadding` · `PumlContainerMargin` · `PumlBorderRadius` | PlantUML 容器内边距 / 外边距 / 圆角 |
| `PumlDarkInvert` | PlantUML 暗色反色强度（0–1） |

**代码高亮** —— highlight.js 各 token 的颜色（`string`，如 `"#569cd6"`）：

`HljsKeyword`, `HljsLiteral`, `HljsSymbol`, `HljsName`, `HljsBuiltIn`, `HljsType`, `HljsClass`, `HljsNumber`, `HljsString`, `HljsMetaString`, `HljsTitle`, `HljsTitleClass`, `HljsTitleClassInherited`, `HljsParams`, `HljsVariable`, `HljsTemplateVariable`, `HljsComment`, `HljsQuote`, `HljsAttr`, `HljsAttribute`, `HljsMeta`, `HljsTag`, `HljsSelectorAttr`, `HljsSelectorClass`, `HljsSelectorId`, `HljsSelectorPseudo`, `HljsSelectorTag`, `HljsBullet`, `HljsSection`, `HljsLink`, `HljsRegexp`, `HljsTemplateTag`, `HljsDoctag`, `HljsBackground`, `HljsForeground` —— 以及 diff 标记 `HljsAdditionBg`/`HljsAdditionColor` 与 `HljsDeletionBg`/`HljsDeletionColor`。

> 说明：内置 `MarkdownThemeView` 面板覆盖颜色 / 排版 / 图表；代码高亮 token 通过上述 `Hljs*` 属性在代码里配置（或在自己 UI 中绑定）。

---

## 演示

仓库包含可运行的示例应用（`AvalonMarkdown.Test.Desktop` 及 Android/iOS/Browser 版本），覆盖以上两个层面。

## 许可证

[MIT](LICENSE.txt)
