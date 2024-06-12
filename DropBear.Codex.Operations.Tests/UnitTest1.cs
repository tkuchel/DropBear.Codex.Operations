// File: OperationManagerTests.cs
// Description: Unit tests for the OperationManager class.

using DropBear.Codex.Core;

namespace DropBear.Codex.Operations.Tests;

[TestFixture]
public class OperationManagerTests : IDisposable
{
    [SetUp]
    public void SetUp()
    {
        _operationManager = new OperationManager();
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
        Func<Task<Result<int>>> operation = async () => Result<int>.Success(1);
        Func<Task<Result>> rollbackOperation = async () => Result.Success();

        _operationManager.AddOperation(operation, rollbackOperation);

        Assert.AreEqual(1, _operationManager.Operations.Count);
        Assert.AreEqual(1, _operationManager.RollbackOperations.Count);
    }

    [Test]
    public async Task ExecuteAsync_ShouldExecuteAllOperationsSuccessfully()
    {
        Func<Task<Result>> operation = async () => Result.Success();
        _operationManager.AddOperation(operation, async () => Result.Success());

        var result = await _operationManager.ExecuteAsync();

        Assert.IsTrue(result.IsSuccess);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackOnFailure()
    {
        Func<Task<Result>> failingOperation = async () => Result.Failure("Operation failed");
        Func<Task<Result>> rollbackOperation = async () => Result.Success();
        _operationManager.AddOperation(failingOperation, rollbackOperation);

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

        _operationManager.OperationStarted += (sender, args) => operationStartedTriggered = true;
        _operationManager.OperationCompleted += (sender, args) => operationCompletedTriggered = true;
        _operationManager.OperationFailed += (sender, args) => operationFailedTriggered = true;
        _operationManager.RollbackStarted += (sender, args) => rollbackStartedTriggered = true;

        Func<Task<Result>> operation = async () => Result.Success();
        _operationManager.AddOperation(operation, async () => Result.Success());

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
        _operationManager.ProgressChanged += (sender, args) =>
        {
            progressEvents.Add(args.ProgressPercentage);
            Console.WriteLine($"Progress: {args.ProgressPercentage}%, Message: {args.Message}");
        };

        Func<Task<Result>> operation = async () => Result.Success();
        _operationManager.AddOperation(operation, async () => Result.Success());
        _operationManager.AddOperation(operation, async () => Result.Success());

        await _operationManager.ExecuteAsync();

        Assert.AreEqual(2, progressEvents.Count);
        Assert.AreEqual(50, progressEvents[0]);
        Assert.AreEqual(100, progressEvents[1]);
    }

    [Test]
    public async Task ExecuteAsync_ShouldTriggerLogEvents()
    {
        var logMessages = new List<string>();
        _operationManager.Log += (sender, args) =>
        {
            logMessages.Add(args.Message);
            Console.WriteLine($"Log: {args.Message}");
        };

        Func<Task<Result>> operation = async () => Result.Success();
        _operationManager.AddOperation(operation, async () => Result.Success());

        await _operationManager.ExecuteAsync();

        Assert.IsNotEmpty(logMessages);
    }

    [Test]
    public async Task ExecuteWithResultsAsync_ShouldReturnResults()
    {
        Func<Task<Result<int>>> operation1 = async () => Result<int>.Success(1);
        Func<Task<Result<int>>> operation2 = async () => Result<int>.Success(2);
        Func<Task<Result>> rollbackOperation = async () => Result.Success();

        _operationManager.AddOperation(operation1, rollbackOperation);
        _operationManager.AddOperation(operation2, rollbackOperation);

        var result = await _operationManager.ExecuteWithResultsAsync<int>();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(2, result.Value.Count);
        Assert.AreEqual(1, result.Value[0]);
        Assert.AreEqual(2, result.Value[1]);
    }

    [Test]
    public async Task ExecuteWithResultsAsync_ShouldRollbackOnFailure()
    {
        Func<Task<Result<int>>> failingOperation = async () => Result<int>.Failure("Operation failed");
        Func<Task<Result>> rollbackOperation = async () => Result.Success();
        _operationManager.AddOperation(failingOperation, rollbackOperation);

        var result = await _operationManager.ExecuteWithResultsAsync<int>();

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(1, _operationManager.RollbackOperations.Count);
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