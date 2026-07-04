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

    // 每个视图独立的计时器
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
                TimingInfo = _firstLoad ? $"⏱ #{index} 首次: {ms}ms" : $"⏱ #{index}: {ms}ms";
                break;
            case 2:
                ms = _sw2.ElapsedMilliseconds;
                TimingInfo2 = _firstLoad ? $"⏱ #{index} 首次: {ms}ms" : $"⏱ #{index}: {ms}ms";
                break;
            case 3:
                ms = _sw3.ElapsedMilliseconds;
                TimingInfo3 = _firstLoad ? $"⏱ #{index} 首次: {ms}ms" : $"⏱ #{index}: {ms}ms";
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
        # Markdown 渲染测试套件 🎯

        ## 1. 文本格式

        **粗体** *斜体* ~~删除线~~ `行内代码`

        ## 2. 数学公式

        行内公式：$E = mc^2$

        独立公式：
        $$
        \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}
        $$

        ## 3. 代码高亮

        ```csharp
        public class Hello
        {
            public static void Main()
            {
                Console.WriteLine("Hello World!");
            }
        }
        ```

        ## 4. 图表 (Mermaid)

        ```mermaid
        graph TD
            A[开始] --> B{判断}
            B -->|是| C[处理]
            B -->|否| D[结束]
        ```

        ## 5. 表格

        | 名称 | 价格 | 数量 |
        |------|------|------|
        | 苹果 | ¥5.0 | 100 |
        | 香蕉 | ¥3.5 | 200 |
        """;

    public static string GetSimpleMarkdown() => """
        # Hello

        快速渲染测试

        - 项目 A
        - 项目 B
        - 项目 C
        """;

    public static string GetBigDocumentMarkdown() => """
        # 大文档压力测试

        ## 第一章

        这是一段很长的文本，用于测试大规模 Markdown 文档的渲染性能。
        重复内容用于模拟真实场景下的长文档。

        ### 1.1 代码块

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
        // 复杂业务逻辑模拟
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
                    category: item.type === 'A' ? '高端' : '标准'
                }));
            }
        }
        ```

        ### 1.2 数学公式

        行内公式：当 $a \ne 0$ 时，方程 $ax^2 + bx + c = 0$ 的解为：

        $$
        x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}
        $$

        ### 1.3 表格

        | 城市 | 温度(°C) | 湿度(%) | 风速(km/h) | 空气质量 |
        |------|----------|---------|------------|----------|
        | 北京 | 28 | 45 | 12 | 良好 |
        | 上海 | 32 | 72 | 8 | 中等 |
        | 广州 | 35 | 85 | 6 | 轻度污染 |
        | 深圳 | 33 | 78 | 10 | 中等 |
        | 成都 | 29 | 65 | 5 | 良好 |

        ## 第二章

        > 这是一段很长的引用文本，用于验证引用块的渲染效果。
        > 第二行引用内容。
        >
        > > 嵌套引用
        > > 嵌套第二行

        - [x] 任务一
        - [ ] 任务二
        - [x] 任务三

        ---

        > 性能测试结论：大文档应在 200ms 内完成渲染。
        """;
}
