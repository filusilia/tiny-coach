using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using AegisCoach.Core.Models;

namespace AegisCoach.Core.Services;

/// <summary>
/// GSI 异步顺序处理管线：
/// 单消费者顺序执行对局追踪（MatchSessionTracker）、规则计算（CoachRuleEngine）与实时状态计算，
/// 完全脱离 UI Dispatcher 线程。
/// 对 UI 状态采用“只保留最新值”的合并更新机制，限制 Dispatcher 中待处理更新任务至多存在 1 个；
/// 规则提醒（CoachAdvice）与比赛完成记录单独排队，绝不因为 UI 状态合并而丢失。
/// </summary>
public sealed class GsiProcessingPipeline : IAsyncDisposable
{
    private const int QueueCapacity = 1024;
    private readonly Channel<MatchSnapshot> _channel;
    private readonly SemaphoreSlim _queueSlots = new(QueueCapacity, QueueCapacity);
    private readonly ConcurrentQueue<CoachAdvice> _pendingAdvices = new();
    private readonly ConcurrentQueue<MatchSessionRecord> _pendingFinishedMatches = new();
    private readonly Action<Action>? _uiDispatcher;
    private readonly Action<CoachAdvice>? _onAdvicePublished;
    private readonly Action<MatchSessionRecord>? _onMatchFinished;
    private readonly Action<MatchSnapshot>? _onStatePublished;

    private CoachRuleEngine _ruleEngine;
    private readonly MatchSessionTracker _sessionTracker;
    private readonly object _lifecycleSync = new();

    private CancellationTokenSource? _cts;
    private Task? _consumerLoop;
    private Task? _stopTask;
    private MatchSnapshot? _previousSnapshot;
    private MatchSnapshot? _latestPendingState;

    private int _isUiUpdateScheduled;
    private int _queueDepth;
    private bool _hasStarted;
    private long _enqueuedCount;
    private long _droppedCount;
    private long _processedCount;

