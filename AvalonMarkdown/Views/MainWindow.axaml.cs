using Avalonia.Controls;

namespace AvalonMarkdown.Views;

/// <summary>
/// Desktop 窗口 — 仅作为 MainView 的容器，所有逻辑均在 MainView 中。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
