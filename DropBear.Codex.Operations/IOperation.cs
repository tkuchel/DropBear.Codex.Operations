using DropBear.Codex.Core;

namespace DropBear.Codex.Operations;

public interface IOperation
{
    Task<Result> ExecuteAsync(CancellationToken cancellationToken = default);
    Task<Result> RollbackAsync(CancellationToken cancellationToken = default);

    TimeSpan ExecuteTimeout { get; set; }
    TimeSpan RollbackTimeout { get; set; }

    bool ContinueOnFailure { get; set; }

    event EventHandler<ProgressEventArgs> ProgressChanged;
    event EventHandler<LogEventArgs> Log;
}

public interface IOperation<T> : IOperation
{
    new Task<Result<T>> ExecuteAsync(CancellationToken cancellationToken = default);
}
