namespace AegisCoach.Native.Services;

public sealed record NpuRuntimeReport(
    bool IsReady,
    string? ProviderName,
    string Summary)
{
    public static NpuRuntimeReport Unavailable { get; } = new(
        false,
        null,
        "未发现可注册的 NPU provider；可使用规则引擎或云端/本地大模型接口。");
}

/// <summary>
/// 检测本机 NPU 硬件与运行状态，无需依赖外部重型 ML 运行时库。
/// </summary>
public sealed class NpuExecutionProviderService
{
    public NpuRuntimeReport Current { get; private set; } = NpuRuntimeReport.Unavailable;

    public Task<NpuRuntimeReport> RegisterAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var detector = NpuCapabilityDetector.Detect();
            if (detector.IsDetected && detector.DeviceNames.Count > 0)
            {
                var name = detector.DeviceNames[0];
                return Set(new NpuRuntimeReport(true, name, $"已就绪 NPU 设备：{string.Join("、", detector.DeviceNames)}"));
            }
            return Set(NpuRuntimeReport.Unavailable);
        }, cancellationToken);

    private NpuRuntimeReport Set(NpuRuntimeReport report)
    {
        Current = report;
        return report;
    }
}
