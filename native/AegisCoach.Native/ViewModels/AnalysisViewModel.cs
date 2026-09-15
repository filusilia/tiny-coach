using System.Collections.ObjectModel;
using System.Diagnostics;
using AegisCoach.Core.Models;
using AegisCoach.Core.Services;
using AegisCoach.Native.Data;
using AegisCoach.Native.Models;
using AegisCoach.Native.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace AegisCoach.Native.ViewModels;

/// <summary>
/// 赛后分析 ViewModel：历史报告摘要列表（按需加载详情）、API 对局拉取、
/// 本地 GSI 回放操作提取、AI 复盘（NPU/云端/规则兜底）及习惯统计展示。
/// </summary>
public sealed partial class AnalysisViewModel : ObservableObject
{
    private readonly Dictionary<string, MatchReport> _reportCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _ctsLock = new();
    private CancellationTokenSource _lifetimeCts = new();

    private CancellationToken ActiveToken
    {
        get
        {
            lock (_ctsLock)
            {
                if (_lifetimeCts.IsCancellationRequested)
                {
                    _lifetimeCts.Dispose();
                    _lifetimeCts = new CancellationTokenSource();
                }
                return _lifetimeCts.Token;
            }
        }
    }

    private ReportRepository? _reportRepository;
    private DotaDataService? _dataService;
    private Func<string>? _snapshotDirectoryProvider;
    private Func<AppSettings>? _settingsProvider;
    private Func<DotaCatalog?>? _catalogProvider;
    private Func<IReadOnlyList<ItemCardItem>>? _allItemsProvider;
    private NpuGenAiCoachService? _npuCoach;
    private CloudCoachService? _cloudCoach;
    private ApiMatchAnalyzer? _apiMatchAnalyzer;
    private Action<Task>? _registerBackgroundTask;
    private Action<string>? _logAction;
    private DispatcherQueue? _dispatcherQueue;

    private CancellationTokenSource? _loadDetailCts;
    private PlayerHabits _habits = new();

    public BatchObservableCollection<MatchReportSummary> AnalysisReports { get; } = [];

    [ObservableProperty]
    public partial MatchReportSummary? SelectedSummary { get; set; }

    [ObservableProperty]
    public partial MatchReport? SelectedReport { get; set; }

    [ObservableProperty]
    public partial string MatchIdInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsFetchingMatch { get; set; }

