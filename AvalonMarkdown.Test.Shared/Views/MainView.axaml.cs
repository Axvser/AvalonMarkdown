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

        // 单视图
        _singlePreview = this.FindControl<MarkdownView>("Preview")!;
        _singlePreview.OnReady += (_, _) => OnPreviewReady(_singlePreview, _vm.MarkdownText, 1);
        MarkdownEditor.TextChanged += OnMarkdownChanged;

        // 多视图（按需初始化）

        // 预设按钮
        SimplePresetButton.Click += (_, _) => LoadPreset(MainViewModel.GetSimpleMarkdown());
        FullPresetButton.Click += (_, _) => LoadPreset(MainViewModel.GetDefaultMarkdown());
        BigDocPresetButton.Click += (_, _) => LoadPreset(MainViewModel.GetBigDocumentMarkdown());

        // 多视图切换
        MultiViewToggle.Click += (_, _) => ToggleMultiView();
    }

    // ====================================================================
    // 单视图
    // ====================================================================

    private async void OnPreviewReady(MarkdownView? preview, string markdown, int index)
    {
        _vm.RecordReady(index);

        // 等待 WebView 内部 JS 完全就绪
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
    // 多视图切换
    // ====================================================================

    private void ToggleMultiView()
    {
        _multiViewMode = !_multiViewMode;
        MultiViewToggle.Content = _multiViewMode ? "单视图" : "多视图";
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
        if (_multiPv1 is not null) return; // 已初始化

        _multiPv1 = this.FindControl<MarkdownView>("Preview1")!;
        _multiPv2 = this.FindControl<MarkdownView>("Preview2")!;
        _multiPv3 = this.FindControl<MarkdownView>("Preview3")!;

        // 视图1: 简单
        _multiPv1.OnReady += (_, _) =>
            OnPreviewReady(_multiPv1, MainViewModel.GetSimpleMarkdown(), 1);

        // 视图2: 完整
        _multiPv2.OnReady += (_, _) =>
            OnPreviewReady(_multiPv2, MainViewModel.GetDefaultMarkdown(), 2);

        // 视图3: 大文档
        _multiPv3.OnReady += (_, _) =>
            OnPreviewReady(_multiPv3, MainViewModel.GetBigDocumentMarkdown(), 3);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
    }
}
