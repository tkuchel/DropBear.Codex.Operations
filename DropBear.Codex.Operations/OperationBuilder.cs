using DropBear.Codex.Core;

namespace DropBear.Codex.Operations;

public class OperationBuilder
{
    private bool _continueOnFailure;
    private Func<CancellationToken, Task<Result>>? _executeAsync;
    private Func<Dictionary<string, object>, CancellationToken, Task<Result>>? _executeAsyncWithParams;
    private TimeSpan _executeTimeout = TimeSpan.FromMinutes(1);
    private EventHandler<LogEventArgs>? _log;
    private Dictionary<string, object> _parameters = new(StringComparer.OrdinalIgnoreCase);
    private EventHandler<ProgressEventArgs>? _progressChanged;
    private Func<CancellationToken, Task<Result>>? _rollbackAsync;
    private Func<Dictionary<string, object>, CancellationToken, Task<Result>>? _rollbackAsyncWithParams;
    private TimeSpan _rollbackTimeout = TimeSpan.FromMinutes(1);

    public OperationBuilder WithExecuteAsync(Func<CancellationToken, Task<Result>> executeAsync)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        return this;
    }

    public OperationBuilder WithExecuteAsync(
        Func<Dictionary<string, object>, CancellationToken, Task<Result>> executeAsyncWithParams)
    {
        _executeAsyncWithParams =
            executeAsyncWithParams ?? throw new ArgumentNullException(nameof(executeAsyncWithParams));
        return this;
    }

    public OperationBuilder WithRollbackAsync(Func<CancellationToken, Task<Result>> rollbackAsync)
    {
        _rollbackAsync = rollbackAsync ?? throw new ArgumentNullException(nameof(rollbackAsync));
        return this;
    }

    public OperationBuilder WithRollbackAsync(
        Func<Dictionary<string, object>, CancellationToken, Task<Result>> rollbackAsyncWithParams)
    {
        _rollbackAsyncWithParams =
            rollbackAsyncWithParams ?? throw new ArgumentNullException(nameof(rollbackAsyncWithParams));
        return this;
    }

    public OperationBuilder WithParameters(Dictionary<string, object> parameters)
    {
        _parameters = new Dictionary<string, object>(parameters, StringComparer.OrdinalIgnoreCase);
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
            ExecuteAsyncWithParams = _executeAsyncWithParams,
            RollbackAsync = _rollbackAsync,
            RollbackAsyncWithParams = _rollbackAsyncWithParams,
            ExecuteTimeout = _executeTimeout,
            RollbackTimeout = _rollbackTimeout,
            ContinueOnFailure = _continueOnFailure,
            Parameters = _parameters
        };

        if (_progressChanged is not null) operation.ProgressChanged += _progressChanged;

        if (_log is not null) operation.Log += _log;

        return operation;
    }

    private sealed class Operation : IOperation
    {
        public Func<CancellationToken, Task<Result>>? ExecuteAsync { get; init; }
        public Func<Dictionary<string, object>, CancellationToken, Task<Result>>? ExecuteAsyncWithParams { get; init; }
        public Func<CancellationToken, Task<Result>>? RollbackAsync { get; init; }
        public Func<Dictionary<string, object>, CancellationToken, Task<Result>>? RollbackAsyncWithParams { get; init; }
        public TimeSpan ExecuteTimeout { get; set; }
        public TimeSpan RollbackTimeout { get; set; }
        public bool ContinueOnFailure { get; set; }
        public Dictionary<string, object> Parameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public event EventHandler<ProgressEventArgs>? ProgressChanged;
        public event EventHandler<LogEventArgs>? Log;

        async Task<Result> IOperation.ExecuteAsync(CancellationToken cancellationToken)
        {
            if (ExecuteAsync is null && ExecuteAsyncWithParams is null)
                return Result.Failure("Execute not implemented.");

            var result = ExecuteAsyncWithParams is not null
                ? await ExecuteAsyncWithParams(Parameters, cancellationToken).ConfigureAwait(false)
                : await ExecuteAsync!(cancellationToken).ConfigureAwait(false);

            OnProgressChanged(new ProgressEventArgs(100, "Operation completed."));
            OnLog(new LogEventArgs("Operation completed."));
            return result;
        }

        async Task<Result> IOperation.RollbackAsync(CancellationToken cancellationToken)
        {
            if (RollbackAsync is null && RollbackAsyncWithParams is null)
                return Result.Failure("Rollback not implemented.");

            return RollbackAsyncWithParams is not null
                ? await RollbackAsyncWithParams(Parameters, cancellationToken).ConfigureAwait(false)
                : await RollbackAsync!(cancellationToken).ConfigureAwait(false);
        }

        private void OnProgressChanged(ProgressEventArgs e) => ProgressChanged?.Invoke(this, e);

        private void OnLog(LogEventArgs e) => Log?.Invoke(this, e);
    }
}