    [ObservableProperty]
    public partial bool IsRegenerating { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingDetail { get; set; }

    [ObservableProperty]
    public partial bool IsDetailVisible { get; set; }

    [ObservableProperty]
    public partial bool IsDetailContentVisible { get; set; }

    [ObservableProperty]
    public partial bool IsDetailStateVisible { get; set; }

    [ObservableProperty]
    public partial string DetailStateTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetailStateMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HabitsSummaryText { get; set; } = "暂无历史对局…";

    [ObservableProperty]
    public partial bool HasHabits { get; set; }

    [ObservableProperty]
    public partial bool HasReports { get; set; }

    [ObservableProperty]
    public partial string HabitTotalMatchesText { get; set; } = "0";

    [ObservableProperty]
    public partial string HabitWinRateText { get; set; } = "--";

    [ObservableProperty]
    public partial bool HabitIsWinRatePositive { get; set; }

    [ObservableProperty]
    public partial string HabitAverageKdaText { get; set; } = "--";

    [ObservableProperty]
    public partial string HabitMostPlayedHeroText { get; set; } = "--";

    [ObservableProperty]
    public partial string HabitMostBoughtItemText { get; set; } = "--";

    [ObservableProperty]
    public partial string HabitAverageDurationText { get; set; } = "--";

    [ObservableProperty]
    public partial string ReportCountText { get; set; } = "0 份报告";

    // 详情字段
    [ObservableProperty]
    public partial string DetailHeroName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetailResultText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool? DetailResultWon { get; set; }

    [ObservableProperty]
    public partial string DetailDurationText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetailDateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetailKdaText { get; set; } = "--";

    [ObservableProperty]
    public partial string DetailLastHitsText { get; set; } = "--";

    [ObservableProperty]
    public partial string DetailDeniesText { get; set; } = "--";

    [ObservableProperty]
    public partial string DetailGpmText { get; set; } = "--";

    [ObservableProperty]
    public partial string DetailXpmText { get; set; } = "--";

    [ObservableProperty]
    public partial string DetailMaxLevelText { get; set; } = "--";

    [ObservableProperty]
    public partial string DetailTitleText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetailMetaText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetailBackendText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetailReviewText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetailItemsText { get; set; } = "--";

    [ObservableProperty]
    public partial string DetailMomentsText { get; set; } = "--";

    [ObservableProperty]
    public partial string DetailEconomyText { get; set; } = "--";

    [ObservableProperty]
    public partial string DetailOperationsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasOperations { get; set; }

    [ObservableProperty]
    public partial string DetailApiText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasApiData { get; set; }

    public ObservableCollection<string> DetailFinalItems { get; } = [];

    [ObservableProperty]
    public partial bool HasFinalItems { get; set; }

    public ObservableCollection<string> DetailKeyMoments { get; } = [];

    [ObservableProperty]
    public partial bool HasKeyMoments { get; set; }

    public ObservableCollection<EconomySample> DetailEconomySamples { get; } = [];

    [ObservableProperty]
    public partial bool HasEconomySamples { get; set; }

    public void ConfigureServices(
        ReportRepository reportRepository,
        DotaDataService dataService,
        Func<string> snapshotDirectoryProvider,
        Func<AppSettings> settingsProvider,
        Func<DotaCatalog?> catalogProvider,
        Func<IReadOnlyList<ItemCardItem>> allItemsProvider,
        NpuGenAiCoachService npuCoach,
        CloudCoachService cloudCoach,
        ApiMatchAnalyzer apiMatchAnalyzer,
        Action<Task> registerBackgroundTask,
        Action<string>? logAction,
        DispatcherQueue dispatcherQueue)
    {
        _reportRepository = reportRepository;
        _dataService = dataService;
        _snapshotDirectoryProvider = snapshotDirectoryProvider;
        _settingsProvider = settingsProvider;
        _catalogProvider = catalogProvider;
        _allItemsProvider = allItemsProvider;
        _npuCoach = npuCoach;
        _cloudCoach = cloudCoach;
        _apiMatchAnalyzer = apiMatchAnalyzer;
        _registerBackgroundTask = registerBackgroundTask;
        _logAction = logAction;
        _dispatcherQueue = dispatcherQueue;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_reportRepository is null) return;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ActiveToken, cancellationToken);
        var ct = linkedCts.Token;

