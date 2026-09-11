using System.Diagnostics;

namespace AegisCoach.Core.Services;

/// <summary>
/// 性能测试与运行场景枚举。
/// </summary>
public enum PerformanceScenario
{
    Unknown,
    ColdStart,          // 冷启动（无本地缓存或首次运行）
    WarmStart,          // 缓存启动（本地缓存命中，秒级加载）
    GsiContinuousInput  // GSI 连续输入（高频快照连续推送与对局进行中）
}

/// <summary>
/// 性能快照只读模型，便于无锁导出、比对和生成基线报告。
/// </summary>
public sealed class PerformanceSnapshot
{
    public PerformanceScenario Scenario { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    // 启动相关耗时（毫秒）
    public long WindowConstructedMs { get; init; }
    public long WindowLoadedMs { get; init; }
    public long SettingsLoadedMs { get; init; }
    public long HeroesAvailableMs { get; init; }
    public long FirstScreenInteractiveMs { get; init; }
    public long ItemsAvailableMs { get; init; }

    // GSI 快照相关
    public long SnapshotsReceivedCount { get; init; }
    public long GsiSnapshotEnqueuedCount { get; init; }
    public long GsiSnapshotDroppedCount { get; init; }
    public long GsiQueueDepth { get; init; }
    public long GsiQueuePeakDepth { get; init; }
    public long SnapshotsProcessedCount { get; init; }
    public long TotalSnapshotProcessingTimeMs { get; init; }
    public long MaxSnapshotProcessingTimeMs { get; init; }
    public long MinSnapshotProcessingTimeMs { get; init; }
    public long LastSnapshotProcessingTimeMs { get; init; }
    public double AverageSnapshotProcessingTimeMs =>
        SnapshotsProcessedCount > 0 ? (double)TotalSnapshotProcessingTimeMs / SnapshotsProcessedCount : 0;

    // 规则处理延迟（毫秒）
    public long RuleEvaluationCount { get; init; }
    public long RuleEvaluationTotalTimeMs { get; init; }
    public long RuleEvaluationMaxTimeMs { get; init; }
    public long RuleEvaluationMinTimeMs { get; init; }
    public long LastRuleEvaluationTimeMs { get; init; }
    public double AverageRuleEvaluationTimeMs =>
        RuleEvaluationCount > 0 ? (double)RuleEvaluationTotalTimeMs / RuleEvaluationCount : 0;

    // UI 调度与合并
    public long UiDispatcherPendingCount { get; init; }
    public long UiDispatcherPeakPendingCount { get; init; }
    public long UiStateMergedCount { get; init; }

    // UI 展示延迟（毫秒）
    public long UiRenderCount { get; init; }
    public long UiRenderTotalTimeMs { get; init; }
    public long UiRenderMaxTimeMs { get; init; }
    public long UiRenderMinTimeMs { get; init; }
    public long LastUiRenderTimeMs { get; init; }
    public double AverageUiRenderTimeMs =>
        UiRenderCount > 0 ? (double)UiRenderTotalTimeMs / UiRenderCount : 0;

    // 推荐与克制分析
    public long ItemRecommendationRunCount { get; init; }
    public long ItemRecommendationTotalTimeMs { get; init; }
    public long CounterAnalysisRunCount { get; init; }
    public long CounterAnalysisTotalTimeMs { get; init; }

    // 图片预取
    public long ImagePrefetchBatchCount { get; init; }
    public long ImagePrefetchRequestedCount { get; init; }
    public long ImagePrefetchTotalTimeMs { get; init; }
}

/// <summary>
/// 核心性能指标度量与基线记录服务。
/// 线程安全设计，无 WinUI 依赖，同时适用于桌面端与 Smoke 测试命令行。
/// 验收条件保证：输出与报告严格不包含 Token、API Key、Steam ID 和原始快照。
/// </summary>
public sealed class PerformanceMetrics
{
    private static readonly Lazy<PerformanceMetrics> LazyInstance = new(() => new PerformanceMetrics());
    public static PerformanceMetrics Instance => LazyInstance.Value;

    private readonly object _syncRoot = new();

    private PerformanceScenario _scenario = PerformanceScenario.Unknown;

    // 启动耗时
    private long _windowConstructedMs;
    private long _windowLoadedMs;
    private long _settingsLoadedMs;
    private long _heroesAvailableMs;
    private long _firstScreenInteractiveMs;
    private long _itemsAvailableMs;