public class OperationBuilder<T>
{
    private bool _continueOnFailure;
    private Func<CancellationToken, Task<Result<T>>>? _executeAsync;
    private Func<Dictionary<string, object>, CancellationToken, Task<Result<T>>>? _executeAsyncWithParams;
    private TimeSpan _executeTimeout = TimeSpan.FromMinutes(1);
    private EventHandler<LogEventArgs>? _log;
    private Dictionary<string, object> _parameters = new(StringComparer.OrdinalIgnoreCase);
    private EventHandler<ProgressEventArgs>? _progressChanged;
    private Func<CancellationToken, Task<Result>>? _rollbackAsync;
    private Func<Dictionary<string, object>, CancellationToken, Task<Result>>? _rollbackAsyncWithParams;
    private TimeSpan _rollbackTimeout = TimeSpan.FromMinutes(1);

    public OperationBuilder<T> WithExecuteAsync(Func<CancellationToken, Task<Result<T>>> executeAsync)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        return this;
    }

    public OperationBuilder<T> WithExecuteAsync(
        Func<Dictionary<string, object>, CancellationToken, Task<Result<T>>> executeAsyncWithParams)
    {
        _executeAsyncWithParams =
            executeAsyncWithParams ?? throw new ArgumentNullException(nameof(executeAsyncWithParams));
        return this;
    }

    public OperationBuilder<T> WithRollbackAsync(Func<CancellationToken, Task<Result>> rollbackAsync)
    {
        _rollbackAsync = rollbackAsync ?? throw new ArgumentNullException(nameof(rollbackAsync));
        return this;
    }

    public OperationBuilder<T> WithRollbackAsync(
        Func<Dictionary<string, object>, CancellationToken, Task<Result>> rollbackAsyncWithParams)
    {
        _rollbackAsyncWithParams =
            rollbackAsyncWithParams ?? throw new ArgumentNullException(nameof(rollbackAsyncWithParams));
        return this;
    }

    public OperationBuilder<T> WithParameters(Dictionary<string, object> parameters)
    {
        _parameters = new Dictionary<string, object>(parameters, StringComparer.OrdinalIgnoreCase);
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
            ExecuteAsyncWithParams = _executeAsyncWithParams,
            RollbackAsync = _rollbackAsync,
            RollbackAsyncWithParams = _rollbackAsyncWithParams,
            ExecuteTimeout = _executeTimeout,
            RollbackTimeout = _rollbackTimeout,
            ContinueOnFailure = _continueOnFailure,
            Parameters = _parameters
        };

        if (_progressChanged is not null) operation.ProgressChanged += _progressChanged;

        if (_log is not null) operation.Log += _log;

        return operation;
    }

    private sealed class Operation : IOperation<T>
    {
        public Func<CancellationToken, Task<Result<T>>>? ExecuteAsync { get; init; }
        public Func<Dictionary<string, object>, CancellationToken, Task<Result<T>>>? ExecuteAsyncWithParams { get; init; }
        public Func<CancellationToken, Task<Result>>? RollbackAsync { get; init; }
        public Func<Dictionary<string, object>, CancellationToken, Task<Result>>? RollbackAsyncWithParams { get; init; }
        public TimeSpan ExecuteTimeout { get; set; }
        public TimeSpan RollbackTimeout { get; set; }
        public bool ContinueOnFailure { get; set; }
        public Dictionary<string, object> Parameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public event EventHandler<ProgressEventArgs>? ProgressChanged;
        public event EventHandler<LogEventArgs>? Log;

        async Task<Result<T>> IOperation<T>.ExecuteAsync(CancellationToken cancellationToken)
        {
            if (ExecuteAsync is null && ExecuteAsyncWithParams is null)
                return Result<T>.Failure("Execute not implemented.");

            var result = ExecuteAsyncWithParams is not null
                ? await ExecuteAsyncWithParams(Parameters, cancellationToken).ConfigureAwait(false)
                : await ExecuteAsync!(cancellationToken).ConfigureAwait(false);

            OnProgressChanged(new ProgressEventArgs(100, "Operation completed."));
            OnLog(new LogEventArgs("Operation completed."));
            return result;
        }

        async Task<Result> IOperation.ExecuteAsync(CancellationToken cancellationToken)
        {
            if (ExecuteAsync is null && ExecuteAsyncWithParams is null)
                return Result.Failure("Execute not implemented.");
            
            var result = ExecuteAsyncWithParams is not null
                ? await ExecuteAsyncWithParams(Parameters, cancellationToken).ConfigureAwait(false)
                : await ExecuteAsync!(cancellationToken).ConfigureAwait(false);
            
            return result.IsSuccess
                ? Result.Success()
                : Result.Failure(result.ErrorMessage ?? "Unknown Error", result.Exception);
        }

        async Task<Result> IOperation.RollbackAsync(CancellationToken cancellationToken)
        {
            if (RollbackAsync is null && RollbackAsyncWithParams is null)
                return Result.Failure("Rollback not implemented.");
            
            return RollbackAsyncWithParams is not null
                ? await RollbackAsyncWithParams(Parameters, cancellationToken).ConfigureAwait(false)
                : await RollbackAsync!(cancellationToken).ConfigureAwait(false);
        }

        private void OnProgressChanged(ProgressEventArgs e) => ProgressChanged?.Invoke(this, e);

        private void OnLog(LogEventArgs e) => Log?.Invoke(this, e);
    }
}