using System.Text.Json;
using System.Text.Json.Serialization;
using AegisCoach.Core.Services;
using AegisCoach.Native.Services;

namespace AegisCoach.Native.Models;

/// <summary>实时教练的 AI 解释后端。</summary>
public enum AiBackendKind
{
    /// <summary>本地 NPU（OpenVINO GenAI），不占用 GPU，也不占用 CPU 主核。</summary>
    Npu,

    /// <summary>云端 OpenAI 兼容接口，本地零性能开销，需要接口地址与密钥。</summary>
    Cloud,

    /// <summary>不调用模型，直接显示规则引擎原文，零消耗。</summary>
    Rules,
}

/// <summary>仅供本应用使用的设置，保存在用户文档目录。</summary>
public sealed class AppSettings
{
    public const int GsiPort = 4444;

    public bool SetupCompleted { get; set; }
    public bool AutoStartGsi { get; set; } = true;
    public string? DotaCfgManualPath { get; set; }
    public bool SmartCoachEnabled { get; set; } = true;
    /// <summary>AI 后端标识：npu（默认）/ cloud / rules。</summary>
    public string AiBackend { get; set; } = "npu";
    public string CloudEndpoint { get; set; } = "https://api.openai.com/v1";
    public string CloudModel { get; set; } = "gpt-4o-mini";
    [JsonIgnore]
    public string CloudApiKey { get; set; } = string.Empty;
    public string NpuModelPath { get; set; } = Path.Combine(AegisCoachPaths.Models, "qwen2.5-1.5b-int4-ov");
    public bool LiveHeroTrackingEnabled { get; set; } = true;
    /// <summary>英雄资料页默认统计分段：all 或 1-8。</summary>
    public string DefaultRankId { get; set; } = "all";
    /// <summary>Stratz API 密钥（可选）。填写后对任意公开对局都可实时获取全场英雄；留空时仅热门对局可实时，普通对局赛后由 OpenDota 补全。</summary>
    [JsonIgnore]
    public string StratzApiKey { get; set; } = string.Empty;
    public int SmartCoachMinIntervalSeconds { get; set; } = 3;
    public CoachRuleOptions RuleOptions { get; set; } = new();

    [JsonIgnore]
    public string CacheDirectory => AegisCoachPaths.Cache;

    [JsonIgnore]
    public string AnalysisDirectory => AegisCoachPaths.Analysis;

    [JsonIgnore]
    public AiBackendKind BackendKind => AiBackend switch
    {
        "cloud" => AiBackendKind.Cloud,
        "rules" => AiBackendKind.Rules,
        _ => AiBackendKind.Npu,
    };

    [JsonIgnore]
    public string? LastSaveError { get; private set; }

    public bool Save()
    {
        try
        {
            LastSaveError = null;
            if (!AppCredentialStore.Save(nameof(CloudApiKey), CloudApiKey, out var cloudError))
            {
                LastSaveError = $"云端 API 密钥无法保存到 Windows 凭据保险库：{cloudError}";
                return false;
            }

            if (!AppCredentialStore.Save(nameof(StratzApiKey), StratzApiKey, out var stratzError))
            {
                LastSaveError = $"Stratz API 密钥无法保存到 Windows 凭据保险库：{stratzError}";
                return false;
            }

            var directory = Path.GetDirectoryName(AegisCoachPaths.SettingsFile);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            AtomicFile.WriteAllText(AegisCoachPaths.SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
            return true;
        }
        catch (Exception exception)
        {
            LastSaveError = exception.Message;
            return false;
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AegisCoachPaths.SettingsFile))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AegisCoachPaths.SettingsFile), JsonOptions);
                if (loaded is not null)
                {
                    loaded.RuleOptions ??= new CoachRuleOptions();
                    loaded.RuleOptions.Normalize();
                    loaded.Normalize();
                    loaded.CloudApiKey = AppCredentialStore.Load(nameof(CloudApiKey));
                    loaded.StratzApiKey = AppCredentialStore.Load(nameof(StratzApiKey));
                    return loaded;
                }
            }
        }
        catch
        {
            // Fall back to defaults when settings are corrupt.
        }

        return new AppSettings();
    }

    private void Normalize()
    {
        if (AiBackend is not ("npu" or "cloud" or "rules"))
        {
            AiBackend = "npu";
        }

        if (string.IsNullOrWhiteSpace(CloudEndpoint)) CloudEndpoint = "https://api.openai.com/v1";
        if (string.IsNullOrWhiteSpace(CloudModel)) CloudModel = "gpt-4o-mini";
        if (string.IsNullOrWhiteSpace(NpuModelPath))
        {
            NpuModelPath = Path.Combine(AegisCoachPaths.Models, "qwen2.5-1.5b-int4-ov");
        }

        if (DefaultRankId is not ("all" or "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8"))
        {
            DefaultRankId = "all";
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
