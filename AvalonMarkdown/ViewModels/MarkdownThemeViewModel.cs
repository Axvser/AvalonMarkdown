using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Threading;
using AvalonMarkdown.Views;

namespace AvalonMarkdown.ViewModels;

/// <summary>
/// Configures all CSS variable values for a single theme (dark or light).
/// The 6 core colors are exposed as R/G/B integer triplets (0-255) for
/// slider binding. Derived surface colors (BgSecondary, InlineCodeBg,
/// PreBg, TableHeaderBg, …) update automatically.
/// Call <see cref="GenerateCss"/> to produce complete CSS text.
/// </summary>
public class MarkdownThemeViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    // ─── Dirty-flag auto-apply engine ───

    private bool _isDirty;
    private readonly List<WeakReference<MarkdownView>> _renderers = new();
    private readonly DispatcherTimer _timer;
    private bool _started;

    /// <summary>
    /// Register a MarkdownView renderer for automatic CSS updates.
    /// The reference is held weakly so GC is not blocked.
    /// </summary>
    public void RegisterRenderer(MarkdownView view)
    {
        // Avoid registering the same instance twice
        for (var i = _renderers.Count - 1; i >= 0; i--)
        {
            if (_renderers[i].TryGetTarget(out var existing))
            {
                if (existing == view) return;
            }
            else
            {
                _renderers.RemoveAt(i);
            }
        }
        _renderers.Add(new WeakReference<MarkdownView>(view));
    }

    /// <summary>
    /// Start the auto-apply loop (10 Hz on the UI thread).
    /// Safe to call multiple times; only the first call starts the timer.
    /// </summary>
    public void StartAutoApply()
    {
        if (_started) return;
        _started = true;
        _timer.Start();
    }

    /// <summary>
    /// Stop the auto-apply loop and clear all registered renderers.
    /// </summary>
    public void StopAutoApply()
    {
        _started = false;
        _timer.Stop();
        _renderers.Clear();
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (!_isDirty) return;

        // Purge dead weak references
        _renderers.RemoveAll(r => !r.TryGetTarget(out _));

        if (_renderers.Count == 0)
        {
            _isDirty = false;
            return;
        }

        _isDirty = false;
        var css = GenerateCss();

        foreach (var wr in _renderers)
        {
            if (wr.TryGetTarget(out var view))
            {
                try { await view.ApplyCustomCssAsync(css); } catch { /* swallow */ }
            }
        }
    }

    private void MarkDirty() => _isDirty = true;

    private void OnPropertyChanged([CallerMemberName] string name = "")
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public MarkdownThemeViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += OnTimerTick;
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        // Also notify all derived color properties that may have changed
        OnPropertyChanged(nameof(BgPrimary));
        OnPropertyChanged(nameof(BgSecondary));
        OnPropertyChanged(nameof(TextPrimary));
        OnPropertyChanged(nameof(TextSecondary));
        OnPropertyChanged(nameof(LinkColor));
        OnPropertyChanged(nameof(HeadingColor));
        OnPropertyChanged(nameof(InlineCodeColor));
        OnPropertyChanged(nameof(BorderColor));
        OnPropertyChanged(nameof(InlineCodeBg));
        OnPropertyChanged(nameof(BlockquoteBorder));
        OnPropertyChanged(nameof(BlockquoteBg));
        OnPropertyChanged(nameof(PreBg));
        OnPropertyChanged(nameof(TableHeaderBg));
        OnPropertyChanged(nameof(BgHex));
        OnPropertyChanged(nameof(TextHex));
        OnPropertyChanged(nameof(LinkHex));
        OnPropertyChanged(nameof(HeadingHex));
        OnPropertyChanged(nameof(CodeHex));
        OnPropertyChanged(nameof(BorderHex));
        OnPropertyChanged(nameof(BgSecHex));
        OnPropertyChanged(nameof(TextSecHex));
        OnPropertyChanged(nameof(CodeBgHex));
        OnPropertyChanged(nameof(PreBgHex));
        OnPropertyChanged(nameof(TableBgHex));
        OnPropertyChanged(nameof(MermaidBgHex));
        OnPropertyChanged(nameof(PumlBgHex));
        OnPropertyChanged(nameof(PlotBgHex));
        OnPropertyChanged(nameof(ChatBgHex));
        OnPropertyChanged(nameof(CodeFontSize));
        OnPropertyChanged(nameof(BorderRadius));
        MarkDirty();
        return true;
    }

    // ─── Core colors (R/G/B triplets, 0-255) ───

    #region Core color RGB properties
    // Background  #1e1e1e
    private int _bgR = 30;
    private int _bgG = 30;
    private int _bgB = 30;
    [Description("页面主背景色的红色通道（0-255）")]
    public int BgR { get => _bgR; set => SetProperty(ref _bgR, value); }
    [Description("页面主背景色的绿色通道（0-255）")]
    public int BgG { get => _bgG; set => SetProperty(ref _bgG, value); }
    [Description("页面主背景色的蓝色通道（0-255）")]
    public int BgB { get => _bgB; set => SetProperty(ref _bgB, value); }

    // Text  #d4d4d4
    private int _textR = 212;
    private int _textG = 212;
    private int _textB = 212;
    [Description("正文文字色的红色通道（0-255）")]
    public int TextR { get => _textR; set => SetProperty(ref _textR, value); }
    [Description("正文文字色的绿色通道（0-255）")]
    public int TextG { get => _textG; set => SetProperty(ref _textG, value); }
    [Description("正文文字色的蓝色通道（0-255）")]
    public int TextB { get => _textB; set => SetProperty(ref _textB, value); }

    // Link  #3794ff
    private int _linkR = 55;
    private int _linkG = 148;
    private int _linkB = 255;
    [Description("链接色的红色通道（0-255）")]
    public int LinkR { get => _linkR; set => SetProperty(ref _linkR, value); }
    [Description("链接色的绿色通道（0-255）")]
    public int LinkG { get => _linkG; set => SetProperty(ref _linkG, value); }
    [Description("链接色的蓝色通道（0-255）")]
    public int LinkB { get => _linkB; set => SetProperty(ref _linkB, value); }

    // Heading  #569cd6
    private int _headingR = 86;
    private int _headingG = 156;
    private int _headingB = 214;
    [Description("标题文字色的红色通道（0-255）")]
    public int HeadingR { get => _headingR; set => SetProperty(ref _headingR, value); }
    [Description("标题文字色的绿色通道（0-255）")]
    public int HeadingG { get => _headingG; set => SetProperty(ref _headingG, value); }
    [Description("标题文字色的蓝色通道（0-255）")]
    public int HeadingB { get => _headingB; set => SetProperty(ref _headingB, value); }

    // Inline code  #ce9178
    private int _codeR = 206;
    private int _codeG = 145;
    private int _codeB = 120;
    [Description("行内代码文字色的红色通道（0-255）")]
    public int CodeR { get => _codeR; set => SetProperty(ref _codeR, value); }
    [Description("行内代码文字色的绿色通道（0-255）")]
    public int CodeG { get => _codeG; set => SetProperty(ref _codeG, value); }
    [Description("行内代码文字色的蓝色通道（0-255）")]
    public int CodeB { get => _codeB; set => SetProperty(ref _codeB, value); }

    // Border  #3c3c3c
    private int _borderR = 60;
    private int _borderG = 60;
    private int _borderB = 60;
    [Description("边框色的红色通道（0-255）")]
    public int BorderR { get => _borderR; set => SetProperty(ref _borderR, value); }
    [Description("边框色的绿色通道（0-255）")]
    public int BorderG { get => _borderG; set => SetProperty(ref _borderG, value); }
    [Description("边框色的蓝色通道（0-255）")]
    public int BorderB { get => _borderB; set => SetProperty(ref _borderB, value); }
    #endregion

    // ─── Extended colors (R/G/B triplets, independent controls) ───

    #region Extended color RGB properties
    // BgSecondary  #252526
    private int _bgSecR = 37;
    private int _bgSecG = 37;
    private int _bgSecB = 38;
    [Description("次级背景色的红色通道（0-255）")]
    public int BgSecR { get => _bgSecR; set => SetProperty(ref _bgSecR, value); }
    [Description("次级背景色的绿色通道（0-255）")]
    public int BgSecG { get => _bgSecG; set => SetProperty(ref _bgSecG, value); }
    [Description("次级背景色的蓝色通道（0-255）")]
    public int BgSecB { get => _bgSecB; set => SetProperty(ref _bgSecB, value); }

    // TextSecondary  #cccccc
    private int _textSecR = 204;
    private int _textSecG = 204;
    private int _textSecB = 204;
    [Description("次级文字色的红色通道（0-255）")]
    public int TextSecR { get => _textSecR; set => SetProperty(ref _textSecR, value); }
    [Description("次级文字色的绿色通道（0-255）")]
    public int TextSecG { get => _textSecG; set => SetProperty(ref _textSecG, value); }
    [Description("次级文字色的蓝色通道（0-255）")]
    public int TextSecB { get => _textSecB; set => SetProperty(ref _textSecB, value); }

    // InlineCodeBg  #2d2d2d
    private int _codeBgR = 45;
    private int _codeBgG = 45;
    private int _codeBgB = 45;
    [Description("行内代码背景色的红色通道（0-255）")]
    public int CodeBgR { get => _codeBgR; set => SetProperty(ref _codeBgR, value); }
    [Description("行内代码背景色的绿色通道（0-255）")]
    public int CodeBgG { get => _codeBgG; set => SetProperty(ref _codeBgG, value); }
    [Description("行内代码背景色的蓝色通道（0-255）")]
    public int CodeBgB { get => _codeBgB; set => SetProperty(ref _codeBgB, value); }

    // PreBg  #1e1e1e
    private int _preBgR = 30;
    private int _preBgG = 30;
    private int _preBgB = 30;
    [Description("代码块背景色的红色通道（0-255）")]
    public int PreBgR { get => _preBgR; set => SetProperty(ref _preBgR, value); }
    [Description("代码块背景色的绿色通道（0-255）")]
    public int PreBgG { get => _preBgG; set => SetProperty(ref _preBgG, value); }
    [Description("代码块背景色的蓝色通道（0-255）")]
    public int PreBgB { get => _preBgB; set => SetProperty(ref _preBgB, value); }

    // TableHeaderBg  #2d2d2d
    private int _tableBgR = 45;
    private int _tableBgG = 45;
    private int _tableBgB = 45;
    [Description("表头背景色的红色通道（0-255）")]
    public int TableBgR { get => _tableBgR; set => SetProperty(ref _tableBgR, value); }
    [Description("表头背景色的绿色通道（0-255）")]
    public int TableBgG { get => _tableBgG; set => SetProperty(ref _tableBgG, value); }
    [Description("表头背景色的蓝色通道（0-255）")]
    public int TableBgB { get => _tableBgB; set => SetProperty(ref _tableBgB, value); }
    #endregion

    // ─── Typography / layout ───

    #region Typography properties
    private double _codeFontSize = 13;
    private int _borderRadius = 4;

    [Description("代码字体大小（px）")]
    public double CodeFontSize  { get => _codeFontSize; set => SetProperty(ref _codeFontSize, value); }
    [Description("代码块/pre 圆角半径（px）")]
    public int    BorderRadius  { get => _borderRadius; set => SetProperty(ref _borderRadius, value); }
    #endregion

    // ─── Derived computed properties ───

    [Description("派生：页面主背景色（只读）")]
    public string BgPrimary => $"#{BgR:X2}{BgG:X2}{BgB:X2}";
    [Description("派生：次级背景色（只读）")]
    public string BgSecondary => $"#{BgSecR:X2}{BgSecG:X2}{BgSecB:X2}";
    [Description("派生：正文文字色（只读）")]
    public string TextPrimary => $"#{TextR:X2}{TextG:X2}{TextB:X2}";
    [Description("派生：次级文字色（只读）")]
    public string TextSecondary => $"#{TextSecR:X2}{TextSecG:X2}{TextSecB:X2}";
    [Description("派生：链接色（只读）")]
    public string LinkColor => $"#{LinkR:X2}{LinkG:X2}{LinkB:X2}";
    [Description("派生：标题文字色（只读）")]
    public string HeadingColor => $"#{HeadingR:X2}{HeadingG:X2}{HeadingB:X2}";
    [Description("派生：行内代码文字色（只读）")]
    public string InlineCodeColor => $"#{CodeR:X2}{CodeG:X2}{CodeB:X2}";
    [Description("派生：边框色（只读）")]
    public string BorderColor => $"#{BorderR:X2}{BorderG:X2}{BorderB:X2}";
    [Description("派生：行内代码背景色（只读）")]
    public string InlineCodeBg => $"#{CodeBgR:X2}{CodeBgG:X2}{CodeBgB:X2}";
    [Description("派生：引用块边框色（=链接色，只读）")]
    public string BlockquoteBorder => LinkColor;
    [Description("派生：引用块背景色（只读）")]
    public string BlockquoteBg => $"rgba({BgR},{BgG},{BgB},0.2)";
    [Description("派生：代码块背景色（只读）")]
    public string PreBg => $"#{PreBgR:X2}{PreBgG:X2}{PreBgB:X2}";
    [Description("派生：表头背景色（只读）")]
    public string TableHeaderBg => $"#{TableBgR:X2}{TableBgG:X2}{TableBgB:X2}";

    // ─── Hex preview properties for UI binding ───
    [Description("主背景色 HEX 预览（只读）")]
    public string BgHex      => $"#{BgR:X2}{BgG:X2}{BgB:X2}";
    [Description("正文文字色 HEX 预览（只读）")]
    public string TextHex    => $"#{TextR:X2}{TextG:X2}{TextB:X2}";
    [Description("链接色 HEX 预览（只读）")]
    public string LinkHex    => $"#{LinkR:X2}{LinkG:X2}{LinkB:X2}";
    [Description("标题文字色 HEX 预览（只读）")]
    public string HeadingHex => $"#{HeadingR:X2}{HeadingG:X2}{HeadingB:X2}";
    [Description("行内代码文字色 HEX 预览（只读）")]
    public string CodeHex    => $"#{CodeR:X2}{CodeG:X2}{CodeB:X2}";
    [Description("边框色 HEX 预览（只读）")]
    public string BorderHex  => $"#{BorderR:X2}{BorderG:X2}{BorderB:X2}";
    [Description("次级背景色 HEX 预览（只读）")]
    public string BgSecHex   => $"#{BgSecR:X2}{BgSecG:X2}{BgSecB:X2}";
    [Description("次级文字色 HEX 预览（只读）")]
    public string TextSecHex => $"#{TextSecR:X2}{TextSecG:X2}{TextSecB:X2}";
    [Description("行内代码背景色 HEX 预览（只读）")]
    public string CodeBgHex  => $"#{CodeBgR:X2}{CodeBgG:X2}{CodeBgB:X2}";
    [Description("代码块背景色 HEX 预览（只读）")]
    public string PreBgHex   => $"#{PreBgR:X2}{PreBgG:X2}{PreBgB:X2}";
    [Description("表头背景色 HEX 预览（只读）")]
    public string TableBgHex => $"#{TableBgR:X2}{TableBgG:X2}{TableBgB:X2}";
    // ──────────────────────────────────────────────────

    // ════════════════════════════════════════════════════════════
    //  Mermaid Diagrams section
    // ════════════════════════════════════════════════════════════

    #region Mermaid properties
    // Mermaid container background
    private int _mermaidBgR = 30;
    private int _mermaidBgG = 30;
    private int _mermaidBgB = 30;
    [Description("Mermaid 容器背景色的红色通道（0-255）")]
    public int MermaidBgR { get => _mermaidBgR; set => SetProperty(ref _mermaidBgR, value); }
    [Description("Mermaid 容器背景色的绿色通道（0-255）")]
    public int MermaidBgG { get => _mermaidBgG; set => SetProperty(ref _mermaidBgG, value); }
    [Description("Mermaid 容器背景色的蓝色通道（0-255）")]
    public int MermaidBgB { get => _mermaidBgB; set => SetProperty(ref _mermaidBgB, value); }
    [Description("Mermaid 容器背景色 HEX 预览（只读）")]
    public string MermaidBgHex => $"#{MermaidBgR:X2}{MermaidBgG:X2}{MermaidBgB:X2}";

    private double _mermaidContainerPadding = 8;
    private double _mermaidContainerMargin = 16;
    private int _mermaidBorderRadius = 4;

    [Description("Mermaid 容器内边距（px）")]
    public double MermaidContainerPadding { get => _mermaidContainerPadding; set => SetProperty(ref _mermaidContainerPadding, value); }
    [Description("Mermaid 容器上下外边距（px）")]
    public double MermaidContainerMargin  { get => _mermaidContainerMargin;  set => SetProperty(ref _mermaidContainerMargin,  value); }
    [Description("Mermaid 容器圆角（px）")]
    public int    MermaidBorderRadius     { get => _mermaidBorderRadius;     set => SetProperty(ref _mermaidBorderRadius,     value); }
    #endregion

    // ════════════════════════════════════════════════════════════
    //  PlantUML Diagrams section
    // ════════════════════════════════════════════════════════════

    #region PlantUML properties
    // PlantUML container background
    private int _pumlBgR = 30;
    private int _pumlBgG = 30;
    private int _pumlBgB = 30;
    [Description("PlantUML 容器背景色的红色通道（0-255）")]
    public int PumlBgR { get => _pumlBgR; set => SetProperty(ref _pumlBgR, value); }
    [Description("PlantUML 容器背景色的绿色通道（0-255）")]
    public int PumlBgG { get => _pumlBgG; set => SetProperty(ref _pumlBgG, value); }
    [Description("PlantUML 容器背景色的蓝色通道（0-255）")]
    public int PumlBgB { get => _pumlBgB; set => SetProperty(ref _pumlBgB, value); }
    [Description("PlantUML 容器背景色 HEX 预览（只读）")]
    public string PumlBgHex => $"#{PumlBgR:X2}{PumlBgG:X2}{PumlBgB:X2}";

    private double _pumlContainerPadding = 12;
    private double _pumlContainerMargin = 8;
    private int _pumlBorderRadius = 6;
    private double _pumlDarkInvert = 0.882;

    [Description("PlantUML 容器内边距（px）")]
    public double PumlContainerPadding { get => _pumlContainerPadding; set => SetProperty(ref _pumlContainerPadding, value); }
    [Description("PlantUML 容器上下外边距（px）")]
    public double PumlContainerMargin  { get => _pumlContainerMargin;  set => SetProperty(ref _pumlContainerMargin,  value); }
    [Description("PlantUML 容器圆角（px）")]
    public int    PumlBorderRadius     { get => _pumlBorderRadius;     set => SetProperty(ref _pumlBorderRadius,     value); }
    [Description("暗色下 PlantUML 反色强度（0-1）")]
    public double PumlDarkInvert       { get => _pumlDarkInvert;       set => SetProperty(ref _pumlDarkInvert,       value); }
    #endregion

    // ════════════════════════════════════════════════════════════
    //  Function Plot section
    // ════════════════════════════════════════════════════════════

    #region Function plot properties
    // Function plot container background
    private int _plotBgR = 30;
    private int _plotBgG = 30;
    private int _plotBgB = 30;
    [Description("函数图像容器背景色的红色通道（0-255）")]
    public int PlotBgR { get => _plotBgR; set => SetProperty(ref _plotBgR, value); }
    [Description("函数图像容器背景色的绿色通道（0-255）")]
    public int PlotBgG { get => _plotBgG; set => SetProperty(ref _plotBgG, value); }
    [Description("函数图像容器背景色的蓝色通道（0-255）")]
    public int PlotBgB { get => _plotBgB; set => SetProperty(ref _plotBgB, value); }
    [Description("函数图像容器背景色 HEX 预览（只读）")]
    public string PlotBgHex => $"#{PlotBgR:X2}{PlotBgG:X2}{PlotBgB:X2}";

    private double _plotContainerPadding = 8;
    private double _plotContainerMargin = 16;
    private int _plotBorderRadius = 4;
    private string _plotAxisColor = "#cccccc";

    [Description("函数图像容器内边距（px）")]
    public double PlotContainerPadding { get => _plotContainerPadding; set => SetProperty(ref _plotContainerPadding, value); }
    [Description("函数图像容器上下外边距（px）")]
    public double PlotContainerMargin  { get => _plotContainerMargin;  set => SetProperty(ref _plotContainerMargin,  value); }
    [Description("函数图像容器圆角（px）")]
    public int    PlotBorderRadius     { get => _plotBorderRadius;     set => SetProperty(ref _plotBorderRadius,     value); }
    [Description("函数图像坐标轴 / 网格 / 标题颜色（CSS 颜色值）")]
    public string PlotAxisColor        { get => _plotAxisColor;        set => SetProperty(ref _plotAxisColor,        value); }
    #endregion

    // ════════════════════════════════════════════════════════════
    //  Chat / collapsible blocks section
    // ════════════════════════════════════════════════════════════

    #region Chat properties
    // Chat block background
    private int _chatBgR = 37;
    private int _chatBgG = 37;
    private int _chatBgB = 38;
    [Description("对话块背景色的红色通道（0-255）")]
    public int ChatBgR { get => _chatBgR; set => SetProperty(ref _chatBgR, value); }
    [Description("对话块背景色的绿色通道（0-255）")]
    public int ChatBgG { get => _chatBgG; set => SetProperty(ref _chatBgG, value); }
    [Description("对话块背景色的蓝色通道（0-255）")]
    public int ChatBgB { get => _chatBgB; set => SetProperty(ref _chatBgB, value); }
    [Description("对话块背景色 HEX 预览（只读）")]
    public string ChatBgHex => $"#{ChatBgR:X2}{ChatBgG:X2}{ChatBgB:X2}";

    private double _chatContainerPadding = 12;
    private double _chatContainerMargin = 16;
    private int _chatBorderRadius = 6;
    private string _chatAccentColor = "rgba(255,255,255,0.76)";
    private string _chatGlassColor = "rgba(255,255,255,0.18)";
    private double _chatGlassBlur = 5;

    [Description("对话块正文内边距（px）")]
    public double ChatContainerPadding { get => _chatContainerPadding; set => SetProperty(ref _chatContainerPadding, value); }
    [Description("对话块上下外边距（px）")]
    public double ChatContainerMargin  { get => _chatContainerMargin;  set => SetProperty(ref _chatContainerMargin,  value); }
    [Description("对话块右侧圆角（px；左侧始终为直角）")]
    public int    ChatBorderRadius     { get => _chatBorderRadius;     set => SetProperty(ref _chatBorderRadius,     value); }
    [Description("状态徽标颜色（CSS 颜色值，含透明度）")]
    public string ChatAccentColor      { get => _chatAccentColor;      set => SetProperty(ref _chatAccentColor,      value); }
    [Description("状态徽标辉光颜色（CSS 颜色值，含透明度）")]
    public string ChatGlassColor       { get => _chatGlassColor;       set => SetProperty(ref _chatGlassColor,       value); }
    [Description("状态徽标辉光模糊半径（px）")]
    public double ChatGlassBlur        { get => _chatGlassBlur;        set => SetProperty(ref _chatGlassBlur,        value); }
    #endregion

    // ──────────────────────────────────────────────────
    //  highlight.js — keyword / literal / symbol / name
    // ──────────────────────────────────────────────────

    [Description("highlight.js 关键字")]
    public string HljsKeyword { get; set; } = "#569cd6";
    [Description("highlight.js 字面量")]
    public string HljsLiteral { get; set; } = "#569cd6";
    [Description("highlight.js 符号")]
    public string HljsSymbol { get; set; } = "#569cd6";
    [Description("highlight.js 名称")]
    public string HljsName { get; set; } = "#569cd6";

    // ──────────────────────────────────────────────────
    //  highlight.js — built-in / type
    // ──────────────────────────────────────────────────

    [Description("highlight.js 内置函数")]
    public string HljsBuiltIn { get; set; } = "#4ec9b0";
    [Description("highlight.js 类型")]
    public string HljsType { get; set; } = "#4ec9b0";

    // ──────────────────────────────────────────────────
    //  highlight.js — class / number
    // ──────────────────────────────────────────────────

    [Description("highlight.js 类名")]
    public string HljsClass { get; set; } = "#b5cea8";
    [Description("highlight.js 数字")]
    public string HljsNumber { get; set; } = "#b5cea8";

    // ──────────────────────────────────────────────────
    //  highlight.js — string / meta-string
    // ──────────────────────────────────────────────────

    [Description("highlight.js 字符串")]
    public string HljsString { get; set; } = "#d69d85";
    [Description("highlight.js 元字符串")]
    public string HljsMetaString { get; set; } = "#d69d85";

    // ──────────────────────────────────────────────────
    //  highlight.js — title
    // ──────────────────────────────────────────────────

    [Description("highlight.js 标题（定义名）")]
    public string HljsTitle { get; set; } = "#DCDCAA";
    [Description("highlight.js 类定义标题")]
    public string HljsTitleClass { get; set; } = "#4EC9B0";
    [Description("highlight.js 继承类定义标题")]
    public string HljsTitleClassInherited { get; set; } = "#4EC9B0";

    // ──────────────────────────────────────────────────
    //  highlight.js — params / variable / template-variable
    // ──────────────────────────────────────────────────

    [Description("highlight.js 形参")]
    public string HljsParams { get; set; } = "#9CDCFE";
    [Description("highlight.js 变量")]
    public string HljsVariable { get; set; } = "#9CDCFE";
    [Description("highlight.js 模板变量")]
    public string HljsTemplateVariable { get; set; } = "#bd63c5";

    // ──────────────────────────────────────────────────
    //  highlight.js — comment / quote
    // ──────────────────────────────────────────────────

    [Description("highlight.js 注释")]
    public string HljsComment { get; set; } = "#6a9955";
    [Description("highlight.js 引用文本")]
    public string HljsQuote { get; set; } = "#6a9955";

    // ──────────────────────────────────────────────────
    //  highlight.js — attr / attribute / meta / tag
    // ──────────────────────────────────────────────────

    [Description("highlight.js 属性名")]
    public string HljsAttr { get; set; } = "#9cdcfe";
    [Description("highlight.js 属性")]
    public string HljsAttribute { get; set; } = "#9cdcfe";
    [Description("highlight.js 元信息")]
    public string HljsMeta { get; set; } = "#9b9b9b";
    [Description("highlight.js 标签")]
    public string HljsTag { get; set; } = "#569cd6";

    // ──────────────────────────────────────────────────
    //  highlight.js — selector-*
    // ──────────────────────────────────────────────────

    [Description("highlight.js 属性选择器")]
    public string HljsSelectorAttr { get; set; } = "#d7ba7d";
    [Description("highlight.js 类选择器")]
    public string HljsSelectorClass { get; set; } = "#d7ba7d";
    [Description("highlight.js ID 选择器")]
    public string HljsSelectorId { get; set; } = "#d7ba7d";
    [Description("highlight.js 伪类选择器")]
    public string HljsSelectorPseudo { get; set; } = "#d7ba7d";
    [Description("highlight.js 标签选择器")]
    public string HljsSelectorTag { get; set; } = "#d7ba7d";
    [Description("highlight.js 列表符号")]
    public string HljsBullet { get; set; } = "#d7ba7d";

    // ──────────────────────────────────────────────────
    //  highlight.js — section / link / emphasis / strong
    // ──────────────────────────────────────────────────

    [Description("highlight.js 章节标题")]
    public string HljsSection { get; set; } = "gold";
    [Description("highlight.js 链接文字")]
    public string HljsLink { get; set; } = "#569cd6";

    // ──────────────────────────────────────────────────
    //  highlight.js — addition / deletion
    // ──────────────────────────────────────────────────

    [Description("highlight.js 增补(diff)背景")]
    public string HljsAdditionBg { get; set; } = "rgba(155,185,85,0.15)";
    [Description("highlight.js 增补(diff)文字")]
    public string HljsAdditionColor { get; set; } = "#b5cea8";
    [Description("highlight.js 删除(diff)背景")]
    public string HljsDeletionBg { get; set; } = "rgba(192,0,0,0.1)";
    [Description("highlight.js 删除(diff)文字")]
    public string HljsDeletionColor { get; set; } = "#ce9178";

    // ──────────────────────────────────────────────────
    //  highlight.js — regexp / template-tag / doctag
    // ──────────────────────────────────────────────────

    [Description("highlight.js 正则表达式")]
    public string HljsRegexp { get; set; } = "#9a5334";
    [Description("highlight.js 模板标签")]
    public string HljsTemplateTag { get; set; } = "#9a5334";
    [Description("highlight.js 文档标签")]
    public string HljsDoctag { get; set; } = "#608b4e";

    // ──────────────────────────────────────────────────
    //  highlight.js — background / foreground
    // ──────────────────────────────────────────────────

    [Description("highlight.js 代码块背景")]
    public string HljsBackground { get; set; } = "#1e1e1e";
    [Description("highlight.js 代码块前景文字")]
    public string HljsForeground { get; set; } = "#dcdcdc";

    /// <summary>
    /// Generates complete CSS text that overrides the built-in renderer.css.
    /// Uses the 6 core R/G/B channels for CSS variables and the string
    /// hljs* properties for highlight.js overrides.
    /// </summary>
    public string GenerateCss()
    {
        var sb = new StringBuilder();
        sb.AppendLine("/* === Generated by AvalonMarkdown Theme Editor === */");
        sb.AppendLine();

        // ── CSS variables (dual theme) ──
        sb.AppendLine("html.theme-dark, html.theme-light {");
        AppendVar(sb, "--bg-primary", BgPrimary);
        AppendVar(sb, "--bg-secondary", BgSecondary);
        AppendVar(sb, "--text-primary", TextPrimary);
        AppendVar(sb, "--text-secondary", TextSecondary);
        AppendVar(sb, "--border-color", BorderColor);
        AppendVar(sb, "--link-color", LinkColor);
        AppendVar(sb, "--inline-code-bg", InlineCodeBg);
        AppendVar(sb, "--inline-code-color", InlineCodeColor);
        AppendVar(sb, "--blockquote-border", BlockquoteBorder);
        AppendVar(sb, "--blockquote-bg", BlockquoteBg);
        AppendVar(sb, "--heading-color", HeadingColor);
        AppendVar(sb, "--pre-bg", PreBg);
        AppendVar(sb, "--table-header-bg", TableHeaderBg);
        sb.AppendLine("}");
        sb.AppendLine();

        // ── Typography / layout overrides ──
        sb.AppendLine("/* === Typography overrides === */");
        sb.AppendLine("html.theme-dark h1, html.theme-light h1,");
        sb.AppendLine("html.theme-dark h2, html.theme-light h2,");
        sb.AppendLine("html.theme-dark h3, html.theme-light h3 {");
        sb.AppendLine("    margin-top: 20px; margin-bottom: 12px;");
        sb.AppendLine("}");
        sb.AppendLine("html.theme-dark pre, html.theme-light pre,");
        sb.AppendLine("html.theme-dark .code-block-wrapper, html.theme-light .code-block-wrapper {");
        sb.AppendLine($"    border-radius: {BorderRadius}px;");
        sb.AppendLine("}");
        sb.AppendLine("html.theme-dark code, html.theme-light code {");
        sb.AppendLine($"    font-size: {CodeFontSize}px;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("/* === Custom highlight.js base === */");
        sb.AppendLine("html.theme-dark .hljs, html.theme-light .hljs {");
        sb.AppendLine($"    background: {HljsBackground};");
        sb.AppendLine($"    color: {HljsForeground};");
        sb.AppendLine("}");
        sb.AppendLine();

        // ── highlight.js token colors (one standalone rule per token) ──
        AppendHljsSingle(sb, "keyword", HljsKeyword);
        AppendHljsSingle(sb, "literal", HljsLiteral);
        AppendHljsSingle(sb, "symbol", HljsSymbol);
        AppendHljsSingle(sb, "name", HljsName);
        AppendHljsSingle(sb, "built_in", HljsBuiltIn);
        AppendHljsSingle(sb, "type", HljsType);
        AppendHljsSingle(sb, "class", HljsClass);
        AppendHljsSingle(sb, "number", HljsNumber);
        AppendHljsSingle(sb, "string", HljsString);
        AppendHljsSingle(sb, "meta-string", HljsMetaString);
        AppendHljsSingle(sb, "title", HljsTitle);
        AppendHljsGroup(sb, ".hljs-title.class_, .hljs-title.class_.inherited__", HljsTitleClass);
        AppendHljsSingle(sb, ".hljs-title.class_.inherited__", HljsTitleClassInherited);
        AppendHljsSingle(sb, "params", HljsParams);
        AppendHljsSingle(sb, "comment", HljsComment);
        AppendHljsSingle(sb, "quote", HljsQuote);
        AppendHljsSingle(sb, "variable", HljsVariable);
        AppendHljsSingle(sb, "template-variable", HljsTemplateVariable);
        AppendHljsSingle(sb, "attr", HljsAttr);
        AppendHljsSingle(sb, "attribute", HljsAttribute);
        AppendHljsSingle(sb, "meta", HljsMeta);
        AppendHljsSingle(sb, "tag", HljsTag);
        AppendHljsSingle(sb, "bullet", HljsBullet);
        AppendHljsSingle(sb, "selector-attr", HljsSelectorAttr);
        AppendHljsSingle(sb, "selector-class", HljsSelectorClass);
        AppendHljsSingle(sb, "selector-id", HljsSelectorId);
        AppendHljsSingle(sb, "selector-pseudo", HljsSelectorPseudo);
        AppendHljsSingle(sb, "selector-tag", HljsSelectorTag);
        AppendHljsSingle(sb, "section", HljsSection);
        sb.AppendLine("html.theme-dark .hljs-link,");
        sb.AppendLine("html.theme-light .hljs-link {");
        sb.AppendLine($"    color: {HljsLink};");
        sb.AppendLine("    text-decoration: underline;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("html.theme-dark .hljs-emphasis,");
        sb.AppendLine("html.theme-light .hljs-emphasis { font-style: italic; }");
        sb.AppendLine("html.theme-dark .hljs-strong,");
        sb.AppendLine("html.theme-light .hljs-strong { font-weight: bold; }");
        sb.AppendLine();
        sb.AppendLine("html.theme-dark .hljs-addition,");
        sb.AppendLine("html.theme-light .hljs-addition {");
        sb.AppendLine($"    background: {HljsAdditionBg};");
        sb.AppendLine($"    color: {HljsAdditionColor};");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("html.theme-dark .hljs-deletion,");
        sb.AppendLine("html.theme-light .hljs-deletion {");
        sb.AppendLine($"    background: {HljsDeletionBg};");
        sb.AppendLine($"    color: {HljsDeletionColor};");
        sb.AppendLine("}");
        sb.AppendLine();
        AppendHljsSingle(sb, "regexp", HljsRegexp);
        AppendHljsSingle(sb, "template-tag", HljsTemplateTag);
        AppendHljsSingle(sb, "doctag", HljsDoctag);

        // ════════════════════════════════════════════════════════════
        //  Mermaid Diagrams
        // ════════════════════════════════════════════════════════════
        sb.AppendLine("/* === Mermaid diagram overrides === */");
        sb.AppendLine($".mermaid-container {{");
        sb.AppendLine($"    background: {MermaidBgHex};");
        sb.AppendLine($"    padding: {MermaidContainerPadding}px;");
        sb.AppendLine($"    margin: {MermaidContainerMargin}px 0;");
        sb.AppendLine($"    border-radius: {MermaidBorderRadius}px;");
        sb.AppendLine("}");
        sb.AppendLine();

        // ════════════════════════════════════════════════════════════
        //  PlantUML Diagrams
        // ════════════════════════════════════════════════════════════
        sb.AppendLine("/* === PlantUML diagram overrides === */");
        sb.AppendLine($".puml-container {{");
        sb.AppendLine($"    background: {PumlBgHex};");
        sb.AppendLine($"    padding: {PumlContainerPadding}px;");
        sb.AppendLine($"    margin: {PumlContainerMargin}px 0;");
        sb.AppendLine($"    border-radius: {PumlBorderRadius}px;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(".puml-dark img {");
        sb.AppendLine($"    filter: invert({PumlDarkInvert:F3}) hue-rotate(180deg);");
        sb.AppendLine($"    -webkit-filter: invert({PumlDarkInvert:F3}) hue-rotate(180deg);");
        sb.AppendLine("}");
        sb.AppendLine();

        // ════════════════════════════════════════════════════════════
        //  Function plots
        // ════════════════════════════════════════════════════════════
        // `color` drives the whole coordinate frame: renderer.css overrides the
        // library's hard-coded black strokes with currentColor and sets
        // fill: currentColor on the svg, so one value colours the grid, the axes,
        // the tick labels and the title.
        sb.AppendLine("/* === Function plot overrides === */");
        sb.AppendLine($".plot-container {{");
        sb.AppendLine($"    background: {PlotBgHex};");
        sb.AppendLine($"    padding: {PlotContainerPadding}px;");
        sb.AppendLine($"    margin: {PlotContainerMargin}px 0;");
        sb.AppendLine($"    border-radius: {PlotBorderRadius}px;");
        sb.AppendLine($"    color: {PlotAxisColor};");
        sb.AppendLine("}");
        sb.AppendLine();

        // ════════════════════════════════════════════════════════════
        //  Chat / collapsible blocks
        // ════════════════════════════════════════════════════════════
        // Chat panel surface and the top-right status badge's colour / glow.
        sb.AppendLine("/* === Chat block overrides === */");
        sb.AppendLine("html.theme-dark, html.theme-light {");
        sb.AppendLine($"    --chat-bg: {ChatBgHex};");
        sb.AppendLine($"    --chat-status-color: {ChatAccentColor};");
        sb.AppendLine($"    --chat-status-glow: {ChatGlassColor};");
        sb.AppendLine($"    --chat-glass-blur: {ChatGlassBlur.ToString("F1", CultureInfo.InvariantCulture)}px;");
        sb.AppendLine("}");
        sb.AppendLine("html.theme-dark .chat-block-wrap, html.theme-light .chat-block-wrap {");
        sb.AppendLine($"    margin: {ChatContainerMargin.ToString("F1", CultureInfo.InvariantCulture)}px 0;");
        sb.AppendLine("}");
        sb.AppendLine("html.theme-dark .chat-block, html.theme-light .chat-block {");
        sb.AppendLine($"    border-radius: 0 {ChatBorderRadius}px {ChatBorderRadius}px 0;");
        sb.AppendLine("}");
        sb.AppendLine("html.theme-dark .chat-body, html.theme-light .chat-body {");
        sb.AppendLine($"    padding: {ChatContainerPadding.ToString("F1", CultureInfo.InvariantCulture)}px;");
        sb.AppendLine("}");
        sb.AppendLine();

        return sb.ToString();
    }

    private static void AppendVar(StringBuilder sb, string name, string value)
    {
        sb.AppendLine($"    {name}: {value};");
    }

    private static void AppendHljsGroup(StringBuilder sb, string selectors, string color)
    {
        var parts = selectors.Split(',');
        var first = true;
        foreach (var part in parts)
        {
            var normalized = NormalizeHljsSelector(part.Trim());
            if (!first) sb.AppendLine(",");
            first = false;
            sb.Append($"html.theme-dark .{normalized}");
        }
        sb.AppendLine(",");
        var firstLight = true;
        foreach (var part in parts)
        {
            var normalized = NormalizeHljsSelector(part.Trim());
            if (!firstLight) sb.AppendLine(",");
            firstLight = false;
            sb.Append($"html.theme-light .{normalized}");
        }
        sb.AppendLine(" {");
        sb.AppendLine($"    color: {color};");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void AppendHljsSingle(StringBuilder sb, string selector, string color)
    {
        var norm = NormalizeHljsSelector(selector.Trim());
        sb.AppendLine($"html.theme-dark .{norm},");
        sb.AppendLine($"html.theme-light .{norm} {{");
        sb.AppendLine($"    color: {color};");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static string NormalizeHljsSelector(string raw)
    {
        var s = raw.TrimStart('.');
        if (s.StartsWith("hljs-", StringComparison.Ordinal))
            return s;
        return "hljs-" + s;
    }
}
