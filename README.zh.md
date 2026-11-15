# AvalonMarkdown

面向 **AvaloniaUI** 的 Markdown 预览控件——支持数学公式（KaTeX）、语法高亮（highlight.js）、图表（Mermaid / PlantUML）、函数图像、视频嵌入，以及可折叠的对话 / 状态块。

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

### 函数图像

`plot` 围栏用于绘制 `y = f(x)`。围栏体是 [function-plot](https://mauriciopoppe.github.io/function-plot/) 的 options 对象**原始 JSON**，该库支持的选项都可直接透传：

````markdown
```plot
{
  "title": "sin(x)/x",
  "grid": true,
  "xAxis": { "domain": [-12, 12] },
  "yAxis": { "domain": [-0.4, 1.1] },
  "data": [
    { "fn": "sin(x)/x" },
    { "fn": "1/x", "color": "#e06c75" }
  ]
}
```
````

只有两项会被填默认值，且**你的 JSON 永远优先**：

- **`color`** —— 未指定时按数据序号取自主题调色板。曲线颜色是逐条数据的选项（`color` 只在 datum 上），因此无法由 CSS 提供。
- **`disableZoom`** —— 默认为 `true`，即图像静态、滚轮照常滚动页面。写 `"disableZoom": false` 才在该块上启用滚轮缩放与平移。这个默认值是有意为之：该库的缩放绑定会吞掉图上方的所有滚轮事件，在阅读场景里等于把滚轮卡住。

`width` 与 `height` 是**含边距的整个 SVG 尺寸**（左 40 / 右 20 / 下 20、上 20 —— 有 `title` 时为 40），所以 `"height": 350` 实际绘图区只有 310px。省略 `width` 时会自适应容器宽度。

JSON 写错时会在原位渲染一个错误块，不会影响预览的其余部分。围栏按 info 字符串精确匹配，因此普通代码围栏（包括 `json`）完全不受影响。

表达式字符串在交给库的求值器（它会把表达式编译成 JavaScript）之前会过一遍字符白名单。比较、三元与取模运算符都在白名单内，因此**分段函数可用** —— `"fn": "x > 0 ? x : -x"`；但引号、分号、大括号、方括号与反斜杠会被拒绝。

表达式语法有两点需要注意，两者都来自该库本身：

- **常量是大写的** —— 是 `PI` 和 `E`，不是 `pi` / `e`。小写形式未定义，该条曲线会静默画不出来。
- 可用函数包括 `sin` `cos` `tan` `asin` `acos` `atan` `sqrt` `abs` `exp` `log` `ln` `max` `min`；此外的名称（如 `floor`、`sign`）均未定义。

### 对话 / 可折叠块

`chat` 围栏渲染一个默认折叠的 `<details>` 面板，适合展示流式 Agent 输出中可展开的「思考中」「工具调用中」等步骤：

````markdown
```chat[思考中]
正在检查 `renderer.js` 的分派规则…
```

```chat[工具调用中][open]
运行 `dotnet build`：

- `--configuration Release`
```
````

方括号里的标签完全自定义、按原样显示。第二个方括号组用于配置面板：`open`（或 `expanded`）初始展开，`collapsed` 保持折叠（默认），`static` / `idle` 冻结状态光圈（默认 `spin` 旋转）。围栏体是普通 Markdown，会递归渲染，因此行内代码、列表、表格乃至嵌套围栏都能正常工作。标签中不能包含 `]`；info 字符串写错或选项未知时，会渲染一个可见的错误块，而不是静默降级成代码块。

右上角的状态徽标会根据步骤状态切换图标：**思考 → 灯泡**、**工具 → 扳手**、**完成 → 对勾**、**错误 → 叉号**，其他情况显示聊天气泡。状态会自动从标签文字推断（`思考` / `think`、`工具` / `tool`、`完成` / `done`、`错误` / `fail`），也可以用 `[thinking]`、`[tool]`、`[done]`、`[error]`、`[chat]` 显式指定。步骤进行中光圈旋转；`done` / `error` 会冻结光圈并**完全隐藏外圈**，`[spin]` / `[static]` 可覆盖动画。也可以用 `svg="M12 2 L22 12 L12 22 L2 12 Z"` 传入自定义图标（路径含逗号时用引号括起来），或直接传完整的 `<svg>…</svg>` 字符串。颜色与辉光可通过 `MarkdownThemeViewModel` 的 `Chat*` 属性配置。

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

### 将图表导出为图片

右键 Mermaid 图表、函数图像或 PlantUML 图（触摸端长按），选择 **另存为 SVG** 或
**另存为 PNG**，文件通过平台自带的保存对话框写出。

只有这三类成员会被拦截，文档其他位置的右键仍然是平台原生菜单。把
`EnableContextMenu` 设为 `False` 可整体关闭，所有位置都回到原生菜单：

```xml
<md:MarkdownView Text="{Binding MarkdownContent}" EnableContextMenu="False" />
```

公式**有意不支持导出**：KaTeX 渲染的是定位过的 HTML 而非 SVG，没有矢量产物可交付，
出图只能靠给 DOM 截图。

三点需要知道：

- **导出的 PlantUML 图是白底黑图**，即使在深色主题下。这是 PlantUML 自身的输出，
  有意不做滤镜 —— 你在屏幕上看到的暗色反相只是显示期的视觉效果。它打印和粘贴都正确，
  但会和深色主题导出的 Mermaid 图看起来不一致。
- **导出的 Mermaid 与函数图像带不透明背景**（取自页面），所以深色主题的图贴到白底上
  依然清晰。
- **过大的图可能拒绝另存为 PNG**（会在预览里提示）—— JS→C# 桥在 Android 上走 Binder，
  约 1MB 上限。SVG 没有这个限制，改用 SVG 或把图缩小即可。

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
| `EnableContextMenu` | `bool` | 右键 / 长按图片可另存为（默认 `true`） |
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
| 函数图像 | 容器背景 / 内边距 / 外边距 / 圆角；坐标轴、网格与标题颜色 |
| 对话 / 状态块 | 对话块背景 / 正文内边距 / 外边距 / 右侧圆角（左侧为直角且无边框）；状态徽标的颜色与辉光 |

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
| `PlotBgR` `PlotBgG` `PlotBgB` | 函数图像容器背景（0–255） |
| `PlotContainerPadding` · `PlotContainerMargin` · `PlotBorderRadius` | 函数图像容器内边距 / 外边距 / 圆角 |
| `PlotAxisColor` | 函数图像坐标轴 / 网格 / 标题颜色（CSS 颜色 `string`，仅代码设置） |
| `ChatBgR` `ChatBgG` `ChatBgB` | 对话块背景（0–255） |
| `ChatContainerPadding` · `ChatContainerMargin` · `ChatBorderRadius` | 对话块正文内边距 / 容器外边距 / 右侧圆角（左侧为直角且无边框） |
| `ChatAccentColor` · `ChatGlassColor` | 状态徽标的颜色与辉光颜色（CSS 颜色 `string`，含透明度，仅代码设置） |
| `ChatGlassBlur` | 状态徽标辉光模糊半径（px） |

**代码高亮** —— highlight.js 各 token 的颜色（`string`，如 `"#569cd6"`）：

`HljsKeyword`, `HljsLiteral`, `HljsSymbol`, `HljsName`, `HljsBuiltIn`, `HljsType`, `HljsClass`, `HljsNumber`, `HljsString`, `HljsMetaString`, `HljsTitle`, `HljsTitleClass`, `HljsTitleClassInherited`, `HljsParams`, `HljsVariable`, `HljsTemplateVariable`, `HljsComment`, `HljsQuote`, `HljsAttr`, `HljsAttribute`, `HljsMeta`, `HljsTag`, `HljsSelectorAttr`, `HljsSelectorClass`, `HljsSelectorId`, `HljsSelectorPseudo`, `HljsSelectorTag`, `HljsBullet`, `HljsSection`, `HljsLink`, `HljsRegexp`, `HljsTemplateTag`, `HljsDoctag`, `HljsBackground`, `HljsForeground` —— 以及 diff 标记 `HljsAdditionBg`/`HljsAdditionColor` 与 `HljsDeletionBg`/`HljsDeletionColor`。

> 说明：内置 `MarkdownThemeView` 面板覆盖颜色 / 排版 / 图表 / 函数图像以及对话块的背景、内边距、圆角、辉光；代码高亮 token（`Hljs*`）、`PlotAxisColor`、`ChatAccentColor` 与 `ChatGlassColor` 需在代码中配置（或在自己 UI 中绑定）。

---

## 书写规范与校验器

[`skills/avalonmarkdown-syntax/`](skills/avalonmarkdown-syntax/) 记录了这个控件渲染的
Markdown 方言 —— 每个语法成员都配有正反案例，包括数学公式的定界符规则和函数图像的
表达式文法。

[`references/templates.md`](skills/avalonmarkdown-syntax/references/templates.md) 提供
可直接复制的源码模板。这些片段不是凭记忆写的：`scripts/check_templates.py` 会把每一段
抽出来、送进真实管线渲染，任何一段出问题就失败。

### 作为 Claude Code skill 安装

这个目录本身就是一个合法的 Claude Code skill，可直接用于**你自己的**项目 —— 如果你正在
生成会交给本控件渲染的 Markdown，就把它装到生成发生的那个项目里：

```bash
mkdir -p .claude/skills
cp -r <path-to>/AvalonMarkdown/skills/avalonmarkdown-syntax .claude/skills/
```

Claude Code 会自动加载它，并在写 Markdown 之前参考。它**有意不**装进本仓库自己的
`.claude/` —— 面向文档写作的 skill 不是仓库本身需要的东西，而在这里提交一份 agent
指令文件等于把它强加给每个克隆这个仓库的人。

它还带了一个校验器，回答预览本身回答不了的问题：**这段写得出来吗？** 这里的失败大多
是静默的 —— KaTeX 自己画一个错误 span 且不 log；未闭合的 `$$` 块会删掉文档余下全部；
PlantUML 语法错误返回的是一张**能正常显示的报错图**，尺寸正常，看起来像画好了。

```bash
python skills/avalonmarkdown-syntax/scripts/render_check.py doc.md
```

两层。静态层离线即时地复刻渲染器自己的分派规则；存在构建产物时，它还会把文件送进真实
WebView 管线渲染并检查 DOM —— 这是唯一能确认公式编译通过、图表解析成功的方式。

```bash
python skills/avalonmarkdown-syntax/scripts/render_check.py --self-test
```

`--self-test` 会用 `skills/avalonmarkdown-syntax/fixtures/` 里的已知错误样本断言每一条
检查都真的触发 —— 没有已知错误样本的校验器不可信，所以样本与断言本身就是交付物的一部分。

## 演示

仓库包含可运行的示例应用（`AvalonMarkdown.Test.Desktop` 及 Android/iOS/Browser 版本），覆盖以上两个层面。

工具条上除总览之外还有三个**专项展示**入口 —— `Tables`、`Diagrams`、`Plots`。
每个只针对一类元素、跑一大批用例，这样该类元素的回归会一眼看到，而不是埋在大杂烩里：

- **表格** —— 对齐、代码块内转义竖线、单元格里的公式与脚注、中文与 emoji、
  八列布局，以及三种**不该被当作表格**的形状。
- **图表** —— 15 种 Mermaid 加 11 种 PlantUML，含中文与 HTML 标签；每种各配一个
  故意写坏的样例，让失败长什么样和成功并排摆着。
- **函数图像** —— 完整的 Penner / easings.net 缓动集（sine 到 bounce，`in`/`out`/`inOut`），
  外加非 `y = f(x)` 的图类型：参数方程、极坐标、隐函数、散点、向量。

它们以真实的 `.md` 文件存在 `AvalonMarkdown.Test.Shared/Assets/Showcase/` 下，而不是
C# 字符串字面量 —— 这样校验器检查的就是演示实际渲染的那份文件。捕获驱动会断言三者都能
加载，因为资源键写错只会静默退化成一个回退错误块。

## 许可证

[MIT](LICENSE.txt)
