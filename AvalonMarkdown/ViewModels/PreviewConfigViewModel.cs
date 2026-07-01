using CommunityToolkit.Mvvm.ComponentModel;

namespace AvalonMarkdown.ViewModels;

public partial class PreviewConfigViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isDarkTheme = true;

    [ObservableProperty]
    private int _fontSize = 14;

    [ObservableProperty]
    private double _lineHeight = 1.6;

    [ObservableProperty]
    private bool _showCodeLanguage = true;

    [ObservableProperty]
    private bool _showCopyButton = true;

    [ObservableProperty]
    private int _maxCodeBlockHeight = 480;

    public string ToJsCallExpression()
    {
        return $"setPreviewConfig({{fontSize:{FontSize},lineHeight:{LineHeight},showCodeLanguage:{ShowCodeLanguage.ToString().ToLower()},showCopyButton:{ShowCopyButton.ToString().ToLower()},maxCodeBlockHeight:{MaxCodeBlockHeight}}})";
    }
}
