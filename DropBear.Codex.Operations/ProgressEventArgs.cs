namespace DropBear.Codex.Operations;

public class ProgressEventArgs : EventArgs
{
    public ProgressEventArgs(int progressPercentage, string message = "")
    {
        ProgressPercentage = progressPercentage;
        Message = message;
    }

    public int ProgressPercentage { get; }
    public string Message { get; }
}
