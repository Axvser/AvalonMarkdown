using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using AvalonMarkdown.Views;
using AvalonMarkdown.Test.Shared.ViewModels;

namespace AvalonMarkdown.Test.Shared.Views;

public partial class MainView : UserControl
{
    private readonly MainViewModel _vm;
    private MarkdownView? _singlePreview;
    private MarkdownView? _multiPv1, _multiPv2, _multiPv3;
    private bool _multiViewMode;

    public MainView()
    {
        InitializeComponent();

        _vm = new MainViewModel();
        DataContext = _vm;

        // Single view
        _singlePreview = this.FindControl<MarkdownView>("Preview")!;
        _singlePreview.OnReady += (_, _) => OnPreviewReady(_singlePreview, _vm.MarkdownText, 1);
        MarkdownEditor.TextChanged += OnMarkdownChanged;

        // Multi-view (initialized on demand)

        // Preset buttons
        SimplePresetButton.Click += (_, _) => LoadPreset(MainViewModel.GetSimpleMarkdown());
        FullPresetButton.Click += (_, _) => LoadPreset(MainViewModel.GetDefaultMarkdown());
        BigDocPresetButton.Click += (_, _) => LoadPreset(MainViewModel.GetBigDocumentMarkdown());

        // Multi-view toggle
        MultiViewToggle.Click += (_, _) => ToggleMultiView();
    }

    // ====================================================================
    // Single view
    // ====================================================================

    private async void OnPreviewReady(MarkdownView? preview, string markdown, int index)
    {
        _vm.RecordReady(index);

        // Wait for WebView internal JS to fully initialize
        await Task.Delay(500);

        if (preview is not null)
            await preview.RenderMarkdownAsync(markdown);

        _vm.RecordRendered(index);
    }

    private async void OnMarkdownChanged(object? sender, EventArgs e)
    {
        if (_singlePreview is not null)
        {
            _vm.RecordReady(1);
            await _singlePreview.RenderMarkdownAsync(MarkdownEditor.Text);
            _vm.RecordRendered(1);
        }
    }

    private async void LoadPreset(string markdown)
    {
        _vm.MarkdownText = markdown;
        if (_singlePreview is not null)
        {
            _vm.RecordReady(1);
            await _singlePreview.RenderMarkdownAsync(markdown);
            _vm.RecordRendered(1);
        }
    }

    // ====================================================================
    // Multi-view toggle
    // ====================================================================

    private void ToggleMultiView()
    {
        _multiViewMode = !_multiViewMode;
        MultiViewToggle.Content = _multiViewMode ? "Single" : "Multi";
        SingleView.IsVisible = !_multiViewMode;
        MultiView.IsVisible = _multiViewMode;

        if (_multiViewMode)
        {
            _vm.IsMultiView = true;
            _vm.ResetMultiView();
            InitMultiView();
        }
        else
        {
            _vm.IsMultiView = false;
        }
    }

    private void InitMultiView()
    {
        if (_multiPv1 is not null) return; // already initialized

        _multiPv1 = this.FindControl<MarkdownView>("Preview1")!;
        _multiPv2 = this.FindControl<MarkdownView>("Preview2")!;
        _multiPv3 = this.FindControl<MarkdownView>("Preview3")!;

        // View 1: Simple
        _multiPv1.OnReady += (_, _) =>
            OnPreviewReady(_multiPv1, MainViewModel.GetSimpleMarkdown(), 1);

        // View 2: Full
        _multiPv2.OnReady += (_, _) =>
            OnPreviewReady(_multiPv2, MainViewModel.GetDefaultMarkdown(), 2);

        // View 3: Big Doc
        _multiPv3.OnReady += (_, _) =>
            OnPreviewReady(_multiPv3, MainViewModel.GetBigDocumentMarkdown(), 3);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
    }
}
