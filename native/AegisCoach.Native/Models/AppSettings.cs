using System.Text.Json;
using System.Text.Json.Serialization;
using AegisCoach.Core.Services;
using AegisCoach.Native.Services;

namespace AegisCoach.Native.Models;

/// <summary>实时教练的 AI 解释后端。</summary>
public enum AiBackendKind
{
    /// <summary>不调用模型，直接显示规则引擎原文，零消耗（默认）。</summary>
    Rules,

    /// <summary>本地 NPU（OpenVINO GenAI），不占用 GPU，也不占用 CPU 主核。</summary>
    Npu,

    /// <summary>本地 Ollama 服务（localhost:11434），调用本机运行的 LLM 模型。</summary>
    Ollama,

    /// <summary>云端 OpenAI / Response API 兼容接口，本地零性能开销。</summary>
    Cloud,
}

/// <summary>仅供本应用使用的设置，保存在用户文档目录。</summary>
public sealed class AppSettings
{
    public const int GsiPort = 4444;

    public bool SetupCompleted { get; set; }
    public bool AutoStartGsi { get; set; } = true;
    public string? DotaCfgManualPath { get; set; }
    public bool SmartCoachEnabled { get; set; } = true;
    /// <summary>AI 后端标识：rules（默认）/ npu / ollama / cloud。</summary>
    public string AiBackend { get; set; } = "rules";
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = string.Empty;
    public string CloudEndpoint { get; set; } = string.Empty;
    public string CloudModel { get; set; } = string.Empty;
    [JsonIgnore]
    public string CloudApiKey { get; set; } = string.Empty;
    public string CloudApiProtocol { get; set; } = "responses";
    public string NpuModelPath { get; set; } = DetectDefaultNpuModelPath();
    public bool LiveHeroTrackingEnabled { get; set; } = true;
    /// <summary>英雄资料页默认统计分段：all 或 1-8。</summary>
    public string DefaultRankId { get; set; } = "all";
    /// <summary>Stratz API 密钥（可选）。填写后对任意公开对局都可实时获取全场英雄；留空时仅热门对局可实时，普通对局赛后由 OpenDota 补全。</summary>
    [JsonIgnore]
    public string StratzApiKey { get; set; } = string.Empty;
    public int SmartCoachMinIntervalSeconds { get; set; } = 3;
    public CoachRuleOptions RuleOptions { get; set; } = new();
    /// <summary>AI 提示关键词 / 指导要点（保存在 settings.json 中，供实时教练提示词注入，可在设置中自定义修改）。</summary>
    public string AiPromptKeywords { get; set; } = "给出可执行动作, 不超过40字, 结合战场状态, 不编造敌方信息";

    [JsonIgnore]
    public string CacheDirectory => AegisCoachPaths.Cache;

    [JsonIgnore]
    public string AnalysisDirectory => AegisCoachPaths.Analysis;

    [JsonIgnore]
    public AiBackendKind BackendKind => AiBackend switch
    {
        "npu" => AiBackendKind.Npu,
        "ollama" => AiBackendKind.Ollama,
        "cloud" => AiBackendKind.Cloud,
        _ => AiBackendKind.Rules,
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
        if (AiBackend is not ("rules" or "npu" or "ollama" or "cloud"))
        {
            AiBackend = "rules";
        }

        if (CloudApiProtocol is not ("chat" or "responses"))
        {
            CloudApiProtocol = "responses";
        }

        if (string.IsNullOrWhiteSpace(OllamaEndpoint))
        {
            OllamaEndpoint = "http://localhost:11434";
        }

        if (string.IsNullOrWhiteSpace(NpuModelPath) || !Directory.Exists(NpuModelPath))
        {
            NpuModelPath = DetectDefaultNpuModelPath();
        }

        if (DefaultRankId is not ("all" or "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8"))
        {
            DefaultRankId = "all";
        }

        if (string.IsNullOrWhiteSpace(AiPromptKeywords))
        {
            AiPromptKeywords = "给出可执行动作, 不超过40字, 结合战场状态, 不编造敌方信息";
        }
    }

    /// <summary>
    /// 优先自动探测用户已下载的最新 Qwen3.5 / Qwen3 / Qwen2.5 本地量化模型。
    /// </summary>
    public static string DetectDefaultNpuModelPath()
    {
        var modelsDir = AegisCoachPaths.Models;
        var candidates = new[]
        {
            "Qwen3.5-0.8B-int8-ov",
            "qwen3.5-0.8b-int8-ov",
            "Qwen3.5-0.8B-int4-ov",
            "qwen3.5-0.8b-int4-ov",
            "Qwen3.5-2B-int4-ov",
            "qwen3.5-2b-int4-ov",
            "qwen3-1.7b-int4-ov",
            "qwen2.5-1.5b-int4-ov",
        };

        if (Directory.Exists(modelsDir))
        {
            foreach (var name in candidates)
            {
                var candidatePath = Path.Combine(modelsDir, name);
                if (Directory.Exists(candidatePath)) return candidatePath;
            }
        }

        return Path.Combine(modelsDir, "Qwen3.5-0.8B-int8-ov");
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
