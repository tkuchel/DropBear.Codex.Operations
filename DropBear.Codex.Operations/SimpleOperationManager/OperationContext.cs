namespace DropBear.Codex.Operations.SimpleOperationManager;

public class OperationContext
{
    public SharedCache Cache { get; } = new();
    public CancellationToken CancellationToken { get; set; }
}
