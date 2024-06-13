﻿// File: OperationManager.cs
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
    private readonly List<IOperation> _operations = new();
    private readonly List<IOperation> _rollbackOperations = new();

    public IReadOnlyList<IOperation> Operations
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

    public IReadOnlyList<IOperation> RollbackOperations
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

    public void AddOperation(IOperation operation)
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation), "Operation cannot be null");

        lock (_lock)
        {
            _operations.Add(operation);
            _rollbackOperations.Add(operation);
        }

        LogMessage("Operation and rollback operation added.");
    }

    public async Task<Result> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        List<IOperation> operationsCopy;
        List<IOperation> rollbackOperationsCopy;
        lock (_lock)
        {
            operationsCopy = new List<IOperation>(_operations);
            rollbackOperationsCopy = new List<IOperation>(_rollbackOperations);
        }

        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        var totalOperations = operationsCopy.Count;
        var completedOperations = 0;

        LogMessage($"Starting execution of {totalOperations} operations.");

        try
        {
            foreach (var operation in operationsCopy)
            {
                cancellationToken.ThrowIfCancellationRequested();

                OperationStarted?.Invoke(this, EventArgs.Empty);
                LogMessage("Operation started.");
                var result = await ExecuteOperationAsync(operation, cancellationToken).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    var exception = new InvalidOperationException(result.ErrorMessage, result.Exception);
                    _exceptions.Add(exception);
                    OperationFailed?.Invoke(this, new OperationFailedEventArgs(exception));
                    LogMessage($"Operation failed: {result.ErrorMessage}");
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
            var rollbackResult = await ExecuteRollbacksAsync(rollbackOperationsCopy, cancellationToken).ConfigureAwait(false);

            if (!rollbackResult.IsSuccess)
                _exceptions.Add(new InvalidOperationException("Rollback failed", rollbackResult.Exception));

            return Result.Failure(new Collection<Exception>(_exceptions));
        }

        scope.Complete();
        LogMessage("All operations completed successfully.");
        return Result.Success();
    }

    public async Task<Result<List<T>>> ExecuteWithResultsAsync<T>(CancellationToken cancellationToken = default)
    {
        List<IOperation> operationsCopy;
        List<IOperation> rollbackOperationsCopy;
        lock (_lock)
        {
            operationsCopy = new List<IOperation>(_operations);
            rollbackOperationsCopy = new List<IOperation>(_rollbackOperations);
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
                cancellationToken.ThrowIfCancellationRequested();

                OperationStarted?.Invoke(this, EventArgs.Empty);
                LogMessage("Operation started.");
                var result = await ExecuteOperationAsync(operation, cancellationToken).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    var exception = new InvalidOperationException(result.ErrorMessage, result.Exception);
                    _exceptions.Add(exception);
                    OperationFailed?.Invoke(this, new OperationFailedEventArgs(exception));
                    LogMessage($"Operation failed: {result.ErrorMessage}");
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
            var rollbackResult = await ExecuteRollbacksAsync(rollbackOperationsCopy, cancellationToken).ConfigureAwait(false);
            return rollbackResult.IsSuccess
                ? Result<List<T>>.Failure(new Collection<Exception>(_exceptions))
                : Result<List<T>>.Failure(rollbackResult.ErrorMessage);
        }

        scope.Complete();
        LogMessage("All operations completed successfully.");
        return Result<List<T>>.Success(results);
    }

    private static async Task<Result> ExecuteRollbacksAsync(List<IOperation> rollbackOperations, CancellationToken cancellationToken = default)
    {
        var rollbackExceptions = new List<Exception>();

        var rollbackTasks = rollbackOperations.Select(async operation =>
        {
            var result = await operation.RollbackAsync(cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                rollbackExceptions.Add(new InvalidOperationException(result.ErrorMessage, result.Exception));
                Console.WriteLine($"Rollback failed: {result.ErrorMessage}");
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

    private static async Task<Result> ExecuteOperationAsync(IOperation operation, CancellationToken cancellationToken = default)
    {
        try
        {
            return await operation.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message, ex);
        }
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
