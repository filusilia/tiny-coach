using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AegisCoach.Core.Models;

namespace AegisCoach.Core.Services;

/// <summary>
/// 本地 GSI 接收器：监听 127.0.0.1 的 HTTP POST，解析 Dota 2 Game State Integration 快照。
/// 使用 TcpListener 而非 HttpListener，避免 http.sys URL ACL 权限问题。
/// </summary>
public sealed class GsiReceiver : IAsyncDisposable
{
    private const string Loopback = "127.0.0.1";
    private const int MaxRequestBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly int _port;
    private readonly string? _token;
    private readonly TimeSpan _disconnectTimeout;
    private readonly GsiSnapshotRecorder? _snapshotRecorder;
    private readonly object _sync = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private string? _lastBodyHash;
    private DateTime _lastBodyAt;
    private DateTimeOffset _lastActivityAt = DateTimeOffset.MinValue;

    public event EventHandler<MatchSnapshot>? SnapshotReceived;
    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<string>? Log;

    public GsiReceiver(int port = 4444, string? token = null, TimeSpan? disconnectTimeout = null, string? snapshotDirectory = null)
    {
        _port = port;
        _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        _disconnectTimeout = disconnectTimeout ?? TimeSpan.FromSeconds(8);
        _snapshotRecorder = string.IsNullOrWhiteSpace(snapshotDirectory) ? null : new GsiSnapshotRecorder(snapshotDirectory);
    }

