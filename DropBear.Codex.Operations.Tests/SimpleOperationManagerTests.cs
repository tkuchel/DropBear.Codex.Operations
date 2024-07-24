#region

using System.Diagnostics;
using System.Text;
using DropBear.Codex.Operations.SimpleOperationManager;

#endregion

namespace DropBear.Codex.Operations.Tests;

[TestFixture]
public class TaskManagerTests
{
    [SetUp]
    public void Setup()
    {
        debugOutput = new StringBuilder();
        stringWriter = new StringWriter(debugOutput);
        listener = new TextWriterTraceListener(stringWriter);
        Trace.Listeners.Add(listener);
    }

    [TearDown]
    public void TearDown()
    {
        Trace.Listeners.Remove(listener);
        listener.Dispose();
        stringWriter.Dispose();
    }

    private StringBuilder debugOutput;
    private StringWriter stringWriter;
    private TraceListener listener;

    // SharedCache tests remain unchanged

    [Test]
    public async Task TaskManager_ExecuteSuccessfulOperations_ShouldReturnSuccess()
    {
        var taskManager = new TaskManager();
        taskManager.AddOperation("Op1", async context =>
        {
            context.Cache.Set("data", "test");
            return new OperationResult { Success = true };
        });
        taskManager.AddOperation("Op2", async context =>
        {
            var data = context.Cache.Get<string>("data");
            return new OperationResult { Success = true, Message = data };
        });

        var result = await taskManager.ExecuteAsync();

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Is.EqualTo("All operations completed successfully."));
    }

    [Test]
    public async Task TaskManager_ExecuteWithFailingOperation_ShouldReturnFailure()
    {
        var taskManager = new TaskManager();
        taskManager.AddOperation("Op1", async context => new OperationResult { Success = true });
        taskManager.AddOperation("Op2",
            async context => new OperationResult { Success = false, Message = "Operation 2 failed" });

        Assert.ThrowsAsync<InvalidOperationException>(async () => await taskManager.ExecuteAsync());
    }

    [Test]
    public async Task TaskManager_ExecuteWithException_ShouldCatchAndThrow()
    {
        var taskManager = new TaskManager();
        taskManager.AddOperation("Op1", async context => throw new InvalidOperationException("Test exception"));

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await taskManager.ExecuteAsync());
        Assert.That(ex.Message, Contains.Substring("Operation Op1 failed after 0 retries"));
    }

    [Test]
    public async Task TaskManager_ConditionalBranch_ShouldSkipOperationWhenConditionIsFalse()
    {
        var taskManager = new TaskManager();
        var op2Executed = false;

        taskManager.AddOperation("Op1", async context =>
        {
            context.Cache.Set("flag", false);
            return new OperationResult { Success = true };
        });

        taskManager.AddConditionalBranch("Op2", context => Task.FromResult(context.Cache.Get<bool>("flag")));

        taskManager.AddOperation("Op2", async context =>
        {
            op2Executed = true;
            return new OperationResult { Success = true };
        });

        await taskManager.ExecuteAsync();

        Assert.That(op2Executed, Is.False, "Op2 should not have been executed");
    }

    [Test]
    public async Task TaskManager_ProgressReporting_ShouldReportCorrectProgress()
    {
        var taskManager = new TaskManager();
        taskManager.AddOperation("Op1", async context =>
        {
            await Task.Delay(10); // Small delay to simulate work
            return new OperationResult { Success = true };
        });
        taskManager.AddOperation("Op2", async context =>
        {
            await Task.Delay(10); // Small delay to simulate work
            return new OperationResult { Success = true };
        });

        var reportedProgress = new List<(string OperationName, int ProgressPercentage)>();
        var progress = new Progress<(string, int)>(p =>
        {
            reportedProgress.Add(p);
            Console.WriteLine($"Progress reported: {p.Item1} - {p.Item2}%");
        });

        await taskManager.ExecuteAsync(progress);

        Trace.Flush(); // Ensure all debug output is captured

        Console.WriteLine("Debug output:");
        Console.WriteLine(debugOutput.ToString());

        Console.WriteLine("Reported progress:");
        foreach (var p in reportedProgress)
        {
            Console.WriteLine($"{p.OperationName} - {p.ProgressPercentage}%");
        }

        Assert.That(reportedProgress, Has.Count.EqualTo(2), "Expected progress to be reported twice");
        Assert.That(reportedProgress[0].OperationName, Is.EqualTo("Op1"), "First progress report should be for Op1");
        Assert.That(reportedProgress[0].ProgressPercentage, Is.EqualTo(50), "First progress report should be 50%");
        Assert.That(reportedProgress[1].OperationName, Is.EqualTo("Op2"), "Second progress report should be for Op2");
        Assert.That(reportedProgress[1].ProgressPercentage, Is.EqualTo(100), "Second progress report should be 100%");
    }

    [Test]
    public async Task TaskManager_ParallelExecution_ShouldExecuteInParallel()
    {
        var taskManager = new TaskManager();
        var executionTimes = new List<long>();

        for (var i = 0; i < 3; i++)
        {
            var index = i;
            taskManager.AddOperation($"Op{index}", async context =>
            {
                var sw = Stopwatch.StartNew();
                await Task.Delay(1000);
                sw.Stop();
                executionTimes.Add(sw.ElapsedMilliseconds);
                return new OperationResult { Success = true };
            }, new ExecutionOptions { AllowParallel = true });
        }

        var result = await taskManager.ExecuteAsync();

        Assert.That(result.Success, Is.True);
        Assert.That(executionTimes.Max(), Is.LessThan(2000), "Operations should execute in parallel");
    }

    [Test]
    public async Task TaskManager_RetryMechanism_ShouldRetryFailedOperations()
    {
        var taskManager = new TaskManager();
        var attempts = 0;

        taskManager.AddOperation("FailingOp", async context =>
        {
            attempts++;
            if (attempts < 3)
            {
                return new OperationResult { Success = false, Message = "Temporary failure" };
            }

            return new OperationResult { Success = true };
        }, new ExecutionOptions { MaxRetries = 2, RetryDelay = TimeSpan.FromMilliseconds(100) });

        var result = await taskManager.ExecuteAsync();

        Assert.That(result.Success, Is.True);
        Assert.That(attempts, Is.EqualTo(3));
    }

    [Test]
    public async Task TaskManager_ComplexBranching_ShouldExecuteCorrectBranch()
    {
        var taskManager = new TaskManager();
        var executedOperations = new List<string>();

        taskManager.AddOperation("Op1", async context =>
        {
            context.Cache.Set("flag", true);
            executedOperations.Add("Op1");
            return new OperationResult { Success = true };
        });

        taskManager.AddConditionalBranch("Op2", context => Task.FromResult(context.Cache.Get<bool>("flag")));

        taskManager.AddOperation("Op2", async context =>
        {
            executedOperations.Add("Op2");
            return new OperationResult { Success = true };
        });

        taskManager.AddOperation("Op3", async context =>
        {
            executedOperations.Add("Op3");
            return new OperationResult { Success = true };
        });

        var result = await taskManager.ExecuteAsync();

        Assert.That(result.Success, Is.True);
        Assert.That(executedOperations, Is.EqualTo(new[] { "Op1", "Op2", "Op3" }));
    }

    [Test]
    public async Task TaskManager_DetailedLogging_ShouldLogOperationTimes()
    {
        var taskManager = new TaskManager();

        taskManager.AddOperation("Op1", async context =>
        {
            await Task.Delay(100);
            return new OperationResult { Success = true };
        });

        await taskManager.ExecuteAsync();

        Trace.Flush();
        var log = debugOutput.ToString();

        Assert.That(log, Contains.Substring("Operation Op1 completed in"));
        Assert.That(log, Contains.Substring("All operations completed in"));
    }

    [Test]
    public async Task TaskManager_Cancellation_ShouldCancelExecution()
    {
        var taskManager = new TaskManager();
        var longOperationCancelled = false;

        taskManager.AddOperation("Op1", async context =>
        {
            await Task.Delay(100);
            return new OperationResult { Success = true };
        });

        taskManager.AddOperation("LongOp", async context =>
        {
            try
            {
                await Task.Delay(5000, context.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                longOperationCancelled = true;
                throw;
            }

            return new OperationResult { Success = true };
        });

        var executionTask = taskManager.ExecuteAsync();

        await Task.Delay(200); // Wait for the first operation to complete

        taskManager.Cancel();

        try
        {
            await executionTask;
            Assert.Fail(
                "Expected an OperationCanceledException or TaskCanceledException, but no exception was thrown.");
        }
        catch (Exception ex)
        {
            Assert.That(ex, Is.InstanceOf<OperationCanceledException>(),
                "Expected OperationCanceledException or a derived type.");
            Assert.That(longOperationCancelled, Is.True, "The long operation should have been cancelled.");
        }
    }

    [Test]
    public async Task TaskManager_PauseAndResume_ShouldPauseAndResumeExecution()
    {
        var taskManager = new TaskManager();
        var operationStartTimes = new List<long>();
        var operationEndTimes = new List<long>();
        var pausedAfterOp = new TaskCompletionSource<bool>();

        taskManager.AddOperation("Op1", async context =>
        {
            operationStartTimes.Add(DateTimeOffset.Now.ToUnixTimeMilliseconds());
            await Task.Delay(100);
            operationEndTimes.Add(DateTimeOffset.Now.ToUnixTimeMilliseconds());
            return new OperationResult { Success = true };
        });

        taskManager.AddOperation("Op2", async context =>
        {
            operationStartTimes.Add(DateTimeOffset.Now.ToUnixTimeMilliseconds());
            pausedAfterOp.SetResult(true);
            await Task.Delay(100);
            operationEndTimes.Add(DateTimeOffset.Now.ToUnixTimeMilliseconds());
            return new OperationResult { Success = true };
        });

        taskManager.AddOperation("Op3", async context =>
        {
            operationStartTimes.Add(DateTimeOffset.Now.ToUnixTimeMilliseconds());
            await Task.Delay(100);
            operationEndTimes.Add(DateTimeOffset.Now.ToUnixTimeMilliseconds());
            return new OperationResult { Success = true };
        });

        var executionTask = taskManager.ExecuteAsync();

        await pausedAfterOp.Task;
        taskManager.Pause();

        await Task.Delay(2000); // Pause for 2 seconds

        taskManager.Resume();

        var result = await executionTask;

        Assert.That(result.Success, Is.True);

        // Check that there was a significant delay between Op2 and Op3
        var delayBetweenOp2AndOp3 = operationStartTimes[2] - operationEndTimes[1];
        Assert.That(delayBetweenOp2AndOp3, Is.GreaterThan(1900),
            "The pause should cause a delay of at least 1900ms between Op2 and Op3");

        // Output timing information for debugging
        Console.WriteLine($"Op1 Start: {operationStartTimes[0]}, End: {operationEndTimes[0]}");
        Console.WriteLine($"Op2 Start: {operationStartTimes[1]}, End: {operationEndTimes[1]}");
        Console.WriteLine($"Op3 Start: {operationStartTimes[2]}, End: {operationEndTimes[2]}");
        Console.WriteLine($"Delay between Op2 and Op3: {delayBetweenOp2AndOp3}ms");
    }
}
