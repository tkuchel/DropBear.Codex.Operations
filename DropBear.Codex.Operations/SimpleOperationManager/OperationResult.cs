namespace DropBear.Codex.Operations.SimpleOperationManager;

public record OperationResult
{

    public OperationResult()
    {
        Success = false;
        Message = string.Empty;
        Exception = null;
    }
    public OperationResult(bool success, string message, Exception exception)
    {
        Success = success;
        Message = message;
        Exception = exception;
    }

    public bool Success { get; init; }
    public string Message { get; init; }
    public Exception? Exception { get; init; }
}
