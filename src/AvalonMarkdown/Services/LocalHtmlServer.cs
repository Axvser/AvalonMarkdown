using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
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
/// <remarks>
/// Fault-tolerance rules, because this listener is reachable by any local process:
/// <list type="bullet">
/// <item>headers are read with a hard byte cap (a client cannot make the server
/// allocate an unbounded line);</item>
/// <item>a connection slot limit and a per-connection deadline stop leaked clients
/// from pinning the preview forever;</item>
/// <item>only GET/HEAD are served, every request gets a bounded response, and any
/// socket failure closes that one connection without taking the server down;</item>
/// <item><see cref="DisposeAsync"/> is idempotent, cancels in-flight clients, and
/// waits for them so the port is released before a restart binds a new one.</item>
/// </list>
/// </remarks>
public sealed class LocalHtmlServer : IAsyncDisposable
{
    // A request header larger than this is refused, not buffered further.
    private const int MaxHeaderBytes = 16 * 1024;

    // Loopback-only, but a runaway page can still open many sockets.
    private const int MaxConcurrentClients = 16;

    // A browser sends its request immediately; this only catches stalled clients.
    private static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DisposeGrace = TimeSpan.FromSeconds(2);

    private readonly byte[] _htmlBytes;
    private readonly byte[] _notFoundBytes = Encoding.UTF8.GetBytes("Not Found");
    private readonly byte[] _methodNotAllowedBytes = Encoding.UTF8.GetBytes("Method Not Allowed");

    private readonly ConcurrentDictionary<long, Task> _clients = new();
    private readonly SemaphoreSlim _clientSlots = new(MaxConcurrentClients, MaxConcurrentClients);
    private readonly CancellationTokenSource _cts = new();

    private TcpListener? _listener;
    private Task? _serverTask;
    private long _clientId;
    private int _started;
    private int _disposed;

    public LocalHtmlServer(string htmlContent)
    {
        ArgumentNullException.ThrowIfNull(htmlContent);
        _htmlBytes = Encoding.UTF8.GetBytes(htmlContent);
    }

    public int Port { get; private set; }

    public string BaseUrl => $"http://127.0.0.1:{Port}/";

    /// <summary>Whether the listener is bound and has not been disposed.</summary>
    public bool IsRunning => Volatile.Read(ref _started) == 1 && Volatile.Read(ref _disposed) == 0;

    public Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (Interlocked.Exchange(ref _started, 1) == 1)
            return Task.CompletedTask;

