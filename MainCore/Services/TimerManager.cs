using System.Collections.Concurrent;

using MainCore.Infrasturecture.Extensions;
using MainCore.Tasks.Base;

using Microsoft.Extensions.DependencyInjection;

using Polly;
using Polly.Retry;

namespace MainCore.Services
{
    [RegisterSingleton<TimerManager>]
    public sealed class TimerManager
    {
        private readonly ConcurrentDictionary<AccountId, CancellationTokenSource> _loops = [];

        private volatile bool _isShutdown = false;

        private readonly TaskManager _taskManager;
        private readonly RxQueue _rxQueue;
        private readonly CustomServiceScopeFactory _serviceScopeFactory;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        private static readonly ResiliencePropertyKey<ContextData> contextDataKey = new(nameof(ContextData));
        private readonly ResiliencePipeline<Result> _pipeline;

        public TimerManager(TaskManager taskManager, CustomServiceScopeFactory serviceScopeFactory, RxQueue rxQueue, IDbContextFactory<AppDbContext> contextFactory)
        {
            _taskManager = taskManager;
            _serviceScopeFactory = serviceScopeFactory;
            _rxQueue = rxQueue;
            _contextFactory = contextFactory;

            static async ValueTask OnRetry(OnRetryArguments<Result> args)
            {
                if (!args.Context.Properties.TryGetValue(contextDataKey, out var contextData)) return;

                var (taskName, browser) = contextData;
                if (contextData.Task is ConquestTask.Task conquest)
                {
                    conquest.Status = "Waiting to retry";
                    conquest.LastError = args.Outcome.Exception?.Message ??
                        string.Join(Environment.NewLine, args.Outcome.Result?.Reasons.Select(reason => reason.Message) ?? []);
                    conquest.Persist?.Invoke();
                }
                var error = args.Outcome;
                if (error.Exception is not null)
                {
                    var exception = error.Exception;
                    browser.Logger.Error(exception, "{Message}", exception.Message);
                }
                if (error.Result is not null)
                {
                    var message = string.Join(Environment.NewLine, error.Result.Reasons.Select(e => e.Message));
                    if (!string.IsNullOrEmpty(message))
                    {
                        browser.Logger.Warning("Task {TaskName} failed", taskName, message);
                        browser.Logger.Warning("{Message}", message);
                    }
                }
                if (contextData.Task is not ConquestTask.Task)
                    await browser.Refresh();
                browser.Logger.Warning("{TaskName} will retry after {RetryDelay} (#{AttemptNumber} times)", taskName, args.RetryDelay, args.AttemptNumber + 1);
            }

            var retryOptions = new RetryStrategyOptions<Result>()
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(30),
                UseJitter = true,
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = args =>
                {
                    if (args.Outcome.Exception is OperationCanceledException ||
                        args.Context.CancellationToken.IsCancellationRequested)
                        return ValueTask.FromResult(false);

                    if (args.Context.Properties.TryGetValue(contextDataKey, out var data) &&
                        data.Task is ConquestTask.Task conquest)
                    {
                        return ValueTask.FromResult(
                            args.AttemptNumber < 1 &&
                            conquest.Attempts < 2 &&
                            !conquest.ConfirmationAttempted &&
                            (args.Outcome.Exception is not null ||
                             (args.Outcome.Result is { IsFailed: true } result &&
                              !result.HasError<Skip>() && !result.HasError<Cancel>())));
                    }

                    return ValueTask.FromResult(args.Outcome.Exception is not null ||
                        args.Outcome.Result?.HasError<Retry>() == true);
                },
                OnRetry = OnRetry
            };

            _pipeline = new ResiliencePipelineBuilder<Result>()
                .AddRetry(retryOptions)
                .Build();
        }