    public GsiProcessingPipeline(
        CoachRuleEngine ruleEngine,
        MatchSessionTracker sessionTracker,
        Action<Action>? uiDispatcher = null,
        Action<CoachAdvice>? onAdvicePublished = null,
        Action<MatchSessionRecord>? onMatchFinished = null,
        Action<MatchSnapshot>? onStatePublished = null)
    {
        _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
        _sessionTracker = sessionTracker ?? throw new ArgumentNullException(nameof(sessionTracker));
        _uiDispatcher = uiDispatcher;
        _onAdvicePublished = onAdvicePublished;
        _onMatchFinished = onMatchFinished;
        _onStatePublished = onStatePublished;

        // 有界通道把异常高频输入限制在可控内存内；正常速率下仍由单消费者顺序处理。
        _channel = Channel.CreateBounded<MatchSnapshot>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public long EnqueuedCount => Volatile.Read(ref _enqueuedCount);
    public long DroppedCount => Volatile.Read(ref _droppedCount);
    public long ProcessedCount => Volatile.Read(ref _processedCount);
    public int QueueDepth => Math.Max(0, Volatile.Read(ref _queueDepth));
    public bool IsRunning => _cts is not null && !_cts.IsCancellationRequested;

    public void UpdateRuleEngine(CoachRuleEngine ruleEngine)
    {
        _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
    }

    public void Start()
    {
        lock (_lifecycleSync)
        {
            if (IsRunning || _hasStarted) return;

            _hasStarted = true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _consumerLoop = Task.Run(() => ConsumerLoopAsync(token), token);
        }
    }

    public bool Enqueue(MatchSnapshot snapshot)
    {
        if (snapshot is null || !IsRunning)
        {
            Interlocked.Increment(ref _droppedCount);
            PerformanceMetrics.Instance.RecordGsiSnapshotDropped();
            return false;
        }

        var slotAcquired = false;
        try
        {
            var cts = _cts;
            if (cts is null || cts.IsCancellationRequested)
            {
                throw new OperationCanceledException();
            }

            // 先占用一个容量槽，再写入通道。这样消费者不能在生产者递增计数前抢先递减，
            // 队列深度始终表示“已占用或等待写入”的快照数，峰值不会超过有界容量。
            _queueSlots.Wait(cts.Token);
            slotAcquired = true;
            Interlocked.Increment(ref _queueDepth);

            // 不能用 TryWrite 在有界通道满时静默丢弃已接受快照。
            // GSI 接收回调不在 UI 线程，必要时由接收任务短暂背压，保证规则引擎看到完整的有序输入。
            _channel.Writer.WriteAsync(snapshot, cts.Token).AsTask().GetAwaiter().GetResult();
            Interlocked.Increment(ref _enqueuedCount);
            var queueDepth = Math.Max(0, Volatile.Read(ref _queueDepth));
            PerformanceMetrics.Instance.RecordGsiSnapshotEnqueued(queueDepth);
            return true;
        }
        catch (ChannelClosedException)
        {
            if (slotAcquired) ReleaseQueueSlot();
            Interlocked.Increment(ref _droppedCount);
            PerformanceMetrics.Instance.RecordGsiSnapshotDropped();
            return false;
        }
        catch (OperationCanceledException)
        {
            if (slotAcquired) ReleaseQueueSlot();
            Interlocked.Increment(ref _droppedCount);
            PerformanceMetrics.Instance.RecordGsiSnapshotDropped();
            return false;
        }
    }

    private async Task ConsumerLoopAsync(CancellationToken cancellationToken)
    {
        var reader = _channel.Reader;

        try
        {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var snapshot))
                {
                    var queueDepth = Math.Max(0, Interlocked.Decrement(ref _queueDepth));
                    _queueSlots.Release();
                    PerformanceMetrics.Instance.RecordGsiSnapshotDequeued(queueDepth);
                    cancellationToken.ThrowIfCancellationRequested();

                    var sw = Stopwatch.StartNew();

                    // 1. 顺序执行对局追踪
                    var finishedMatch = _sessionTracker.OnSnapshot(snapshot);

                    // 2. 顺序执行规则计算
                    var advice = _ruleEngine.Evaluate(_previousSnapshot, snapshot);

                    // 3. 实时状态推进
                    _previousSnapshot = snapshot;

                    sw.Stop();
                    var elapsedMs = sw.ElapsedMilliseconds;

                    // 记录规则评估延迟与快照处理指标
                    PerformanceMetrics.Instance.RecordRuleEvaluation(elapsedMs);
                    PerformanceMetrics.Instance.RecordSnapshotProcessed(elapsedMs);
                    Interlocked.Increment(ref _processedCount);

                    // 4. 事件与状态派发排队
                    if (finishedMatch is not null)
                    {
                        _pendingFinishedMatches.Enqueue(finishedMatch);
                    }

                    if (advice is { Count: > 0 })
                    {
                        foreach (var item in advice)
                        {
                            item.Snapshot = snapshot;
                            _pendingAdvices.Enqueue(item);
                        }
                    }

                    // UI 状态“只保留最新值”覆盖
                    Interlocked.Exchange(ref _latestPendingState, snapshot);

                    // 5. 触发 UI 调度
                    ScheduleUiDispatchIfNeeded();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消，允许退出
        }
    }

    private void ScheduleUiDispatchIfNeeded()
    {
        if (_uiDispatcher is null) return;

        // Dispatcher 同时最多存在一个待处理的 UI 更新任务
        if (Interlocked.CompareExchange(ref _isUiUpdateScheduled, 1, 0) == 0)
        {
            PerformanceMetrics.Instance.IncrementUiDispatcherPending();
            try
            {
                _uiDispatcher(ExecuteUiBatch);
            }
            catch
            {
                PerformanceMetrics.Instance.DecrementUiDispatcherPending();
                Interlocked.Exchange(ref _isUiUpdateScheduled, 0);
            }
        }
        else
        {
            // 已有 UI 更新在排队等待，最新状态已在 _latestPendingState 中完成合并
            PerformanceMetrics.Instance.RecordUiStateMerged(1);
        }
    }

