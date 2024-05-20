using System.Collections.ObjectModel;
using System.Transactions;
using DropBear.Codex.Core;

namespace DropBear.Codex.Operations;

/// <summary>
///     Manages transactions, ensuring that all operations are executed successfully,
///     or any changes are rolled back in case of failure.
/// </summary>
public class OperationManager
{
    private readonly List<Exception> _exceptions = [];
    private readonly object _lock = new();
    private readonly List<Func<Task<object>>> _operations = [];
    private readonly List<Func<Task<object>>> _rollbackOperations = [];

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
            _operations.Add(async () => await ExecuteOperationAsync(operation).ConfigureAwait(false));
            _rollbackOperations.Add(async () => await ExecuteOperationAsync(rollbackOperation).ConfigureAwait(false));
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
            _operations.Add(async () => await ExecuteOperationAsync(operation).ConfigureAwait(false));
            _rollbackOperations.Add(async () => await ExecuteOperationAsync(rollbackOperation).ConfigureAwait(false));
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
            var result = await ExecuteOperationAsync(operation).ConfigureAwait(false);
            if (result is Result res && !res.IsSuccess) _exceptions.Add(new Exception(res.ErrorMessage, res.Exception));
        }

        if (_exceptions.Count > 0)
        {
            var rollbackResult = await ExecuteRollbacksAsync(rollbackOperationsCopy).ConfigureAwait(false);
            return rollbackResult.IsSuccess ? Result.Failure(new Collection<Exception>(_exceptions)) : rollbackResult;
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
            var result = await ExecuteOperationAsync(operation).ConfigureAwait(false);
            if (result is Result res && !res.IsSuccess)
                _exceptions.Add(new Exception(res.ErrorMessage, res.Exception));
            else if (result is Result<T> typedRes && !typedRes.IsSuccess)
                _exceptions.Add(new Exception(typedRes.ErrorMessage, typedRes.Exception));
        }

        if (_exceptions.Count > 0)
        {
            var rollbackResult = await ExecuteRollbacksAsync(rollbackOperationsCopy).ConfigureAwait(false);
            return rollbackResult.IsSuccess
                ? Result<T>.Failure(new Collection<Exception>(_exceptions))
                : Result<T>.Failure(rollbackResult.ErrorMessage);
        }

        scope.Complete();
        return Result<T>.Success(default);
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
            if (result is Result res && !res.IsSuccess)
                rollbackExceptions.Add(new Exception(res.ErrorMessage, res.Exception));
        }

        return rollbackExceptions.Count == 0
            ? Result.Success()
            : Result.Failure(new Collection<Exception>(rollbackExceptions));
    }

    /// <summary>
    ///     Executes an individual operation and handles any exceptions.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <returns>A Result indicating the success or failure of the operation.</returns>
    private static async Task<object> ExecuteOperationAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            var result = await operation().ConfigureAwait(false);
            return Result<T>.Success(result);
        }
        catch (Exception ex)
        {
            // Log exception or handle it as needed
            Console.WriteLine($"Exception during operation execution: {ex.Message}");
            return Result<T>.Failure(ex.Message, ex);
        }
    }

    /// <summary>
    ///     Executes an individual operation and handles any exceptions.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <returns>A Result indicating the success or failure of the operation.</returns>
    private static async Task<object> ExecuteOperationAsync<T>(Func<Task<Result<T>>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Log exception or handle it as needed
            Console.WriteLine($"Exception during operation execution: {ex.Message}");
            return Result<T>.Failure(ex.Message, ex);
        }
    }

    /// <summary>
    ///     Executes an individual operation and handles any exceptions.
    /// </summary>
    /// <param name="operation">The operation to execute.</param>
    /// <returns>A Result indicating the success or failure of the operation.</returns>
    private static async Task<object> ExecuteOperationAsync(Func<Task<Result>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Log exception or handle it as needed
            Console.WriteLine($"Exception during operation execution: {ex.Message}");
            return Result.Failure(ex.Message, ex);
        }
    }
}
