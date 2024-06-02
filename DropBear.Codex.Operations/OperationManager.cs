// File: OperationManager.cs
// Description: Manages transactions, ensuring all operations are executed successfully or rolled back in case of failure, with added enhancements.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Transactions;
using DropBear.Codex.Core;

namespace DropBear.Codex.Operations;

/// <summary>
///     Manages transactions, ensuring that all operations are executed successfully,
///     or any changes are rolled back in case of failure.
/// </summary>
public class OperationManager
{
    private readonly List<Exception> _exceptions = new();
    private readonly object _lock = new();
    private readonly List<Func<Task<object>>> _operations = new();
    private readonly List<Func<Task<object>>> _rollbackOperations = new();

    /// <summary>
    ///     Gets the list of operations.
    /// </summary>
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

    /// <summary>
    ///     Gets the list of rollback operations.
    /// </summary>
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

    /// <summary>
    ///     Occurs when an operation starts.
    /// </summary>
    public event EventHandler<EventArgs>? OperationStarted;

    /// <summary>
    ///     Occurs when an operation completes successfully.
    /// </summary>
    public event EventHandler<EventArgs>? OperationCompleted;

    /// <summary>
    ///     Occurs when an operation fails.
    /// </summary>
    public event EventHandler<OperationFailedEventArgs>? OperationFailed;

    /// <summary>
    ///     Occurs when rollback operations start.
    /// </summary>
    public event EventHandler<EventArgs>? RollbackStarted;

    /// <summary>
    ///     Adds an operation and its corresponding rollback operation to the transaction.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="operation">The operation to be performed.</param>
    /// <param name="rollbackOperation">The rollback operation to be performed in case of failure.</param>
    public void AddOperation<T>(Func<Task<Result<T>>> operation, Func<Task<Result>> rollbackOperation)
    {
        lock (_lock)
        {
            _operations.Add(() => ExecuteOperationAsync(operation));
            _rollbackOperations.Add(() => ExecuteOperationAsync(rollbackOperation));
        }
    }

    /// <summary>
    ///     Adds an operation and its corresponding rollback operation to the transaction.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="operation">The operation to be performed.</param>
    /// <param name="rollbackOperation">The rollback operation to be performed in case of failure.</param>
    public void AddOperation<T>(Func<Task<T>> operation, Func<Task<Result>> rollbackOperation)
    {
        lock (_lock)
        {
            _operations.Add(() => ExecuteOperationAsync(operation));
            _rollbackOperations.Add(() => ExecuteOperationAsync(rollbackOperation));
        }
    }

    /// <summary>
    ///     Executes all operations in a transaction. If any operation fails, all operations are rolled back.
    /// </summary>
    /// <returns>A Result indicating the success or failure of the transaction.</returns>
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

        if (_exceptions.Count is not 0)
        {
            RollbackStarted?.Invoke(this, EventArgs.Empty);
            var rollbackResult = await ExecuteRollbacksAsync(rollbackOperationsCopy).ConfigureAwait(false);
            return Result.Failure(new Collection<Exception>(_exceptions));
        }

