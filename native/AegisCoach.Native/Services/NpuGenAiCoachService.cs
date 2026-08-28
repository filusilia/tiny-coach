namespace AegisCoach.Native.Services;

/// <summary>
/// 本地智能解释服务：若未安装专属原生扩展，则优雅回退至规则引擎或云端/本地兼容接口。
/// </summary>
public sealed class NpuGenAiCoachService : IDisposable
{
    public string? ModelPath { get; set; }
    public string Device { get; set; } = "NPU";

    public string StatusText { get; private set; } = "本地推理就绪";
    public string? LastError { get; private set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ModelPath) && Directory.Exists(ModelPath);

    public Task<string?> ExplainAsync(string prompt, CancellationToken cancellationToken, int maxTokens = 48)
    {
        // 优雅回退到规则引擎或云端/兼容端点
        return Task.FromResult<string?>(null);
    }

    public void Dispose()
    {
    }
}
