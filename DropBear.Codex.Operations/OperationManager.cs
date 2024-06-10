// File: OperationManager.cs
// Description: Manages transactions, ensuring all operations are executed successfully or rolled back in case of failure, with added enhancements.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Transactions;
using DropBear.Codex.Core;

namespace DropBear.Codex.Operations;

/// <summary>
///     Manages transactions, ensuring all operations are executed successfully or rolled back in case of failure, with
///     added enhancements.
/// </summary>
public class OperationManager : IDisposable
{
    private readonly List<Exception> _exceptions = new();
    private readonly object _lock = new();
    private readonly List<Func<Task<object>>> _operations = new();
    private readonly List<Func<Task<object>>> _rollbackOperations = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();

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

    /// <summary>
    ///     Releases the resources used by the OperationManager.
    /// </summary>
    public void Dispose() => _cancellationTokenSource?.Dispose();

    public event EventHandler<EventArgs>? OperationStarted;
    public event EventHandler<EventArgs>? OperationCompleted;
    public event EventHandler<OperationFailedEventArgs>? OperationFailed;
    public event EventHandler<EventArgs>? RollbackStarted;
    public event EventHandler<ProgressEventArgs>? ProgressChanged;
    public event EventHandler<LogEventArgs>? Log;

    /// <summary>
    ///     Adds an operation and its corresponding rollback operation to the manager.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the operation.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="rollbackOperation">The rollback operation to execute in case of failure.</param>
    /// <exception cref="ArgumentNullException">Thrown if operation or rollbackOperation is null.</exception>
    public void AddOperation<T>(Func<Task<Result<T>>> operation, Func<Task<Result>> rollbackOperation)
    {
        if (operation == null || rollbackOperation == null)
            throw new ArgumentNullException(nameof(operation), "Operations cannot be null");

        lock (_lock)
        {
            _operations.Add(() => ExecuteOperationAsync(operation));
            _rollbackOperations.Add(() => ExecuteOperationAsync(rollbackOperation));
        }

        LogMessage("Operation and rollback operation added.");
    }

    /// <summary>
    ///     Adds an operation and its corresponding rollback operation to the manager.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the operation.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="rollbackOperation">The rollback operation to execute in case of failure.</param>
    /// <exception cref="ArgumentNullException">Thrown if operation or rollbackOperation is null.</exception>
    public void AddOperation<T>(Func<Task<T>> operation, Func<Task<Result>> rollbackOperation)
    {
        if (operation == null || rollbackOperation == null)
            throw new ArgumentNullException(nameof(operation), "Operations cannot be null");

        lock (_lock)
        {
            _operations.Add(() => ExecuteOperationAsync(operation));
            _rollbackOperations.Add(() => ExecuteOperationAsync(rollbackOperation));
        }

        LogMessage("Operation and rollback operation added.");
    }

    /// <summary>
    ///     Executes all operations and rolls back in case of failure.
    /// </summary>
    /// <returns>A Result indicating the success or failure of the operation execution.</returns>
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

        if (_exceptions.Count is not 0)
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

    /// <summary>
    ///     Executes all operations and rolls back in case of failure, returning a result of type T.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the operations.</typeparam>
    /// <returns>A Result of type T indicating the success or failure of the operation execution.</returns>
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
                switch (result)
                {
                    case Result { IsSuccess: false } res:
                        var exception = new InvalidOperationException(res.ErrorMessage, res.Exception);
                        _exceptions.Add(exception);
                        OperationFailed?.Invoke(this, new OperationFailedEventArgs(exception));
                        LogMessage($"Operation failed: {res.ErrorMessage}");
                        break;
                    case Result<T> { IsSuccess: false } typedRes:
                        var typedException = new InvalidOperationException(typedRes.ErrorMessage, typedRes.Exception);
                        _exceptions.Add(typedException);
                        OperationFailed?.Invoke(this, new OperationFailedEventArgs(typedException));
                        LogMessage($"Operation failed: {typedRes.ErrorMessage}");
                        break;
                    default:
                        OperationCompleted?.Invoke(this, EventArgs.Empty);
                        LogMessage("Operation completed.");
                        break;
                }

                completedOperations++;
                ReportProgress(completedOperations, totalOperations);
            }
        }
        catch (OperationCanceledException)
        {
            LogMessage("Operation was canceled.");
            return Result<T>.Failure(new Collection<Exception>(_exceptions));
        }
        catch (Exception ex)
        {
            _exceptions.Add(ex);
            return Result<T>.Failure(new Collection<Exception>(_exceptions));
        }

        if (_exceptions.Count is not 0)
        {
            RollbackStarted?.Invoke(this, EventArgs.Empty);
            LogMessage("Starting rollback due to failures.");
            var rollbackResult = await ExecuteRollbacksAsync(rollbackOperationsCopy).ConfigureAwait(false);
            return rollbackResult.IsSuccess
                ? Result<T>.Failure(new Collection<Exception>(_exceptions))
                : Result<T>.Failure(rollbackResult.ErrorMessage);
        }

        scope.Complete();
        LogMessage("All operations completed successfully.");
        return Result<T>.Success(default!);
    }

    /// <summary>
    ///     Executes the rollback operations in case of failure.
    /// </summary>
    /// <param name="rollbackOperations">The rollback operations to execute.</param>
    /// <returns>A Result indicating the success or failure of the rollback operations.</returns>
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

        await Task.WhenAll(rollbackTasks);

        return rollbackExceptions.Count is 0
            ? Result.Success()
            : Result.Failure(new Collection<Exception>(rollbackExceptions));
    }

    /// <summary>
    ///     Executes an operation with retry and timeout logic.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the operation.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="retryCount">The number of retry attempts.</param>
    /// <param name="timeout">The timeout for the operation.</param>
    /// <returns>The result of the operation execution.</returns>
    private static async Task<object> ExecuteOperationAsync<T>(Func<Task<T>> operation, int retryCount = 3,
        TimeSpan? timeout = null)
    {
        for (var attempt = 0; attempt < retryCount; attempt++)
            try
            {
                var resultTask = timeout.HasValue
                    ? await Task.WhenAny(operation(), Task.Delay(timeout.Value)).ConfigureAwait(false) as Task<T>
                    : operation();
                if (resultTask != null) return await resultTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (attempt == retryCount - 1)
                    return Result<T>.Failure(ex.Message, ex);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt))).ConfigureAwait(false);
            }

        return Result<T>.Failure("Operation failed after all retry attempts");
    }

    /// <summary>
    ///     Reports the progress of the operations.
    /// </summary>
    /// <param name="completedOperations">The number of completed operations.</param>
    /// <param name="totalOperations">The total number of operations.</param>
    private void ReportProgress(int completedOperations, int totalOperations)
    {
        var progressPercentage = (int)(completedOperations / (double)totalOperations * 100);
        ProgressChanged?.Invoke(this,
            new ProgressEventArgs(progressPercentage,
                $"Completed {completedOperations} of {totalOperations} operations."));
        LogMessage(
            $"Progress: {progressPercentage}% - Completed {completedOperations} of {totalOperations} operations.");
    }

    /// <summary>
    ///     Logs a message.
    /// </summary>
    /// <param name="message">The message to log.</param>
    private void LogMessage(string message) => Log?.Invoke(this, new LogEventArgs(message));

    /// <summary>
    ///     Cancels the ongoing operations.
    /// </summary>
    public void Cancel() => _cancellationTokenSource.Cancel();
}
