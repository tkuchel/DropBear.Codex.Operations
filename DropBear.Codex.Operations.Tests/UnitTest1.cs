// File: OperationManagerTests.cs
// Description: Unit tests for the OperationManager class.

using DropBear.Codex.Core;

namespace DropBear.Codex.Operations.Tests;

[TestFixture]
public class OperationManagerTests
{
    [SetUp]
    public void SetUp()
    {
        _operationManager = new OperationManager();
    }

    private OperationManager _operationManager;

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
}