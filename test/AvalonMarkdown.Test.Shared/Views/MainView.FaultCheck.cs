using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AvalonMarkdown.Views;

namespace AvalonMarkdown.Test.Shared.Views;

/// <summary>
/// Fourth capture driver (AVALON_FAULT_CHECK=1): fault injection.
///
/// Two layers, because they find different bugs:
/// <list type="bullet">
/// <item>C#-side tests drive the public API with nulls, non-finite numbers,
/// huge documents, rapid update bursts, restart, and subscribers that throw.
/// These exercise the async-void/event boundaries that a pure JS probe cannot
/// reach.</item>
/// <item>JS-side tests (Assets/FaultCheck.js) drive the renderer with junk
/// config, malformed fences, pathological Markdown, and optional CDN globals
/// removed one at a time.</item>
/// </list>
///
/// Exit code 0 = no failures, 1 = failures, 3 = the preview never became ready
/// or the probe timed out.
/// </summary>
public partial class MainView
{
    private readonly StringBuilder _faultLog = new();
    private readonly List<JsonObject> _faultResults = new();
    private bool _faultDone;
    private bool _faultStarted;

    private void StartFaultCheck()
    {
        _faultLog.AppendLine("=== AvalonMarkdown fault-tolerance check ===");

        _singlePreview!.ConsoleMessage += (_, e) =>
            _faultLog.AppendLine($"[JS:{e.Level}] {e.Message}");
        _singlePreview.ErrorOccurred += (_, e) =>
            _faultLog.AppendLine($"[EVENT] {e.Title} — {e.Message}");

        _singlePreview.OnReady += (_, _) => BeginFaultCheck();
        _ = Task.Delay(TimeSpan.FromSeconds(90)).ContinueWith(_ => FinishFaultCheck(timeout: true));
    }

    private void BeginFaultCheck()
    {
        if (_faultStarted) return;
        _faultStarted = true;
        _ = RunFaultCheckAsync();
    }