        try
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            // The token is already cancelled if DisposeAsync ran concurrently.
            _serverTask = Task.Run(() => RunServerLoopAsync(_cts.Token));
            return Task.CompletedTask;
        }
        catch
        {
            // Never leave a half-started server: a caller that catches the failure
            // must be able to try again on a clean object.
            Volatile.Write(ref _started, 0);
            try { _listener?.Stop(); } catch { /* best effort */ }
            _listener = null;
            throw;
        }
    }

    private async Task RunServerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (InvalidOperationException)
            {
                // Listener stopped underneath us (dispose path).
                break;
            }
            catch (SocketException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException)
            {
                // Transient accept failure (e.g. too many FDs): keep serving.
                continue;
            }
            catch (Exception)
            {
                // A fault in the accept path must not spin the loop.
                break;
            }

            if (!_clientSlots.Wait(0))
            {
                // Over the cap: refuse this connection instead of queueing it
                // forever. The browser will retry, and the slot frees on completion.
                try { client.Dispose(); } catch { /* ignore */ }
                continue;
            }

            var id = Interlocked.Increment(ref _clientId);
            var task = TrackClientAsync(client, id, ct);
            _clients[id] = task;
            // A client that finished before it could be registered has already
            // removed itself; do not leave a completed task in the dictionary.
            if (task.IsCompleted)
                _clients.TryRemove(id, out _);
        }
    }

    private async Task TrackClientAsync(TcpClient client, long id, CancellationToken ct)
    {
        try
        {
            await HandleClientAsync(client, ct).ConfigureAwait(false);
        }
        catch
        {
            // HandleClientAsync owns the failure policy; this is only the last
            // line of defence so a throw cannot become an unobserved task.
        }
        finally
        {
            _clients.TryRemove(id, out _);
            try { _clientSlots.Release(); }
            catch (ObjectDisposedException) { /* disposal won the race */ }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeoutCts.CancelAfter(ClientTimeout);
            var token = timeoutCts.Token;

            try
            {
                client.ReceiveTimeout = (int)ClientTimeout.TotalMilliseconds;
                client.SendTimeout = (int)ClientTimeout.TotalMilliseconds;

                using var stream = client.GetStream();
                var request = await ReadRequestAsync(stream, token).ConfigureAwait(false);

                if (request is null)
                {
                    // Malformed / oversized / client went away: close silently.
                    return;
                }

                if (request.Value.TooLarge)
                {
                    await WriteResponseAsync(stream, 431, "Request Header Fields Too Large",
                        _notFoundBytes, includeBody: false, token).ConfigureAwait(false);
                    return;
                }

                var method = request.Value.Method;
                if (method is not ("GET" or "HEAD"))
                {
                    await WriteResponseAsync(stream, 405, "Method Not Allowed",
                        _methodNotAllowedBytes, includeBody: method != "HEAD", token)
                        .ConfigureAwait(false);
                    return;
                }

                await WriteResponseAsync(stream, 200, "OK", _htmlBytes,
                    includeBody: method != "HEAD", token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown or per-client timeout.
            }
            catch (IOException)
            {
                // Client disconnected early.
            }
            catch (SocketException)
            {
                // Connection reset.
            }
            catch (ObjectDisposedException)
            {
                // Stream disposed during shutdown.
            }
            catch (Exception)
            {
                // Never let one client's failure escape as an unobserved task.
            }
        }
    }

    private readonly record struct HttpRequest(string Method, bool TooLarge);

    /// <summary>
    /// Read the request line and headers into a fixed buffer, stopping at the
    /// header terminator. The cap is what makes this safe against a client that
    /// never sends CRLF; parsing never sees more than <see cref="MaxHeaderBytes"/>.
    /// </summary>
    private static async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[MaxHeaderBytes];
        var total = 0;

        while (total < buffer.Length)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                return null;
            }

            if (read == 0)
                break;

            total += read;

            var end = FindHeaderEnd(buffer, total);
            if (end >= 0)
            {
                total = end + 4; // content after the terminator is ignored
                break;
            }
        }

        if (total == 0)
            return null;

        var headerEnd = FindHeaderEnd(buffer, total);
        if (headerEnd < 0)
        {
            // Either the terminator was never sent or the header exceeded the cap.
            return new HttpRequest("", TooLarge: true);
        }

        var headerText = Encoding.Latin1.GetString(buffer, 0, headerEnd);
        var lines = headerText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        if (lines.Length == 0)
            return null;

        var parts = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        return new HttpRequest(parts[0].ToUpperInvariant(), TooLarge: false);
    }

    private static int FindHeaderEnd(byte[] buffer, int length)
    {
        // Blank line between headers and body: CRLFCRLF, or LFLF for lenient clients.
        for (var i = 0; i + 3 < length; i++)
        {
            if (buffer[i] == (byte)'\r' && buffer[i + 1] == (byte)'\n' &&
                buffer[i + 2] == (byte)'\r' && buffer[i + 3] == (byte)'\n')
                return i;
        }
        for (var i = 0; i + 1 < length; i++)
        {
            if (buffer[i] == (byte)'\n' && buffer[i + 1] == (byte)'\n')
                return i;
        }
        // A request line terminated by a blank CRLF has no headers; treat the
        // first CRLFCRLF / LFLF as the end, which the loops above already do.
        return -1;
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream, int status, string reason, byte[] body,
        bool includeBody, CancellationToken ct)
    {
        var header =
            "HTTP/1.1 " + status + " " + reason + "\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            "Content-Length: " + body.Length + "\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            "Cache-Control: no-cache, no-store, must-revalidate\r\n" +
            "Connection: close\r\n" +
            "\r\n";

        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, ct).ConfigureAwait(false);
        if (includeBody)
            await stream.WriteAsync(body, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        try { _cts.Cancel(); } catch (ObjectDisposedException) { /* already torn down */ }

        var listener = Interlocked.Exchange(ref _listener, null);
        try { listener?.Stop(); } catch { /* best effort */ }

        if (_serverTask is not null)
        {
            try { await _serverTask.ConfigureAwait(false); }
            catch { /* cancellation / disposal */ }
        }

        // Clients were handed the same token; give them a moment to observe it,
        // then move on. A leaked socket must not make disposal hang forever.
        var pending = _clients.Values.ToArray();
        if (pending.Length > 0)
        {
            try
            {
                await Task.WhenAll(pending).WaitAsync(DisposeGrace).ConfigureAwait(false);
            }
            catch
            {
                // Timed out or cancelled — the listener is stopped either way.
            }
        }

        try { _cts.Dispose(); } catch { /* ignore */ }
        try { _clientSlots.Dispose(); } catch { /* ignore */ }

        Volatile.Write(ref _started, 0);
    }
}