        public async Task Execute(AccountId accountId)
        {
            var taskQueue = _taskManager.GetTaskQueue(accountId);

            BaseTask task;
            CancellationTokenSource cts;
            lock (taskQueue)
            {
                if (taskQueue.Status != StatusEnums.Online) return;
                var next = taskQueue.Tasks.OrderBy(x => x.ExecuteAt).FirstOrDefault();
                if (next is null || next.ExecuteAt > DateTime.Now) return;
                task = next;
                taskQueue.IsExecuting = true;
                cts = task is ConquestTask.Task conquest
                    ? CancellationTokenSource.CreateLinkedTokenSource(conquest.CancelToken)
                    : new CancellationTokenSource();
                taskQueue.CancellationTokenSource = cts;
                task.Stage = StageEnums.Executing;
            }
            _rxQueue.Enqueue(new TasksModified(accountId));

            var cacheExecuteTime = task.ExecuteAt;

            using var scope = _serviceScopeFactory.CreateScope(accountId);

            ///===========================================================///
            var browser = scope.ServiceProvider.GetRequiredService<IChromeBrowser>();
            var logger = browser.Logger;

            var contextData = new ContextData(task.Description, browser) { Task = task };

            ///===========================================================///
            var resilienceContext = ResilienceContextPool.Shared.Get(cts.Token);

            resilienceContext.Properties.Set(contextDataKey, contextData);

            var poliResult = await _pipeline.ExecuteOutcomeAsync(
                async (ctx, state) =>
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    if (state is ConquestTask.Task conquest)
                    {
                        if (conquest.ConfirmationAttempted || conquest.Attempts >= 2)
                            return Outcome.FromResult((Result)Stop.Error.WithError(
                                "Conquest confirmation was already attempted or the two-attempt limit was reached."));
                        if (conquest.SendAt <= DateTime.Now)
                        {
                            conquest.Status = "Expired";
                            return Outcome.FromResult((Result)Skip.Error.WithError("The planned send time has passed."));
                        }
                        conquest.Attempts++;
                        conquest.Status = "Preparing";
                        conquest.Persist?.Invoke();
                    }
                    return Outcome.FromResult(await scope.Execute(state, ctx.CancellationToken));
                },
                resilienceContext,
                task);

            ResilienceContextPool.Shared.Return(resilienceContext);
            ///===========================================================///

            lock (taskQueue)
            {
                task.Stage = StageEnums.Waiting;
            }
            _rxQueue.Enqueue(new TasksModified(accountId));

            taskQueue.IsExecuting = false;

            var wasCanceled = cts.IsCancellationRequested;
            cts.Dispose();
            taskQueue.CancellationTokenSource = null;

            var manuallyCanceledConquest = task is ConquestTask.Task { CancelRequested: true };
            if (task is ConquestTask.Task completed)
            {
                completed.LastError = poliResult.Exception?.Message ??
                    (poliResult.Result is { IsFailed: true } failure
                        ? string.Join(Environment.NewLine, failure.Reasons.Select(reason => reason.Message))
                        : completed.LastError);
                completed.Status = manuallyCanceledConquest ? "Canceled" :
                    completed.ConfirmationAttempted && (poliResult.Exception is not null || poliResult.Result?.IsFailed == true)
                    ? "Check in game" :
                    wasCanceled ? "Paused" :
                    poliResult.Result?.IsSuccess == true ? "Completed" :
                    poliResult.Result?.HasError<Skip>() == true
                        ? completed.Status == "Expired" ? "Expired" : "Skipped" : "Failed";
                completed.IsFinished = !wasCanceled || manuallyCanceledConquest;
                completed.Persist?.Invoke();
            }

            var cancelFailedConquest = task is ConquestTask.Task &&
                poliResult.Exception is not OperationCanceledException &&
                !wasCanceled &&
                (poliResult.Exception is not null || poliResult.Result is { IsFailed: true });

