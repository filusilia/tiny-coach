using System.Text.Json;
using AegisCoach.Core.Models;

namespace AegisCoach.Core.Services;

/// <summary>
/// 历史赛后报告仓储：启动时只读取摘要，用户选择后再读取完整报告。
/// </summary>
public sealed class ReportRepository
{
    private const int DefaultSummaryLimit = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly Func<string> _directoryProvider;

    public ReportRepository(Func<string> directoryProvider)
    {
        _directoryProvider = directoryProvider;
    }

    public async Task<IReadOnlyList<MatchReportSummary>> LoadSummariesAsync(
        int maxCount = DefaultSummaryLimit,
        CancellationToken cancellationToken = default)
    {
        var candidates = await Task.Run(
            () =>
            {
                var directory = _directoryProvider();
                if (!Directory.Exists(directory)) return [];

                // 先按文件时间截取，再打开 JSON，避免 200 份限制失效后仍解析全量历史文件。
                return Directory.EnumerateFiles(directory, "*.json")
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Take(Math.Max(1, maxCount))
                    .ToArray();
            },
            cancellationToken).ConfigureAwait(false);

        var summaries = new List<MatchReportSummary>(candidates.Length);
        foreach (var file in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                summaries.Add(await ReadSummaryAsync(file, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 单个损坏文件保留在列表中，用户可看到具体错误，不阻断其他报告。
                summaries.Add(MatchReportSummary.FromLoadError(file.FullName, exception));
            }
        }

        return summaries
            .OrderByDescending(summary => summary.GeneratedAt)
            .ToList();
    }

    public async Task<MatchReport> LoadReportAsync(
        MatchReportSummary summary,
        CancellationToken cancellationToken = default)
    {
        var filePath = summary.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            var id = Path.GetFileName(summary.Id);
            if (!string.Equals(id, summary.Id, StringComparison.Ordinal))
                throw new InvalidDataException("报告标识包含非法路径");
            filePath = Path.Combine(_directoryProvider(), id + ".json");
        }

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        var report = await JsonSerializer.DeserializeAsync<MatchReport>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return report ?? throw new InvalidDataException("报告内容为空");
    }

    private static async Task<MatchReportSummary> ReadSummaryAsync(
        FileInfo file,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 32 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var root = document.RootElement;

        var id = root.TryGetProperty("id", out var idElement)
            ? idElement.GetString()
            : null;
        var generatedAt = root.TryGetProperty("generatedAt", out var generatedElement) &&
                          generatedElement.TryGetDateTimeOffset(out var generated)
            ? generated
            : file.LastWriteTimeUtc;
        var backend = root.TryGetProperty("backend", out var backendElement)
            ? backendElement.GetString() ?? string.Empty
            : string.Empty;
        var match = root.TryGetProperty("match", out var matchElement)
            ? matchElement.Deserialize<MatchSessionRecord>(JsonOptions)
            : null;

        return new MatchReportSummary
        {
            Id = string.IsNullOrWhiteSpace(id) ? Path.GetFileNameWithoutExtension(file.Name) : id,
            GeneratedAt = generatedAt,
            Backend = backend,
            Match = match ?? new MatchSessionRecord { HeroName = "报告摘要缺失" },
            FilePath = file.FullName,
        };
    }
}
