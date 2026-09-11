using System.Diagnostics;
using OpenVinoSharp.GenAI;

namespace AegisCoach.Native.Services;

/// <summary>
/// 本地 NPU/CPU 智能解释服务：基于 OpenVINO GenAI 加载本地量化大模型（如 Qwen2.5-1.5B INT4）。
/// 线程安全设计，模型按需延迟在后台加载，不阻塞 UI 线程；
/// 若设备不支持或推理失败，自动记录原因并优雅回退至规则原文。
/// </summary>
public sealed class NpuGenAiCoachService : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _initLock = new();
    private LLMPipeline? _pipeline;
    private string? _loadedModelPath;
    private string? _loadedDevice;
    private bool _isInitializing;
    private bool _isDisposed;

    public string? ModelPath { get; set; }
    public string Device { get; set; } = "NPU";

    public string StatusText { get; private set; } = "等待配置本地模型";
    public string? LastError { get; private set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ModelPath) && Directory.Exists(ModelPath);

    public bool IsModelLoaded
    {
        get
        {
            lock (_initLock)
            {
                return _pipeline is not null;
            }
        }
    }

    /// <summary>
    /// 确保模型已加载。如果尚未加载，在后台线程完成初始化。
    /// </summary>
    public async Task<bool> EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            StatusText = "未配置模型目录";
            return false;
        }

        lock (_initLock)
        {
            if (_pipeline is not null &&
                string.Equals(_loadedModelPath, ModelPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_loadedDevice, Device, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (_isInitializing) return false;
            _isInitializing = true;
        }

        try
        {
            StatusText = $"正在加载模型至 {Device} 设备…";
            var success = await Task.Run(() =>
            {
                try
                {
                    GenAI.Initialize();

                    lock (_initLock)
                    {
                        _pipeline?.Dispose();
                        _pipeline = null;
                    }

                    var targetDevice = string.IsNullOrWhiteSpace(Device) ? "NPU" : Device.Trim();
                    LLMPipeline pipe;
                    try
                    {
                        pipe = new LLMPipeline(ModelPath!, targetDevice);
                        _loadedDevice = targetDevice;
                    }
                    catch (Exception ex) when (!targetDevice.Equals("CPU", StringComparison.OrdinalIgnoreCase))
                    {
                        // 若指定 NPU 失败，自动尝试降级至 CPU
                        LastError = $"设备 {targetDevice} 初始化失败，尝试回退 CPU：{ex.Message}";
                        pipe = new LLMPipeline(ModelPath!, "CPU");
                        _loadedDevice = "CPU";
                    }

                    lock (_initLock)
                    {
                        _pipeline = pipe;
                        _loadedModelPath = ModelPath;
                    }

                    LastError = null;
                    StatusText = $"本地模型就绪（运行于 {_loadedDevice}）";
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = $"模型加载失败：{ex.Message}";
                    StatusText = "模型加载失败";
                    return false;
                }
            }, cancellationToken).ConfigureAwait(false);

            return success;
        }
        finally
        {
            lock (_initLock)
            {
                _isInitializing = false;
            }
        }
    }

    public async Task<string?> ExplainAsync(string prompt, CancellationToken cancellationToken, int maxTokens = 48)
    {
        if (_isDisposed || string.IsNullOrWhiteSpace(prompt) || !IsConfigured) return null;

        // 等待排队执行，保证线程安全（单请求独占 pipeline 生成）
        if (!await _gate.WaitAsync(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            if (!IsModelLoaded)
            {
                var loaded = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
                if (!loaded || _pipeline is null) return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var result = await Task.Run(() =>
            {
                try
                {
                    LLMPipeline? currentPipe;
                    lock (_initLock)
                    {
                        currentPipe = _pipeline;
                    }

                    if (currentPipe is null) return null;

                    using var config = new GenerationConfig()
                        .SetMaxNewTokens((ulong)Math.Clamp(maxTokens, 16, 256))
                        .SetTemperature(0.1f);

                    using var results = currentPipe.Generate(prompt, config);
                    var text = results.Text;
                    return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
                }
                catch (Exception ex)
                {
                    LastError = $"推理执行失败：{ex.Message}";
                    return null;
                }
            }, cancellationToken).ConfigureAwait(false);

            return result;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            LastError = $"推理异常：{ex.Message}";
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        lock (_initLock)
        {
            _pipeline?.Dispose();
            _pipeline = null;
        }

        _gate.Dispose();
    }
}
