using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AegisCoach.Core.Services;

/// <summary>
/// 将通过 token 校验的 GSI 快照按天追加为 NDJSON，便于后续离线回放。
/// auth.token 会在写盘前脱敏，避免把配置凭据存进比赛记录。
/// </summary>
public sealed class GsiSnapshotRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _sync = new();
    private readonly string _directory;

    public GsiSnapshotRecorder(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("记录目录不能为空。", nameof(directory));
        _directory = Path.GetFullPath(directory);
    }

    public string DirectoryPath => _directory;
    public string? LastError { get; private set; }
    public string? LastFilePath { get; private set; }

    public bool Record(ReadOnlySpan<byte> body, DateTimeOffset receivedAt)
    {
        try
        {
            var snapshot = JsonNode.Parse(body) as JsonObject
                ?? throw new JsonException("快照根节点不是 JSON 对象。");

            if (snapshot["auth"] is JsonObject auth && auth.ContainsKey("token"))
            {
                auth["token"] = "[REDACTED]";
            }

            var record = new JsonObject
            {
                ["received_at"] = receivedAt.ToUniversalTime().ToString("O"),
                ["snapshot"] = snapshot,
            };
            var filePath = Path.Combine(_directory, $"snapshots-{receivedAt.ToLocalTime():yyyy-MM-dd}.ndjson");
            var line = record.ToJsonString(JsonOptions) + Environment.NewLine;

            lock (_sync)
            {
                System.IO.Directory.CreateDirectory(_directory);
                File.AppendAllText(filePath, line, new UTF8Encoding(false));
                LastFilePath = filePath;
                LastError = null;
            }

            return true;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            return false;
        }
    }
}
