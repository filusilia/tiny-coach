using AegisCoach.Core.Services;

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]) || args[0] is "-h" or "--help")
{
    Console.WriteLine("用法：dotnet run --project tools\\GsiReplay\\AegisCoach.Replay.csproj -- <NDJSON 文件或记录目录>");
    return args.Length == 0 ? 2 : 0;
}

try
{
    var result = new GsiReplayService().Replay(args[0]);
    Console.WriteLine($"文件：{result.FilesRead}，行：{result.LinesRead}，有效快照：{result.Frames.Count}，问题：{result.Issues.Count}");

    foreach (var frame in result.Frames)
    {
        Console.WriteLine($"[{frame.ReceivedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}] {frame.Snapshot.PhaseText} {frame.Snapshot.GameTimeText} " +
            $"比赛 {frame.Snapshot.MatchId}，血量 {frame.Snapshot.Hero.HealthPercent:0}%");
    }

    Console.WriteLine($"规则提醒：{result.Advice.Count}");
    foreach (var advice in result.Advice)
    {
        Console.WriteLine($"- [{advice.SeverityText}] {advice.Title}：{advice.Message} " +
            $"(证据：{advice.EvidenceText})");
    }

    foreach (var issue in result.Issues)
        Console.Error.WriteLine($"! {issue.FilePath}:{issue.LineNumber} {issue.Message}");

    return result.Issues.Count == 0 ? 0 : 1;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FileNotFoundException)
{
    Console.Error.WriteLine($"回放失败：{exception.Message}");
    return 1;
}
