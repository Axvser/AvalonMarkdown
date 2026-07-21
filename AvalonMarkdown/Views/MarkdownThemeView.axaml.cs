using Avalonia;
using Avalonia.Controls;
using AvalonMarkdown.ViewModels;

namespace AvalonMarkdown.Views;

/// <summary>
/// An out-of-the-box theme editor control that provides RGB sliders for
/// customising the MarkdownView renderer appearance.
///
/// Usage:
/// <code>
/// &lt;md:MarkdownView x:Name="Preview" /&gt;
/// &lt;md:MarkdownThemeView Target="{Binding #Preview}" /&gt;
/// </code>
/// </summary>
public partial class MarkdownThemeView : UserControl
{
    private readonly ThemeConfigViewModel _themeConfig;

    /// <summary>
    /// The ThemeConfigViewModel driving this editor.
    /// </summary>
    public ThemeConfigViewModel ThemeConfig => _themeConfig;

    /// <summary>
    /// Gets or sets the MarkdownView this editor is controlling.
    /// When set, the editor automatically registers and starts auto-apply.
    /// </summary>
    public static readonly StyledProperty<MarkdownView?> TargetProperty =
        AvaloniaProperty.Register<MarkdownThemeView, MarkdownView?>(nameof(Target));

    public MarkdownView? Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    static MarkdownThemeView()
    {
        TargetProperty.Changed.AddClassHandler<MarkdownThemeView>(OnTargetChanged);
    }

    public MarkdownThemeView()
    {
        _themeConfig = new ThemeConfigViewModel();
        DataContext = _themeConfig;
        InitializeComponent();

        // Wire toggle header
        var header = this.FindControl<Border>("ToggleHeader")!;
        header.PointerPressed += (_, _) => ToggleContent();
    }

    private void ToggleContent()
    {
        var expanded = this.FindControl<ScrollViewer>("ContentPanel")!.IsVisible;
        this.FindControl<ScrollViewer>("ContentPanel")!.IsVisible = !expanded;
        this.FindControl<TextBlock>("ArrowExpanded")!.IsVisible = !expanded;
        this.FindControl<TextBlock>("ArrowCollapsed")!.IsVisible = expanded;
    }

    private static void OnTargetChanged(MarkdownThemeView sender, AvaloniaPropertyChangedEventArgs e)
    {
        var oldTarget = e.OldValue as MarkdownView;
        var newTarget = e.NewValue as MarkdownView;

        if (oldTarget is not null)
            sender._themeConfig.StopAutoApply();

        if (newTarget is not null)
        {
            newTarget.OnReady += (_, _) =>
            {
                sender._themeConfig.RegisterRenderer(newTarget);
                sender._themeConfig.StartAutoApply();
            };
        }
    }
}
