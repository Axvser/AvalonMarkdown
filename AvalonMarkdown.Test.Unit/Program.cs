using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AvalonMarkdown.Services;
using AvalonMarkdown.ViewModels;
using AvalonMarkdown.Views;

namespace AvalonMarkdown.Test.Unit;

/// <summary>
/// Offline regression suite for the parts of AvalonMarkdown that can be exercised
/// without a window, a WebView2 runtime, or a network connection:
/// <list type="bullet">
/// <item>the Myers diff, JavaScript literal encoder and export-name sanitiser;</item>
/// <item>theme-CSS generation under hostile culture / out-of-range input;</item>
/// <item>the local HTTP server under malformed, oversized and concurrent clients.</item>
/// </list>
///
/// Exit code 0 = all tests passed, 1 = at least one failed.
/// </summary>
internal static class Program
{
    private static readonly List<(string Name, Func<Task> Run)> Tests = new();
    private static int _passed;
    private static int _failed;

    public static async Task<int> Main()
    {
        RegisterDiffTests();
        RegisterJsAndNameTests();
        RegisterThemeTests();
        RegisterAssetProviderTests();
        RegisterLocalServerTests();

        Console.WriteLine($"AvalonMarkdown unit tests — {Tests.Count} cases");
        Console.WriteLine(new string('-', 72));

        foreach (var (name, run) in Tests)
        {
            try
            {
                await run();
                _passed++;
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine($"FAIL  {name}");
                Console.WriteLine($"      {ex.GetType().Name}: {ex.Message}");
                if (ex.StackTrace is { Length: > 0 } stack)
                    Console.WriteLine("      " + stack.Split('\n')[0].Trim());
            }
        }

        Console.WriteLine(new string('-', 72));
        Console.WriteLine($"RESULT: {_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    // ====================================================================
    // Myers diff
    // ====================================================================

    private static void RegisterDiffTests()
    {
        Add("diff.are-equal", () =>
        {
            Assert(MyersDiff.AreEqual(null, null), "null/null must be equal");
            Assert(!MyersDiff.AreEqual(null, ""), "null/empty must differ");
            Assert(MyersDiff.AreEqual("abc", "abc"), "identical strings must be equal");
            Assert(!MyersDiff.AreEqual("abc", "abd"), "different strings must differ");
            Assert(!MyersDiff.AreEqual("abc", "abcd"), "different lengths must differ");
            Assert(MyersDiff.AreEqual("中文🙂", "中文🙂"), "unicode must compare by content");
            return Task.CompletedTask;
        });

        Add("diff.reconstructs-both-sides", () =>
        {
            VerifyDiff("", "");
            VerifyDiff("a", "");
            VerifyDiff("", "b");
            VerifyDiff("abc", "abc");
            VerifyDiff("abc", "axc");
            VerifyDiff("kitten", "sitting");
            VerifyDiff("中文内容", "中文新内容");
            VerifyDiff("🙂🙂🙂", "🙂x🙂");

            // Larger than MaxTraceLength in the middle: exercises the coarse-but-
            // correct fallback rather than the trace table.
            VerifyDiff(new string('a', 1500) + new string('b', 1500),
                       new string('a', 1500) + new string('c', 1500));
            VerifyDiff(new string('x', 5000), new string('x', 5000));
            return Task.CompletedTask;
        });

        Add("diff.fuzz-200-random-pairs", () =>
        {
            var random = new Random(12345);
            const string alphabet = "abc";
            for (var i = 0; i < 200; i++)
            {
                var oldText = RandomString(random, alphabet, random.Next(0, 8));
                var newText = RandomString(random, alphabet, random.Next(0, 8));
                VerifyDiff(oldText, newText);
            }
            return Task.CompletedTask;
        });
    }

    private static string RandomString(Random random, string alphabet, int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = alphabet[random.Next(alphabet.Length)];
        return new string(chars);
    }

    private static void VerifyDiff(string oldText, string newText)
    {
        var ops = MyersDiff.Diff(oldText, newText);

        var rebuiltOld = new StringBuilder();
        var rebuiltNew = new StringBuilder();
        foreach (var op in ops)
        {
            Assert(op.Value.Length == 1,
                $"diff op value must be one character (got {op.Value.Length}) for '{oldText}' → '{newText}'");
            if (op.Kind != OpKind.Insert) rebuiltOld.Append(op.Value);
            if (op.Kind != OpKind.Delete) rebuiltNew.Append(op.Value);
        }

        var description = string.Join(" ", ops.Select(o => $"{o.Kind}:{o.Value}"));
        Assert(rebuiltOld.ToString() == oldText,
            $"Delete/Equal ops must rebuild the old text: '{oldText}' → '{rebuiltOld}' [{description}]");
        Assert(rebuiltNew.ToString() == newText,
            $"Insert/Equal ops must rebuild the new text: '{newText}' → '{rebuiltNew}' [{description}]");
    }

    // ====================================================================
    // JavaScript literal + export file names
    // ====================================================================

    private static void RegisterJsAndNameTests()
    {
        Add("js-literal.round-trips-as-json", () =>
        {
            var lineSeparator = ((char)0x2028).ToString();
            var paragraphSeparator = ((char)0x2029).ToString();
            var samples = new[]
            {
                "", "plain", "a'b\"c\\d", "line\nbreak\r\ttab", "\0\u0001\u001f",
                "中文🙂", lineSeparator + paragraphSeparator, "</script><script>alert(1)</script>",
            };

            foreach (var sample in samples)
            {
                var literal = MarkdownJs.StringLiteral(sample);
                Assert(literal.StartsWith('"') && literal.EndsWith('"'),
                    $"literal must be quoted: {literal}");
                Assert(!literal.Contains('\n') && !literal.Contains('\r'),
                    $"literal must not contain a raw line break: {literal}");

                using var doc = JsonDocument.Parse(literal);
                Assert(doc.RootElement.GetString() == sample,
                    $"literal must round-trip: '{sample}' ↔ {literal}");
            }

            var separators = MarkdownJs.StringLiteral(lineSeparator + paragraphSeparator);
            Assert(separators.Contains("\\u2028", StringComparison.Ordinal) &&
                   separators.Contains("\\u2029", StringComparison.Ordinal),
                "U+2028/U+2029 must be escaped for older JavaScript parsers");

            Assert(MarkdownJs.StringLiteral(null) == "\"\"", "null must become an empty literal");
            return Task.CompletedTask;
        });

        Add("export-name.sanitises-untrusted-names", () =>
        {
            Assert(MarkdownExport.SanitizeFileName(null) == "image", "null → image");
            Assert(MarkdownExport.SanitizeFileName("") == "image", "empty → image");
            Assert(MarkdownExport.SanitizeFileName("   ") == "image", "whitespace → image");

            var traversal = MarkdownExport.SanitizeFileName("../../etc/passwd.png");
            Assert(traversal == Path.GetFileName(traversal), "must not contain a path");
            Assert(!traversal.Contains('/') && !traversal.Contains('\\'), "must not contain separators");
            Assert(!traversal.StartsWith('.'), "must not start with a dot");

            var illegal = MarkdownExport.SanitizeFileName("a:b*c?d\"e<f>g|h.png");
            Assert(illegal == "a_b_c_d_e_f_g_h.png", $"illegal chars must be replaced, got '{illegal}'");

            Assert(MarkdownExport.SanitizeFileName("CON.txt") == "_CON.txt",
                "Windows device name must be prefixed");
            Assert(MarkdownExport.SanitizeFileName("nul") == "_nul",
                "device name check is case-insensitive");
            Assert(MarkdownExport.SanitizeFileName("图表-1.png") == "图表-1.png",
                "CJK letters and safe punctuation must survive");

            var longName = MarkdownExport.SanitizeFileName(new string('a', 400) + ".png");
            Assert(longName.Length <= 100, $"name must be capped, got {longName.Length}");
            return Task.CompletedTask;
        });
    }

    // ====================================================================
    // Theme CSS
    // ====================================================================

    private static void RegisterThemeTests()
    {
        Add("theme.css-is-culture-invariant", () =>
        {
            var previousCulture = CultureInfo.CurrentCulture;
            var previousUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                CultureInfo.CurrentUICulture = new CultureInfo("de-DE");

                var vm = new MarkdownThemeViewModel
                {
                    HeadingMarginTop = 20.5,
                    HeadingMarginBottom = 12.25,
                    CodeFontSize = 13.5,
                    MermaidContainerPadding = 8.5,
                    ChatContainerPadding = 12.75,
                    PumlDarkInvert = 0.882,
                };
                var css = vm.GenerateCss();

                Assert(css.Contains("margin-top: 20.5px", StringComparison.Ordinal),
                    "decimal heading margin must use '.' regardless of culture");
                Assert(css.Contains("font-size: 13.5px", StringComparison.Ordinal),
                    "decimal code font size must use '.'");
                Assert(css.Contains("invert(0.882)", StringComparison.Ordinal),
                    "decimal invert value must use '.'");
                // A decimal comma looks like "20,5px" / "0,882)". RGB commas are
                // followed by another digit that ends in ',' or '.' and must not
                // trip this check.
                Assert(!System.Text.RegularExpressions.Regex.IsMatch(css, @"\d,\d+(px|\))"),
                    "no decimal comma may reach the CSS");
                Assert(!css.Contains("20,5", StringComparison.Ordinal) &&
                       !css.Contains("0,882", StringComparison.Ordinal),
                    "expected decimal points, not commas");
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
                CultureInfo.CurrentUICulture = previousUiCulture;
            }
            return Task.CompletedTask;
        });

        Add("theme.css-saturates-out-of-range-channels", () =>
        {
            var vm = new MarkdownThemeViewModel
            {
                BgR = 999,
                BgG = -5,
                BgB = 128,
                PlotAxisR = int.MaxValue,
                PlotAxisG = int.MinValue,
                PlotAxisB = 7,
            };

            Assert(vm.BgPrimary == "#FF0080", $"channels must clamp, got {vm.BgPrimary}");
            Assert(vm.PlotAxisColor == "#FF0007", $"channels must clamp, got {vm.PlotAxisColor}");
            Assert(vm.BlockquoteBg == "rgba(255,0,128,0.2)",
                $"blockquote channels must clamp, got {vm.BlockquoteBg}");

            var css = vm.GenerateCss();
            Assert(!css.Contains("999", StringComparison.Ordinal), "raw out-of-range value leaked");
            return Task.CompletedTask;
        });

        Add("theme.css-never-emits-nan-or-infinity", () =>
        {
            var vm = new MarkdownThemeViewModel
            {
                HeadingMarginTop = double.NaN,
                HeadingMarginBottom = double.PositiveInfinity,
                CodeFontSize = double.NegativeInfinity,
                BorderRadius = -10,
                PumlDarkInvert = double.NaN,
                ChatGlassBlur = double.NaN,
                ChatBorderRadius = -1,
            };

            var css = vm.GenerateCss();
            Assert(!css.Contains("NaN", StringComparison.OrdinalIgnoreCase),
                "CSS must not contain NaN");
            Assert(!css.Contains("Infinity", StringComparison.OrdinalIgnoreCase),
                "CSS must not contain Infinity");
            Assert(css.Contains("margin-top: 0px", StringComparison.Ordinal),
                "NaN margin must fall back to the minimum");
            Assert(css.Contains("border-radius: 0px", StringComparison.Ordinal),
                "negative radius must clamp to 0");
            Assert(css.Contains("invert(0.000)", StringComparison.Ordinal),
                "NaN invert must fall back to 0");
            return Task.CompletedTask;
        });

        Add("theme.css-is-deterministic-and-complete", () =>
        {
            var vm = new MarkdownThemeViewModel();
            var first = vm.GenerateCss();
            var second = vm.GenerateCss();
            Assert(first == second, "GenerateCss must be deterministic");

            foreach (var marker in new[]
            {
                "--bg-primary", "--text-primary", "--border-color", "--link-color",
                "--heading-color", "--pre-bg", "--table-header-bg",
                ".mermaid-container", ".puml-container", ".plot-container", "--chat-bg",
                "--chat-status-color", "--chat-glass-blur",
            })
            {
                Assert(first.Contains(marker, StringComparison.Ordinal),
                    $"generated CSS is missing {marker}");
            }

            vm.HljsKeyword = "#123456";
            Assert(vm.GenerateCss().Contains("color: #123456;", StringComparison.Ordinal),
                "highlight.js overrides must reach the CSS");
            return Task.CompletedTask;
        });
    }

    // ====================================================================
    // Embedded HTML theme injection
    // ====================================================================

    private static void RegisterAssetProviderTests()
    {
        Add("asset.theme-class-rewrite", () =>
        {
            Assert(EmbeddedHtmlSourceProvider.ApplyThemeClass(
                    "<html class=\"theme-dark\">", "theme-light") == "<html class=\"theme-light\">",
                "single theme class must be replaced");
            Assert(EmbeddedHtmlSourceProvider.ApplyThemeClass(
                    "<html class=\"foo theme-dark bar\">", "theme-light") ==
                    "<html class=\"foo theme-light bar\">",
                "theme token must be replaced in place");
            Assert(EmbeddedHtmlSourceProvider.ApplyThemeClass(
                    "<html class=\"foo\">", "theme-dark") == "<html class=\"foo theme-dark\">",
                "missing theme token must be appended");
            Assert(EmbeddedHtmlSourceProvider.ApplyThemeClass(
                    "<html>", "theme-light") == "<html class=\"theme-light\">",
                "missing class attribute must be added");
            Assert(EmbeddedHtmlSourceProvider.ApplyThemeClass(
                    "<!doctype html><body>x</body>", "theme-dark") ==
                    "<!doctype html><body>x</body>",
                "a document without <html> must be left alone");

            var once = EmbeddedHtmlSourceProvider.ApplyThemeClass(
                "<html class=\"theme-dark\">", "theme-light");
            var twice = EmbeddedHtmlSourceProvider.ApplyThemeClass(once, "theme-light");
            Assert(once == twice, "theme rewrite must be idempotent");
            return Task.CompletedTask;
        });
    }

    // ====================================================================
    // Local HTTP server
    // ====================================================================

    private static void RegisterLocalServerTests()
    {
        Add("server.serves-exact-utf8-body", async () =>
        {
            var html = "<!doctype html><html><body>中文内容 🙂</body></html>";
            await using var server = new LocalHtmlServer(html);
            await server.StartAsync();

            Assert(server.IsRunning, "server must report running after StartAsync");
            Assert(server.Port > 0, "a dynamic port must be assigned");
            Assert(server.BaseUrl.StartsWith("http://127.0.0.1:", StringComparison.Ordinal),
                "URL must bind to loopback");

            using var http = NewClient();
            using var response = await http.GetAsync(server.BaseUrl).WaitAsync(Timeout(10));
            Assert(response.StatusCode == HttpStatusCode.OK, $"GET must be 200, got {response.StatusCode}");
            Assert(response.Content.Headers.ContentLength == Encoding.UTF8.GetByteCount(html),
                "Content-Length must be the UTF-8 byte count");
            Assert(response.Content.Headers.ContentType?.MediaType == "text/html",
                "Content-Type must be text/html");
            Assert(await response.Content.ReadAsStringAsync() == html,
                "body must round-trip exactly");
        });

        Add("server.head-has-headers-no-body", async () =>
        {
            var html = "<html><body>head</body></html>";
            await using var server = new LocalHtmlServer(html);
            await server.StartAsync();

            using var http = NewClient();
            using var request = new HttpRequestMessage(HttpMethod.Head, server.BaseUrl);
            using var response = await http.SendAsync(request).WaitAsync(Timeout(10));
            Assert(response.StatusCode == HttpStatusCode.OK, "HEAD must be 200");
            Assert(response.Content.Headers.ContentLength == Encoding.UTF8.GetByteCount(html),
                "HEAD must still advertise the body length");
            Assert((await response.Content.ReadAsByteArrayAsync()).Length == 0,
                "HEAD must not send a body");
        });

        Add("server.serves-concurrent-clients", async () =>
        {
            var html = "<html><body>" + new string('x', 200_000) + "</body></html>";
            await using var server = new LocalHtmlServer(html);
            await server.StartAsync();

            using var http = NewClient();
            var requests = Enumerable.Range(0, 12)
                .Select(async _ => await (await http.GetAsync(server.BaseUrl)).Content.ReadAsStringAsync())
                .ToArray();
            var bodies = await Task.WhenAll(requests).WaitAsync(Timeout(30));
            Assert(bodies.All(b => b == html), "every concurrent request must get the document intact");
        });

        Add("server.rejects-non-get", async () =>
        {
            await using var server = new LocalHtmlServer("<html></html>");
            await server.StartAsync();

            using var http = NewClient();
            using var response = await http.PostAsync(server.BaseUrl,
                new StringContent("x")).WaitAsync(Timeout(10));
            Assert(response.StatusCode == HttpStatusCode.MethodNotAllowed,
                $"POST must be 405, got {response.StatusCode}");
        });

        Add("server.survives-malformed-request", async () =>
        {
            await using var server = new LocalHtmlServer("<html>ok</html>");
            await server.StartAsync();

            await SendRawAsync(server.Port, "GARBAGE\r\n\r\n");

            using var http = NewClient();
            using var response = await http.GetAsync(server.BaseUrl).WaitAsync(Timeout(10));
            Assert(response.StatusCode == HttpStatusCode.OK, "server must still answer after junk");
            Assert(await response.Content.ReadAsStringAsync() == "<html>ok</html>",
                "body must still be correct");
        });

        Add("server.survives-oversized-header", async () =>
        {
            await using var server = new LocalHtmlServer("<html>ok</html>");
            await server.StartAsync();

            // 20 KB with no line terminator — larger than the 16 KB header cap.
            await SendRawAsync(server.Port, new string('A', 20 * 1024));

            using var http = NewClient();
            using var response = await http.GetAsync(server.BaseUrl).WaitAsync(Timeout(10));
            Assert(response.StatusCode == HttpStatusCode.OK,
                "an oversized request must not take the server down");
        });

        Add("server.dispose-is-idempotent-and-releases-port", async () =>
        {
            var server = new LocalHtmlServer("<html></html>");
            await server.StartAsync();
            var port = server.Port;

            await server.DisposeAsync().AsTask().WaitAsync(Timeout(10));
            await server.DisposeAsync().AsTask().WaitAsync(Timeout(10));
            Assert(!server.IsRunning, "disposed server must not report running");

            await AssertThrowsAsync<ObjectDisposedException>(() => server.StartAsync());

            using var probe = new TcpClient();
            var failed = false;
            try
            {
                await probe.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout(3));
            }
            catch
            {
                failed = true;
            }
            Assert(failed, "the port must be released after dispose");
        });

        Add("server.dispose-before-start", async () =>
        {
            var server = new LocalHtmlServer("<html></html>");
            await server.DisposeAsync().AsTask().WaitAsync(Timeout(10));
            await AssertThrowsAsync<ObjectDisposedException>(() => server.StartAsync());
        });

        Add("server.start-is-idempotent", async () =>
        {
            await using var server = new LocalHtmlServer("<html></html>");
            await server.StartAsync();
            var port = server.Port;
            await server.StartAsync();
            Assert(server.Port == port, "a second StartAsync must not rebind");
        });

        Add("server.dispose-with-idle-client-does-not-hang", async () =>
        {
            var server = new LocalHtmlServer("<html></html>");
            await server.StartAsync();

            using var idle = new TcpClient();
            await idle.ConnectAsync(IPAddress.Loopback, server.Port).WaitAsync(Timeout(5));
            // Deliberately send nothing; the client timeout is 10s, and disposal
            // must cancel it instead of waiting for it.
            await server.DisposeAsync().AsTask().WaitAsync(Timeout(5));
        });

        Add("server.null-content-rejected", () =>
        {
            AssertThrows<ArgumentNullException>(() => new LocalHtmlServer(null!));
            return Task.CompletedTask;
        });
    }