        try
        {
            var habitsTask = Task.Run(MatchSessionTracker.LoadHabits, ct);
            var summariesTask = _reportRepository.LoadSummariesAsync(200, ct);
            await Task.WhenAll(habitsTask, summariesTask).ConfigureAwait(false);

            _habits = await habitsTask.ConfigureAwait(false);
            var summaries = await summariesTask.ConfigureAwait(false);

            RunOnUi(() =>
            {
                _reportCache.Clear();
                AnalysisReports.Clear();
                AnalysisReports.AddRange(summaries);
                ReportCountText = $"{AnalysisReports.Count} 份报告";
                UpdateHabitsSummary();
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logAction?.Invoke($"历史对局数据初始化异常：{ex.Message}");
        }
    }

    public async Task SelectSummaryAsync(MatchReportSummary? summary, CancellationToken cancellationToken = default)
    {
        if (summary is null)
        {
            SelectedSummary = null;
            SelectedReport = null;
            IsDetailVisible = false;
            IsDetailContentVisible = false;
            IsDetailStateVisible = false;
            return;
        }

        _loadDetailCts?.Cancel();
        _loadDetailCts?.Dispose();
        _loadDetailCts = CancellationTokenSource.CreateLinkedTokenSource(ActiveToken, cancellationToken);
        var ct = _loadDetailCts.Token;

        SelectedSummary = summary;
        SelectedReport = null;
        IsDetailVisible = true;

        if (_reportCache.TryGetValue(summary.Id, out var cachedReport))
        {
            SelectedReport = cachedReport;
            DisplayReport(cachedReport);
            return;
        }

        if (summary.IsLoadError)
        {
            DisplayState(summary, "报告不可用", summary.ErrorText);
            return;
        }

        DisplayState(summary, "正在读取报告…", "仅在选中后读取完整复盘、操作记录和 AI 内容。");
        IsLoadingDetail = true;

        try
        {
            if (_reportRepository is null) return;
            var report = await _reportRepository.LoadReportAsync(summary, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested || !ReferenceEquals(SelectedSummary, summary)) return;

            _reportCache[summary.Id] = report;
            RunOnUi(() =>
            {
                SelectedReport = report;
                DisplayReport(report);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested && ReferenceEquals(SelectedSummary, summary))
            {
                summary.MarkLoadError(ex);
                RunOnUi(() => DisplayState(summary, "报告读取失败", ex.Message));
            }
        }
        finally
        {
            RunOnUi(() => IsLoadingDetail = false);
        }
    }

    [RelayCommand]
    public async Task FetchMatchAsync()
    {
        var matchIdText = MatchIdInput.Trim();
        if (string.IsNullOrWhiteSpace(matchIdText) || !long.TryParse(matchIdText, out var matchId) || matchId <= 0)
        {
            _logAction?.Invoke("请输入有效的比赛 ID（纯数字）");
            return;
        }

        if (IsFetchingMatch || _apiMatchAnalyzer is null) return;
        IsFetchingMatch = true;

        try
        {
            var steamId = _habits.SteamId ?? string.Empty;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ActiveToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));

            var api = await _apiMatchAnalyzer.FetchAsync(
                matchId,
                steamId,
                0,
                ResolveApiHeroInternalName,
                ResolveApiItemName,
                ResolveApiItemById,
                timeoutCts.Token).ConfigureAwait(false);

            if (api is null || api.HeroId <= 0)
            {
                _logAction?.Invoke($"API 拉取失败：{api?.Error ?? "无返回"}");
                return;
            }

            EnrichApiNames(api);
            var record = new MatchSessionRecord
            {
                MatchId = matchId,
                HeroId = api.HeroId,
                HeroName = LocalizedHeroNameById(api.HeroId),
                SteamId = _habits.SteamId ?? string.Empty,
                StartedAt = DateTimeOffset.UtcNow,
                EndedAt = DateTimeOffset.UtcNow,
                DurationSeconds = api.DurationSeconds,
                Won = api.RadiantWin is null ? null : api.RadiantWin == api.IsRadiant,
                Kills = api.Kills,
                Deaths = api.Deaths,
                Assists = api.Assists,
                LastHits = api.LastHits,
                Denies = api.Denies,
                Gpm = api.Gpm,
                Xpm = api.Xpm,
                MaxLevel = api.Level,
                FinalItems = api.FinalItems,
            };

            var report = new MatchReport
            {
                Id = $"report-{DateTime.Now:yyyyMMdd-HHmmss}-{matchId}",
                GeneratedAt = DateTimeOffset.UtcNow,
                Match = record,
                Api = api,
                AiReview = BuildFallbackReview(record, null),
                Backend = "OpenDota",
            };

            var summary = MatchReportSummary.FromReport(report);
            _reportCache[report.Id] = report;

            RunOnUi(() =>
            {
                AnalysisReports.Insert(0, summary);
                if (AnalysisReports.Count > 200) AnalysisReports.RemoveAt(AnalysisReports.Count - 1);
                ReportCountText = $"{AnalysisReports.Count} 份报告";
                SelectedSummary = summary;
                SelectedReport = report;
                DisplayReport(report);
            });

            _logAction?.Invoke($"已从 OpenDota 拉取比赛 {matchId}，正在生成复盘…");
            var reviewTask = GenerateMatchReviewAsync(report);
            _registerBackgroundTask?.Invoke(reviewTask);
        }
        catch (Exception ex)
        {
            _logAction?.Invoke($"拉取对局发生异常：{ex.Message}");
        }
        finally
        {
            RunOnUi(() => IsFetchingMatch = false);
        }
    }

    [RelayCommand]
    public async Task RegenerateReviewAsync()
    {
        if (SelectedReport is null || IsRegenerating) return;
        IsRegenerating = true;

        try
        {
            await GenerateMatchReviewAsync(SelectedReport).ConfigureAwait(false);
        }
        finally
        {
            RunOnUi(() => IsRegenerating = false);
        }
    }