    // GSI 快照指标
    private long _snapshotsReceivedCount;
    private long _gsiSnapshotEnqueuedCount;
    private long _gsiSnapshotDroppedCount;
    private long _gsiQueueDepth;
    private long _gsiQueuePeakDepth;
    private long _snapshotsProcessedCount;
    private long _totalSnapshotProcessingTimeMs;
    private long _maxSnapshotProcessingTimeMs;
    private long _minSnapshotProcessingTimeMs = long.MaxValue;
    private long _lastSnapshotProcessingTimeMs;

    // 规则处理延迟指标
    private long _ruleEvaluationCount;
    private long _ruleEvaluationTotalTimeMs;
    private long _ruleEvaluationMaxTimeMs;
    private long _ruleEvaluationMinTimeMs = long.MaxValue;
    private long _lastRuleEvaluationTimeMs;

    // UI Dispatcher 与合并指标
    private long _uiDispatcherPendingCount;
    private long _uiDispatcherPeakPendingCount;
    private long _uiStateMergedCount;

    // UI 展示延迟指标
    private long _uiRenderCount;
    private long _uiRenderTotalTimeMs;
    private long _uiRenderMaxTimeMs;
    private long _uiRenderMinTimeMs = long.MaxValue;
    private long _lastUiRenderTimeMs;

    // 推荐与克制分析指标
    private long _itemRecommendationRunCount;
    private long _itemRecommendationTotalTimeMs;
    private long _counterAnalysisRunCount;
    private long _counterAnalysisTotalTimeMs;

    // 图片预取指标
    private long _imagePrefetchBatchCount;
    private long _imagePrefetchRequestedCount;
    private long _imagePrefetchTotalTimeMs;

    public PerformanceScenario CurrentScenario
    {
        get
        {
            lock (_syncRoot) return _scenario;
        }
    }

    public void SetScenario(PerformanceScenario scenario)
    {
        lock (_syncRoot)
        {
            _scenario = scenario;
        }
    }

    public void RecordWindowConstructed(long elapsedMs)
    {
        Interlocked.Exchange(ref _windowConstructedMs, elapsedMs);
    }

    public void RecordWindowLoaded(long elapsedMs)
    {
        Interlocked.Exchange(ref _windowLoadedMs, elapsedMs);
    }

    public void RecordSettingsLoaded(long elapsedMs)
    {
        Interlocked.Exchange(ref _settingsLoadedMs, elapsedMs);
    }

    public void RecordHeroesAvailable(long elapsedMs)
    {
        Interlocked.Exchange(ref _heroesAvailableMs, elapsedMs);
    }

    public void RecordFirstScreenInteractive(long elapsedMs)
    {
        Interlocked.Exchange(ref _firstScreenInteractiveMs, elapsedMs);
    }

    public void RecordItemsAvailable(long elapsedMs)
    {
        Interlocked.Exchange(ref _itemsAvailableMs, elapsedMs);
    }

    public void RecordSnapshotReceived()
    {
        Interlocked.Increment(ref _snapshotsReceivedCount);
    }

    public void RecordGsiSnapshotEnqueued(long queueDepth)
    {
        Interlocked.Increment(ref _gsiSnapshotEnqueuedCount);
        Interlocked.Exchange(ref _gsiQueueDepth, Math.Max(0, queueDepth));
        lock (_syncRoot)
        {
            if (queueDepth > _gsiQueuePeakDepth)
            {
                _gsiQueuePeakDepth = queueDepth;
            }
        }
    }

    public void RecordGsiSnapshotDequeued(long queueDepth)
    {
        Interlocked.Exchange(ref _gsiQueueDepth, Math.Max(0, queueDepth));
    }

    public void RecordGsiSnapshotDropped()
    {
        Interlocked.Increment(ref _gsiSnapshotDroppedCount);
    }

    public void RecordSnapshotProcessed(long elapsedMs)
    {
        Interlocked.Increment(ref _snapshotsProcessedCount);
        Interlocked.Add(ref _totalSnapshotProcessingTimeMs, elapsedMs);
        Interlocked.Exchange(ref _lastSnapshotProcessingTimeMs, elapsedMs);

        lock (_syncRoot)
        {
            if (elapsedMs > _maxSnapshotProcessingTimeMs)
            {
                _maxSnapshotProcessingTimeMs = elapsedMs;
            }

            if (elapsedMs < _minSnapshotProcessingTimeMs)
            {
                _minSnapshotProcessingTimeMs = elapsedMs;
            }
        }
    }