    // ====================================================================
    // Test plumbing
    // ====================================================================

    private static void Add(string name, Func<Task> run) => Tests.Add((name, run));

    private static HttpClient NewClient() => new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private static TimeSpan Timeout(int seconds) => TimeSpan.FromSeconds(seconds);

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"expected {typeof(TException).Name}, got {ex.GetType().Name}");
        }
        throw new InvalidOperationException($"expected {typeof(TException).Name}, nothing was thrown");
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"expected {typeof(TException).Name}, got {ex.GetType().Name}");
        }
        throw new InvalidOperationException($"expected {typeof(TException).Name}, nothing was thrown");
    }

    /// <summary>Send bytes on a raw socket and wait for the server to close.</summary>
    private static async Task SendRawAsync(int port, string payload)
    {
        using var client = new TcpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
            await using var stream = client.GetStream();
            var bytes = Encoding.ASCII.GetBytes(payload);
            await stream.WriteAsync(bytes, cts.Token);
            await stream.FlushAsync(cts.Token);

            // Read until the server closes or forgets us.
            var buffer = new byte[4096];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cts.Token);
                if (read == 0) break;
            }
        }
        catch (OperationCanceledException)
        {
            // The server may close without draining our write; that is a valid pass.
        }
        catch (IOException)
        {
            // Connection reset after the server refused the request.
        }
        catch (SocketException)
        {
            // Same.
        }
    }
}
