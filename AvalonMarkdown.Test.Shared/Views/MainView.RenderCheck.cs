using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Platform;

namespace AvalonMarkdown.Test.Shared.Views;

/// <summary>
/// Third capture driver (AVALON_RENDER_CHECK=1): render one markdown file through
/// the real WebView pipeline and report every failure surface.
///
/// Exists because most failures in this renderer are silent — KaTeX builds its own
/// error span without throwing or logging, a PlantUML server failure leaves a
/// broken image with no event, and function-plot's JSON path logs nothing. Only a
/// DOM audit can see them.
///
/// Lives in its own file so the diff to MainView.axaml.cs stays at a handful of
/// lines. See <c>skills/avalonmarkdown-syntax/scripts/render_check.py</c>, which
/// drives this mode.
/// </summary>
public partial class MainView
{
    private readonly StringBuilder _renderCheckLog = new();
    private readonly List<string> _renderCheckConsole = new();
    private bool _renderCheckDone;
    private bool _renderCheckStarted;

    private string _rcFile = "";
    private string _rcOut = "";
    private string _rcLog = "";
    private int _rcSettleMs = 20000;
    private int _rcTimeoutMs = 75000;
    private string _rcTheme = "dark";

    private static string EnvOr(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;

    private static int EnvInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : fallback;

    private void StartRenderCheck()
    {
        _rcFile = EnvOr("AVALON_RENDER_CHECK_FILE", "");
        _rcOut = EnvOr("AVALON_RENDER_CHECK_OUT",
            Path.Combine(Directory.GetCurrentDirectory(), "render-check.json"));
        _rcLog = EnvOr("AVALON_RENDER_CHECK_LOG",
            Path.Combine(Directory.GetCurrentDirectory(), "render-check.log"));
        _rcSettleMs = EnvInt("AVALON_RENDER_CHECK_SETTLE_MS", 20000);
        _rcTimeoutMs = EnvInt("AVALON_RENDER_CHECK_TIMEOUT_MS", 75000);
        _rcTheme = EnvOr("AVALON_RENDER_CHECK_THEME", "dark");

        if (string.IsNullOrEmpty(_rcFile) || !File.Exists(_rcFile))
        {
            // Fail before the WebView ever starts rather than after a 15s CDN wait.
            Console.Error.WriteLine(
                $"[render-check] AVALON_RENDER_CHECK_FILE is missing or not found: '{_rcFile}'");
            Console.Out.Flush();
            Console.Error.Flush();
            Environment.Exit(2);
            return;
        }

        _renderCheckLog.AppendLine("=== AvalonMarkdown render check ===");
        _renderCheckLog.AppendLine($"[source] {_rcFile}");
        _renderCheckLog.AppendLine($"[config] theme={_rcTheme} settleMs={_rcSettleMs} timeoutMs={_rcTimeoutMs}");

        _singlePreview!.ConsoleMessage += (_, e) =>
        {
            // Kept because the DOM cannot show these: the abort path's console.error,
            // and the generation-guarded [Mermaid]/[Plot] warnings that a superseded
            // render swallows.
            lock (_renderCheckConsole)
            {
                _renderCheckConsole.Add($"[{e.Level}] {e.Message}");
            }
        };
        _singlePreview.ErrorOccurred += (_, e) =>
        {
            lock (_renderCheckConsole)
            {
                _renderCheckConsole.Add($"[EVENT] {e.Title} — {e.Message}");
            }
        };
        _singlePreview.OnReady += (_, _) => BeginRenderCheck();

        // Armed in the constructor so it covers WebView2 + local server + CDN startup.
        _ = Task.Delay(TimeSpan.FromMilliseconds(_rcTimeoutMs))
                .ContinueWith(_ => FinishRenderCheck(timeout: true));
    }

    private void BeginRenderCheck()
    {
        if (_renderCheckStarted) return;
        _renderCheckStarted = true;
        _ = RunRenderCheckAsync();
    }

    private async Task RunRenderCheckAsync()
    {
        var source = await File.ReadAllTextAsync(_rcFile);
        var report = new JsonObject
        {
            ["schema"] = "avalonmarkdown.render-check/1",
            ["sourceFile"] = _rcFile,
            ["sourceBytes"] = Encoding.UTF8.GetByteCount(source),
            ["watchdogFired"] = false
        };

        try
        {
            await _singlePreview!.InvokeScriptAsync($"setTheme('{_rcTheme}')");

            var js = ReadAssetJs("RenderCheck.js");
            if (string.IsNullOrEmpty(js))
            {
                report["status"] = "not-ready";
                report["note"] = "cannot load RenderCheck.js asset — is the build current?";
                FinishRenderCheck(false, report);
                return;
            }
            await _singlePreview.InvokeScriptAsync(js);

            if (string.IsNullOrWhiteSpace(source))
            {
                // renderMarkdown('') takes the empty branch, sets no data-source and
                // logs NO "Render complete" — waiting for one would just burn 15s.
                report["status"] = "empty-source";
                FinishRenderCheck(false, report);
                return;
            }

            // RenderMarkdownAsync bypasses the Text-binding path on purpose:
            // OnTextChanged short-circuits on MyersDiff.AreEqual, so rendering the
            // same document twice produces no completion signal at all. A validator
            // that cannot re-render its own input is not a validator.
            // Attach the watcher BEFORE rendering, or a fast render's log line can
            // arrive before anything is listening for it.
            var completionTask = WaitForRenderCompleteAsync();
            await _singlePreview.RenderMarkdownAsync(source);
            var rendered = await completionTask;

            if (!rendered)
            {
                _renderCheckLog.AppendLine("[WARN] no 'Render complete' signal within 15s");
            }

            await SettleAsync();

            var auditRaw = await _singlePreview.InvokeScriptAsync("__renderCheck.audit()");
            JsonNode? audit = null;
            if (!string.IsNullOrWhiteSpace(auditRaw))
            {
                try { audit = JsonNode.Parse(auditRaw); }
                catch (JsonException ex)
                {
                    _renderCheckLog.AppendLine($"[WARN] could not parse audit JSON: {ex.Message}");
                }
            }

            if (audit is null)
            {
                report["status"] = "not-ready";
                report["note"] = "the probe returned no audit object";
                FinishRenderCheck(false, report);
                return;
            }

            foreach (var kv in audit.AsObject())
            {
                if (kv.Key != "findings" && kv.Key != "unvalidated") report[kv.Key] = kv.Value?.DeepClone();
            }
            report["findings"] = audit["findings"]?.DeepClone() ?? new JsonArray();
            report["unvalidated"] = audit["unvalidated"]?.DeepClone() ?? new JsonArray();

            // Export audit: drive every exportable member through both formats.
            // Separate from the settle loop because rasterising is async. An export
            // failure is NOT a document defect, so it never moves the status — it is
            // reported alongside as its own section.
            try
            {
                await _singlePreview.InvokeScriptAsync("__renderCheck.exportsStart()");
                var exportDeadline = DateTime.UtcNow.AddSeconds(35);
                while (DateTime.UtcNow < exportDeadline)
                {
                    await Task.Delay(300);
                    var rd = await _singlePreview.InvokeScriptAsync("__renderCheck.exportsReady()");
                    if (rd?.Trim().Trim('"') == "1") break;
                }
                var exportsRaw = await _singlePreview.InvokeScriptAsync("__renderCheck.exports()");
                if (!string.IsNullOrWhiteSpace(exportsRaw))
                {
                    report["exports"] = JsonNode.Parse(exportsRaw);
                }
            }
            catch (Exception exEx)
            {
                _renderCheckLog.AppendLine($"[WARN] export audit: {exEx.Message}");
            }

            var status = audit["ok"]?.GetValue<bool>() == true ? "pass" : "findings";
            if (audit["findings"] is JsonArray arr)
            {
                foreach (var f in arr)
                {
                    if (f?["severity"]?.GetValue<string>() == "environment")
                    {
                        // Nothing but environment findings is still not a pass — the
                        // caller has to know the document was not fully verified.
                        if (status == "pass") status = "environment";
                        break;
                    }
                }
            }
            report["status"] = status;
            report["ok"] = status == "pass";

            FinishRenderCheck(false, report);
        }
        catch (Exception ex)
        {
            report["status"] = "not-ready";
            report["note"] = $"driver failed: {ex.GetType().Name}: {ex.Message}";
            FinishRenderCheck(false, report);
        }
    }

    /// <summary>
    /// Wait for the JS "Render complete" log line, bounded. Attach the watcher
    /// BEFORE calling RenderMarkdownAsync or a fast render's line can arrive first.
    /// </summary>
    private async Task<bool> WaitForRenderCompleteAsync()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<AvalonMarkdown.Views.MarkdownConsoleMessageEventArgs>? watcher = null;
        watcher = (_, e) =>
        {
            if (e.Level == "LOG" && e.Message.Contains("Render complete")) tcs.TrySetResult(true);
        };
        _singlePreview!.ConsoleMessage += watcher;
        try
        {
            var finished = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            return finished == tcs.Task && tcs.Task.Result;
        }
        finally
        {
            _singlePreview.ConsoleMessage -= watcher;
        }
    }

