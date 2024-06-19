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
        Assert.That(_operationManager.Operations, Is.Empty);
    }

    [Test]
    public void RollbackOperations_ShouldBeEmpty_OnInitialization()
    {
        Assert.That(_operationManager.RollbackOperations, Is.Empty);
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

        Assert.That(_operationManager.Operations.Count, Is.EqualTo(1));
        Assert.That(_operationManager.RollbackOperations.Count, Is.EqualTo(1));
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

        Assert.That(result.IsSuccess, Is.True);
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

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(_operationManager.RollbackOperations.Count, Is.EqualTo(1));
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

        Assert.That(operationStartedTriggered, Is.True);
        Assert.That(operationCompletedTriggered, Is.True);
        Assert.That(operationFailedTriggered, Is.False);
        Assert.That(rollbackStartedTriggered, Is.False);
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

        Assert.That(progressEvents.Count,
            Is.EqualTo(3)); // Expect 1 operation with 2 progress updates + 1 final progress update
        Assert.That(progressEvents[0], Is.EqualTo(50));
        Assert.That(progressEvents[1], Is.EqualTo(100));
        Assert.That(progressEvents[2], Is.EqualTo(100));
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

        Assert.That(logMessages, Is.Not.Empty);
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

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Count, Is.EqualTo(2));
        Assert.That(result.Value[0], Is.EqualTo(1));
        Assert.That(result.Value[1], Is.EqualTo(2));
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

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(_operationManager.RollbackOperations.Count, Is.EqualTo(1));
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

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value[0], Is.EqualTo(1));
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

        Assert.That(result.IsSuccess, Is.False);
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