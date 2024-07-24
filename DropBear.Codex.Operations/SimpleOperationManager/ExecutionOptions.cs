namespace DropBear.Codex.Operations.SimpleOperationManager;

public class ExecutionOptions
{
    public bool AllowParallel { get; set; }
    public int MaxRetries { get; set; }
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
}
