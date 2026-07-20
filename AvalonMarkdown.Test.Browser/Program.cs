using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;

namespace AvalonMarkdown.Test.Browser;

internal sealed partial class Program
{
    private static Task Main(string[] args) => BuildAvaloniaApp()
            .WithInterFont()
            // Register custom fonts (Chinese/Emoji/Italic) for XAML FontFamily fallback chain
            .WithSystemFontSource(new Uri("avares://AvalonMarkdown.Test.Shared/Assets/Fonts/msyh.ttf"))
            .WithSystemFontSource(new Uri("avares://AvalonMarkdown.Test.Shared/Assets/Fonts/NotoColorEmoji.ttf"))
            .WithSystemFontSource(new Uri("avares://AvalonMarkdown.Test.Shared/Assets/Fonts/NotoSans-Italic-VariableFont_wdth,wght.ttf"))
            .StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<AvalonMarkdown.Test.Shared.App>();
}