    public void RecordRuleEvaluation(long elapsedMs)
    {
        Interlocked.Increment(ref _ruleEvaluationCount);
        Interlocked.Add(ref _ruleEvaluationTotalTimeMs, elapsedMs);
        Interlocked.Exchange(ref _lastRuleEvaluationTimeMs, elapsedMs);

        lock (_syncRoot)
        {
            if (elapsedMs > _ruleEvaluationMaxTimeMs)
            {
                _ruleEvaluationMaxTimeMs = elapsedMs;
            }

            if (elapsedMs < _ruleEvaluationMinTimeMs)
            {
                _ruleEvaluationMinTimeMs = elapsedMs;
            }
        }
    }

    public void IncrementUiDispatcherPending()
    {
        var current = Interlocked.Increment(ref _uiDispatcherPendingCount);
        lock (_syncRoot)
        {
            if (current > _uiDispatcherPeakPendingCount)
            {
                _uiDispatcherPeakPendingCount = current;
            }
        }
    }

    public void DecrementUiDispatcherPending()
    {
        Interlocked.Decrement(ref _uiDispatcherPendingCount);
    }

    public void RecordUiStateMerged(int count = 1)
    {
        if (count > 0)
        {
            Interlocked.Add(ref _uiStateMergedCount, count);
        }
    }

    public void RecordUiRender(long elapsedMs)
    {
        Interlocked.Increment(ref _uiRenderCount);
        Interlocked.Add(ref _uiRenderTotalTimeMs, elapsedMs);
        Interlocked.Exchange(ref _lastUiRenderTimeMs, elapsedMs);

        lock (_syncRoot)
        {
            if (elapsedMs > _uiRenderMaxTimeMs)
            {
                _uiRenderMaxTimeMs = elapsedMs;
            }

            if (elapsedMs < _uiRenderMinTimeMs)
            {
                _uiRenderMinTimeMs = elapsedMs;
            }
        }
    }

    public void RecordItemRecommendation(long elapsedMs)
    {
        Interlocked.Increment(ref _itemRecommendationRunCount);
        Interlocked.Add(ref _itemRecommendationTotalTimeMs, elapsedMs);
    }

    public void RecordCounterAnalysis(long elapsedMs)
    {
        Interlocked.Increment(ref _counterAnalysisRunCount);
        Interlocked.Add(ref _counterAnalysisTotalTimeMs, elapsedMs);
    }

    public void RecordImagePrefetch(int requestedCount, long elapsedMs)
    {
        Interlocked.Increment(ref _imagePrefetchBatchCount);
        Interlocked.Add(ref _imagePrefetchRequestedCount, requestedCount);
        Interlocked.Add(ref _imagePrefetchTotalTimeMs, elapsedMs);
    }