    /// <summary>
    /// Poll the probe's in-flight counter until it reads zero twice in a row.
    ///
    /// The waiting has to happen here rather than inside the page: InvokeScript is a
    /// synchronous bridge call, so a busy-wait in injected JS would hold the event
    /// loop and requestAnimationFrame — which is exactly what hydrates the plots and
    /// runs mermaid — would never fire. The probe would deadlock on its own subject.
    /// </summary>
    private async Task SettleAsync()
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(_rcSettleMs);
        var zeroStreak = 0;
        var lastPending = -1;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
            var raw = await _singlePreview!.InvokeScriptAsync("__renderCheck.pending()");
            if (!int.TryParse(raw?.Trim().Trim('"'), out var pending)) continue;
            lastPending = pending;
            if (pending == 0)
            {
                if (++zeroStreak >= 2) return;
            }
            else
            {
                zeroStreak = 0;
            }
        }
        _renderCheckLog.AppendLine(
            $"[WARN] settle deadline reached with {lastPending} item(s) still pending");
    }

    private void FinishRenderCheck(bool timeout, JsonObject? report = null)
    {
        if (_renderCheckDone) return;
        _renderCheckDone = true;

        report ??= new JsonObject { ["schema"] = "avalonmarkdown.render-check/1", ["status"] = "not-ready" };
        if (timeout)
        {
            report["watchdogFired"] = true;
            report["status"] = "not-ready";
            report["note"] = $"watchdog fired after {_rcTimeoutMs}ms — the preview never became ready. " +
                             "Check network access to cdn.jsdelivr.net.";
            _renderCheckLog.AppendLine($"[FATAL] {report["note"]}");
        }

        lock (_renderCheckConsole)
        {
            var arr = new JsonArray();
            for (var i = 0; i < _renderCheckConsole.Count; i++)
            {
                arr.Add(new JsonObject { ["i"] = i, ["line"] = _renderCheckConsole[i] });
            }
            report["console"] = arr;

            foreach (var line in _renderCheckConsole) _renderCheckLog.AppendLine("[console] " + line);
        }

        var fails = 0;
        var envs = 0;
        if (report["findings"] is JsonArray findings)
        {
            foreach (var f in findings)
            {
                var sev = f?["severity"]?.GetValue<string>();
                if (sev == "fail") fails++;
                else if (sev == "environment") envs++;
            }
        }

        var status = report["status"]?.GetValue<string>() ?? "not-ready";
        _renderCheckLog.AppendLine();
        _renderCheckLog.AppendLine($"[result] status={status} fails={fails} environment={envs}");
        if (report["unvalidated"] is JsonArray unv && unv.Count > 0)
        {
            foreach (var u in unv) _renderCheckLog.AppendLine($"[unvalidated] {u}");
        }
        var ok = status == "pass";
        _renderCheckLog.AppendLine(ok ? "RESULT: PASS" : "RESULT: FAIL");

        try
        {
            File.WriteAllText(_rcOut, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(_rcLog, _renderCheckLog.ToString());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[render-check] could not write report: {ex.Message}");
        }

        var exit = timeout || status == "not-ready" ? 3 : fails > 0 ? 1 : ok ? 0 : 3;
        Console.WriteLine($"[render-check] {(ok ? "PASS" : "FAIL")} ({status}) — {_rcOut}");
        Console.Out.Flush();
        Console.Error.Flush();
        Environment.Exit(exit);
    }
}
