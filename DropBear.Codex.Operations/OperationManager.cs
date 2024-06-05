// File: OperationManager.cs
// Description: Manages transactions, ensuring all operations are executed successfully or rolled back in case of failure, with added enhancements.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Transactions;
using DropBear.Codex.Core;

namespace DropBear.Codex.Operations;

public class OperationManager
{
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

    public event EventHandler<EventArgs>? OperationStarted;
    public event EventHandler<EventArgs>? OperationCompleted;
    public event EventHandler<OperationFailedEventArgs>? OperationFailed;
    public event EventHandler<EventArgs>? RollbackStarted;

    public void AddOperation<T>(Func<Task<Result<T>>> operation, Func<Task<Result>> rollbackOperation)
    {
        lock (_lock)
        {
            _operations.Add(() => ExecuteOperationAsync(operation));
            _rollbackOperations.Add(() => ExecuteOperationAsync(rollbackOperation));
        }
    }

    public void AddOperation<T>(Func<Task<T>> operation, Func<Task<Result>> rollbackOperation)
    {
        lock (_lock)
        {
            _operations.Add(() => ExecuteOperationAsync(operation));
            _rollbackOperations.Add(() => ExecuteOperationAsync(rollbackOperation));
        }
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
        foreach (var operation in operationsCopy)
        {
            OperationStarted?.Invoke(this, EventArgs.Empty);
            var result = await ExecuteOperationAsync(operation).ConfigureAwait(false);
            if (result is Result { IsSuccess: false } res)
            {
                var exception = new InvalidOperationException(res.ErrorMessage, res.Exception);
                _exceptions.Add(exception);
                OperationFailed?.Invoke(this, new OperationFailedEventArgs(exception));
                break;
            }

            OperationCompleted?.Invoke(this, EventArgs.Empty);
        }

        if (_exceptions.Count != 0)
        {
            RollbackStarted?.Invoke(this, EventArgs.Empty);
            var rollbackResult = await ExecuteRollbacksAsync(rollbackOperationsCopy).ConfigureAwait(false);

            if (!rollbackResult.IsSuccess)
                _exceptions.Add(new InvalidOperationException("Rollback failed", rollbackResult.Exception));

            return Result.Failure(new Collection<Exception>(_exceptions));
        }

        scope.Complete();
        return Result.Success();
    }

    public async Task<Result<T>> ExecuteAsync<T>()
    {
        List<Func<Task<object>>> operationsCopy;
        List<Func<Task<object>>> rollbackOperationsCopy;
        lock (_lock)
        {
            operationsCopy = new List<Func<Task<object>>>(_operations);
            rollbackOperationsCopy = new List<Func<Task<object>>>(_rollbackOperations);
        }

        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        foreach (var operation in operationsCopy)
        {
            OperationStarted?.Invoke(this, EventArgs.Empty);
            var result = await ExecuteOperationAsync(operation).ConfigureAwait(false);
            switch (result)
            {
                case Result { IsSuccess: false } res:
                    var exception = new InvalidOperationException(res.ErrorMessage, res.Exception);
                    _exceptions.Add(exception);
                    OperationFailed?.Invoke(this, new OperationFailedEventArgs(exception));
                    break;
                case Result<T> { IsSuccess: false } typedRes:
                    var typedException = new InvalidOperationException(typedRes.ErrorMessage, typedRes.Exception);
                    _exceptions.Add(typedException);
                    OperationFailed?.Invoke(this, new OperationFailedEventArgs(typedException));
                    break;
                default:
                    OperationCompleted?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        if (_exceptions.Count != 0)
        {
            RollbackStarted?.Invoke(this, EventArgs.Empty);
            var rollbackResult = await ExecuteRollbacksAsync(rollbackOperationsCopy).ConfigureAwait(false);
            return rollbackResult.IsSuccess
                ? Result<T>.Failure(new Collection<Exception>(_exceptions))
                : Result<T>.Failure(rollbackResult.ErrorMessage);
        }

        scope.Complete();
        return Result<T>.Success(default!);
    }

    private async Task<Result> ExecuteRollbacksAsync(List<Func<Task<object>>> rollbackOperations)
    {
        var rollbackExceptions = new List<Exception>();

        foreach (var rollbackOperation in rollbackOperations)
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
        }

        return rollbackExceptions.Count == 0
            ? Result.Success()
            : Result.Failure(new Collection<Exception>(rollbackExceptions));
    }

    private async Task<object> ExecuteOperationAsync<T>(Func<Task<T>> operation, int retryCount = 3, TimeSpan? timeout = null)
    {
        for (var attempt = 0; attempt < retryCount; attempt++)
        {
            try
            {
                Task<T> resultTask;

                if (timeout.HasValue)
                {
                    using var cts = new CancellationTokenSource(timeout.Value);
                    var task = await Task.WhenAny(operation(), Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                    if (task is not Task<T> rt) continue;
                    resultTask = rt;
                }
                else
                {
                    resultTask = operation();
                }

                var result = await resultTask.ConfigureAwait(false);
                return result;
            }
            catch (Exception ex)
            {
                if (attempt == retryCount - 1)
                    return Result<T>.Failure(ex.Message, ex);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt))).ConfigureAwait(false); // Exponential backoff
            }
        }

        return Result<T>.Failure("Operation failed after all retry attempts");
    }

    private async Task<object> ExecuteOperationAsync<T>(Func<Task<Result<T>>> operation, int retryCount = 3, TimeSpan? timeout = null)
    {
        for (var attempt = 0; attempt < retryCount; attempt++)
        {
            try
            {
                Task<Result<T>> resultTask;

                if (timeout.HasValue)
                {
                    using var cts = new CancellationTokenSource(timeout.Value);
                    var task = await Task.WhenAny(operation(), Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                    if (task is not Task<Result<T>> rt) continue;
                    resultTask = rt;
                }
                else
                {
                    resultTask = operation();
                }

                var result = await resultTask.ConfigureAwait(false);
                return result;
            }
            catch (Exception ex)
            {
                if (attempt == retryCount - 1)
                    return Result<T>.Failure(ex.Message, ex);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt))).ConfigureAwait(false); // Exponential backoff
            }
        }

        return Result<T>.Failure("Operation failed after all retry attempts");
    }

    private async Task<object> ExecuteOperationAsync(Func<Task<Result>> operation, int retryCount = 3, TimeSpan? timeout = null)
    {
        for (var attempt = 0; attempt < retryCount; attempt++)
        {
            try
            {
                Task<Result> resultTask;

                if (timeout.HasValue)
                {
                    using var cts = new CancellationTokenSource(timeout.Value);
                    var task = await Task.WhenAny(operation(), Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                    if (task is not Task<Result> rt) continue;
                    resultTask = rt;
                }
                else
                {
                    resultTask = operation();
                }

                var result = await resultTask.ConfigureAwait(false);
                return result;
            }
            catch (Exception ex)
            {
                if (attempt == retryCount - 1)
                    return Result.Failure(ex.Message, ex);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt))).ConfigureAwait(false); // Exponential backoff
            }
        }

        return Result.Failure("Operation failed after all retry attempts");
    }
}
