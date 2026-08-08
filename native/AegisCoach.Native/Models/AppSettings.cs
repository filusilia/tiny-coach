using System.Text.Json;
using AegisCoach.Core.Services;

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

/// <summary>应用本地设置，持久化到 %LOCALAPPDATA%\AegisCoach\settings.json。</summary>
public sealed class AppSettings
{
    private static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AegisCoach");

    private string _cacheDirectory = Path.Combine(DefaultRoot, "cache");
    private string _analysisDirectory = Path.Combine(DefaultRoot, "analysis");

    public int GsiPort { get; set; } = 4444;
    public string GsiToken { get; set; } = string.Empty;
    public bool AutoStartGsi { get; set; } = true;
    public string? DotaCfgManualPath { get; set; }
    public bool SmartCoachEnabled { get; set; } = true;
    /// <summary>AI 后端标识：npu（默认）/ cloud / rules。旧设置中的 NpuEnabled 会自动迁移。</summary>
    public string AiBackend { get; set; } = "npu";
    public string CloudEndpoint { get; set; } = "https://api.openai.com/v1";
    public string CloudModel { get; set; } = "gpt-4o-mini";
    public string CloudApiKey { get; set; } = string.Empty;
    public string NpuModelPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AegisCoach",
        "models",
        "qwen2.5-1.5b-int4-ov");
    public bool LiveHeroTrackingEnabled { get; set; } = true;
    /// <summary>Stratz API 密钥（可选）。填写后对任意公开对局都可实时获取全场英雄；留空时仅热门对局可实时，普通对局赛后由 OpenDota 补全。</summary>
    public string StratzApiKey { get; set; } = string.Empty;
    public int SmartCoachMinIntervalSeconds { get; set; } = 3;
    public CoachRuleOptions RuleOptions { get; set; } = new();

    /// <summary>物品/英雄资料缓存目录，可在设置页自定义。</summary>
    public string CacheDirectory
    {
        get => _cacheDirectory;
        set => _cacheDirectory = string.IsNullOrWhiteSpace(value) ? Path.Combine(DefaultRoot, "cache") : value;
    }

    /// <summary>赛后分析报告保存目录，可在设置页自定义。</summary>
    public string AnalysisDirectory
    {
        get => _analysisDirectory;
        set => _analysisDirectory = string.IsNullOrWhiteSpace(value) ? Path.Combine(DefaultRoot, "analysis") : value;
    }

    public AiBackendKind BackendKind => AiBackend switch
    {
        "cloud" => AiBackendKind.Cloud,
        "rules" => AiBackendKind.Rules,
        _ => AiBackendKind.Npu,
    };

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AegisCoach",
        "settings.json");

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Settings persistence is best effort; losing it only resets preferences.
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (loaded is not null)
                {
                    loaded.RuleOptions ??= new CoachRuleOptions();
                    loaded.RuleOptions.Normalize();
                    loaded.MigrateLegacySettings();
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

    /// <summary>
    /// 兼容旧版 settings.json：曾用 NpuEnabled 布尔值表示是否启用 NPU；
    /// 若该字段为 true 而 AiBackend 仍是默认值，则迁移到 NPU 后端。
    /// </summary>
    private void MigrateLegacySettings()
    {
        if (AiBackend is not ("npu" or "cloud" or "rules"))
        {
            AiBackend = "npu";
        }

        if (string.IsNullOrWhiteSpace(CloudEndpoint)) CloudEndpoint = "https://api.openai.com/v1";
        if (string.IsNullOrWhiteSpace(CloudModel)) CloudModel = "gpt-4o-mini";
        if (string.IsNullOrWhiteSpace(NpuModelPath))
        {
            NpuModelPath = Path.Combine(DefaultRoot, "models", "qwen2.5-1.5b-int4-ov");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
