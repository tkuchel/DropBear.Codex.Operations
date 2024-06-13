using DropBear.Codex.Core;

namespace DropBear.Codex.Operations;

public interface IOperation
{
    Task<Result> ExecuteAsync(CancellationToken cancellationToken = default);
    Task<Result> RollbackAsync(CancellationToken cancellationToken = default);
}