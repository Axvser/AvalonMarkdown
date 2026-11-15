using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AvalonMarkdown.Test.Shared.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _markdownText = GetDefaultMarkdown();

    [ObservableProperty]
    private string _timingInfo = "";

    [ObservableProperty]
    private string _timingInfo2 = "";

    [ObservableProperty]
    private string _timingInfo3 = "";

    [ObservableProperty]
    private bool _isMultiView;

    [ObservableProperty]
    private bool _editorExpanded = true;

    public void ToggleEditor()
    {
        EditorExpanded = !EditorExpanded;
    }

    // Independent stopwatch per view
    private readonly Stopwatch _sw1 = new();
    private readonly Stopwatch _sw2 = new();
    private readonly Stopwatch _sw3 = new();
    private bool _firstLoad = true;
    private int _readyCount;

    public void RecordReady(int index)
    {
        switch (index)
        {
            case 1: _sw1.Restart(); break;
            case 2: _sw2.Restart(); break;
            case 3: _sw3.Restart(); break;
        }
    }

    public void RecordRendered(int index)
    {
        long ms;
        switch (index)
        {
            case 1:
                ms = _sw1.ElapsedMilliseconds;
                TimingInfo = _firstLoad ? $"⏱ #{index} First: {ms}ms" : $"⏱ #{index}: {ms}ms";
                break;
            case 2:
                ms = _sw2.ElapsedMilliseconds;
                TimingInfo2 = _firstLoad ? $"⏱ #{index} First: {ms}ms" : $"⏱ #{index}: {ms}ms";
                break;
            case 3:
                ms = _sw3.ElapsedMilliseconds;
                TimingInfo3 = _firstLoad ? $"⏱ #{index} First: {ms}ms" : $"⏱ #{index}: {ms}ms";
                break;
        }

        if (index == 1) _readyCount++;
        if (_readyCount >= 3) _firstLoad = false;
    }

    public void ResetMultiView()
    {
        _readyCount = 0;
        TimingInfo = "";
        TimingInfo2 = "";
        TimingInfo3 = "";
    }

    public static string GetDefaultMarkdown() => """
        # Markdown Rendering Test Suite 🎯

        A feature tour of **AvalonMarkdown** — text formatting, links, lists, tables, math (KaTeX), code highlighting, diagrams (Mermaid / PlantUML), function plots, video embeds, footnotes, HTML, and error resilience.

        ## 0. 目录 · 页内跳转（# 锚点试验）

        > 下面都用 `#` 引用**本页**目标（同一文档内跳转），不会发给 C#。
        > 渲染器会给每个标题自动生成 slug `id`（同名标题追加 `-1`/`-2`…），
        > 所以“跳标题”的链接生效；指向手写 HTML `id` 目标、以及脚注 `fn1`/`fnref1` 的链接同样生效。

        - [跳到 “1. Text Formatting”](#1-text-formatting)
        - [跳到 “3. Lists”](#3-lists)
        - [跳到 “10. Function Plots”](#10-function-plots)
        - [跳到 “14. HTML Inline”](#14-html-inline)
        - [跳到 “16. Chat / Collapsible Blocks”](#16-chat-collapsible-blocks)
        - [跳到 手写 HTML id 目标（应生效）](#inpage-demo-target)
        - [跳到 脚注定义 fn1（应生效）](#fn1)
        - [跳回 脚注引用处 fnref1（应生效）](#fnref1)

        ## 1. Text Formatting

        **Bold** *Italic* ~~Strikethrough~~ `Inline code` <u>Underline (HTML)</u>

        ## 2. Links & Images

        [Visit GitHub](https://github.com)

        [About this app — in-app navigation](app://about)

        ![NuGet Version](https://img.shields.io/nuget/v/AvalonMarkdown?logo=nuget&label=NuGet)

        ## 3. Lists

        ### Ordered List

        1. First item
        2. Second item
        3. Third item

        ### Unordered List

        - Item A
        - Item B
        - Item C

        ### Task List

        - [x] Completed task
        - [ ] Incomplete task
        - [x] Another done task

        ## 4. Blockquotes

        > This is a blockquote.
        > Second line of the quote.
        >
        > > Nested blockquote
        > > More nested content

        ## 5. Code Blocks with Syntax Highlighting

        ```csharp
        public class Hello
        {
            public static void Main()
            {
                Console.WriteLine("Hello World!");
            }
        }
        ```

        ```python
        def fibonacci(n):
            a, b = 0, 1
            for _ in range(n):
                yield a
                a, b = b, a + b
        ```

        ```javascript
        const greet = (name) => {
            return `Hello, ${name}!`;
        };
        console.log(greet('World'));
        ```

        ## 6. Tables

        | Language | Typing | Speed | Popularity |
        |----------|--------|-------|------------|
        | C#       | Static | Fast  | ★★★★★      |
        | Python   | Dynamic| Medium| ★★★★★      |
        | Rust     | Static | Fast  | ★★★★☆      |
        | JavaScript| Dynamic| Fast  | ★★★★★      |

        ## 7. Math (KaTeX)

        Inline math: $E = mc^2$

        Block math — the quadratic formula:

        $$
        x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}
        $$

        ## 8. Mermaid Diagrams

        ```mermaid
        graph TD
            A[Start] --> B{Decision}
            B -->|Yes| C[Process]
            B -->|No| D[End]
        ```

        ```mermaid
        sequenceDiagram
            Alice->>John: Hello John, how are you?
            John-->>Alice: Great!
        ```

        ## 9. PlantUML Diagrams

        ```plantuml
        @startuml
        Alice -> Bob: Authentication Request
        Bob --> Alice: Authentication Response
        Alice -> Bob: Another request
        Bob --> Alice: OK
        @enduml
        ```

        ```plantuml
        @startuml
        start
        :Initialize;
        if (Is valid?) then (yes)
          :Process data;
        else (no)
          :Show error;
          stop
        endif
        :Complete;
        stop
        @enduml
        ```

        ## 10. Function Plots

        > `plot` 围栏的内容是 function-plot 的 options 对象（**原始 JSON**）。
        > 未写 `color` 时曲线颜色按数据序号取自主题调色板。
        > 默认**静态**（滚轮留给页面滚动）；写 `"disableZoom": false` 才可缩放平移。

        ```plot
        {
          "title": "sin(x)/x and its envelope",
          "grid": true,
          "xAxis": { "domain": [-12, 12] },
          "yAxis": { "domain": [-0.4, 1.1] },
          "data": [
            { "fn": "sin(x)/x" },
            { "fn": "1/x", "skipTip": true },
            { "fn": "-1/x", "skipTip": true }
          ]
        }
        ```

        Explicit colours, axis labels, a tangent line, and zoom opt-in:

        ```plot
        {
          "title": "x^2/8 - 2 with tangent at x0 = 3",
          "grid": true,
          "disableZoom": false,
          "xAxis": { "domain": [-6, 6], "label": "x" },
          "yAxis": { "domain": [-3, 4], "label": "y" },
          "data": [
            { "fn": "x^2/8 - 2", "color": "#e06c75",
              "derivative": { "fn": "x/4", "x0": 3 } },
            { "fn": "sin(x)", "color": "#98c379" }
          ]
        }
        ```

        Deliberately malformed — exercises the error path (a trailing comma is invalid JSON):

        ```plot
        { "data": [ { "fn": "x^2" }, ] }
        ```

        The `plot` interception matches the info string exactly, so an ordinary `json` fence is
        untouched. This is **the same object as the first block above**, rendered as a highlighted
        code block instead of a chart:

        ```json
        {
          "title": "sin(x)/x and its envelope",
          "grid": true,
          "xAxis": { "domain": [-12, 12] },
          "yAxis": { "domain": [-0.4, 1.1] },
          "data": [
            { "fn": "sin(x)/x" },
            { "fn": "1/x", "skipTip": true },
            { "fn": "-1/x", "skipTip": true }
          ]
        }
        ```

        ## 11. Video Embeds

        > Bilibili Video

        ![Test](https://www.bilibili.com/video/BV1Pv411j7xd)

        > YouTube Video

        ![Test](https://www.youtube.com/watch?v=YhKuZImznEE&list=PLrW43fNmjaQWMhFHxS1jpQ34TkHroHJLb)

        > Vimeo Video

        ![Test](https://vimeo.com/76979871)

        ## 12. Footnotes

        Here is a footnote reference[^1] and another[^2].

        [^1]: This is the first footnote.
        [^2]: This is the second footnote with more details.

        ## 13. Horizontal Rule

        ---

        <a id="inpage-demo-target"></a>

        ## 14. HTML Inline

        <span style="color:orange">Orange text via HTML</span>

        <details>
        <summary>Click to expand</summary>
        Hidden content here.
        </details>

        ## 15. Emojis

        🚀 ✅ ❤️ ⭐ 🎉 🔥 🎯

        ## 16. Chat / Collapsible Blocks

        ```chat[思考中]
        正在检查 `renderer.js` 的分派规则…
        ```

        ```chat[工具调用中][open]
        运行 `dotnet build`：

        - `--configuration Release`
        - `--no-restore`
        ```

        ```chat[已完成][static]
        任务结束，状态光圈停止旋转。
        ```
        """;

    /// <summary>
    /// Returns the markdown content for an in-app route — the demo target of
    /// <see cref="AvalonMarkdown.Views.MarkdownView.LinkClicked"/>. Links use the
    /// <c>app://</c> scheme and are intercepted in-app instead of opening the browser.
    /// </summary>
    public static string GetInternalPage(string url) => url.Trim().ToLowerInvariant() switch
    {
        "app://about" => """
            # 📖 About — in-app page

            This page was **routed internally** through `MarkdownView.LinkClicked`:

            - The link you clicked used the `app://` scheme.
            - No system browser was opened.
            - The demo set `e.Handled = true` to take over navigation.

            [← Back to demo](app://home)
            """,
        "app://home" => GetDefaultMarkdown(),
        _ => $"""
            # Unknown in-app route

            `{url}` isn't a known route.

            [← Back to demo](app://home)
            """
    };

    public static string GetSimpleMarkdown() => """
        # Hello

        Quick render test

        - Item A
        - Item B
        - Item C

        **Bold** *Italic* `code`
        """;

    public static string GetBigDocumentMarkdown() => """
        # Large Document Stress Test

        ## Chapter 1

        This is a long paragraph designed to test rendering performance of large Markdown documents.
        Repeated content simulates real-world long documents.

        ### 1.1 Code Blocks

        ```python
        def fibonacci(n):
            a, b = 0, 1
            for _ in range(n):
                yield a
                a, b = b, a + b

        for i, val in enumerate(fibonacci(20)):
            print(f"F[{i}] = {val}")
        ```

        ```javascript
        // Complex business logic simulation
        class DataProcessor {
            constructor(data) {
                this.data = data;
                this.cache = new Map();
            }

            async process(batchSize = 100) {
                const results = [];
                for (let i = 0; i < this.data.length; i += batchSize) {
                    const batch = this.data.slice(i, i + batchSize);
                    results.push(await this._analyze(batch));
                }
                return results.flat();
            }

            async _analyze(batch) {
                return batch.map(item => ({
                    id: item.id,
                    score: Math.sqrt(item.value) * item.weight,
                    category: item.type === 'A' ? 'Premium' : 'Standard'
                }));
            }
        }
        ```

        ### 1.2 Math Formulas

        Inline math: when $a \ne 0$, the solution to $ax^2 + bx + c = 0$ is:

        $$
        x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}
        $$

        ### 1.3 Tables

        | City       | Temp(°C) | Humidity(%) | Wind(km/h) | Air Quality |
        |------------|----------|-------------|------------|-------------|
        | Beijing    | 28       | 45          | 12         | Good        |
        | Shanghai   | 32       | 72          | 8          | Moderate    |
        | Guangzhou  | 35       | 85          | 6          | Light Poll. |
        | Shenzhen   | 33       | 78          | 10         | Moderate    |
        | Chengdu    | 29       | 65          | 5          | Good        |

        ## Chapter 2

        > This is a long blockquote for testing blockquote rendering.
        > Second line of quoted content.
        >
        > > Nested blockquote
        > > Nested second line

        - [x] Task one
        - [ ] Task two
        - [x] Task three

        ---

        > Performance test conclusion: Large documents should render within 200ms.
        """;

    // ─── Special-member showcases ───────────────────────────────────────────
    // Kept as .md assets under Assets/Showcase/ rather than C# raw strings, so
    // render_check.py can validate the EXACT file the demo renders instead of a
    // hand-extracted copy. Embedded automatically by the Assets\** glob.

    private static string LoadShowcase(string fileName)
    {
        const string fallback = "# Showcase asset missing\n\n" +
            "Expected `Assets/Showcase/{0}` to be embedded as an AvaloniaResource.";
        try
        {
            using var stream = AssetLoader.Open(
                new Uri($"avares://AvalonMarkdown.Test.Shared/Assets/Showcase/{fileName}"));
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            return string.Format(fallback, fileName) + $"\n\n```\n{ex.Message}\n```";
        }
    }

    /// <summary>Every table variant — alignment, escaping, CJK, wide, degenerate.</summary>
    public static string GetTablesShowcase() => LoadShowcase("tables.md");

    /// <summary>Mermaid and PlantUML diagram types, including deliberate failures.</summary>
    public static string GetDiagramsShowcase() => LoadShowcase("diagrams.md");

    /// <summary>Function plots — the full easing set plus other graph types.</summary>
    public static string GetPlotsShowcase() => LoadShowcase("plots.md");
}
