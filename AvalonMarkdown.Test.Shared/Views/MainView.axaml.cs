using System;
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

        // Single view — binding handles Text, OnReady just tracks timing
        _singlePreview = this.FindControl<MarkdownView>("Preview")!;
        _singlePreview.OnReady += (_, _) =>
        {
            _vm.RecordReady(1);
            _vm.RecordRendered(1);
        };

        // Preset buttons — setting MarkdownText triggers the Text binding
        SimplePresetButton.Click += (_, _) => _vm.MarkdownText = MainViewModel.GetSimpleMarkdown();
        FullPresetButton.Click += (_, _) => _vm.MarkdownText = MainViewModel.GetDefaultMarkdown();
        BigDocPresetButton.Click += (_, _) => _vm.MarkdownText = MainViewModel.GetBigDocumentMarkdown();

        // Multi-view toggle
        MultiViewToggle.Click += (_, _) => ToggleMultiView();

        // Editor collapse toggle
        EditorToggle.PointerPressed += (_, _) =>
        {
            _vm.ToggleEditor();
            EditorArrowExpanded.IsVisible = _vm.EditorExpanded;
            EditorArrowCollapsed.IsVisible = !_vm.EditorExpanded;
        };
    }

    // ====================================================================
    // Single view — binding handles rendering via Text property
    // ====================================================================

    // Private void OnPreviewReady removed — binding + _pendingMarkdown
    // handles the "Text set before OnReady" timing automatically.

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

        // Set Text directly — the property handles deferred rendering if not yet ready
        _multiPv1.Text = MainViewModel.GetSimpleMarkdown();
        _multiPv2.Text = MainViewModel.GetDefaultMarkdown();
        _multiPv3.Text = MainViewModel.GetBigDocumentMarkdown();

        // OnReady still useful for timing measurement
        _multiPv1.OnReady += (_, _) => { _vm.RecordReady(1); _vm.RecordRendered(1); };
        _multiPv2.OnReady += (_, _) => { _vm.RecordReady(2); _vm.RecordRendered(2); };
        _multiPv3.OnReady += (_, _) => { _vm.RecordReady(3); _vm.RecordRendered(3); };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
    }
}
