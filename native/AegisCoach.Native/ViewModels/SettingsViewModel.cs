using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using AegisCoach.Core.Models;
using AegisCoach.Core.Services;
using AegisCoach.Native.Models;
using AegisCoach.Native.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AegisCoach.Native.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private AppSettings _settings = new();

    public Func<Task<string?>>? PickFolderHandler { get; set; }
    public Func<Task<string?>>? PickModelFolderHandler { get; set; }
    public Func<bool, Task>? ToggleGsiHandler { get; set; }
    public Action<AppSettings>? OnSettingsApplied { get; set; }

    [ObservableProperty]
    public partial string IntroTitle { get; set; } = "应用设置";

    [ObservableProperty]
    public partial string IntroText { get; set; } = "常用选项立即生效，自定义接口与提醒阈值均可在本页灵活调整。";

    // 1. Dota 2 实时连接
    [ObservableProperty]
    public partial bool AutoStartGsi { get; set; } = true;

    [ObservableProperty]
    public partial string DotaPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsGsiRunning { get; set; }

    [ObservableProperty]
    public partial string GsiButtonText { get; set; } = "立即连接";

    [ObservableProperty]
    public partial bool IsInfoOpen { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity InfoSeverity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial string InfoTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string InfoMessage { get; set; } = string.Empty;

    // 2. 教练与 AI 推理
    [ObservableProperty]
    public partial bool SmartCoachEnabled { get; set; } = true;

    [ObservableProperty]
    public partial int BackendIndex { get; set; } // 0: Rules, 1: Npu, 2: Ollama, 3: Cloud

    [ObservableProperty]
    public partial bool IsRulesVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsNpuVisible { get; set; }

    [ObservableProperty]
    public partial bool IsOllamaVisible { get; set; }

    [ObservableProperty]
    public partial bool IsCloudVisible { get; set; }

    // 本地 Ollama 参数
    [ObservableProperty]
    public partial string OllamaEndpoint { get; set; } = "http://localhost:11434";

    [ObservableProperty]
    public partial string OllamaModel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<string> AvailableOllamaModels { get; set; } = new();

    [ObservableProperty]
    public partial bool IsFetchingOllamaModels { get; set; }

    [ObservableProperty]
    public partial string FetchOllamaModelsStatus { get; set; } = string.Empty;

    // 远程 Response API / OpenAI 参数
    [ObservableProperty]
    public partial int ApiProtocolIndex { get; set; } = 1; // 0: Chat Completions API, 1: Responses API

    [ObservableProperty]
    public partial string CloudEndpoint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CloudModel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CloudApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<string> AvailableCloudModels { get; set; } = new();

    [ObservableProperty]
    public partial bool IsFetchingModels { get; set; }

    [ObservableProperty]
    public partial string FetchModelsStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int IntervalSeconds { get; set; } = 3;

    [ObservableProperty]
    public partial bool LiveHeroTrackingEnabled { get; set; }

    [ObservableProperty]
    public partial string StratzApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AiPromptKeywords { get; set; } = "给出可执行动作, 不超过40字, 结合战场状态, 不编造敌方信息";

    // 3. 数据与硬件
    [ObservableProperty]
    public partial string DataDirectory { get; set; } = AegisCoachPaths.Root;

    [ObservableProperty]
    public partial string NpuStatus { get; set; } = "就绪";

    [ObservableProperty]
    public partial string NpuModelPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NpuModelStatus { get; set; } = "未配置本地模型";

    // 4. 阈值与 15 个规则开关
    [ObservableProperty] public partial string LowHealthPercentText { get; set; } = "35";
    [ObservableProperty] public partial string LowManaPercentText { get; set; } = "20";
    [ObservableProperty] public partial string FightHealthPercentText { get; set; } = "75";
    [ObservableProperty] public partial string FightManaPercentText { get; set; } = "50";

    [ObservableProperty] public partial bool EnableLowHealth { get; set; } = true;
    [ObservableProperty] public partial bool EnableLowMana { get; set; } = true;
    [ObservableProperty] public partial bool EnableUltimate { get; set; } = true;
    [ObservableProperty] public partial bool EnableDeath { get; set; } = true;
    [ObservableProperty] public partial bool EnableControl { get; set; } = true;
    [ObservableProperty] public partial bool EnableBuyback { get; set; } = true;
    [ObservableProperty] public partial bool EnableFightReady { get; set; } = true;
    [ObservableProperty] public partial bool EnableSmoke { get; set; } = true;
    [ObservableProperty] public partial bool EnableSmoked { get; set; } = true;
    [ObservableProperty] public partial bool EnableNight { get; set; } = true;
    [ObservableProperty] public partial bool EnableBuilding { get; set; } = true;
    [ObservableProperty] public partial bool EnableRoshan { get; set; } = true;
    [ObservableProperty] public partial bool EnableItemReady { get; set; } = true;
    [ObservableProperty] public partial bool EnableAbilityReady { get; set; } = true;
    [ObservableProperty] public partial bool EnableMapAwareness { get; set; } = true;

    // 5. 检查更新
    [ObservableProperty] public partial string CurrentVersion { get; set; } = "1.0.0";
    [ObservableProperty] public partial string LatestVersion { get; set; } = "1.0.0";
    [ObservableProperty] public partial bool IsCheckingUpdate { get; set; }
    [ObservableProperty] public partial bool CanCheckUpdate { get; set; } = true;
    [ObservableProperty] public partial bool HasUpdate { get; set; }
    [ObservableProperty] public partial string UpdateStatusText { get; set; } = "点击检查更新";
    [ObservableProperty] public partial string ReleaseNotes { get; set; } = string.Empty;
    [ObservableProperty] public partial string ReleaseUrl { get; set; } = "https://github.com/filus/aegis-coach/releases";

    // 6. 运行日志
    [ObservableProperty] public partial string CoachLogText { get; set; } = "尚未启动";

    public SettingsViewModel()
    {
        CurrentVersion = UpdateCheckService.Instance.CurrentVersion;
    }

    public void LoadFromSettings(AppSettings settings, bool isCoachRunning)
    {
        _settings = settings;
        _settings.RuleOptions ??= new CoachRuleOptions();
        _settings.RuleOptions.Normalize();

        AutoStartGsi = _settings.AutoStartGsi;
        DotaPath = _settings.DotaCfgManualPath ?? string.Empty;
        IsGsiRunning = isCoachRunning;
        GsiButtonText = isCoachRunning ? "停止监听" : "立即连接";

        SmartCoachEnabled = _settings.SmartCoachEnabled;
        BackendIndex = _settings.BackendKind switch
        {
            AiBackendKind.Npu => 1,
            AiBackendKind.Ollama => 2,
            AiBackendKind.Cloud => 3,
            _ => 0,
        };
        IsRulesVisible = BackendIndex == 0;
        IsNpuVisible = BackendIndex == 1;
        IsOllamaVisible = BackendIndex == 2;
        IsCloudVisible = BackendIndex == 3;

        OllamaEndpoint = string.IsNullOrWhiteSpace(_settings.OllamaEndpoint) ? "http://localhost:11434" : _settings.OllamaEndpoint;
        OllamaModel = _settings.OllamaModel ?? string.Empty;
        AvailableOllamaModels.Clear();
        if (!string.IsNullOrWhiteSpace(OllamaModel))
        {
            AvailableOllamaModels.Add(OllamaModel);
        }

        ApiProtocolIndex = string.Equals(_settings.CloudApiProtocol, "responses", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        CloudEndpoint = _settings.CloudEndpoint;
        CloudModel = _settings.CloudModel;
        CloudApiKey = _settings.CloudApiKey;
        AvailableCloudModels.Clear();
        if (!string.IsNullOrWhiteSpace(CloudModel))
        {
            AvailableCloudModels.Add(CloudModel);
        }

        IntervalSeconds = _settings.SmartCoachMinIntervalSeconds;
        LiveHeroTrackingEnabled = _settings.LiveHeroTrackingEnabled;
        StratzApiKey = _settings.StratzApiKey;
        AiPromptKeywords = string.IsNullOrWhiteSpace(_settings.AiPromptKeywords)
            ? "给出可执行动作, 不超过40字, 结合战场状态, 不编造敌方信息"
            : _settings.AiPromptKeywords;

        DataDirectory = AegisCoachPaths.Root;
        NpuModelPath = _settings.NpuModelPath;

        var r = _settings.RuleOptions;
        LowHealthPercentText = r.LowHealthPercent.ToString("0.#");
        LowManaPercentText = r.LowManaPercent.ToString("0.#");
        FightHealthPercentText = r.FightReadyHealthPercent.ToString("0.#");
        FightManaPercentText = r.FightReadyManaPercent.ToString("0.#");

        EnableLowHealth = r.EnableLowHealth;
        EnableLowMana = r.EnableLowMana;
        EnableUltimate = r.EnableUltimateReady;
        EnableDeath = r.EnableDeath;
        EnableControl = r.EnableControlLocked;
        EnableBuyback = r.EnableBuybackWindow;
        EnableFightReady = r.EnableFightReady;
        EnableSmoke = r.EnableSmoke;
        EnableSmoked = r.EnableSmokedState;
        EnableNight = r.EnableNightFall;
        EnableBuilding = r.EnableBuildings;
        EnableRoshan = r.EnableRoshan;
        EnableItemReady = r.EnableItemReady;
        EnableAbilityReady = r.EnableAbilityReady;
        EnableMapAwareness = r.EnableMapAwareness;

        UpdateNpuStatusUi();
    }

    partial void OnBackendIndexChanged(int value)
    {
        IsRulesVisible = value == 0;
        IsNpuVisible = value == 1;
        IsOllamaVisible = value == 2;
        IsCloudVisible = value == 3;
        if (value == 1)
        {
            UpdateNpuStatusUi();
        }
    }

    [RelayCommand]
    public async Task FetchOllamaModelsAsync()
    {
        var endpoint = string.IsNullOrWhiteSpace(OllamaEndpoint) ? "http://localhost:11434" : OllamaEndpoint.Trim();
        try
        {
            IsFetchingOllamaModels = true;
            FetchOllamaModelsStatus = "正在向本机 Ollama 服务拉取模型列表…";
            var service = new CloudCoachService();
            var models = await service.FetchModelsAsync(endpoint, "");
            if (models.Count == 0)
            {
                FetchOllamaModelsStatus = "连接成功，但 Ollama 尚未下载模型。请在终端执行 'ollama pull qwen2.5:7b'。";
                ShowMessage(InfoBarSeverity.Warning, "未发现模型", FetchOllamaModelsStatus);
                return;
            }

            AvailableOllamaModels.Clear();
            foreach (var m in models)
            {
                AvailableOllamaModels.Add(m);
            }

            if (string.IsNullOrWhiteSpace(OllamaModel) || !AvailableOllamaModels.Contains(OllamaModel))
            {
                OllamaModel = AvailableOllamaModels[0];
            }

            FetchOllamaModelsStatus = $"连接成功！获取到 {models.Count} 个本地模型";
            ShowMessage(InfoBarSeverity.Success, "Ollama 连接成功", $"已成功获取到 {models.Count} 个本地模型，已填入下拉列表。");
        }
        catch (Exception ex)
        {
            FetchOllamaModelsStatus = $"连接 Ollama 失败：{ex.Message}。请确保本机已运行 Ollama 服务 (http://localhost:11434)。";
            ShowMessage(InfoBarSeverity.Warning, "Ollama 服务未就绪", FetchOllamaModelsStatus);
        }
        finally
        {
            IsFetchingOllamaModels = false;
        }
    }

    [RelayCommand]
    public async Task FetchRemoteModelsAsync()
    {
        if (string.IsNullOrWhiteSpace(CloudEndpoint))
        {
            FetchModelsStatus = "请先填写接口地址。";
            ShowMessage(InfoBarSeverity.Warning, "拉取提示", "请先填写远程 API 接口地址。");
            return;
        }

        try
        {
            IsFetchingModels = true;
            FetchModelsStatus = "正在向远程接口拉取模型列表…";
            var service = new CloudCoachService();
            var models = await service.FetchModelsAsync(CloudEndpoint.Trim(), CloudApiKey.Trim());
            if (models.Count == 0)
            {
                FetchModelsStatus = "拉取成功，但接口未返回模型列表，请手动输入模型名称。";
                ShowMessage(InfoBarSeverity.Informational, "拉取完成", FetchModelsStatus);
                return;
            }

            AvailableCloudModels.Clear();
            foreach (var m in models)
            {
                AvailableCloudModels.Add(m);
            }

            if (string.IsNullOrWhiteSpace(CloudModel) || !AvailableCloudModels.Contains(CloudModel))
            {
                CloudModel = AvailableCloudModels[0];
            }

            FetchModelsStatus = $"成功获取到 {models.Count} 个可用模型";
            ShowMessage(InfoBarSeverity.Success, "模型拉取成功", $"已成功获取到 {models.Count} 个可用模型，已填入下拉框。");
        }
        catch (Exception ex)
        {
            FetchModelsStatus = $"拉取失败：{ex.Message}";
            ShowMessage(InfoBarSeverity.Warning, "拉取模型异常", FetchModelsStatus);
        }
        finally
        {
            IsFetchingModels = false;
        }
    }

    [RelayCommand]
    public void SaveSettings()
    {
        _settings.AutoStartGsi = AutoStartGsi;
        _settings.DotaCfgManualPath = string.IsNullOrWhiteSpace(DotaPath) ? null : DotaPath.Trim();
        _settings.SmartCoachEnabled = SmartCoachEnabled;
        _settings.AiBackend = BackendIndex switch
        {
            1 => "npu",
            2 => "ollama",
            3 => "cloud",
            _ => "rules",
        };
        _settings.OllamaEndpoint = string.IsNullOrWhiteSpace(OllamaEndpoint) ? "http://localhost:11434" : OllamaEndpoint.Trim();
        _settings.OllamaModel = OllamaModel.Trim();
        _settings.CloudApiProtocol = ApiProtocolIndex == 1 ? "responses" : "chat";
        _settings.CloudEndpoint = CloudEndpoint.Trim();
        _settings.CloudModel = CloudModel.Trim();
        _settings.CloudApiKey = CloudApiKey.Trim();
        _settings.SmartCoachMinIntervalSeconds = Math.Clamp(IntervalSeconds, 1, 60);
        _settings.NpuModelPath = string.IsNullOrWhiteSpace(NpuModelPath) ? _settings.NpuModelPath : NpuModelPath.Trim();
        _settings.StratzApiKey = StratzApiKey.Trim();
        _settings.LiveHeroTrackingEnabled = LiveHeroTrackingEnabled;
        _settings.AiPromptKeywords = string.IsNullOrWhiteSpace(AiPromptKeywords)
            ? "给出可执行动作, 不超过40字, 结合战场状态, 不编造敌方信息"
            : AiPromptKeywords.Trim();

        var r = _settings.RuleOptions ??= new CoachRuleOptions();
        if (double.TryParse(LowHealthPercentText, out var lhp)) r.LowHealthPercent = Math.Clamp(lhp, 1, 100);
        if (double.TryParse(LowManaPercentText, out var lmp)) r.LowManaPercent = Math.Clamp(lmp, 1, 100);
        if (double.TryParse(FightHealthPercentText, out var fhp)) r.FightReadyHealthPercent = Math.Clamp(fhp, 1, 100);
        if (double.TryParse(FightManaPercentText, out var fmp)) r.FightReadyManaPercent = Math.Clamp(fmp, 1, 100);

        r.EnableLowHealth = EnableLowHealth;
        r.EnableLowMana = EnableLowMana;
        r.EnableUltimateReady = EnableUltimate;
        r.EnableDeath = EnableDeath;
        r.EnableControlLocked = EnableControl;
        r.EnableBuybackWindow = EnableBuyback;
        r.EnableFightReady = EnableFightReady;
        r.EnableSmoke = EnableSmoke;
        r.EnableSmokedState = EnableSmoked;
        r.EnableNightFall = EnableNight;
        r.EnableBuildings = EnableBuilding;
        r.EnableRoshan = EnableRoshan;
        r.EnableItemReady = EnableItemReady;
        r.EnableAbilityReady = EnableAbilityReady;
        r.EnableMapAwareness = EnableMapAwareness;
        r.Normalize();

        _settings.SetupCompleted = true;
        if (_settings.Save())
        {
            ShowMessage(InfoBarSeverity.Success, "设置已保存", "配置已成功保存并立即生效。");
            OnSettingsApplied?.Invoke(_settings);
        }
        else
        {
            ShowMessage(InfoBarSeverity.Error, "保存失败", _settings.LastSaveError ?? "无法写入设置文件。");
        }
    }

    [RelayCommand]
    public async Task AutoConfigureDotaAsync()
    {
        ShowMessage(InfoBarSeverity.Informational, "正在配置", "正在扫描 Steam 与 Dota 2 路径…");
        var manualPath = string.IsNullOrWhiteSpace(DotaPath) ? null : DotaPath.Trim();
        var token = EnsureGsiAuthToken();
        var result = await Task.Run(() => GsiConfigInstaller.Install(manualPath, AppSettings.GsiPort, token));
        ShowMessage(
            result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
            result.Success ? "GSI 配置已安装" : "GSI 配置未安装",
            result.Success ? $"{result.Message} 请完全退出并重启 Dota 2。" : result.Message);
    }

    [RelayCommand]
    public async Task ToggleGsiAsync()
    {
        if (ToggleGsiHandler is not null)
        {
            await ToggleGsiHandler(!IsGsiRunning);
            GsiButtonText = IsGsiRunning ? "停止监听" : "立即连接";
        }
    }

    [RelayCommand]
    public async Task BrowseGameDirectoryAsync()
    {
        if (PickFolderHandler is not null)
        {
            var path = await PickFolderHandler();
            if (!string.IsNullOrWhiteSpace(path)) DotaPath = path;
        }
    }

    [RelayCommand]
    public async Task BrowseNpuModelAsync()
    {
        if (PickModelFolderHandler is not null)
        {
            var path = await PickModelFolderHandler();
            if (!string.IsNullOrWhiteSpace(path))
            {
                NpuModelPath = path;
                UpdateNpuStatusUi();
            }
        }
    }

    [RelayCommand]
    public async Task OpenCfgDirectoryAsync()
    {
        var manualPath = string.IsNullOrWhiteSpace(DotaPath) ? null : DotaPath.Trim();
        var dir = await Task.Run(() => GsiConfigInstaller.LocateDotaCfgDirectory(manualPath));
        if (dir is null)
        {
            ShowMessage(InfoBarSeverity.Warning, "未找到 Dota 2 cfg 目录", "请点击“选择游戏目录”指定 game\\dota\\cfg 路径。");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowMessage(InfoBarSeverity.Error, "无法打开目录", ex.Message);
        }
    }

    [RelayCommand]
    public void OpenDataDirectory()
    {
        try
        {
            Directory.CreateDirectory(AegisCoachPaths.Root);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AegisCoachPaths.Root}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowMessage(InfoBarSeverity.Error, "无法打开数据目录", ex.Message);
        }
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        if (IsCheckingUpdate || !CanCheckUpdate) return;
        IsCheckingUpdate = true;
        CanCheckUpdate = false;
        UpdateStatusText = "正在检查最新版本…";
        try
        {
            var info = await UpdateCheckService.Instance.CheckForUpdatesAsync();
            LatestVersion = info.LatestVersion;
            HasUpdate = info.HasUpdate;
            ReleaseUrl = info.ReleaseUrl;
            ReleaseNotes = info.ReleaseNotes;

            if (info.HasUpdate)
            {
                UpdateStatusText = $"发现新版本 v{info.LatestVersion}！点击前往下载。";
                ShowMessage(InfoBarSeverity.Success, "发现新版本", $"最新版本为 v{info.LatestVersion}，建议前往下载更新。");
            }
            else if (!string.IsNullOrWhiteSpace(info.ErrorMessage))
            {
                UpdateStatusText = info.ErrorMessage;
                ShowMessage(InfoBarSeverity.Warning, "检查更新提示", info.ErrorMessage);
            }
            else
            {
                UpdateStatusText = $"当前已是最新版本 (v{CurrentVersion})";
                ShowMessage(InfoBarSeverity.Informational, "已是最新版本", $"神盾教练 v{CurrentVersion} 运行在最新版本。");
            }
        }
        finally
        {
            IsCheckingUpdate = false;
            CanCheckUpdate = true;
        }
    }

    [RelayCommand]
    public void OpenReleaseUrl()
    {
        var target = string.IsNullOrWhiteSpace(ReleaseUrl) ? "https://github.com/filus/aegis-coach/releases" : ReleaseUrl;
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowMessage(InfoBarSeverity.Error, "无法打开浏览器", ex.Message);
        }
    }

    public void AppendLog(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        CoachLogText = string.IsNullOrWhiteSpace(CoachLogText) || CoachLogText == "尚未启动"
            ? line
            : $"{line}\n{CoachLogText}";
        var lines = CoachLogText.Split('\n');
        if (lines.Length > 80) CoachLogText = string.Join('\n', lines.Take(80));
    }

    private void ShowMessage(InfoBarSeverity severity, string title, string message)
    {
        InfoSeverity = severity;
        InfoTitle = title;
        InfoMessage = message;
        IsInfoOpen = true;
    }

    private void UpdateNpuStatusUi()
    {
        var detector = NpuCapabilityDetector.Detect();
        NpuStatus = detector.Summary;
        if (string.IsNullOrWhiteSpace(NpuModelPath))
        {
            NpuModelStatus = "未配置本地模型路径（可下载 Qwen3.5-0.8B-ov 等模型体验端侧推理）";
        }
        else if (Directory.Exists(NpuModelPath))
        {
            NpuModelStatus = $"模型目录已就绪：{NpuModelPath}";
        }
        else
        {
            NpuModelStatus = "指定的模型目录不存在，请重新选择";
        }
    }

    private static string EnsureGsiAuthToken()
    {
        const string TokenCredentialKey = "GsiAuthToken";
        var token = AppCredentialStore.Load(TokenCredentialKey);
        if (string.IsNullOrWhiteSpace(token) || token.Length < 16)
        {
            Span<byte> randomBytes = stackalloc byte[16];
            System.Security.Cryptography.RandomNumberGenerator.Fill(randomBytes);
            token = Convert.ToHexString(randomBytes).ToLowerInvariant();
            AppCredentialStore.Save(TokenCredentialKey, token, out _);
        }
        return token;
    }
}
