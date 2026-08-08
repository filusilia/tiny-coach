using System.Text.Json;
using AegisCoach.Core.Models;

namespace AegisCoach.Core.Services;

public sealed record GsiReplayFrame(
    int LineNumber,
    DateTimeOffset ReceivedAt,
    MatchSnapshot Snapshot);

public sealed record GsiReplayIssue(
    string FilePath,
    int LineNumber,
    string Message);

public sealed class GsiReplayResult
{
    public int FilesRead { get; init; }
    public int LinesRead { get; init; }
    public IReadOnlyList<GsiReplayFrame> Frames { get; init; } = [];
    public IReadOnlyList<GsiReplayIssue> Issues { get; init; } = [];
    public IReadOnlyList<CoachAdvice> Advice { get; init; } = [];
}

/// <summary>
/// Replays the sanitized NDJSON written by GsiSnapshotRecorder through the same rule engine used by the UI.
/// </summary>
public sealed class GsiReplayService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public GsiReplayResult Replay(string path, CoachRuleEngine? engine = null)
    {
        var files = ResolveFiles(path);
        var frames = new List<GsiReplayFrame>();
        var issues = new List<GsiReplayIssue>();
        var linesRead = 0;

        foreach (var filePath in files)
        {
            foreach (var item in ReadFile(filePath, issues))
            {
                linesRead++;
                if (item is not null) frames.Add(item);
            }
        }

        var orderedFrames = frames
            .OrderBy(frame => frame.ReceivedAt)
            .ThenBy(frame => frame.LineNumber)
            .ToArray();
        var replayedAdvice = new List<CoachAdvice>();
        var ruleEngine = engine ?? new CoachRuleEngine();
        MatchSnapshot? previous = null;

        foreach (var frame in orderedFrames)
        {
            replayedAdvice.AddRange(ruleEngine.Evaluate(previous, frame.Snapshot));
            previous = frame.Snapshot;
        }

        return new GsiReplayResult
        {
            FilesRead = files.Count,
            LinesRead = linesRead,
            Frames = orderedFrames,
            Issues = issues,
            Advice = replayedAdvice,
        };
    }

    private static List<string> ResolveFiles(string path)
    {
        if (File.Exists(path)) return [Path.GetFullPath(path)];
        if (Directory.Exists(path))
        {
            return Directory.EnumerateFiles(path, "snapshots-*.ndjson", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .Select(Path.GetFullPath)
                .ToList();
        }

        throw new FileNotFoundException("找不到 NDJSON 文件或记录目录。", path);
    }

    private static IEnumerable<GsiReplayFrame?> ReadFile(
        string filePath,
        ICollection<GsiReplayIssue> issues)
    {
        var lineNumber = 0;
        foreach (var line in File.ReadLines(filePath))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            GsiReplayFrame? frame;
            try
            {
                using var document = JsonDocument.Parse(line);
                if (!document.RootElement.TryGetProperty("snapshot", out var snapshotElement))
                    throw new JsonException("缺少 snapshot 字段。");

                var raw = snapshotElement.Deserialize<GsiSnapshot>(JsonOptions)
                    ?? throw new JsonException("snapshot 不是有效对象。");
                var receivedAt = ParseReceivedAt(document.RootElement, lineNumber);
                frame = new GsiReplayFrame(lineNumber, receivedAt, MatchSnapshot.FromGsi(raw));
            }
            catch (JsonException exception)
            {
                issues.Add(new GsiReplayIssue(filePath, lineNumber, exception.Message));
                frame = null;
            }

            yield return frame;
        }
    }

    private static DateTimeOffset ParseReceivedAt(JsonElement root, int lineNumber)
    {
        if (!root.TryGetProperty("received_at", out var value) || value.ValueKind != JsonValueKind.String)
            return DateTimeOffset.MinValue.AddTicks(lineNumber);

        return DateTimeOffset.TryParse(value.GetString(), out var receivedAt)
            ? receivedAt
            : DateTimeOffset.MinValue.AddTicks(lineNumber);
    }
}
