using DropBear.Codex.Core;

namespace DropBear.Codex.Operations.Tests;

[TestFixture]
public class OperationManagerTests : IDisposable
{
    [SetUp]
    public void SetUp()
    {
        _operationManager = new OperationManagerBuilder().Build();
    }

    [TearDown]
    public void TearDown()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private OperationManager _operationManager;
    private bool _disposedValue;

    [Test]
    public void Operations_ShouldBeEmpty_OnInitialization()
    {
        Assert.IsEmpty(_operationManager.Operations);
    }

    [Test]
    public void RollbackOperations_ShouldBeEmpty_OnInitialization()
    {
        Assert.IsEmpty(_operationManager.RollbackOperations);
    }

    [Test]
    public void AddOperation_ShouldAddOperationAndRollbackOperation()
    {
        var operation = new OperationBuilder()
            .WithExecuteAsync(async ct => Result.Success())
            .WithRollbackAsync(async ct => Result.Success())
            .Build();

        _operationManager = new OperationManagerBuilder()
            .WithOperation(operation)
            .Build();

        Assert.AreEqual(1, _operationManager.Operations.Count);
        Assert.AreEqual(1, _operationManager.RollbackOperations.Count);
    }

    [Test]
    public async Task ExecuteAsync_ShouldExecuteAllOperationsSuccessfully()
    {
        var operation = new OperationBuilder()
            .WithExecuteAsync(async ct => Result.Success())
            .WithRollbackAsync(async ct => Result.Success())
            .Build();

        _operationManager = new OperationManagerBuilder()
            .WithOperation(operation)
            .Build();

        var result = await _operationManager.ExecuteAsync();

        Assert.IsTrue(result.IsSuccess);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackOnFailure()
    {
        var failingOperation = new OperationBuilder()
            .WithExecuteAsync(async ct => Result.Failure("Operation failed"))
            .WithRollbackAsync(async ct => Result.Success())
            .Build();

        _operationManager = new OperationManagerBuilder()
            .WithOperation(failingOperation)
            .Build();

        var result = await _operationManager.ExecuteAsync();

        Console.WriteLine($"ExecuteAsync result: {result.IsSuccess}");
        foreach (var ex in result.Exceptions) Console.WriteLine($"Exception: {ex.Message}");

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(1, _operationManager.RollbackOperations.Count);
    }

    [Test]
    public async Task ExecuteAsync_ShouldTriggerEvents()
    {
        var operationStartedTriggered = false;
        var operationCompletedTriggered = false;
        var operationFailedTriggered = false;
        var rollbackStartedTriggered = false;

        var operation = new OperationBuilder()
            .WithExecuteAsync(async ct => Result.Success())
            .WithRollbackAsync(async ct => Result.Success())
            .Build();

        _operationManager = new OperationManagerBuilder()
            .WithOperation(operation)
            .OnOperationStarted((sender, args) => operationStartedTriggered = true)
            .OnOperationCompleted((sender, args) => operationCompletedTriggered = true)
            .OnOperationFailed((sender, args) => operationFailedTriggered = true)
            .OnRollbackStarted((sender, args) => rollbackStartedTriggered = true)
            .Build();

        await _operationManager.ExecuteAsync();

        Assert.IsTrue(operationStartedTriggered);
        Assert.IsTrue(operationCompletedTriggered);
        Assert.IsFalse(operationFailedTriggered);
        Assert.IsFalse(rollbackStartedTriggered);
    }

    [Test]
    public async Task ExecuteAsync_ShouldTriggerProgressEvents()
    {
        var progressEvents = new List<int>();

        var operation = new OperationBuilder()
            .WithExecuteAsync(async ct =>
            {
                for (var i = 0; i < 2; i++)
                {
                    await Task.Delay(50, ct);
                    var progressPercentage = 50 * (i + 1);
                    // Use the OnProgressChanged method here
                    _operationManager.OnProgressChanged(new ProgressEventArgs(progressPercentage,
                        $"Progress: {progressPercentage}%"));
                }

                return Result.Success();
            })
            .WithRollbackAsync(async ct => Result.Success())
            .Build();

        _operationManager = new OperationManagerBuilder()
            .WithOperation(operation)
            .OnProgressChanged((sender, args) =>
            {
                progressEvents.Add(args.ProgressPercentage);
                Console.WriteLine($"Progress: {args.ProgressPercentage}%, Message: {args.Message}");
            })
            .Build();

        await _operationManager.ExecuteAsync();

        Assert.AreEqual(3,
            progressEvents.Count); // Expect 1 operation with 2 progress updates + 1 final progress update
        Assert.AreEqual(50, progressEvents[0]);
        Assert.AreEqual(100, progressEvents[1]);
        Assert.AreEqual(100, progressEvents[2]);
    }


    [Test]
    public async Task ExecuteAsync_ShouldTriggerLogEvents()
    {
        var logMessages = new List<string>();

        var operation = new OperationBuilder()
            .WithExecuteAsync(async ct => Result.Success())
            .WithRollbackAsync(async ct => Result.Success())
            .Build();

        _operationManager = new OperationManagerBuilder()
            .WithOperation(operation)
            .OnLog((sender, args) =>
            {
                logMessages.Add(args.Message);
                Console.WriteLine($"Log: {args.Message}");
            })
            .Build();

        await _operationManager.ExecuteAsync();

        Assert.IsNotEmpty(logMessages);
    }

    [Test]
    public async Task ExecuteWithResultsAsync_ShouldReturnResults()
    {
        var operation1 = new OperationBuilder<int>()
            .WithExecuteAsync(async ct => Result<int>.Success(1))
            .WithRollbackAsync(async ct => Result.Success())
            .Build();

        var operation2 = new OperationBuilder<int>()
            .WithExecuteAsync(async ct => Result<int>.Success(2))
            .WithRollbackAsync(async ct => Result.Success())
            .Build();

        _operationManager = new OperationManagerBuilder()
            .WithOperation(operation1)
            .WithOperation(operation2)
            .Build();

        var result = await _operationManager.ExecuteWithResultsAsync<int>();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(2, result.Value.Count);
        Assert.AreEqual(1, result.Value[0]);
        Assert.AreEqual(2, result.Value[1]);
    }

    [Test]
    public async Task ExecuteWithResultsAsync_ShouldRollbackOnFailure()
    {
        var failingOperation = new OperationBuilder<int>()
            .WithExecuteAsync(async ct => Result<int>.Failure("Operation failed"))
            .WithRollbackAsync(async ct => Result.Success())
            .Build();

        _operationManager = new OperationManagerBuilder()
            .WithOperation(failingOperation)
            .Build();

        var result = await _operationManager.ExecuteWithResultsAsync<int>();

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(1, _operationManager.RollbackOperations.Count);
    }

    [Test]
    public async Task ExecuteWithResultsAsync_WithCancellationToken_ShouldSucceedBeforeTimeout()
    {
        var operation = new OperationBuilder<int>()
            .WithExecuteAsync(async ct =>
            {
                await Task.Delay(100, ct); // Simulate some work
                return Result<int>.Success(1);
            })
            .WithRollbackAsync(async ct => Result.Success())
            .Build();

        _operationManager = new OperationManagerBuilder()
            .WithOperation(operation)
            .Build();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var result = await _operationManager.ExecuteWithResultsAsync<int>(cts.Token);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value[0]);
    }

    [Test]
    public async Task ExecuteWithResultsAsync_WithCancellationToken_ShouldFailAfterTimeout()
    {
        var operation = new OperationBuilder<int>()
            .WithExecuteAsync(async ct =>
            {
                await Task.Delay(2000, ct); // Simulate longer work
                return Result<int>.Success(1);
            })
            .WithRollbackAsync(async ct => Result.Success())
            .Build();

        _operationManager = new OperationManagerBuilder()
            .WithOperation(operation)
            .Build();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var result = await _operationManager.ExecuteWithResultsAsync<int>(cts.Token);

        Assert.IsFalse(result.IsSuccess);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing) _operationManager?.Dispose();

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}