    /// <summary>
    /// 在 UI 线程中由 Dispatcher 批量执行，
    /// 完整派发所有提醒事件（一条不漏），并应用最新合并快照。
    /// </summary>
    public void ExecuteUiBatch()
    {
        PerformanceMetrics.Instance.DecrementUiDispatcherPending();
        var sw = Stopwatch.StartNew();

        try
        {
            var iterations = 0;
            while (iterations++ < 8)
            {
                // 先重置调度标志，允许后续快照调度新的任务
                Interlocked.Exchange(ref _isUiUpdateScheduled, 0);

                // 1. 提醒事件单独排队，逐条处理，绝不丢失
                while (_pendingAdvices.TryDequeue(out var advice))
                {
                    _onAdvicePublished?.Invoke(advice);
                }

                // 2. 对局结束事件单独排队，逐条处理，绝不丢失
                while (_pendingFinishedMatches.TryDequeue(out var match))
                {
                    _onMatchFinished?.Invoke(match);
                }

                // 3. UI 状态只发布最新值
                var state = Interlocked.Exchange(ref _latestPendingState, null);
                if (state is not null)
                {
                    _onStatePublished?.Invoke(state);
                }

                // 4. 若在当前批处理期间又有新到达的提醒或状态，且未被重新调度，则在本轮一并处理
                if (_pendingAdvices.IsEmpty && _pendingFinishedMatches.IsEmpty && Volatile.Read(ref _latestPendingState) is null)
                {
                    break;
                }

                if (Interlocked.CompareExchange(ref _isUiUpdateScheduled, 1, 0) != 0)
                {
                    // 已经被其它线程成功调度了新的批次，由下一个 Dispatcher 任务执行
                    break;
                }
            }

            // 若达到上限仍有未消费项目，再次调度下一个 Dispatcher 批次
            if (!_pendingAdvices.IsEmpty || !_pendingFinishedMatches.IsEmpty || Volatile.Read(ref _latestPendingState) is not null)
            {
                ScheduleUiDispatchIfNeeded();
            }
        }
        finally
        {
            sw.Stop();
            PerformanceMetrics.Instance.RecordUiRender(sw.ElapsedMilliseconds);
        }
    }

    private void ReleaseQueueSlot()
    {
        Interlocked.Decrement(ref _queueDepth);
        _queueSlots.Release();
    }

    /// <summary>
    /// 等待管线当前所有已入队快照全部处理完毕（用于基线测试与对局结束冲刷）。
    /// </summary>
    public async Task WaitForIdleAsync(TimeSpan? timeout = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(10);
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < limit)
        {
            if (EnqueuedCount == ProcessedCount && QueueDepth == 0)
            {
                return;
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    public Task StopAsync()
    {
        lock (_lifecycleSync)
        {
            if (_stopTask is not null) return _stopTask;
            if (_cts is null) return Task.CompletedTask;

            _channel.Writer.TryComplete();
            _stopTask = StopCoreAsync(_cts, _consumerLoop);
            return _stopTask;
        }
    }

    private async Task StopCoreAsync(CancellationTokenSource cts, Task? consumerLoop)
    {
        try
        {
            // 先完成 writer，再等待消费者自然排空已接受快照；不会静默丢失尾部状态。
            if (consumerLoop is not null)
            {
                await consumerLoop.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Cancel();
            cts.Dispose();
            lock (_lifecycleSync)
            {
                if (ReferenceEquals(_cts, cts))
                {
                    _cts = null;
                    _consumerLoop = null;
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _queueSlots.Dispose();
    }
}