    public PerformanceSnapshot CaptureSnapshot()
    {
        lock (_syncRoot)
        {
            return new PerformanceSnapshot
            {
                Scenario = _scenario,
                WindowConstructedMs = Volatile.Read(ref _windowConstructedMs),
                WindowLoadedMs = Volatile.Read(ref _windowLoadedMs),
                SettingsLoadedMs = Volatile.Read(ref _settingsLoadedMs),
                HeroesAvailableMs = Volatile.Read(ref _heroesAvailableMs),
                FirstScreenInteractiveMs = Volatile.Read(ref _firstScreenInteractiveMs),
                ItemsAvailableMs = Volatile.Read(ref _itemsAvailableMs),
                SnapshotsReceivedCount = Volatile.Read(ref _snapshotsReceivedCount),
                GsiSnapshotEnqueuedCount = Volatile.Read(ref _gsiSnapshotEnqueuedCount),
                GsiSnapshotDroppedCount = Volatile.Read(ref _gsiSnapshotDroppedCount),
                GsiQueueDepth = Volatile.Read(ref _gsiQueueDepth),
                GsiQueuePeakDepth = _gsiQueuePeakDepth,
                SnapshotsProcessedCount = Volatile.Read(ref _snapshotsProcessedCount),
                TotalSnapshotProcessingTimeMs = Volatile.Read(ref _totalSnapshotProcessingTimeMs),
                MaxSnapshotProcessingTimeMs = _maxSnapshotProcessingTimeMs,
                MinSnapshotProcessingTimeMs = _minSnapshotProcessingTimeMs == long.MaxValue ? 0 : _minSnapshotProcessingTimeMs,
                LastSnapshotProcessingTimeMs = Volatile.Read(ref _lastSnapshotProcessingTimeMs),
                RuleEvaluationCount = Volatile.Read(ref _ruleEvaluationCount),
                RuleEvaluationTotalTimeMs = Volatile.Read(ref _ruleEvaluationTotalTimeMs),
                RuleEvaluationMaxTimeMs = _ruleEvaluationMaxTimeMs,
                RuleEvaluationMinTimeMs = _ruleEvaluationMinTimeMs == long.MaxValue ? 0 : _ruleEvaluationMinTimeMs,
                LastRuleEvaluationTimeMs = Volatile.Read(ref _lastRuleEvaluationTimeMs),
                UiDispatcherPendingCount = Volatile.Read(ref _uiDispatcherPendingCount),
                UiDispatcherPeakPendingCount = _uiDispatcherPeakPendingCount,
                UiStateMergedCount = Volatile.Read(ref _uiStateMergedCount),
                UiRenderCount = Volatile.Read(ref _uiRenderCount),
                UiRenderTotalTimeMs = Volatile.Read(ref _uiRenderTotalTimeMs),
                UiRenderMaxTimeMs = _uiRenderMaxTimeMs,
                UiRenderMinTimeMs = _uiRenderMinTimeMs == long.MaxValue ? 0 : _uiRenderMinTimeMs,
                LastUiRenderTimeMs = Volatile.Read(ref _lastUiRenderTimeMs),
                ItemRecommendationRunCount = Volatile.Read(ref _itemRecommendationRunCount),
                ItemRecommendationTotalTimeMs = Volatile.Read(ref _itemRecommendationTotalTimeMs),
                CounterAnalysisRunCount = Volatile.Read(ref _counterAnalysisRunCount),
                CounterAnalysisTotalTimeMs = Volatile.Read(ref _counterAnalysisTotalTimeMs),
                ImagePrefetchBatchCount = Volatile.Read(ref _imagePrefetchBatchCount),
                ImagePrefetchRequestedCount = Volatile.Read(ref _imagePrefetchRequestedCount),
                ImagePrefetchTotalTimeMs = Volatile.Read(ref _imagePrefetchTotalTimeMs),
            };
        }
    }

    public void Reset()
    {
        lock (_syncRoot)
        {
            _scenario = PerformanceScenario.Unknown;
            _windowConstructedMs = 0;
            _windowLoadedMs = 0;
            _settingsLoadedMs = 0;
            _heroesAvailableMs = 0;
            _firstScreenInteractiveMs = 0;
            _itemsAvailableMs = 0;
            _snapshotsReceivedCount = 0;
            _gsiSnapshotEnqueuedCount = 0;
            _gsiSnapshotDroppedCount = 0;
            _gsiQueueDepth = 0;
            _gsiQueuePeakDepth = 0;
            _snapshotsProcessedCount = 0;
            _totalSnapshotProcessingTimeMs = 0;
            _maxSnapshotProcessingTimeMs = 0;
            _minSnapshotProcessingTimeMs = long.MaxValue;
            _lastSnapshotProcessingTimeMs = 0;
            _ruleEvaluationCount = 0;
            _ruleEvaluationTotalTimeMs = 0;
            _ruleEvaluationMaxTimeMs = 0;
            _ruleEvaluationMinTimeMs = long.MaxValue;
            _lastRuleEvaluationTimeMs = 0;
            _uiDispatcherPendingCount = 0;
            _uiDispatcherPeakPendingCount = 0;
            _uiStateMergedCount = 0;
            _uiRenderCount = 0;
            _uiRenderTotalTimeMs = 0;
            _uiRenderMaxTimeMs = 0;
            _uiRenderMinTimeMs = long.MaxValue;
            _lastUiRenderTimeMs = 0;
            _itemRecommendationRunCount = 0;
            _itemRecommendationTotalTimeMs = 0;
            _counterAnalysisRunCount = 0;
            _counterAnalysisTotalTimeMs = 0;
            _imagePrefetchBatchCount = 0;
            _imagePrefetchRequestedCount = 0;
            _imagePrefetchTotalTimeMs = 0;
        }
    }

