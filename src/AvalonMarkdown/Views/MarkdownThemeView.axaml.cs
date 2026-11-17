using System;
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
    private readonly MarkdownThemeViewModel _themeConfig;
    private MarkdownView? _attachedTarget;
    private EventHandler? _readyHandler;

    /// <summary>
    /// The MarkdownThemeViewModel driving this editor.
    /// </summary>
    public MarkdownThemeViewModel ThemeConfig => _themeConfig;

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
        _themeConfig = new MarkdownThemeViewModel();
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
        sender.AttachTarget(e.NewValue as MarkdownView);
    }

    /// <summary>
    /// Detach from the previous preview and attach to the new one. The old
    /// OnReady subscription is removed: without this, changing Target repeatedly
    /// leaves each former preview registered and re-applying CSS forever.
    /// </summary>
    private void AttachTarget(MarkdownView? target)
    {
        DetachTarget();

        _attachedTarget = target;
        if (target is null)
            return;

        if (target.IsReady)
        {
            _themeConfig.RegisterRenderer(target);
            _themeConfig.StartAutoApply();
            return;
        }

        _readyHandler = (_, _) =>
        {
            // The target may have changed while the preview was loading.
            if (!ReferenceEquals(_attachedTarget, target))
                return;
            _themeConfig.RegisterRenderer(target);
            _themeConfig.StartAutoApply();
        };
        target.OnReady += _readyHandler;
    }

    private void DetachTarget()
    {
        if (_attachedTarget is not null && _readyHandler is not null)
            _attachedTarget.OnReady -= _readyHandler;

        _readyHandler = null;
        _attachedTarget = null;
        _themeConfig.StopAutoApply();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // The editor may have been detached while the preview stayed alive.
        if (_attachedTarget is null && Target is { } target)
        {
            AttachTarget(target);
        }
        else if (_attachedTarget is { IsReady: true })
        {
            _themeConfig.RegisterRenderer(_attachedTarget);
            _themeConfig.StartAutoApply();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // Stop the 10 Hz timer while the editor is not visible. The Target
        // property (and its OnReady subscription) is kept so re-attaching resumes
        // without the caller having to re-bind.
        _themeConfig.StopAutoApply();
    }
}
