using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvalonMarkdown.Test.Shared.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _markdownText = GetDefaultMarkdown();

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
}
