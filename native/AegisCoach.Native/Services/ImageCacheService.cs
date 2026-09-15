using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AegisCoach.Core.Services;

namespace AegisCoach.Native.Services;

/// <summary>
/// 把远程图片（英雄/物品/技能图）下载并缓存到本地目录。
/// 首次联网时后台低并发预热，之后断网或 CDN 失效时直接使用本地文件，避免图片空白。
/// 严格限制并发数与超时时间，杜绝弱网环境下打垮线程池和阻塞 UI 渲染管线。
/// </summary>
public sealed class ImageCacheService
{
    private static readonly Lazy<ImageCacheService> LazyInstance =
        new(() => new ImageCacheService(AegisCoachPaths.Cache));

    public static ImageCacheService Instance => LazyInstance.Value;

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(4),
    };
    private readonly string _cacheDirectory;
    private readonly ConcurrentQueue<string> _downloadQueue = new();
    private readonly ConcurrentDictionary<string, byte> _queuedUrls = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _workerGate = new(2, 2);
    private int _activeWorkers;

    public ImageCacheService(string cacheRoot)
    {
        _cacheDirectory = Path.Combine(cacheRoot, "images");
        try { Directory.CreateDirectory(_cacheDirectory); } catch { /* 缓存不可用时回退原 URL */ }
    }

    /// <summary>返回可直接显示的 URI：本地缓存命中返回 file://，否则返回原始 URL。</summary>
    public Uri Resolve(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return new Uri("ms-appx:///Assets/Icons/app_256.png");
        var local = LocalPathFor(url);
        return File.Exists(local) ? new Uri(local) : new Uri(url);
    }

    /// <summary>返回可绑定的字符串 URI（用于 Image.Source 字符串绑定）。</summary>
    public string ResolveText(string? url) => Resolve(url).ToString();

    /// <summary>后台受控预下载一批图片（只下载缺失的，平滑入队由后台低并发 worker 处理，绝不阻塞线程池）。</summary>
    public Task PrefetchAsync(IEnumerable<string> urls, CancellationToken cancellationToken)
    {
        foreach (var url in urls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.Ordinal))
        {
            var local = LocalPathFor(url);
            if (File.Exists(local)) continue;
            if (_queuedUrls.TryAdd(url, 0))
            {
                _downloadQueue.Enqueue(url);
            }
        }

        StartWorkers(cancellationToken);
        return Task.CompletedTask;
    }

    private void StartWorkers(CancellationToken cancellationToken)
    {
        while (_activeWorkers < 2 && !_downloadQueue.IsEmpty)
        {
            Interlocked.Increment(ref _activeWorkers);
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested && _downloadQueue.TryDequeue(out var url))
                    {
                        _queuedUrls.TryRemove(url, out _);
                        var local = LocalPathFor(url);
                        if (File.Exists(local)) continue;

                        await _workerGate.WaitAsync(cancellationToken);
                        try
                        {
                            await DownloadAsync(url, local, cancellationToken);
                        }
                        catch
                        {
                            // 单张图片下载失败或弱网超时静默跳过，绝不阻塞主程序
                        }
                        finally
                        {
                            _workerGate.Release();
                        }

                        // 每次下载后主动出让时间片，避免连续占用网络栈
                        await Task.Delay(50, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    Interlocked.Decrement(ref _activeWorkers);
                }
            }, cancellationToken);
        }
    }

    private string LocalPathFor(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..20];
        var extension = ".png";
        try
        {
            var path = new Uri(url).AbsolutePath;
            var ext = Path.GetExtension(path);
            if (!string.IsNullOrWhiteSpace(ext) && ext.Length is > 1 and <= 6) extension = ext;
        }
        catch
        {
            // URL 解析失败仍可缓存。
        }

        return Path.Combine(_cacheDirectory, hash + extension);
    }

    private async Task DownloadAsync(string url, string localPath, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) return;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        var tempPath = localPath + ".tmp";
        Directory.CreateDirectory(_cacheDirectory);
        await using (var target = File.Create(tempPath))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        File.Move(tempPath, localPath, overwrite: true);
    }
}
