using System;
using System.Threading.Tasks;

namespace AvalonMarkdown.Services;

/// <summary>
/// Markdown 预览服务接口 — 各平台提供不同实现
/// </summary>
public interface IMarkdownPreviewService
{
    /// <summary>加载 HTML 内容到预览控件</summary>
    Task LoadHtmlAsync(string htmlContent);

    /// <summary>执行 JavaScript 脚本</summary>
    Task InvokeScriptAsync(string script);

    /// <summary>导航/加载完成时触发</summary>
    event EventHandler? NavigationCompleted;
}