    public int BoundPort { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsConnected { get; private set; }
    public DateTimeOffset? LastSnapshotAt { get; private set; }
    public long LastMatchId { get; private set; }
    public long SnapshotCount { get; private set; }
    public string? LastError { get; private set; }
    public string? SnapshotLogPath => _snapshotRecorder?.LastFilePath;
    public string? SnapshotRecordingError => _snapshotRecorder?.LastError;

    public void Start()
    {
        Action? notify = null;
        lock (_sync)
        {
            if (IsRunning) return;
            _cts = new CancellationTokenSource();
            try
            {
                _listener = new TcpListener(IPAddress.Parse(Loopback), _port);
                _listener.Start();
                BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
                LastError = null;
            }
            catch (SocketException exception)
            {
                LastError = $"无法监听 {_port} 端口：{exception.Message}";
                _listener = null;
                _cts.Dispose();
                _cts = null;
                notify = () => Log?.Invoke(this, LastError!);
            }

            if (_listener is not null && _cts is not null)
            {
                var cts = _cts;
                IsRunning = true;
                var started = $"GSI 接收器已启动，监听 http://{Loopback}:{BoundPort}/";
                notify = () => Log?.Invoke(this, started);
                _acceptLoop = Task.Run(() => AcceptLoopAsync(cts.Token), cts.Token);
                _ = Task.Run(() => WatchdogAsync(cts.Token), cts.Token);
            }
        }

        notify?.Invoke();
    }

    public void Stop()
    {
        Action? notify = null;
        lock (_sync)
        {
            if (!IsRunning) return;
            IsRunning = false;
            _cts?.Cancel();
            _cts = null;
            _listener?.Stop();
            _listener = null;
            notify = () => Log?.Invoke(this, "GSI 接收器已停止");
            if (IsConnected)
            {
                IsConnected = false;
                notify += () => ConnectionChanged?.Invoke(this, false);
            }
        }

        notify?.Invoke();
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        return ValueTask.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException exception)
            {
                Log?.Invoke(this, $"接收连接失败：{exception.Message}");
                continue;
            }

            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var request = await ReadRequestBodyAsync(stream, cancellationToken);
                if (!request.Ok || request.Body.Length == 0)
                {
                    await WriteResponseAsync(stream, "400 Bad Request", cancellationToken);
                    return;
                }

                await WriteResponseAsync(stream, "200 OK", cancellationToken);
                ProcessBody(request.Body);
            }
            catch (Exception exception)
            {
                Log?.Invoke(this, $"连接处理失败：{exception.Message}");
            }
        }
    }

    private void ProcessBody(byte[] body)
    {
        var hash = Convert.ToHexString(SHA1.HashData(body));
        bool isDuplicate;
        lock (_sync)
        {
            isDuplicate = hash == _lastBodyHash && DateTime.UtcNow - _lastBodyAt < TimeSpan.FromSeconds(2);
            _lastBodyHash = hash;
            _lastBodyAt = DateTime.UtcNow;
            _lastActivityAt = DateTimeOffset.UtcNow;
        }

        // 心跳或重复快照只维持连接状态，不重复触发规则。
        if (isDuplicate)
        {
            return;
        }

        GsiSnapshot raw;
        try
        {
            raw = JsonSerializer.Deserialize<GsiSnapshot>(body, JsonOptions)
                ?? throw new InvalidOperationException("快照内容为空");
        }
        catch (JsonException exception)
        {
            Log?.Invoke(this, $"快照 JSON 解析失败：{exception.Message}");
            return;
        }

        if (_token is not null)
        {
            var token = raw.Auth is not null && raw.Auth.TryGetValue("token", out var value) ? value : null;
            if (!string.Equals(token, _token, StringComparison.Ordinal))
            {
                Log?.Invoke(this, "GSI token 校验失败，已忽略该快照");
                return;
            }
        }

        var snapshot = MatchSnapshot.FromGsi(raw);
        if (_snapshotRecorder is not null && !_snapshotRecorder.Record(body, DateTimeOffset.UtcNow))
        {
            Log?.Invoke(this, $"快照已接收，但落盘失败：{_snapshotRecorder.LastError}");
        }
        lock (_sync)
        {
            SnapshotCount++;
            LastMatchId = snapshot.MatchId;
            LastSnapshotAt = DateTimeOffset.UtcNow;
        }

        SnapshotReceived?.Invoke(this, snapshot);
    }

    private async Task WatchdogAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            bool shouldBeConnected;
            lock (_sync)
            {
                shouldBeConnected = _lastActivityAt != DateTimeOffset.MinValue
                    && DateTimeOffset.UtcNow - _lastActivityAt < _disconnectTimeout;
            }

            if (shouldBeConnected != IsConnected)
            {
                IsConnected = shouldBeConnected;
                ConnectionChanged?.Invoke(this, shouldBeConnected);
            }
        }
    }

    private static async Task<(bool Ok, byte[] Body)> ReadRequestBodyAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var received = new MemoryStream();
        var headerEnd = -1;
        while (received.Length < MaxRequestBytes)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            received.Write(buffer, 0, read);
            headerEnd = FindHeaderEnd(received.GetBuffer(), (int)received.Length);
            if (headerEnd >= 0) break;
        }

        if (headerEnd < 0) return (false, []);
        var header = Encoding.ASCII.GetString(received.GetBuffer(), 0, headerEnd);
        if (!header.StartsWith("POST", StringComparison.OrdinalIgnoreCase)) return (false, []);

        var contentLength = ParseContentLength(header);
        var body = new MemoryStream();
        var all = received.GetBuffer();
        var bodyStart = headerEnd + 4;
        var available = (int)received.Length - bodyStart;
        if (available > 0) body.Write(all, bodyStart, available);

        while (contentLength > body.Length)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            body.Write(buffer, 0, read);
        }

        return (true, body.ToArray());
    }

    private static int FindHeaderEnd(byte[] buffer, int length)
    {
        for (var i = 0; i + 3 < length; i++)
        {
            if (buffer[i] == (byte)'\r' && buffer[i + 1] == (byte)'\n'
                && buffer[i + 2] == (byte)'\r' && buffer[i + 3] == (byte)'\n')
            {
                return i;
            }
        }

        return -1;
    }

    private static int ParseContentLength(string header)
    {
        foreach (var line in header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;
            var name = line[..separator].Trim();
            if (!name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            return int.TryParse(line[(separator + 1)..].Trim(), out var length) ? length : 0;
        }

        return 0;
    }

    private static async Task WriteResponseAsync(Stream stream, string status, CancellationToken cancellationToken)
    {
        var response = $"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(response);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}