            if (manuallyCanceledConquest)
            {
                logger.Information("Conquest task canceled by user: {TaskName}", task.Description);
                _taskManager.Remove(accountId, task);
                _rxQueue.Enqueue(new TasksModified(accountId));
            }
            else if (cancelFailedConquest)
            {
                if (poliResult.Exception is not null)
                    logger.Error(poliResult.Exception, "Conquest task failed: {Message}", poliResult.Exception.Message);
                if (poliResult.Result is { } failedResult)
                    logger.Warning("Conquest task failed: {Errors}",
                        string.Join(Environment.NewLine, failedResult.Reasons.Select(reason => reason.Message)));
                if (task is ConquestTask.Task { ConfirmationAttempted: true })
                    logger.Warning("Confirmation was attempted. Check the game for the attack outcome; this task will not be sent again.");
                logger.Warning("Canceling {TaskName}; continuing with the next task", task.Description);
                _taskManager.Remove(accountId, task);
                _rxQueue.Enqueue(new TasksModified(accountId));
            }
            else if (poliResult.Exception is not null)
            {
                var ex = poliResult.Exception;

                if (ex is OperationCanceledException)
                {
                    logger.Information("Pause button is pressed");
                }
                else
                {
                    var filename = await browser.Screenshot();
                    logger.Information("Screenshot saved as {FileName}", filename);
                    logger.Warning("There is something wrong. Bot is pausing. Last exception is");
                    logger.Error(ex, "{Message}", ex.Message);
                }

                _taskManager.SetStatus(accountId, StatusEnums.Paused);
            }

            if (!manuallyCanceledConquest && !cancelFailedConquest && poliResult.Result is not null)
            {
                var result = poliResult.Result;
                if (result.IsFailed)
                {
                    if (result.HasError<Stop>() || result.HasError<Retry>())
                    {
                        var filename = await browser.Screenshot();
                        logger.Information(messageTemplate: "Screenshot saved as {FileName}", filename);
                        _taskManager.SetStatus(accountId, StatusEnums.Paused);
                    }
                    else if (result.HasError<Skip>())
                    {
                        if (task.ExecuteAt == cacheExecuteTime)
                        {
                            _taskManager.Remove(accountId, task);
                        }
                        else
                        {
                            logger.Information("Schedule next run at {Time}", task.ExecuteAt.ToString("yyyy-MM-dd HH:mm:ss"));
                        }
                    }
                    else if (result.HasError<Cancel>())
                    {
                        _taskManager.SetStatus(accountId, StatusEnums.Paused);
                    }
                }
                else
                {
                    if (task.ExecuteAt == cacheExecuteTime)
                    {
                        _taskManager.Remove(accountId, task);
                    }
                    else
                    {
                        logger.Information("Schedule next run at {Time}", task.ExecuteAt.ToString("yyyy-MM-dd HH:mm:ss"));
                    }
                }
            }

            using var context = _contextFactory.CreateDbContext();
            var delay = context.ByName(accountId, AccountSettingEnums.TaskDelayMin, AccountSettingEnums.TaskDelayMax);
            await Task.Delay(delay);
        }

        public void Shutdown()
        {
            _isShutdown = true;

            foreach (var accountId in _loops.Keys)
            {
                if (_loops.TryRemove(accountId, out var cancellationTokenSource))
                {
                    cancellationTokenSource.Cancel();
                    cancellationTokenSource.Dispose();
                }
            }
        }

        public void Start(AccountId accountId)
        {
            if (_isShutdown) return;

            var cancellationTokenSource = new CancellationTokenSource();
            if (!_loops.TryAdd(accountId, cancellationTokenSource))
            {
                cancellationTokenSource.Dispose();
                return;
            }

            _ = Task.Run(async () =>
            {
                while (!_isShutdown && !cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Execute(accountId);
                        await Task.Delay(100, cancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException) when (_isShutdown || cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        break;
                    }
                }

                if (_loops.TryRemove(accountId, out var ownedCancellationTokenSource))
                {
                    ownedCancellationTokenSource.Dispose();
                }
            }, cancellationTokenSource.Token);
        }

        public record ContextData(string TaskName, IChromeBrowser Browser)
        {
            public BaseTask? Task { get; init; }
        }
    }
}