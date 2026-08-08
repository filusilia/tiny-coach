using System.Diagnostics;
using OpenVinoSharp.GenAI;

namespace AegisCoach.Native.Services;

/// <summary>
/// 可选 NPU 推理后端：使用 OpenVINO GenAI 在 Intel AI Boost（NPU）上运行
/// Qwen2.5 INT4 量化模型。推理不占用 GPU，也不占用 CPU 主核；
/// 模型目录不存在或推理失败时返回 null，由调用方回退到规则原文或云端接口。
/// </summary>
public sealed class NpuGenAiCoachService : IDisposable
{
    private readonly object _sync = new();
    private LLMPipeline? _pipeline;
    private bool _initialized;
    private string? _loadError;
    private bool _loading;

    public string? ModelPath { get; set; }
    public string Device { get; set; } = "NPU";

    /// <summary>最近一次状态描述，用于页面提示（如首次编译中、失败原因）。</summary>
    public string StatusText { get; private set; } = "NPU 后端未启用";

    public string? LastError { get; private set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ModelPath) && Directory.Exists(ModelPath);

    /// <summary>首次调用会加载并编译模型（本机实测约 30 秒），之后管线常驻。</summary>
    public async Task<string?> ExplainAsync(string prompt, CancellationToken cancellationToken, int maxTokens = 48)
    {
        if (!IsConfigured)
        {
            LastError = "NPU 模型目录不存在";
            StatusText = LastError;
            return null;
        }

        if (!_initialized)
        {
            lock (_sync)
            {
                if (!_initialized && !_loading)
                {
                    _loading = true;
                    StatusText = "正在加载 NPU 模型（首次约 30 秒）…";
                }
            }
        }

        try
        {
            var pipeline = await Task.Run(() => GetPipeline(), cancellationToken);
            if (pipeline is null)
            {
                StatusText = LastError ?? "NPU 管线不可用";
                return null;
            }

            StatusText = "NPU 推理中";
            var config = new GenerationConfig()
                .SetMaxNewTokens((ulong)maxTokens)
                .SetTemperature(0.1f);
            using var results = await Task.Run(() => pipeline.Generate(prompt, config), cancellationToken);
            var text = results.Text?.Trim();
            StatusText = string.IsNullOrWhiteSpace(text) ? "NPU 输出为空" : "NPU 推理完成";
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            StatusText = "NPU 推理失败，已回退 CPU";
            return null;
        }
    }

    private LLMPipeline? GetPipeline()
    {
        lock (_sync)
        {
            if (_pipeline is not null) return _pipeline;
            if (_loadError is not null) return null;

            try
            {
                GenAI.Initialize();
                var sw = Stopwatch.StartNew();
                _pipeline = new LLMPipeline(ModelPath!, Device);
                sw.Stop();
                _initialized = true;
                StatusText = $"NPU 模型就绪（加载 {sw.Elapsed.TotalSeconds:F1}s，设备 {Device}）";
                return _pipeline;
            }
            catch (Exception exception)
            {
                _loadError = exception.Message;
                LastError = exception.Message;
                StatusText = $"NPU 模型加载失败：{exception.Message}";
                return null;
            }
            finally
            {
                _loading = false;
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _pipeline?.Dispose();
            _pipeline = null;
        }
    }
}