    [RelayCommand]
    public void CloseDetail()
    {
        SelectedSummary = null;
        SelectedReport = null;
        IsDetailVisible = false;
        IsDetailContentVisible = false;
        IsDetailStateVisible = false;
    }

    public void AddCompletedReport(MatchReport report)
    {
        var summary = MatchReportSummary.FromReport(report);
        _reportCache[report.Id] = report;

        RunOnUi(() =>
        {
            AnalysisReports.Insert(0, summary);
            if (AnalysisReports.Count > 200) AnalysisReports.RemoveAt(AnalysisReports.Count - 1);
            ReportCountText = $"{AnalysisReports.Count} 份报告";
            SelectedSummary = summary;
            SelectedReport = report;
            DisplayReport(report);
        });

        _logAction?.Invoke($"赛后分析：本局（{report.Match.HeroName}，{report.Match.ResultText}）已记录，正在生成复盘…");
        var settings = _settingsProvider?.Invoke();
        if (settings?.SmartCoachEnabled == true)
        {
            var task = GenerateMatchReviewAsync(report);
            _registerBackgroundTask?.Invoke(task);
        }
        else
        {
            MatchSessionTracker.SaveReport(report);
        }
    }

    public async Task GenerateMatchReviewAsync(MatchReport report)
    {
        var record = report.Match;
        var heroName = string.IsNullOrWhiteSpace(record.HeroName)
            ? LocalizedHeroNameById(record.HeroId)
            : record.HeroName;

        var snapshotDir = _snapshotDirectoryProvider?.Invoke() ?? string.Empty;
        var operations = report.Operations ?? await Task.Run(() =>
            new MatchOperationExtractor().Extract(
                snapshotDir, record.MatchId, record.StartedAt, record.EndedAt)).ConfigureAwait(false);

        if (operations is not null)
        {
            operations.HeroName = heroName;
            operations.Won = record.Won;
            report.Operations = operations;
        }

        ApiMatchSummary? api = report.Api;
        if (api is null && record.MatchId > 0 && _apiMatchAnalyzer is not null)
        {
            var steamId = string.IsNullOrWhiteSpace(record.SteamId) ? _habits.SteamId : record.SteamId;
            using var fetchTimeout = CancellationTokenSource.CreateLinkedTokenSource(ActiveToken);
            fetchTimeout.CancelAfter(TimeSpan.FromSeconds(12));
            api = await _apiMatchAnalyzer.FetchAsync(
                record.MatchId,
                steamId,
                record.HeroId,
                ResolveApiHeroInternalName,
                ResolveApiItemName,
                ResolveApiItemById,
                fetchTimeout.Token).ConfigureAwait(false);

            if (api is { HeroId: > 0 })
            {
                EnrichApiNames(api);
                report.Api = api;
            }
        }

        var settings = _settingsProvider?.Invoke();
        var keywords = settings?.AiPromptKeywords;

        var prompt = operations is not null || api is { HeroId: > 0 }
            ? CoachAiPrompts.BuildMatchReviewPrompt(
                heroName,
                record.Won == true,
                record.DurationSeconds,
                operations ?? EmptyOperationsFrom(record, heroName, api),
                record.FinalItems,
                api,
                keywords)
            : BuildStatsOnlyPrompt(heroName, record, keywords);

        string? review = null;
        if (settings is not null)
        {
            try
            {
                switch (settings.BackendKind)
                {
                    case AiBackendKind.Npu when _npuCoach is not null:
                        review = await _npuCoach.ExplainAsync(prompt, ActiveToken, maxTokens: 420).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(review)) report.Backend = "NPU 本地推理";
                        break;
                    case AiBackendKind.Ollama when _cloudCoach is not null:
                        review = await _cloudCoach.GenerateAsync(
                            prompt,
                            "你是Dota 2赛后复盘教练，只依据给定操作记录分析，不编造数据；区分事实与推断；建议必须带游戏时间。",
                            settings.OllamaEndpoint,
                            settings.OllamaModel,
                            "",
                            maxTokens: 700,
                            ActiveToken,
                            "chat").ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(review)) report.Backend = "Ollama 本地推理";
                        break;
                    case AiBackendKind.Cloud when _cloudCoach is not null:
                        review = await _cloudCoach.GenerateAsync(
                            prompt,
                            "你是Dota 2赛后复盘教练，只依据给定操作记录分析，不编造数据；区分事实与推断；建议必须带游戏时间。",
                            settings.CloudEndpoint,
                            settings.CloudModel,
                            settings.CloudApiKey,
                            maxTokens: 700,
                            ActiveToken,
                            settings.CloudApiProtocol).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(review)) report.Backend = "云端 API";
                        break;
                }
            }
            catch
            {
                // 后端不可用时保留规则摘要。
            }
        }

        if (!string.IsNullOrWhiteSpace(review)) report.AiReview = review;
        MatchSessionTracker.SaveReport(report);

        RunOnUi(() =>
        {
            if (AnalysisReports.FirstOrDefault(item => item.Id == report.Id) is { } summary)
            {
                summary.UpdateFrom(report);
            }
            if (SelectedSummary?.Id == report.Id)
            {
                DisplayReport(report);
            }
        });
    }

    private void DisplayReport(MatchReport report)
    {
        var record = report.Match;
        DetailHeroName = record.HeroName;
        DetailResultText = record.ResultText;
        DetailResultWon = record.Won;
        DetailDurationText = record.DurationText;
        DetailDateText = record.DateText;
        DetailKdaText = record.KdaText;
        DetailLastHitsText = record.LastHits.ToString();
        DetailDeniesText = record.Denies.ToString();
        DetailGpmText = record.Gpm.ToString();
        DetailXpmText = record.Xpm.ToString();
        DetailMaxLevelText = $"Lv{record.MaxLevel}";

        DetailFinalItems.Clear();
        foreach (var item in record.FinalItems) DetailFinalItems.Add(item);
        HasFinalItems = DetailFinalItems.Count > 0;

        DetailKeyMoments.Clear();
        foreach (var moment in record.KeyMoments) DetailKeyMoments.Add(moment);
        HasKeyMoments = DetailKeyMoments.Count > 0;

        DetailEconomySamples.Clear();
        foreach (var sample in record.Economy) DetailEconomySamples.Add(sample);
        HasEconomySamples = DetailEconomySamples.Count > 0;

        DetailTitleText = $"{record.HeroName} · {record.ResultText} · {record.DurationText}";
        DetailMetaText = $"{record.DateText}  KDA {record.KdaText}  补刀 {record.LastHits}  GPM {record.Gpm}  XPM {record.Xpm}  最高等级 {record.MaxLevel}";
        DetailBackendText = $"报告生成于 {report.GeneratedText} · 由 {report.Backend} 生成";
        DetailReviewText = string.IsNullOrWhiteSpace(report.AiReview)
            ? "（暂无复盘内容，AI 后端不可用时请稍后查看规则摘要）"
            : report.AiReview;

        DetailItemsText = record.FinalItems.Count > 0 ? string.Join("、", record.FinalItems) : "无";
        DetailMomentsText = record.KeyMoments.Count > 0 ? string.Join("\n", record.KeyMoments) : "本局没有采集到关键节点";
        DetailEconomyText = record.Economy.Count > 0
            ? string.Join("  ", record.Economy.Select(sample => $"{sample.TimeText} GPM{sample.Gpm}/XPM{sample.Xpm}"))
            : "暂无经济采样";

        DetailOperationsText = FormatOperationsForUi(report.Operations);
        HasOperations = report.Operations is not null;

        if (report.Api is { HeroId: > 0 })
        {
            DetailApiText = string.Join("\n", CoachAiPrompts.BuildApiContextText(report.Api));
            HasApiData = true;
        }
        else
        {
            DetailApiText = string.Empty;
            HasApiData = false;
        }

        IsDetailStateVisible = false;
        IsDetailContentVisible = true;
        IsDetailVisible = true;
    }

    private void DisplayState(MatchReportSummary summary, string title, string message)
    {
        DetailStateTitle = title;
        DetailStateMessage = message;
        DetailMetaText = $"{summary.Match.HeroName} · {summary.GeneratedText}";
        DetailBackendText = summary.Backend;

        DetailHeroName = summary.Match.HeroName;
        DetailResultText = summary.Match.ResultText;
        DetailResultWon = summary.Match.Won;
        DetailDurationText = summary.Match.DurationText;
        DetailDateText = summary.Match.DateText;
        DetailKdaText = summary.Match.KdaText;
        DetailLastHitsText = summary.Match.LastHits.ToString();
        DetailDeniesText = summary.Match.Denies.ToString();
        DetailGpmText = summary.Match.Gpm.ToString();
        DetailXpmText = summary.Match.Xpm.ToString();
        DetailMaxLevelText = $"Lv{summary.Match.MaxLevel}";

        DetailFinalItems.Clear();
        HasFinalItems = false;
        DetailKeyMoments.Clear();
        HasKeyMoments = false;
        DetailEconomySamples.Clear();
        HasEconomySamples = false;

        DetailTitleText = title;
        DetailReviewText = message;
        DetailItemsText = "--";
        DetailMomentsText = "--";
        DetailEconomyText = "--";
        DetailOperationsText = string.Empty;
        HasOperations = false;
        DetailApiText = string.Empty;
        HasApiData = false;

        IsDetailContentVisible = false;
        IsDetailStateVisible = true;
        IsDetailVisible = true;
    }

    private void UpdateHabitsSummary()
    {
        HasHabits = _habits.TotalMatches > 0;
        HasReports = AnalysisReports.Count > 0;
        HabitTotalMatchesText = _habits.TotalMatches.ToString();
        HabitWinRateText = _habits.WinRateText;
        HabitIsWinRatePositive = _habits.WinRate >= 50.0;
        HabitAverageKdaText = _habits.AverageKda;
        HabitMostPlayedHeroText = _habits.MostPlayedHero;
        HabitMostBoughtItemText = _habits.MostBoughtItem;
        HabitAverageDurationText = _habits.AverageDuration;

        HabitsSummaryText = _habits.TotalMatches == 0
            ? "暂无历史对局：通过 GSI 打完一局后，这里会自动记录你的游戏习惯并生成赛后分析。"
            : $"共 {_habits.TotalMatches} 场 · 胜率 {_habits.WinRateText} · 常用英雄 {_habits.MostPlayedHero} · 平均 KDA {_habits.AverageKda} · 常见装备 {_habits.MostBoughtItem} · 场均时长 {_habits.AverageDuration}";
    }

    private string LocalizedHeroNameById(int heroId) =>
        _catalogProvider?.Invoke()?.Heroes.FirstOrDefault(h => h.Id == heroId)?.LocalizedName ?? $"英雄{heroId}";

    private string ResolveApiHeroInternalName(int heroId) =>
        _catalogProvider?.Invoke()?.Heroes.FirstOrDefault(hero => hero.Id == heroId)?.Name ?? string.Empty;

    private static string ResolveApiItemName(string keyOrFullName) =>
        CoachRuleEngine.FriendlyItemName(keyOrFullName);

    private string ResolveApiItemById(int itemId)
    {
        var allItems = _allItemsProvider?.Invoke();
        var item = allItems?.FirstOrDefault(card => card.Item.Id == itemId);
        return item is not null ? item.Name : $"道具{itemId}";
    }

    private void EnrichApiNames(ApiMatchSummary api)
    {
        var catalog = _catalogProvider?.Invoke();
        if (catalog is null) return;
        foreach (var player in api.AllPlayers)
        {
            var hero = catalog.Heroes.FirstOrDefault(item => item.Id == player.HeroId);
            if (hero is not null && !string.IsNullOrWhiteSpace(hero.LocalizedName))
            {
                player.HeroName = hero.LocalizedName;
            }
        }
    }

    private static MatchOperationSummary EmptyOperationsFrom(MatchSessionRecord record, string heroName, ApiMatchSummary? api)
    {
        var operations = new MatchOperationSummary
        {
            MatchId = record.MatchId,
            HeroId = record.HeroId,
            HeroName = heroName,
            Won = record.Won,
            DurationSeconds = record.DurationSeconds,
            ScoreText = api?.ScoreText ?? string.Empty,
            Kills = record.Kills,
            Deaths = record.Deaths,
            Assists = record.Assists,
            LastHits = record.LastHits,
            Denies = record.Denies,
            Gpm = record.Gpm,
            Xpm = record.Xpm,
            MaxLevel = record.MaxLevel,
        };
        if (api is not null)
        {
            operations.Phases = BuildApiPhaseStats(api);
            operations.Economy = api.Economy.Select(point => new EconomySample
            {
                GameTime = point.Minute * 60d,
                Gpm = (int)point.Gold,
                Xpm = (int)point.Xp,
            }).ToList();
        }
        return operations;
    }

    private static List<PhaseStat> BuildApiPhaseStats(ApiMatchSummary api)
    {
        var buckets = new List<PhaseStat>();
        var durationMinutes = Math.Max(1, (int)Math.Ceiling(api.DurationSeconds / 60d));
        var labels = new[] { "对线期", "中期", "中后期", "后期" };
        for (var bucket = 0; bucket < 4; bucket++)
        {
            var start = bucket * 10;
            var end = Math.Min((bucket + 1) * 10, durationMinutes);
            if (start >= durationMinutes) break;
            var points = api.Economy.Where(point => point.Minute >= start && point.Minute < end).ToList();
            if (points.Count == 0) continue;
            var first = points[0];
            var last = points[^1];
            buckets.Add(new PhaseStat
            {
                StartSeconds = start * 60d,
                EndSeconds = end * 60d,
                Label = bucket < labels.Length ? labels[bucket] : $"第{bucket + 1}阶段",
                AvgGpm = points.Average(point => point.Gold),
                AvgXpm = points.Average(point => point.Xp),
                LastHitsDelta = Math.Max(0, last.LastHits - (bucket == 0 ? 0 : first.LastHits)),
                LastHitsPerMinute = Math.Max(0, last.LastHits - (bucket == 0 ? 0 : first.LastHits)) / Math.Max(1, end - start),
            });
        }
        return buckets;
    }

    private static string FormatOperationsForUi(MatchOperationSummary? ops)
    {
        if (ops is null) return string.Empty;
        var lines = new List<string>();
        foreach (var death in ops.DeathsDetailed)
        {
            var parts = new List<string>
            {
                $"{death.TimeText} 第{death.DeathNumber}次阵亡 · 阵亡前血量 {death.HealthPercentAtDeath:0}%（窗口内最低{death.MinHealthPercentInWindow:0}%）",
                $"等级{death.Level} · 金钱{death.Gold}",
            };
            if (death.UltReadyAtDeath) parts.Add("大招就绪未使用");
            if (death.ReadyActivesUnused.Count > 0) parts.Add($"{string.Join("、", death.ReadyActivesUnused)}就绪未使用");
            if (death.TpAvailable) parts.Add("TP可用");
            if (death.RecentCasts.Count > 0) parts.Add("20秒内操作：" + string.Join("、", death.RecentCasts));
            parts.Add(death.BuybackAvailable
                ? $"可买活（{death.BuybackCost}）{(death.BuybackUsed ? "已买活" : "未买活")}"
                : "不可买活");
            lines.Add(string.Join(" · ", parts));
        }

        if (ops.Phases.Count > 0)
        {
            lines.Add("\n阶段表现：");
            foreach (var phase in ops.Phases)
            {
                lines.Add($"{phase.Label}（{phase.TimeText}）补刀+{phase.LastHitsDelta}（{phase.LastHitsPerMinute:0.#}/分）· 杀{phase.KillsDelta}/死{phase.DeathsDelta} · 均值 GPM{phase.AvgGpm:0}/XPM{phase.AvgXpm:0}");
            }
        }

        return string.Join("\n", lines);
    }

    private static string BuildFallbackReview(MatchSessionRecord record, MatchOperationSummary? ops)
    {
        var lines = new List<string>
        {
            record.Won switch
            {
                true => "总体评价：本局获胜，团队执行整体到位。",
                false => "总体评价：本局失利，可以从中复盘关键转折。",
                null => "总体评价：本局结束，结果未知。",
            },
            $"关键数据：KDA {record.KdaText}，GPM {record.Gpm}，XPM {record.Xpm}，补刀 {record.LastHits}，反补 {record.Denies}，最高等级 {record.MaxLevel}，时长 {record.DurationText}。",
        };
        if (ops is not null)
        {
            var laning = ops.Phases.FirstOrDefault(phase => phase.Label == "对线期");
            if (laning is not null)
            {
                lines.Add($"对线期（{laning.TimeText}）补刀 {laning.LastHitsDelta}（{laning.LastHitsPerMinute:0.#}/分），" +
                    $"死亡 {laning.DeathsDelta} 次，击杀 {laning.KillsDelta}。对线期死亡过多会直接拖慢经济与等级；" +
                    (laning.LastHitsPerMinute < 5
                        ? "补刀效率偏低，建议在训练模式练正反补节奏，并用补刀练习地图。"
                        : "补刀节奏正常，继续保持并注意边补边看小地图。"));
            }

            foreach (var death in ops.DeathsDetailed)
            {
                var causes = new List<string>();
                if (death.HealthPercentAtDeath <= 30 && death.LastHitsInWindow > 2)
                    causes.Add($"阵亡前血量已低至 {death.HealthPercentAtDeath:0}% 仍在补刀");
                if (death.UltReadyAtDeath)
                    causes.Add("大招就绪未使用");
                if (death.ReadyActivesUnused.Count > 0)
                    causes.Add($"{string.Join("、", death.ReadyActivesUnused)}就绪未使用");
                if (death.TpAvailable)
                    causes.Add("TP 可用未撤退");
                if (causes.Count > 0)
                {
                    lines.Add($"{death.TimeText} 第{death.DeathNumber}次阵亡：{string.Join("；", causes)}。建议：低血量或资源不足时立即后撤/回城，不要贪刀。");
                }
                else
                {
                    lines.Add($"{death.TimeText} 第{death.DeathNumber}次阵亡（阵亡前血量 {death.HealthPercentAtDeath:0}%）。可回看该时间点核对敌方动向。");
                }
            }

            var buybacksMissed = ops.DeathsDetailed.Count(death => death.BuybackAvailable && !death.BuybackUsed);
            if (buybacksMissed > 0)
                lines.Add($"本局有 {buybacksMissed} 次可买活未使用。买活只在高地防守、肉山团或即将结束比赛时使用，平时留着买活更好。");
        }
        if (record.Deaths >= 8)
        {
            lines.Add("改进点：阵亡偏多。优先保证视野与抱团，避免单人带线过河；每次阵亡后记录敌方位置与技能状态。");
        }

        if (record.Won != true && record.Gpm < 420)
        {
            lines.Add("改进点：经济偏低。提升补刀与参战效率，规划更早的推塔与野区资源，而不是被动等待。");
        }

        return string.Join("\n\n", lines);
    }

    private static string BuildStatsOnlyPrompt(string heroName, MatchSessionRecord record, string? customKeywords = null)
    {
        var guidance = string.IsNullOrWhiteSpace(customKeywords) ? "" : $"关注关键词：{customKeywords.Trim()}。\n";
        return "你是Dota 2赛后复盘教练。请基于下面的统计写一份中文复盘报告，控制在250字以内：\n" +
            "1) 总体评价；2) 指出1-2个最值得改进的点并给出可执行建议；3) 提到做得好的地方。只使用下面的事实，不要编造数据。\n" +
            guidance + "\n" +
            $"英雄：{heroName}，结果：{(record.Won == true ? "胜利" : record.Won == false ? "失败" : "未知")}，" +
            $"时长 {record.DurationText}\nKDA {record.KdaText}，补刀 {record.LastHits}，GPM {record.Gpm}，XPM {record.Xpm}，" +
            $"最高等级 {record.MaxLevel}，最终装备：{(record.FinalItems.Count > 0 ? string.Join("、", record.FinalItems) : "无")}";
    }

    public void Cancel()
    {
        lock (_ctsLock)
        {
            _lifetimeCts.Cancel();
        }
        _loadDetailCts?.Cancel();
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcherQueue is not null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => action());
        }
        else
        {
            action();
        }
    }
}
