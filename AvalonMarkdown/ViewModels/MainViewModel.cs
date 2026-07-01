using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvalonMarkdown.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public PreviewConfigViewModel PreviewConfig { get; } = new();

    [ObservableProperty]
    private string _markdownText = GetDefaultMarkdown();

    public bool IsDarkTheme
    {
        get => PreviewConfig.IsDarkTheme;
        set => PreviewConfig.IsDarkTheme = value;
    }

    public event EventHandler? ThemeChanged;

    [RelayCommand]
    private void ToggleTheme()
    {
        PreviewConfig.IsDarkTheme = !PreviewConfig.IsDarkTheme;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string GetDefaultMarkdown() => "# Markdown 渲染测试套件 🎯\n\n## 1. 文本格式\n\n**粗体** *斜体* ~~删除线~~ `行内代码`\n\n## 2. 数学公式\n\n行内公式：$E = mc^2$\n\n独立公式：\n$$\n\\frac{-b \\pm \\sqrt{b^2 - 4ac}}{2a}\n$$\n\n## 3. 代码高亮\n\n```csharp\npublic class Hello\n{\n    public static void Main()\n    {\n        Console.WriteLine(\"Hello World!\");\n    }\n}\n```\n\n```javascript\nfunction greet(name) {\n    return `Hello, ${name}!`;\n}\nconsole.log(greet(\"World\"));\n```\n\n```python\ndef fibonacci(n):\n    a, b = 0, 1\n    for _ in range(n):\n        yield a\n        a, b = b, a + b\nlist(fibonacci(10))\n```\n\n## 4. Mermaid 图表\n\n```mermaid\ngraph TD\n    A[开始] --> B{判断}\n    B -->|是| C[处理]\n    B -->|否| D[结束]\n    C --> D\n```\n\n```mermaid\nsequenceDiagram\n    Alice->>John: 你好 John\n    John-->>Alice: 嗨 Alice!\n    Alice->>John: 你听到吗？\n    John-->>Alice: 当然！\n```\n\n## 5. 表格\n\n| 功能 | 状态 | 备注 |\n|------|------|------|\n| markdown-it | ✅ | 核心解析器 |\n| KaTeX | ✅ | 数学公式 |\n| Mermaid | ✅ | 图表 |\n| 代码高亮 | ✅ | 190+ 语言 |\n\n## 6. 引用\n\n> 这是一段引用\n> 这是同一引用的第二行\n>\n> > 这是嵌套引用\n\n## 7. 列表\n\n无序列表：\n- 项目一\n- 项目二\n  - 子项目 A\n  - 子项目 B\n- 项目三\n\n有序列表：\n1. 第一步\n2. 第二步\n3. 第三步\n\n## 8. 任务列表\n\n- [x] 完成基本 Markdown 渲染\n- [x] 代码高亮支持\n- [ ] 编辑与预览滚动同步\n- [ ] 导出 PDF\n\n## 9. 脚注\n\n这里有一段文字需要脚注[^1]，这里还有一段[^longnote]。\n\n[^1]: 这是第一个脚注。\n\n[^longnote]: 这是更长的脚注，包含更多说明内容。\n\n---\n\n✅ 所有测试类别加载完成！请检查各项渲染效果。\n";
}

