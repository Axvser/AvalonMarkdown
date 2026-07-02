using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using AvalonMarkdown;

internal sealed partial class Program
{
    private static Task Main(string[] args) => BuildAvaloniaApp()
            .WithInterFont()
            .WithSystemFontSource(new Uri("avares://AvalonMarkdown/Assets/Fonts/msyh.ttf"))
            .WithSystemFontSource(new Uri("avares://AvalonMarkdown/Assets/Fonts/NotoColorEmoji.ttf"))
            .WithSystemFontSource(new Uri("avares://AvalonMarkdown/Assets/Fonts/NotoSans-Italic-VariableFont_wdth,wght.ttf"))
#if DEBUG
            .WithDeveloperTools()
#endif
            .StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}