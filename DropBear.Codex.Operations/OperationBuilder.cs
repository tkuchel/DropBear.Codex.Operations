using DropBear.Codex.Core;

namespace DropBear.Codex.Operations;

public class OperationBuilder
{
    private bool _continueOnFailure;
    private Func<CancellationToken, Task<Result>> _executeAsync;
    private TimeSpan _executeTimeout = TimeSpan.FromMinutes(1);
    private EventHandler<LogEventArgs> _log;
    private EventHandler<ProgressEventArgs> _progressChanged;
    private Func<CancellationToken, Task<Result>> _rollbackAsync;
    private TimeSpan _rollbackTimeout = TimeSpan.FromMinutes(1);

    public OperationBuilder WithExecuteAsync(Func<CancellationToken, Task<Result>> executeAsync)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        return this;
    }

    public OperationBuilder WithRollbackAsync(Func<CancellationToken, Task<Result>> rollbackAsync)
    {
        _rollbackAsync = rollbackAsync ?? throw new ArgumentNullException(nameof(rollbackAsync));
        return this;
    }

    public OperationBuilder WithExecuteTimeout(TimeSpan timeout)
    {
        _executeTimeout = timeout;
        return this;
    }

    public OperationBuilder WithRollbackTimeout(TimeSpan timeout)
    {
        _rollbackTimeout = timeout;
        return this;
    }

    public OperationBuilder WithContinueOnFailure(bool continueOnFailure)
    {
        _continueOnFailure = continueOnFailure;
        return this;
    }

    public OperationBuilder OnProgressChanged(EventHandler<ProgressEventArgs> handler)
    {
        _progressChanged = handler;
        return this;
    }

    public OperationBuilder OnLog(EventHandler<LogEventArgs> handler)
    {
        _log = handler;
        return this;
    }

    public IOperation Build()
    {
        var operation = new Operation
        {
            ExecuteAsync = _executeAsync,
            RollbackAsync = _rollbackAsync,
            ExecuteTimeout = _executeTimeout,
            RollbackTimeout = _rollbackTimeout,
            ContinueOnFailure = _continueOnFailure
        };

        if (_progressChanged != null) operation.ProgressChanged += _progressChanged;

        if (_log != null) operation.Log += _log;

        return operation;
    }

    private class Operation : IOperation
    {
        public Func<CancellationToken, Task<Result>> ExecuteAsync { get; set; }
        public Func<CancellationToken, Task<Result>> RollbackAsync { get; set; }
        public TimeSpan ExecuteTimeout { get; set; }
        public TimeSpan RollbackTimeout { get; set; }
        public bool ContinueOnFailure { get; set; }
        public event EventHandler<ProgressEventArgs> ProgressChanged;
        public event EventHandler<LogEventArgs> Log;

        async Task<Result> IOperation.ExecuteAsync(CancellationToken cancellationToken)
        {
            var result = await ExecuteAsync(cancellationToken).ConfigureAwait(false);
            OnProgressChanged(new ProgressEventArgs(100, "Operation completed."));
            OnLog(new LogEventArgs("Operation completed."));
            return result;
        }

        async Task<Result> IOperation.RollbackAsync(CancellationToken cancellationToken) =>
            await RollbackAsync(cancellationToken).ConfigureAwait(false);

        protected virtual void OnProgressChanged(ProgressEventArgs e) => ProgressChanged?.Invoke(this, e);

        protected virtual void OnLog(LogEventArgs e) => Log?.Invoke(this, e);
    }
}

public class OperationBuilder<T>
{
    private bool _continueOnFailure;
    private Func<CancellationToken, Task<Result<T>>> _executeAsync;
    private TimeSpan _executeTimeout = TimeSpan.FromMinutes(1);
    private EventHandler<LogEventArgs> _log;
    private EventHandler<ProgressEventArgs> _progressChanged;
    private Func<CancellationToken, Task<Result>> _rollbackAsync;
    private TimeSpan _rollbackTimeout = TimeSpan.FromMinutes(1);

    public OperationBuilder<T> WithExecuteAsync(Func<CancellationToken, Task<Result<T>>> executeAsync)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        return this;
    }

    public OperationBuilder<T> WithRollbackAsync(Func<CancellationToken, Task<Result>> rollbackAsync)
    {
        _rollbackAsync = rollbackAsync ?? throw new ArgumentNullException(nameof(rollbackAsync));
        return this;
    }

    public OperationBuilder<T> WithExecuteTimeout(TimeSpan timeout)
    {
        _executeTimeout = timeout;
        return this;
    }

    public OperationBuilder<T> WithRollbackTimeout(TimeSpan timeout)
    {
        _rollbackTimeout = timeout;
        return this;
    }

    public OperationBuilder<T> WithContinueOnFailure(bool continueOnFailure)
    {
        _continueOnFailure = continueOnFailure;
        return this;
    }

    public OperationBuilder<T> OnProgressChanged(EventHandler<ProgressEventArgs> handler)
    {
        _progressChanged = handler;
        return this;
    }

    public OperationBuilder<T> OnLog(EventHandler<LogEventArgs> handler)
    {
        _log = handler;
        return this;
    }

    public IOperation<T> Build()
    {
        var operation = new Operation
        {
            ExecuteAsync = _executeAsync,
            RollbackAsync = _rollbackAsync,
            ExecuteTimeout = _executeTimeout,
            RollbackTimeout = _rollbackTimeout,
            ContinueOnFailure = _continueOnFailure
        };

        if (_progressChanged != null) operation.ProgressChanged += _progressChanged;

        if (_log != null) operation.Log += _log;

        return operation;
    }

    private class Operation : IOperation<T>
    {
        public Func<CancellationToken, Task<Result<T>>> ExecuteAsync { get; set; }
        public Func<CancellationToken, Task<Result>> RollbackAsync { get; set; }
        public TimeSpan ExecuteTimeout { get; set; }
        public TimeSpan RollbackTimeout { get; set; }
        public bool ContinueOnFailure { get; set; }
        public event EventHandler<ProgressEventArgs> ProgressChanged;
        public event EventHandler<LogEventArgs> Log;

        async Task<Result<T>> IOperation<T>.ExecuteAsync(CancellationToken cancellationToken)
        {
            var result = await ExecuteAsync(cancellationToken).ConfigureAwait(false);
            OnProgressChanged(new ProgressEventArgs(100, "Operation completed."));
            OnLog(new LogEventArgs("Operation completed."));
            return result;
        }

        async Task<Result> IOperation.ExecuteAsync(CancellationToken cancellationToken)
        {
            var result = await ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return result.IsSuccess
                ? Result.Success()
                : Result.Failure(result.ErrorMessage, result.Exception);
        }

        async Task<Result> IOperation.RollbackAsync(CancellationToken cancellationToken) =>
            await RollbackAsync(cancellationToken).ConfigureAwait(false);

        protected virtual void OnProgressChanged(ProgressEventArgs e) => ProgressChanged?.Invoke(this, e);

        protected virtual void OnLog(LogEventArgs e) => Log?.Invoke(this, e);
    }
}
