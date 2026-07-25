using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AvalonMarkdown.Services;

/// <summary>
/// Lightweight local HTTP server that serves the Markdown preview HTML page
/// over http://127.0.0.1 on a dynamic port. This avoids the <c>file://</c> origin
/// restrictions that block YouTube and other third-party iframe embeds in WebView2.
/// </summary>
public sealed class LocalHtmlServer : IAsyncDisposable
{
    private readonly string _htmlContent;
    private TcpListener? _listener;
    private Task? _serverTask;
    private CancellationTokenSource? _cts;
    private bool _started;

    public LocalHtmlServer(string htmlContent)
    {
        ArgumentNullException.ThrowIfNull(htmlContent);
        _htmlContent = htmlContent;
    }

    public int Port { get; private set; }

    public string BaseUrl => $"http://127.0.0.1:{Port}/";

    public async Task StartAsync()
    {
        if (_started)
            return;

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();

        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _started = true;

        _serverTask = Task.Run(() => RunServerLoopAsync(_cts.Token));

        await Task.CompletedTask;
    }

    private async Task RunServerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync();
                if (ct.IsCancellationRequested) break;

                // Fire-and-forget each client to avoid serializing requests
                _ = HandleClientAsync(client, ct);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (InvalidOperationException)
            {
                break;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
            {
                // Read the HTTP request line (first line)
                var requestLine = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(requestLine))
                    return;

                // Read and discard remaining headers
                string? headerLine;
                while ((headerLine = await reader.ReadLineAsync(ct)) != null)
                {
                    if (string.IsNullOrEmpty(headerLine))
                        break;
                }

                // Build response
                var bodyBytes = Encoding.UTF8.GetBytes(_htmlContent);
                var header = BuildHttpHeader(bodyBytes.Length);
                var headerBytes = Encoding.ASCII.GetBytes(header);

                await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct);
                await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length, ct);
                await stream.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested, silent exit
        }
        catch (IOException)
        {
            // Client disconnected early — ignore
        }
        catch (ObjectDisposedException)
        {
            // Stream disposed during shutdown — ignore
        }
    }

    private static string BuildHttpHeader(int contentLength)
    {
        return
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            "Content-Length: " + contentLength + "\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "Cache-Control: no-cache, no-store, must-revalidate\r\n" +
            "Connection: close\r\n" +
            "\r\n";
    }

    public async ValueTask DisposeAsync()
    {
        if (!_started)
            return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _listener?.Stop();
        _listener = null;

        if (_serverTask is not null)
        {
            try
            {
                await _serverTask;
            }
            catch
            {
                // Silently ignore task cancellation exceptions
            }
        }

        _started = false;
    }
}
