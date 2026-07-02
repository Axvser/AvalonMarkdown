using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvalonMarkdown.Views;

namespace AvalonMarkdown;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 运行时分发 — 统一使用 EmbeddedHtmlSourceProvider（data:text/html;base64），全平台一致
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new Views.MainView();
        }

        base.OnFrameworkInitializationCompleted();
    }
}