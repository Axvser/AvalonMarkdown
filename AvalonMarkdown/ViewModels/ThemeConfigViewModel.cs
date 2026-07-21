using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Threading;
using AvalonMarkdown.Views;

namespace AvalonMarkdown.ViewModels;

/// <summary>
/// Configures all CSS variable values for a single theme (dark or light).
/// The 6 core colors are exposed as R/G/B integer triplets (0-255) for
/// slider binding. Derived surface colors (BgSecondary, InlineCodeBg,
/// PreBg, TableHeaderBg, Scrollbar*) update automatically.
/// Call <see cref="GenerateCss"/> to produce complete CSS text.
/// </summary>
public class ThemeConfigViewModel : INotifyPropertyChanged
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

    public ThemeConfigViewModel()
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
        OnPropertyChanged(nameof(ScrollbarThumb));
        OnPropertyChanged(nameof(ScrollbarHover));
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
        OnPropertyChanged(nameof(BodyFontSize));
        OnPropertyChanged(nameof(CodeFontSize));
        OnPropertyChanged(nameof(LineHeight));
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
    public int BgR { get => _bgR; set => SetProperty(ref _bgR, value); }
    public int BgG { get => _bgG; set => SetProperty(ref _bgG, value); }
    public int BgB { get => _bgB; set => SetProperty(ref _bgB, value); }

    // Text  #d4d4d4
    private int _textR = 212;
    private int _textG = 212;
    private int _textB = 212;
    public int TextR { get => _textR; set => SetProperty(ref _textR, value); }
    public int TextG { get => _textG; set => SetProperty(ref _textG, value); }
    public int TextB { get => _textB; set => SetProperty(ref _textB, value); }

    // Link  #3794ff
    private int _linkR = 55;
    private int _linkG = 148;
    private int _linkB = 255;
    public int LinkR { get => _linkR; set => SetProperty(ref _linkR, value); }
    public int LinkG { get => _linkG; set => SetProperty(ref _linkG, value); }
    public int LinkB { get => _linkB; set => SetProperty(ref _linkB, value); }

    // Heading  #569cd6
    private int _headingR = 86;
    private int _headingG = 156;
    private int _headingB = 214;
    public int HeadingR { get => _headingR; set => SetProperty(ref _headingR, value); }
    public int HeadingG { get => _headingG; set => SetProperty(ref _headingG, value); }
    public int HeadingB { get => _headingB; set => SetProperty(ref _headingB, value); }

    // Inline code  #ce9178
    private int _codeR = 206;
    private int _codeG = 145;
    private int _codeB = 120;
    public int CodeR { get => _codeR; set => SetProperty(ref _codeR, value); }
    public int CodeG { get => _codeG; set => SetProperty(ref _codeG, value); }
    public int CodeB { get => _codeB; set => SetProperty(ref _codeB, value); }

    // Border  #3c3c3c
    private int _borderR = 60;
    private int _borderG = 60;
    private int _borderB = 60;
    public int BorderR { get => _borderR; set => SetProperty(ref _borderR, value); }
    public int BorderG { get => _borderG; set => SetProperty(ref _borderG, value); }
    public int BorderB { get => _borderB; set => SetProperty(ref _borderB, value); }
    #endregion

    // ─── Extended colors (R/G/B triplets, independent controls) ───

    #region Extended color RGB properties
    // BgSecondary  #252526
    private int _bgSecR = 37;
    private int _bgSecG = 37;
    private int _bgSecB = 38;
    public int BgSecR { get => _bgSecR; set => SetProperty(ref _bgSecR, value); }
    public int BgSecG { get => _bgSecG; set => SetProperty(ref _bgSecG, value); }
    public int BgSecB { get => _bgSecB; set => SetProperty(ref _bgSecB, value); }

    // TextSecondary  #cccccc
    private int _textSecR = 204;
    private int _textSecG = 204;
    private int _textSecB = 204;
    public int TextSecR { get => _textSecR; set => SetProperty(ref _textSecR, value); }
    public int TextSecG { get => _textSecG; set => SetProperty(ref _textSecG, value); }
    public int TextSecB { get => _textSecB; set => SetProperty(ref _textSecB, value); }

    // InlineCodeBg  #2d2d2d
    private int _codeBgR = 45;
    private int _codeBgG = 45;
    private int _codeBgB = 45;
    public int CodeBgR { get => _codeBgR; set => SetProperty(ref _codeBgR, value); }
    public int CodeBgG { get => _codeBgG; set => SetProperty(ref _codeBgG, value); }
    public int CodeBgB { get => _codeBgB; set => SetProperty(ref _codeBgB, value); }

    // PreBg  #1e1e1e
    private int _preBgR = 30;
    private int _preBgG = 30;
    private int _preBgB = 30;
    public int PreBgR { get => _preBgR; set => SetProperty(ref _preBgR, value); }
    public int PreBgG { get => _preBgG; set => SetProperty(ref _preBgG, value); }
    public int PreBgB { get => _preBgB; set => SetProperty(ref _preBgB, value); }

    // TableHeaderBg  #2d2d2d
    private int _tableBgR = 45;
    private int _tableBgG = 45;
    private int _tableBgB = 45;
    public int TableBgR { get => _tableBgR; set => SetProperty(ref _tableBgR, value); }
    public int TableBgG { get => _tableBgG; set => SetProperty(ref _tableBgG, value); }
    public int TableBgB { get => _tableBgB; set => SetProperty(ref _tableBgB, value); }
    #endregion

    // ─── Typography / layout ───

    #region Typography properties
    private double _bodyFontSize = 14;
    private double _codeFontSize = 13;
    private double _lineHeight = 1.6;
    private int _borderRadius = 4;

    public double BodyFontSize  { get => _bodyFontSize; set => SetProperty(ref _bodyFontSize, value); }
    public double CodeFontSize  { get => _codeFontSize; set => SetProperty(ref _codeFontSize, value); }
    public double LineHeight    { get => _lineHeight;   set => SetProperty(ref _lineHeight,   value); }
    public int    BorderRadius  { get => _borderRadius; set => SetProperty(ref _borderRadius, value); }
    #endregion

    // ─── Derived computed properties ───

    public string BgPrimary => $"#{BgR:X2}{BgG:X2}{BgB:X2}";
    public string BgSecondary => $"#{BgSecR:X2}{BgSecG:X2}{BgSecB:X2}";
    public string TextPrimary => $"#{TextR:X2}{TextG:X2}{TextB:X2}";
    public string TextSecondary => $"#{TextSecR:X2}{TextSecG:X2}{TextSecB:X2}";
    public string LinkColor => $"#{LinkR:X2}{LinkG:X2}{LinkB:X2}";
    public string HeadingColor => $"#{HeadingR:X2}{HeadingG:X2}{HeadingB:X2}";
    public string InlineCodeColor => $"#{CodeR:X2}{CodeG:X2}{CodeB:X2}";
    public string BorderColor => $"#{BorderR:X2}{BorderG:X2}{BorderB:X2}";
    public string InlineCodeBg => $"#{CodeBgR:X2}{CodeBgG:X2}{CodeBgB:X2}";
    public string BlockquoteBorder => LinkColor;
    public string BlockquoteBg => $"rgba({BgR},{BgG},{BgB},0.2)";
    public string PreBg => $"#{PreBgR:X2}{PreBgG:X2}{PreBgB:X2}";
    public string TableHeaderBg => $"#{TableBgR:X2}{TableBgG:X2}{TableBgB:X2}";
    public string ScrollbarThumb => $"#{Scale(BorderR, 1.0):X2}{Scale(BorderG, 1.0):X2}{Scale(BorderB, 1.0):X2}";
    public string ScrollbarHover => $"#{Scale(BorderR, 1.2):X2}{Scale(BorderG, 1.2):X2}{Scale(BorderB, 1.2):X2}";

    // ─── Hex preview properties for UI binding ───
    public string BgHex      => $"#{BgR:X2}{BgG:X2}{BgB:X2}";
    public string TextHex    => $"#{TextR:X2}{TextG:X2}{TextB:X2}";
    public string LinkHex    => $"#{LinkR:X2}{LinkG:X2}{LinkB:X2}";
    public string HeadingHex => $"#{HeadingR:X2}{HeadingG:X2}{HeadingB:X2}";
    public string CodeHex    => $"#{CodeR:X2}{CodeG:X2}{CodeB:X2}";
    public string BorderHex  => $"#{BorderR:X2}{BorderG:X2}{BorderB:X2}";
    public string BgSecHex   => $"#{BgSecR:X2}{BgSecG:X2}{BgSecB:X2}";
    public string TextSecHex => $"#{TextSecR:X2}{TextSecG:X2}{TextSecB:X2}";
    public string CodeBgHex  => $"#{CodeBgR:X2}{CodeBgG:X2}{CodeBgB:X2}";
    public string PreBgHex   => $"#{PreBgR:X2}{PreBgG:X2}{PreBgB:X2}";
    public string TableBgHex => $"#{TableBgR:X2}{TableBgG:X2}{TableBgB:X2}";
    // ──────────────────────────────────────────────────

    public string MermaidTheme { get; set; } = "dark";

    // ──────────────────────────────────────────────────
    //  highlight.js — keyword / literal / symbol / name
    // ──────────────────────────────────────────────────

    public string HljsKeyword { get; set; } = "#569cd6";
    public string HljsLiteral { get; set; } = "#569cd6";
    public string HljsSymbol { get; set; } = "#569cd6";
    public string HljsName { get; set; } = "#569cd6";

    // ──────────────────────────────────────────────────
    //  highlight.js — built-in / type
    // ──────────────────────────────────────────────────

    public string HljsBuiltIn { get; set; } = "#4ec9b0";
    public string HljsType { get; set; } = "#4ec9b0";

    // ──────────────────────────────────────────────────
    //  highlight.js — class / number
    // ──────────────────────────────────────────────────

    public string HljsClass { get; set; } = "#b5cea8";
    public string HljsNumber { get; set; } = "#b5cea8";

    // ──────────────────────────────────────────────────
    //  highlight.js — string / meta-string
    // ──────────────────────────────────────────────────

    public string HljsString { get; set; } = "#d69d85";
    public string HljsMetaString { get; set; } = "#d69d85";

    // ──────────────────────────────────────────────────
    //  highlight.js — title
    // ──────────────────────────────────────────────────

    public string HljsTitle { get; set; } = "#DCDCAA";
    public string HljsTitleClass { get; set; } = "#4EC9B0";
    public string HljsTitleClassInherited { get; set; } = "#4EC9B0";

    // ──────────────────────────────────────────────────
    //  highlight.js — params / variable / template-variable
    // ──────────────────────────────────────────────────

    public string HljsParams { get; set; } = "#9CDCFE";
    public string HljsVariable { get; set; } = "#9CDCFE";
    public string HljsTemplateVariable { get; set; } = "#bd63c5";

    // ──────────────────────────────────────────────────
    //  highlight.js — comment / quote
    // ──────────────────────────────────────────────────

    public string HljsComment { get; set; } = "#6a9955";
    public string HljsQuote { get; set; } = "#6a9955";

    // ──────────────────────────────────────────────────
    //  highlight.js — attr / attribute / meta / tag
    // ──────────────────────────────────────────────────

    public string HljsAttr { get; set; } = "#9cdcfe";
    public string HljsAttribute { get; set; } = "#9cdcfe";
    public string HljsMeta { get; set; } = "#9b9b9b";
    public string HljsTag { get; set; } = "#569cd6";

    // ──────────────────────────────────────────────────
    //  highlight.js — selector-*
    // ──────────────────────────────────────────────────

    public string HljsSelectorAttr { get; set; } = "#d7ba7d";
    public string HljsSelectorClass { get; set; } = "#d7ba7d";
    public string HljsSelectorId { get; set; } = "#d7ba7d";
    public string HljsSelectorPseudo { get; set; } = "#d7ba7d";
    public string HljsSelectorTag { get; set; } = "#d7ba7d";
    public string HljsBullet { get; set; } = "#d7ba7d";

    // ──────────────────────────────────────────────────
    //  highlight.js — section / link / emphasis / strong
    // ──────────────────────────────────────────────────

    public string HljsSection { get; set; } = "gold";
    public string HljsLink { get; set; } = "#569cd6";

    // ──────────────────────────────────────────────────
    //  highlight.js — addition / deletion
    // ──────────────────────────────────────────────────

    public string HljsAdditionBg { get; set; } = "rgba(155,185,85,0.15)";
    public string HljsAdditionColor { get; set; } = "#b5cea8";
    public string HljsDeletionBg { get; set; } = "rgba(192,0,0,0.1)";
    public string HljsDeletionColor { get; set; } = "#ce9178";

    // ──────────────────────────────────────────────────
    //  highlight.js — regexp / template-tag / doctag
    // ──────────────────────────────────────────────────

    public string HljsRegexp { get; set; } = "#9a5334";
    public string HljsTemplateTag { get; set; } = "#9a5334";
    public string HljsDoctag { get; set; } = "#608b4e";

    // ──────────────────────────────────────────────────
    //  highlight.js — background / foreground
    // ──────────────────────────────────────────────────

    public string HljsBackground { get; set; } = "#1e1e1e";
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
        AppendVar(sb, "--scrollbar-thumb", ScrollbarThumb);
        AppendVar(sb, "--scrollbar-hover", ScrollbarHover);
        AppendVar(sb, "--mermaid-theme", MermaidTheme);
        sb.AppendLine("}");
        sb.AppendLine();

        // ── Typography / layout overrides ──
        sb.AppendLine("/* === Typography overrides === */");
        sb.AppendLine("html.theme-dark body, html.theme-light body {");
        sb.AppendLine($"    font-size: {BodyFontSize}px;");
        sb.AppendLine($"    line-height: {LineHeight};");
        sb.AppendLine("}");
        sb.AppendLine("html.theme-dark h1, html.theme-light h1,");
        sb.AppendLine("html.theme-dark h2, html.theme-light h2,");
        sb.AppendLine("html.theme-dark h3, html.theme-light h3 {");
        sb.AppendLine("    margin-top: 20px; margin-bottom: 12px;");
        sb.AppendLine("}");
        sb.AppendLine("html.theme-dark pre, html.theme-light pre {");
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

        // ── highlight.js grouped selectors ──
        AppendHljsGroup(sb, "keyword, .hljs-literal, .hljs-symbol, .hljs-name", HljsKeyword);
        AppendHljsGroup(sb, "built_in, .hljs-type", HljsBuiltIn);
        AppendHljsGroup(sb, "class, .hljs-number", HljsClass);
        AppendHljsGroup(sb, "string, .hljs-meta-string", HljsString);
        AppendHljsSingle(sb, "title", HljsTitle);
        AppendHljsGroup(sb, ".hljs-title.class_, .hljs-title.class_.inherited__", HljsTitleClass);
        AppendHljsSingle(sb, ".hljs-title.class_.inherited__", HljsTitleClassInherited);
        AppendHljsSingle(sb, "params", HljsParams);
        AppendHljsGroup(sb, "comment, .hljs-quote", HljsComment);
        AppendHljsSingle(sb, "variable", HljsVariable);
        AppendHljsSingle(sb, "template-variable", HljsTemplateVariable);
        AppendHljsGroup(sb, "attr, .hljs-attribute", HljsAttr);
        AppendHljsSingle(sb, "meta", HljsMeta);
        AppendHljsSingle(sb, "tag", HljsTag);
        AppendHljsGroup(sb, "bullet, .hljs-selector-attr, .hljs-selector-class, .hljs-selector-id, .hljs-selector-pseudo, .hljs-selector-tag", HljsBullet);
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
        AppendHljsGroup(sb, "regexp, .hljs-template-tag", HljsRegexp);
        AppendHljsSingle(sb, "doctag", HljsDoctag);

        // ── Symbol and literal (separate to allow different colors) ──
        sb.AppendLine("html.theme-dark .hljs-symbol,");
        sb.AppendLine("html.theme-light .hljs-symbol {");
        sb.AppendLine($"    color: {HljsSymbol};");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("html.theme-dark .hljs-literal,");
        sb.AppendLine("html.theme-light .hljs-literal {");
        sb.AppendLine($"    color: {HljsLiteral};");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static int Scale(int channel, double factor)
    {
        var val = (int)(channel * factor);
        return Math.Clamp(val, 0, 255);
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
