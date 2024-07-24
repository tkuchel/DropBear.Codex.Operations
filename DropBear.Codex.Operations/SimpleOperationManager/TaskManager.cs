#region

using System.Diagnostics;

#endregion

namespace DropBear.Codex.Operations.SimpleOperationManager;

/// <summary>
///     Represents an operation to be executed by the TaskManager.
/// </summary>
/// <param name="context">The context in which the operation is executed.</param>
/// <returns>A task representing the asynchronous operation, returning an OperationResult.</returns>
#pragma warning disable MA0048
public delegate Task<OperationResult?> Operation(OperationContext context);
#pragma warning restore MA0048

/// <summary>
///     Manages the execution of a series of operations, with support for conditional branching, parallel execution, and
///     error handling.
/// </summary>
public class TaskManager : IDisposable
{
    private readonly Dictionary<string, Func<OperationContext, Task<bool>>> _conditionalBranches =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<(string Name, Operation Operation, ExecutionOptions Options)> _operations = [];
    private readonly SemaphoreSlim _pauseSemaphore = new(1, 1);

    private CancellationTokenSource _cts = new();
    private bool _isDisposed;
    private volatile bool _isPaused;

    /// <summary>
    ///     Disposes of the resources used by the TaskManager.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Adds an operation to the task manager.
    /// </summary>
    /// <param name="name">The name of the operation.</param>
    /// <param name="operation">The operation to be executed.</param>
    /// <param name="options">The execution options for the operation.</param>
    /// <exception cref="ArgumentNullException">Thrown when name or operation is null.</exception>
    /// <exception cref="ArgumentException">Thrown when name is empty.</exception>
    public void AddOperation(string name, Operation operation, ExecutionOptions? options = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Operation name cannot be null or empty.", nameof(name));
        }

        _operations.Add((name, operation ?? throw new ArgumentNullException(nameof(operation)),
            options ?? new ExecutionOptions()));
    }

    /// <summary>
    ///     Adds a conditional branch to the task manager.
    /// </summary>
    /// <param name="name">The name of the operation to which this condition applies.</param>
    /// <param name="condition">The condition to be evaluated.</param>
    /// <exception cref="ArgumentNullException">Thrown when name or condition is null.</exception>
    /// <exception cref="ArgumentException">Thrown when name is empty.</exception>
    public void AddConditionalBranch(string name, Func<OperationContext, Task<bool>> condition)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Branch name cannot be null or empty.", nameof(name));
        }

        _conditionalBranches[name] = condition ?? throw new ArgumentNullException(nameof(condition));
    }

    /// <summary>
    ///     Executes all added operations asynchronously.
    /// </summary>
    /// <param name="progress">An optional progress reporter.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation, returning an OperationResult.</returns>
    public async Task<OperationResult> ExecuteAsync(
        IProgress<(string OperationName, int ProgressPercentage)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
        var context = new OperationContext { CancellationToken = linkedCts.Token };
        var totalOperations = _operations.Count;
        var completedOperations = 0;

        var stopwatch = Stopwatch.StartNew();
        var tasks = new List<Task>();

        foreach (var (name, operation, options) in _operations)
        {
            await _pauseSemaphore.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            try
            {
                while (_isPaused)
                {
                    Trace.WriteLine($"Execution paused before operation: {name}");
                    _pauseSemaphore.Release();
                    await Task.Delay(100, linkedCts.Token).ConfigureAwait(false);
                    await _pauseSemaphore.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                }

                linkedCts.Token.ThrowIfCancellationRequested();

                Trace.WriteLine($"Evaluating operation: {name}");

                var shouldExecute = !_conditionalBranches.ContainsKey(name) ||
                                    await _conditionalBranches[name](context).ConfigureAwait(false);

                if (!shouldExecute)
                {
                    Trace.WriteLine($"Skipping operation {name} due to condition");
                    continue;
                }

                async Task ExecuteOperation(CancellationTokenSource passedLinkedCts)
                {
                    var operationStopwatch = Stopwatch.StartNew();

                    OperationResult? result = null;
                    for (var retry = 0; retry <= options.MaxRetries; retry++)
                    {
                        try
                        {
                            result = await operation(context).ConfigureAwait(false);
                            if (result?.Success is true)
                            {
                                break;
                            }

                            if (retry >= options.MaxRetries)
                            {
                                continue;
                            }

                            Trace.WriteLine(
                                $"Operation {name} failed. Retrying in {options.RetryDelay}. Attempt {retry + 1} of {options.MaxRetries}");
                            await Task.Delay(options.RetryDelay, passedLinkedCts.Token).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            result = new OperationResult
                            {
                                Success = false, Message = $"Operation '{name}' threw an exception", Exception = ex
                            };
                            if (retry == options.MaxRetries)
                            {
                                break;
                            }

                            Trace.WriteLine(
                                $"Operation {name} threw an exception. Retrying in {options.RetryDelay}. Attempt {retry + 1} of {options.MaxRetries}");
                            await Task.Delay(options.RetryDelay, passedLinkedCts.Token).ConfigureAwait(false);
                        }
                    }

                    operationStopwatch.Stop();
                    Trace.WriteLine(
                        $"Operation {name} completed in {operationStopwatch.ElapsedMilliseconds}ms with result: {result?.Success}");

                    if (result is { Success: false })
                    {
                        throw new InvalidOperationException(
                            $"Operation {name} failed after {options.MaxRetries} retries", result.Exception);
                    }

                    Interlocked.Increment(ref completedOperations);
                    progress?.Report((name, completedOperations * 100 / totalOperations));
                }

                if (options.AllowParallel)
                {
                    tasks.Add(ExecuteOperation(linkedCts));
                }
                else
                {
                    await ExecuteOperation(linkedCts).ConfigureAwait(false);
                }
            }
            finally
            {
                _pauseSemaphore.Release();
            }
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        stopwatch.Stop();
        Trace.WriteLine($"All operations completed in {stopwatch.ElapsedMilliseconds}ms");

        return new OperationResult { Success = true, Message = "All operations completed successfully." };
    }

    /// <summary>
    ///     Pauses the execution of operations.
    /// </summary>
    public void Pause()
    {
        _isPaused = true;
        Trace.WriteLine("Execution paused");
    }

    /// <summary>
    ///     Resumes the execution of operations.
    /// </summary>
    public void Resume()
    {
        _isPaused = false;
        Trace.WriteLine("Execution resumed");
    }

    /// <summary>
    ///     Cancels the execution of operations.
    /// </summary>
    public void Cancel()
    {
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        Trace.WriteLine("Execution cancelled");
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            _cts.Dispose();
            _pauseSemaphore.Dispose();
        }

        _isDisposed = true;
    }
}