    /// <summary>
    /// 生成格式化性能基线报告文本。
    /// 严禁输出任何 Token、API Key、完整 Steam ID 和原始快照。
    /// </summary>
    public string GenerateReport(string? title = null)
    {
        var s = CaptureSnapshot();
        var scenarioName = s.Scenario switch
        {
            PerformanceScenario.ColdStart => "冷启动 (Cold Start)",
            PerformanceScenario.WarmStart => "缓存启动 (Warm/Cached Start)",
            PerformanceScenario.GsiContinuousInput => "GSI 连续输入 (Continuous Input)",
            _ => "未指定场景 (General)"
        };

        var header = title ?? $"神盾教练性能基线报告 - {scenarioName}";
        var avgGsiMs = s.AverageSnapshotProcessingTimeMs;
        var avgRuleMs = s.AverageRuleEvaluationTimeMs;
        var avgUiMs = s.AverageUiRenderTimeMs;

        return $"""
            ================================================================================
            【{header}】
            生成时间: {s.Timestamp:yyyy-MM-dd HH:mm:ss} UTC
            当前场景: {scenarioName}
            --------------------------------------------------------------------------------
            [启动与首屏指标]
              - 窗口启动构造耗时:       {s.WindowConstructedMs} ms
              - 窗口 Loaded 耗时:         {s.WindowLoadedMs} ms
              - 设置加载完成时间:       {s.SettingsLoadedMs} ms
              - 英雄目录可用时间:       {s.HeroesAvailableMs} ms
              - 首屏可交互时间:         {s.FirstScreenInteractiveMs} ms
              - 物品目录可用时间:       {s.ItemsAvailableMs} ms

            [GSI 实时状态与处理指标]
              - 快照接收总数:           {s.SnapshotsReceivedCount}
              - 快照入队总数:           {s.GsiSnapshotEnqueuedCount}
              - 快照丢弃总数:           {s.GsiSnapshotDroppedCount}
              - 当前管线积压:           {s.GsiQueueDepth}
              - 管线积压峰值:           {s.GsiQueuePeakDepth}
              - 快照处理完成数:         {s.SnapshotsProcessedCount}
              - 快照平均处理耗时:       {avgGsiMs:F2} ms
              - 快照最大处理耗时:       {s.MaxSnapshotProcessingTimeMs} ms
              - 快照最小处理耗时:       {s.MinSnapshotProcessingTimeMs} ms
              - 最近一次处理耗时:       {s.LastSnapshotProcessingTimeMs} ms

            [规则处理延迟指标]
              - 规则计算执行次数:       {s.RuleEvaluationCount}
              - 规则计算平均延迟:       {avgRuleMs:F2} ms
              - 规则计算最大延迟:       {s.RuleEvaluationMaxTimeMs} ms
              - 规则计算最小延迟:       {s.RuleEvaluationMinTimeMs} ms
              - 最近一次规则延迟:       {s.LastRuleEvaluationTimeMs} ms

            [UI Dispatcher 队列与状态合并]
              - 当前待处理任务数:       {s.UiDispatcherPendingCount}
              - 待处理任务数峰值:       {s.UiDispatcherPeakPendingCount}
              - UI 状态合并数量:        {s.UiStateMergedCount} (阶段一基线值)

            [UI 展示延迟指标]
              - UI 展示执行次数:        {s.UiRenderCount}
              - UI 展示平均延迟:        {avgUiMs:F2} ms
              - UI 展示最大延迟:        {s.UiRenderMaxTimeMs} ms
              - UI 展示最小延迟:        {s.UiRenderMinTimeMs} ms
              - 最近一次展示延迟:       {s.LastUiRenderTimeMs} ms

            [分析与算法执行统计]
              - 出装推荐执行次数:       {s.ItemRecommendationRunCount}
              - 出装推荐累计耗时:       {s.ItemRecommendationTotalTimeMs} ms
              - 克制分析执行次数:       {s.CounterAnalysisRunCount}
              - 克制分析累计耗时:       {s.CounterAnalysisTotalTimeMs} ms

            [图片与静态资源缓存]
              - 图片预取批次数:         {s.ImagePrefetchBatchCount}
              - 预取图片总数量:         {s.ImagePrefetchRequestedCount}
              - 图片预取累计耗时:       {s.ImagePrefetchTotalTimeMs} ms
            ================================================================================
            """;
    }

    /// <summary>
    /// 将基线报告安全落盘写入文档日志目录，不阻断主流程。
    /// </summary>
    public void LogToFile(string? customPath = null, string? title = null)
    {
        try
        {
            var logPath = customPath ?? Path.Combine(AegisCoachPaths.Root, "performance-baseline.log");
            var directory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var report = GenerateReport(title);
            File.AppendAllText(logPath, report + Environment.NewLine + Environment.NewLine);
        }
        catch
        {
            // 日志失败不能影响应用运行。
        }
    }
}