    private async Task RunFaultCheckAsync()
    {
        var report = new JsonObject
        {
            ["schema"] = "avalonmarkdown.fault-check/1",
            ["watchdogFired"] = false
        };

        try
        {
            await RunCsharpFaultTestsAsync();

            var js = ReadAssetJs("FaultCheck.js");
            if (string.IsNullOrEmpty(js))
            {
                RecordFault("js-probe-asset", false, "cannot load FaultCheck.js from the test assembly");
            }
            else
            {
                await _singlePreview!.InvokeScriptAsync(js);
                await _singlePreview.InvokeScriptAsync("__faultCheck.run()");

                var deadline = DateTime.UtcNow.AddSeconds(60);
                var completed = false;
                while (DateTime.UtcNow < deadline)
                {
                    await Task.Delay(200);
                    var ready = await _singlePreview.InvokeScriptAsync("__faultCheck.ready()");
                    if (ready?.Trim().Trim('"') == "1")
                    {
                        completed = true;
                        break;
                    }
                }

                if (!completed)
                {
                    RecordFault("js-probe-completion", false, "the JS fault probe did not finish within 60s");
                }
                else
                {
                    var raw = await _singlePreview.InvokeScriptAsync("__faultCheck.results()");
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        RecordFault("js-probe-results", false, "the JS fault probe returned no results");
                    }
                    else
                    {
                        MergeJsResults(raw, report);
                    }
                }
            }

            report["status"] = HasFaultFailures() ? "fail" : "pass";
            FinishFaultCheck(false, report);
        }
        catch (Exception ex)
        {
            RecordFault("driver", false, $"{ex.GetType().Name}: {ex.Message}");
            report["status"] = "fail";
            FinishFaultCheck(false, report);
        }
    }

    // ====================================================================
    // C#-side fault tests
    // ====================================================================

    private async Task RunCsharpFaultTestsAsync()
    {
        await CsFault("api-null-and-nonfinite-inputs", async () =>
        {
            await _singlePreview!.InvokeScriptAsync(null!);
            await _singlePreview.InvokeScriptAsync("");
            await _singlePreview.ApplyConfigAsync(null!);
            await _singlePreview.ApplyConfigAsync("   ");
            await _singlePreview.ApplyCustomCssAsync(null!);
            await _singlePreview.ApplyCustomCssAsync("");

            var navigated = await _singlePreview.NavigateToHeadingAsync(null!);
            AssertFault(!navigated, "navigate with null must return false");

            await _singlePreview.ScrollToProgressAsync(double.NaN);
            await _singlePreview.ScrollToProgressAsync(double.PositiveInfinity);
            await _singlePreview.ScrollToProgressAsync(double.NegativeInfinity);

            var progress = await _singlePreview.GetScrollProgressAsync();
            AssertFault(progress is null ||
                        (double.IsFinite(progress.Value) && progress.Value >= 0 && progress.Value <= 100),
                $"scroll progress must be finite and in range, got {progress}");
        });

        await CsFault("render-huge-with-special-characters", async () =>
        {
            var sb = new StringBuilder("# Huge\n\n");
            for (var i = 0; i < 3500; i++)
            {
                sb.Append("line ").Append(i)
                  .Append(" with \"quotes\", backslash \\, apostrophe ' and 中文 🙂")
                  .Append((char)0x2028)
                  .Append(" end\n\n");
            }

            var completion = WaitForRenderCompleteAsync();
            await _singlePreview!.RenderMarkdownAsync(sb.ToString());
            await completion;
            await Task.Delay(300);

            var headings = await _singlePreview.GetHeadingsAsync();
            AssertFault(headings.Count >= 1 && headings[0].Text == "Huge",
                "the huge document's heading must render");

            var errorMarker = await _singlePreview.InvokeScriptAsync(
                "document.getElementById('preview').getAttribute('data-render-error')");
            AssertFault(string.IsNullOrEmpty(errorMarker) || errorMarker == "null",
                $"the huge document must not hit the render-error fallback: {errorMarker}");
        });

        await CsFault("rapid-render-burst-last-wins", async () =>
        {
            var completion = WaitForRenderCompleteAsync();
            var tasks = Enumerable.Range(0, 25)
                .Select(i => _singlePreview!.RenderMarkdownAsync($"# Rapid {i}\n\nbody {i}"))
                .ToArray();
            await Task.WhenAll(tasks);
            await completion;
            await Task.Delay(500);

            var headings = await _singlePreview!.GetHeadingsAsync();
            AssertFault(headings.Count == 1,
                $"exactly one document must remain, got {headings.Count} headings");
            AssertFault(headings[0].Text == "Rapid 24",
                $"the last render must win, got '{headings[0].Text}'");
        });

        await CsFault("placeholder-special-characters-cs", async () =>
        {
            const string placeholder = "a'b\"c\\d\n\t`e` 中文 🙂";
            _singlePreview!.Placeholder = placeholder;
            await Task.Delay(150);
            await _singlePreview.RenderMarkdownAsync("");

            var raw = await _singlePreview.InvokeScriptAsync(
                "document.getElementById('preview').textContent");
            var shown = Unquote(raw);
            AssertFault(shown == placeholder,
                $"placeholder must round-trip through the bridge, got {raw}");

            _singlePreview.Placeholder = "";
        });

        await CsFault("restart-recovers-content", async () =>
        {
            _vm.MarkdownText = "# Before Restart\n\nbody";
            await Task.Delay(400);

            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler readyHandler = (_, _) => ready.TrySetResult(true);
            _singlePreview!.OnReady += readyHandler;
            try
            {
                var completion = WaitForRenderCompleteAsync();
                await _singlePreview.RestartPreviewAsync();
                await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(20)));
                await completion;
            }
            finally
            {
                _singlePreview.OnReady -= readyHandler;
            }

            await Task.Delay(500);
            var headings = await _singlePreview.GetHeadingsAsync();
            AssertFault(headings.Any(h => h.Text == "Before Restart"),
                "the restart must re-render the current Text instead of leaving the preview blank");
        });

        await CsFault("throwing-subscribers-are-isolated", async () =>
        {
            EventHandler<MarkdownConsoleMessageEventArgs> consoleThrow = (_, _) =>
                throw new InvalidOperationException("console subscriber exploded");
            EventHandler<MarkdownViewErrorEventArgs> errorThrow = (_, _) =>
                throw new InvalidOperationException("error subscriber exploded");

            _singlePreview!.ConsoleMessage += consoleThrow;
            _singlePreview.ErrorOccurred += errorThrow;
            try
            {
                await _singlePreview.InvokeScriptAsync("console.log('fault-console-probe')");
                await _singlePreview.InvokeScriptAsync(
                    "window.onerror('fault-error-probe','https://example.com/x.js',1,1,new Error('x'))");
                await Task.Delay(250);
                await _singlePreview.RenderMarkdownAsync("# subscribers isolated");
                await Task.Delay(250);
            }
            finally
            {
                _singlePreview.ConsoleMessage -= consoleThrow;
                _singlePreview.ErrorOccurred -= errorThrow;
            }

            var headings = await _singlePreview.GetHeadingsAsync();
            AssertFault(headings.Any(h => h.Text == "subscribers isolated"),
                "the preview must keep working after a subscriber throws");
        });

        await CsFault("throwing-link-subscriber-is-isolated", async () =>
        {
            var secondHandlerRan = false;
            EventHandler<MarkdownLinkClickedEventArgs> linkThrow = (_, e) =>
            {
                e.Handled = true;   // never let the fallback launch a URI in a test
                throw new InvalidOperationException("link subscriber exploded");
            };
            EventHandler<MarkdownLinkClickedEventArgs> linkHandled = (_, e) =>
            {
                secondHandlerRan = true;
                e.Handled = true;
            };

            _singlePreview!.LinkClicked += linkThrow;
            _singlePreview.LinkClicked += linkHandled;
            try
            {
                await _singlePreview.RenderMarkdownAsync("[probe](app://fault)");
                await Task.Delay(300);
                var clicked = await _singlePreview.InvokeScriptAsync(
                    "var a=document.querySelector('a[data-href=\"app://fault\"]'); a ? (a.click(), 1) : 0;");
                await Task.Delay(500);
                AssertFault(clicked?.Trim().Trim('"') == "1", "the probe link must be clickable");
            }
            finally
            {
                _singlePreview.LinkClicked -= linkThrow;
                _singlePreview.LinkClicked -= linkHandled;
            }

            AssertFault(secondHandlerRan,
                "a throwing LinkClicked subscriber must not stop later subscribers");
        });

        await CsFault("bad-script-recovers", async () =>
        {
            // WebView2 may surface a syntax error either as a thrown exception or
            // as an error payload string; the contract under test is only that the
            // control neither crashes nor stops rendering.
            await _singlePreview!.InvokeScriptAsync("this( is not valid javascript");

            await _singlePreview.RenderMarkdownAsync("# recovered from bad script");
            await Task.Delay(300);
            var headings = await _singlePreview.GetHeadingsAsync();
            AssertFault(headings.Any(h => h.Text == "recovered from bad script"),
                "the preview must keep working after a bad script");
        });
    }

    // ====================================================================
    // Reporting
    // ====================================================================

    private async Task CsFault(string name, Func<Task> test)
    {
        try
        {
            await test();
            RecordFault(name, true, "");
        }
        catch (Exception ex)
        {
            RecordFault(name, false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void RecordFault(string name, bool ok, string detail)
    {
        _faultResults.Add(new JsonObject
        {
            ["name"] = name,
            ["ok"] = ok,
            ["detail"] = detail
        });
        _faultLog.AppendLine(ok ? $"PASS  {name}" : $"FAIL  {name}: {detail}");
    }

    private bool HasFaultFailures()
    {
        foreach (var result in _faultResults)
        {
            if (result["ok"]?.GetValue<bool>() == false)
                return true;
        }
        return false;
    }

    private void MergeJsResults(string raw, JsonObject report)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(raw);
        }
        catch (JsonException ex)
        {
            RecordFault("js-probe-parse", false, $"could not parse probe JSON: {ex.Message}");
            return;
        }

        if (parsed?["results"] is not JsonArray results)
        {
            RecordFault("js-probe-results", false, "the probe returned no results array");
            return;
        }

        var jsNames = new JsonArray();
        foreach (var item in results)
        {
            if (item is not JsonObject obj) continue;
            var name = obj["name"]?.GetValue<string>() ?? "unnamed";
            var skipped = obj["skipped"]?.GetValue<bool>() == true;
            var ok = obj["ok"]?.GetValue<bool>() == true || skipped;
            var error = obj["error"]?.GetValue<string>() ?? "";

            _faultResults.Add(new JsonObject
            {
                ["name"] = "js:" + name,
                ["ok"] = ok,
                ["skipped"] = skipped,
                ["detail"] = error
            });
            jsNames.Add(name);

            _faultLog.AppendLine(skipped
                ? $"SKIP  js:{name} — {error}"
                : ok ? $"PASS  js:{name}" : $"FAIL  js:{name}: {error}");
        }

        report["jsTests"] = jsNames;
    }

    private void FinishFaultCheck(bool timeout, JsonObject? report = null)
    {
        if (_faultDone) return;
        _faultDone = true;

        report ??= new JsonObject { ["schema"] = "avalonmarkdown.fault-check/1" };
        if (timeout)
        {
            report["watchdogFired"] = true;
            report["status"] = "not-ready";
            report["note"] = "the preview never became ready, or the probe stalled";
            _faultLog.AppendLine("[FATAL] watchdog fired");
        }

        var results = new JsonArray();
        foreach (var r in _faultResults) results.Add(r.DeepClone());
        report["results"] = results;

        var failures = 0;
        var skipped = 0;
        foreach (var r in _faultResults)
        {
            if (r["ok"]?.GetValue<bool>() == false) failures++;
            if (r["skipped"]?.GetValue<bool>() == true) skipped++;
        }

        var status = report["status"]?.GetValue<string>() ?? (failures == 0 ? "pass" : "fail");
        var ok = !timeout && failures == 0;
        _faultLog.AppendLine();
        _faultLog.AppendLine($"[result] status={status} total={_faultResults.Count} " +
                             $"failures={failures} skipped={skipped}");
        _faultLog.AppendLine(ok ? "RESULT: PASS" : "RESULT: FAIL");

        var outPath = Path.Combine(Directory.GetCurrentDirectory(), "fault-check.json");
        var logPath = Path.Combine(Directory.GetCurrentDirectory(), "fault-check.log");
        try
        {
            File.WriteAllText(outPath, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(logPath, _faultLog.ToString());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[fault-check] could not write report: {ex.Message}");
        }

        Console.WriteLine($"[fault-check] {(ok ? "PASS" : "FAIL")} ({status}) — {outPath}");
        Console.Out.Flush();
        Console.Error.Flush();
        Environment.Exit(timeout ? 3 : ok ? 0 : 1);
    }

    // ====================================================================
    // Small helpers
    // ====================================================================

    private static void AssertFault(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    /// <summary>
    /// WebView2 returns string results JSON-encoded (quoted + escaped). Decode
    /// when possible and fall back to the raw text for platforms that don't.
    /// </summary>
    private static string? Unquote(string? raw)
    {
        if (raw is null) return null;
        try
        {
            // JsonDocument is metadata-free and works with reflection disabled.
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind == JsonValueKind.String
                ? doc.RootElement.GetString()
                : raw;
        }
        catch (JsonException)
        {
            return raw;
        }
    }
}