        scope.Complete();
        return Result.Success();
    }

    /// <summary>
    ///     Executes all operations in a transaction. If any operation fails, all operations are rolled back.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <returns>A Result indicating the success or failure of the transaction.</returns>
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
                {
                    var exception = new InvalidOperationException(res.ErrorMessage, res.Exception);
                    _exceptions.Add(exception);
                    OperationFailed?.Invoke(this, new OperationFailedEventArgs(exception));
                    break;
                }
                case Result<T> { IsSuccess: false } typedRes:
                {
                    var exception = new InvalidOperationException(typedRes.ErrorMessage, typedRes.Exception);
                    _exceptions.Add(exception);
                    OperationFailed?.Invoke(this, new OperationFailedEventArgs(exception));
                    break;
                }
                default:
                    OperationCompleted?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        if (_exceptions.Count is not 0)
        {
            RollbackStarted?.Invoke(this, EventArgs.Empty);
            var rollbackResult = await ExecuteRollbacksAsync(rollbackOperationsCopy).ConfigureAwait(false);
            return Result<T>.Failure(new Collection<Exception>(_exceptions));
        }

        scope.Complete();
        return Result<T>.Success(default!);
    }

    /// <summary>
    ///     Executes all rollback operations.
    /// </summary>
    /// <param name="rollbackOperations">The rollback operations to execute.</param>
    /// <returns>A Result indicating the success or failure of the rollback operations.</returns>
    private async Task<Result> ExecuteRollbacksAsync(List<Func<Task<object>>> rollbackOperations)
    {
        var rollbackExceptions = new List<Exception>();

        foreach (var rollbackOperation in rollbackOperations)
        {
            var result = await ExecuteOperationAsync(rollbackOperation).ConfigureAwait(false);
            if (result is Result { IsSuccess: false } res)
                rollbackExceptions.Add(new InvalidOperationException(res.ErrorMessage, res.Exception));
        }

        return rollbackExceptions.Count is 0
            ? Result.Success()
            : Result.Failure(new Collection<Exception>(rollbackExceptions));
    }

    /// <summary>
    ///     Executes an individual operation with retry logic and handles any exceptions.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="retryCount">The number of times to retry the operation in case of failure.</param>
    /// <param name="timeout">The timeout duration for the operation.</param>
    /// <returns>A Result indicating the success or failure of the operation.</returns>
    private async Task<object> ExecuteOperationAsync<T>(Func<Task<T>> operation, int retryCount = 3,
        TimeSpan? timeout = null)
    {
        for (var attempt = 0; attempt < retryCount; attempt++)
            try
            {
                if (timeout.HasValue)
                {
                    using var cts = new CancellationTokenSource(timeout.Value);
                    var task = await Task.WhenAny(operation(), Task.Delay(Timeout.Infinite, cts.Token))
                        .ConfigureAwait(false);
                    if (task is not Task<T> resultTask) continue;
                    var result = await resultTask.ConfigureAwait(false);
                    return Result<T>.Success(result);
                }
                else
                {
                    var result = await operation().ConfigureAwait(false);
                    return Result<T>.Success(result);
                }
            }
            catch (Exception ex)
            {
                //Log.Error($"Exception during operation execution: {ex.Message}");
                if (attempt == retryCount - 1) return Result<T>.Failure(ex.Message, ex);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)))
                    .ConfigureAwait(false); // Exponential backoff
            }

        return Result<T>.Failure("Operation failed after all retry attempts");
    }

    /// <summary>
    ///     Executes an individual operation with retry logic and handles any exceptions.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="retryCount">The number of times to retry the operation in case of failure.</param>
    /// <param name="timeout">The timeout duration for the operation.</param>
    /// <returns>A Result indicating the success or failure of the operation.</returns>
    private async Task<object> ExecuteOperationAsync<T>(Func<Task<Result<T>>> operation, int retryCount = 3,
        TimeSpan? timeout = null)
    {
        for (var attempt = 0; attempt < retryCount; attempt++)
            try
            {
                if (timeout.HasValue)
                {
                    using var cts = new CancellationTokenSource(timeout.Value);
                    var task = await Task.WhenAny(operation(), Task.Delay(Timeout.Infinite, cts.Token))
                        .ConfigureAwait(false);
                    if (task is Task<Result<T>> resultTask) return await resultTask.ConfigureAwait(false);
                }
                else
                {
                    return await operation().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                //Log.Error($"Exception during operation execution: {ex.Message}");
                if (attempt == retryCount - 1) return Result<T>.Failure(ex.Message, ex);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)))
                    .ConfigureAwait(false); // Exponential backoff
            }

        return Result<T>.Failure("Operation failed after all retry attempts");
    }

    /// <summary>
    ///     Executes an individual operation with retry logic and handles any exceptions.
    /// </summary>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="retryCount">The number of times to retry the operation in case of failure.</param>
    /// <param name="timeout">The timeout duration for the operation.</param>
    /// <returns>A Result indicating the success or failure of the operation.</returns>
    private async Task<object> ExecuteOperationAsync(Func<Task<Result>> operation, int retryCount = 3,
        TimeSpan? timeout = null)
    {
        for (var attempt = 0; attempt < retryCount; attempt++)
            try
            {
                if (timeout.HasValue)
                {
                    using var cts = new CancellationTokenSource(timeout.Value);
                    var task = await Task.WhenAny(operation(), Task.Delay(Timeout.Infinite, cts.Token))
                        .ConfigureAwait(false);
                    if (task is Task<Result> resultTask) return await resultTask.ConfigureAwait(false);
                }
                else
                {
                    return await operation().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                //Log.Error($"Exception during operation execution: {ex.Message}");
                if (attempt == retryCount - 1) return Result.Failure(ex.Message, ex);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)))
                    .ConfigureAwait(false); // Exponential backoff
            }

        return Result.Failure("Operation failed after all retry attempts");
    }
}
