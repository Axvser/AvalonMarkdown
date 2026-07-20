using System;
using System.Diagnostics;
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

        ## 1. Text Formatting

        **Bold** *Italic* ~~Strikethrough~~ `Inline code` <u>Underline (HTML)</u>

        ## 2. Links & Images

        [Visit GitHub](https://github.com)

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

        Block math:

        $$
        \\frac{{-b \\pm \\sqrt{{b^2 - 4ac}}}}{{2a}}
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

        ## 10. Footnotes

        Here is a footnote reference[^1] and another[^2].

        [^1]: This is the first footnote.
        [^2]: This is the second footnote with more details.

        ## 11. Horizontal Rule

        ---

        ## 12. HTML Inline

        <span style="color:orange">Orange text via HTML</span>

        <details>
        <summary>Click to expand</summary>
        Hidden content here.
        </details>

        ## 13. Emojis

        🚀 ✅ ❤️ ⭐ 🎉 🔥 🎯
        """;

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
}
