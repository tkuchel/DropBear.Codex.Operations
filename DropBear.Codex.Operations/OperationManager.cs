// File: OperationManager.cs
// Description: Manages transactions, ensuring all operations are executed successfully or rolled back in case of failure, with added enhancements.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Transactions;
using DropBear.Codex.Core;

namespace DropBear.Codex.Operations;

public class OperationManager : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<Exception> _exceptions = new();
    private readonly object _lock = new();
    private readonly List<Func<Task<object>>> _operations = new();
    private readonly List<Func<Task<object>>> _rollbackOperations = new();

    public IReadOnlyList<Func<Task<object>>> Operations
    {
        [DebuggerStepThrough]
        get
        {
            lock (_lock)
            {
                return _operations.AsReadOnly();
            }
        }
    }

    public IReadOnlyList<Func<Task<object>>> RollbackOperations
    {
        [DebuggerStepThrough]
        get
        {
            lock (_lock)
            {
                return _rollbackOperations.AsReadOnly();
            }
        }
    }

    public void Dispose() => _cancellationTokenSource.Dispose();

    public event EventHandler<EventArgs>? OperationStarted;
    public event EventHandler<EventArgs>? OperationCompleted;
    public event EventHandler<OperationFailedEventArgs>? OperationFailed;
    public event EventHandler<EventArgs>? RollbackStarted;
    public event EventHandler<ProgressEventArgs>? ProgressChanged;
    public event EventHandler<LogEventArgs>? Log;

    public void AddOperation<T>(Func<Task<Result<T>>> operation, Func<Task<Result>> rollbackOperation)
    {
        if (operation is null || rollbackOperation is null)
            throw new ArgumentNullException(nameof(operation), "Operations cannot be null");

        lock (_lock)
        {
            _operations.Add(() => ExecuteOperationAsync(operation));
            _rollbackOperations.Add(() => ExecuteOperationAsync(rollbackOperation));
        }

        LogMessage("Operation and rollback operation added.");
    }

    public void AddOperation<T>(Func<Task<T>> operation, Func<Task<Result>> rollbackOperation)
    {
        if (operation is null || rollbackOperation is null)
            throw new ArgumentNullException(nameof(operation), "Operations cannot be null");

        lock (_lock)
        {
            _operations.Add(() => ExecuteOperationAsync(operation));
            _rollbackOperations.Add(() => ExecuteOperationAsync(rollbackOperation));
        }

        LogMessage("Operation and rollback operation added.");
    }

    public async Task<Result> ExecuteAsync()
    {
        List<Func<Task<object>>> operationsCopy;
        List<Func<Task<object>>> rollbackOperationsCopy;
        lock (_lock)
        {
            operationsCopy = new List<Func<Task<object>>>(_operations);
            rollbackOperationsCopy = new List<Func<Task<object>>>(_rollbackOperations);
        }

        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        var totalOperations = operationsCopy.Count;
        var completedOperations = 0;

        LogMessage($"Starting execution of {totalOperations} operations.");

        try
        {
            foreach (var operation in operationsCopy)
            {
                _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                OperationStarted?.Invoke(this, EventArgs.Empty);
                LogMessage("Operation started.");
                var result = await ExecuteOperationAsync(operation).ConfigureAwait(false);
                if (result is Result { IsSuccess: false } res)
                {
                    var exception = new InvalidOperationException(res.ErrorMessage, res.Exception);
                    _exceptions.Add(exception);
                    OperationFailed?.Invoke(this, new OperationFailedEventArgs(exception));
                    LogMessage($"Operation failed: {res.ErrorMessage}");
                    break;
                }

                OperationCompleted?.Invoke(this, EventArgs.Empty);
                LogMessage("Operation completed.");
                completedOperations++;
                ReportProgress(completedOperations, totalOperations);
            }
        }
        catch (OperationCanceledException)
        {
            LogMessage("Operation was canceled.");
            return Result.Failure(new Collection<Exception>(_exceptions));
        }
        catch (Exception ex)
        {
            _exceptions.Add(ex);
            return Result.Failure(new Collection<Exception>(_exceptions));
        }

        if (_exceptions.Count != 0)
        {
            RollbackStarted?.Invoke(this, EventArgs.Empty);
            LogMessage("Starting rollback due to failures.");
            var rollbackResult = await ExecuteRollbacksAsync(rollbackOperationsCopy).ConfigureAwait(false);

            if (!rollbackResult.IsSuccess)
                _exceptions.Add(new InvalidOperationException("Rollback failed", rollbackResult.Exception));

            return Result.Failure(new Collection<Exception>(_exceptions));
        }

        scope.Complete();
        LogMessage("All operations completed successfully.");
        return Result.Success();
    }

    public async Task<Result<List<T>>> ExecuteWithResultsAsync<T>()
    {
        List<Func<Task<object>>> operationsCopy;
        List<Func<Task<object>>> rollbackOperationsCopy;
        lock (_lock)
        {
            operationsCopy = new List<Func<Task<object>>>(_operations);
            rollbackOperationsCopy = new List<Func<Task<object>>>(_rollbackOperations);
        }

        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        var totalOperations = operationsCopy.Count;
        var completedOperations = 0;
        var results = new List<T>();

        LogMessage($"Starting execution of {totalOperations} operations.");

        try
        {
            foreach (var operation in operationsCopy)
            {
                _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                OperationStarted?.Invoke(this, EventArgs.Empty);
                LogMessage("Operation started.");
                var result = await ExecuteOperationAsync(operation).ConfigureAwait(false);
                if (result is Result { IsSuccess: false } res)
                {
                    var exception = new InvalidOperationException(res.ErrorMessage, res.Exception);
                    _exceptions.Add(exception);
                    OperationFailed?.Invoke(this, new OperationFailedEventArgs(exception));
                    LogMessage($"Operation failed: {res.ErrorMessage}");
                    break;
                }

                if (result is Result<T> typedResult && typedResult.IsSuccess)
                {
                    results.Add(typedResult.Value);
                }
                else
                {
                    var exception = new InvalidOperationException("Unexpected operation result type.");
                    _exceptions.Add(exception);
                    OperationFailed?.Invoke(this, new OperationFailedEventArgs(exception));
                    LogMessage("Unexpected operation result type.");
                    break;
                }

                OperationCompleted?.Invoke(this, EventArgs.Empty);
                LogMessage("Operation completed.");
                completedOperations++;
                ReportProgress(completedOperations, totalOperations);
            }
        }
        catch (OperationCanceledException)
        {
            LogMessage("Operation was canceled.");
            return Result<List<T>>.Failure(new Collection<Exception>(_exceptions));
        }
        catch (Exception ex)
        {
            _exceptions.Add(ex);
            return Result<List<T>>.Failure(new Collection<Exception>(_exceptions));
        }

        if (_exceptions.Count != 0)
        {
            RollbackStarted?.Invoke(this, EventArgs.Empty);
            LogMessage("Starting rollback due to failures.");
            var rollbackResult = await ExecuteRollbacksAsync(rollbackOperationsCopy).ConfigureAwait(false);
            return rollbackResult.IsSuccess
                ? Result<List<T>>.Failure(new Collection<Exception>(_exceptions))
                : Result<List<T>>.Failure(rollbackResult.ErrorMessage);
        }

        scope.Complete();
        LogMessage("All operations completed successfully.");
        return Result<List<T>>.Success(results);
    }

    private static async Task<Result> ExecuteRollbacksAsync(List<Func<Task<object>>> rollbackOperations)
    {
        var rollbackExceptions = new List<Exception>();

        var rollbackTasks = rollbackOperations.Select(async rollbackOperation =>
        {
            var result = await ExecuteOperationAsync(rollbackOperation).ConfigureAwait(false);
            if (result is Result { IsSuccess: false } res)
            {
                rollbackExceptions.Add(new InvalidOperationException(res.ErrorMessage, res.Exception));
                Console.WriteLine($"Rollback failed: {res.ErrorMessage}");
            }
            else
            {
                Console.WriteLine("Rollback succeeded");
            }
        });

        await Task.WhenAll(rollbackTasks).ConfigureAwait(false);

        return rollbackExceptions.Count == 0
            ? Result.Success()
            : Result.Failure(new Collection<Exception>(rollbackExceptions));
    }

    private static async Task<object> ExecuteOperationAsync<T>(Func<Task<T>> operation, int retryCount = 3,
        TimeSpan? timeout = null)
    {
        for (var attempt = 0; attempt < retryCount; attempt++)
            try
            {
                var resultTask = timeout.HasValue
                    ? await Task.WhenAny(operation(), Task.Delay(timeout.Value)).ConfigureAwait(false) as Task<T>
                    : operation();
                if (resultTask is not null) return (await resultTask.ConfigureAwait(false))!;
            }
            catch (Exception ex)
            {
                if (attempt == retryCount - 1)
                    return Result<T>.Failure(ex.Message, ex);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt))).ConfigureAwait(false);
            }

        return Result<T>.Failure("Operation failed after all retry attempts");
    }

    private void ReportProgress(int completedOperations, int totalOperations)
    {
        var progressPercentage = (int)(completedOperations / (double)totalOperations * 100);
        ProgressChanged?.Invoke(this,
            new ProgressEventArgs(progressPercentage,
                $"Completed {completedOperations} of {totalOperations} operations."));
        LogMessage(
            $"Progress: {progressPercentage}% - Completed {completedOperations} of {totalOperations} operations.");
    }

    private void LogMessage(string message) => Log?.Invoke(this, new LogEventArgs(message));

    public void Cancel() => _cancellationTokenSource.Cancel();
}
