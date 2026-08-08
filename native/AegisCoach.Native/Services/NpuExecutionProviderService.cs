using Microsoft.Windows.AI.MachineLearning;

namespace AegisCoach.Native.Services;

public sealed record NpuRuntimeReport(
    bool IsReady,
    string? ProviderName,
    string Summary)
{
    public static NpuRuntimeReport Unavailable { get; } = new(
        false,
        null,
    "未发现可注册的 NPU provider；可回退到规则原文或云端接口。");
}

/// <summary>
/// Registers only execution providers that are explicitly identified as NPU providers.
/// GPU providers such as DirectML are intentionally ignored.
/// </summary>
public sealed class NpuExecutionProviderService
{
    private static readonly string[] NpuProviderKeywords =
    [
        "NPU",
        "QNN",
        "OpenVINO",
        "VitisAI",
        "Vitis_AI",
    ];

    private static readonly string[] GpuProviderKeywords =
    [
        "CUDA",
        "DirectML",
        "TensorRT",
        "ROCm",
        "DML",
    ];

    public NpuRuntimeReport Current { get; private set; } = NpuRuntimeReport.Unavailable;

    public Task<NpuRuntimeReport> RegisterAsync(CancellationToken cancellationToken) =>
        Task.Run(Register, cancellationToken);

    private NpuRuntimeReport Register()
    {
        try
        {
            var catalog = ExecutionProviderCatalog.GetDefault();
            var providers = catalog?.FindAllProviders();
            if (providers is null) return Set(NpuRuntimeReport.Unavailable);

            foreach (var provider in providers)
            {
                if (!IsNpuProvider(provider.Name)) continue;
                if (provider.ReadyState != ExecutionProviderReadyState.Ready) continue;
                if (!provider.TryRegister()) continue;

                return Set(new NpuRuntimeReport(
                    true,
                    provider.Name,
                    $"已注册 NPU provider：{provider.Name}；GPU provider 已禁用。"));
            }

            return Set(new NpuRuntimeReport(
                false,
                null,
        "未发现已就绪的 NPU provider；GPU provider 已禁用，可回退规则原文或云端接口。"));
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return Set(new NpuRuntimeReport(
                false,
                null,
        "Windows ML NPU provider 不可用；GPU provider 已禁用，可回退规则原文或云端接口。"));
        }
    }

    private NpuRuntimeReport Set(NpuRuntimeReport report)
    {
        Current = report;
        return report;
    }

    private static bool IsNpuProvider(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        !GpuProviderKeywords.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase)) &&
        NpuProviderKeywords.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
}
