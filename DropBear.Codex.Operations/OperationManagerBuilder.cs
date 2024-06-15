﻿namespace DropBear.Codex.Operations;

public class OperationManagerBuilder : IDisposable
{
    private readonly OperationManager _operationManager;
    private bool _disposed;

    public OperationManagerBuilder() => _operationManager = new OperationManager();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing) _operationManager.Dispose();

        _disposed = true;
    }

    public OperationManagerBuilder WithOperation(IOperation operation)
    {
        _operationManager.AddOperation(operation);
        return this;
    }

    public OperationManagerBuilder OnOperationStarted(EventHandler<EventArgs> handler)
    {
        _operationManager.OperationStarted += handler;
        return this;
    }

    public OperationManagerBuilder OnOperationCompleted(EventHandler<EventArgs> handler)
    {
        _operationManager.OperationCompleted += handler;
        return this;
    }

    public OperationManagerBuilder OnOperationFailed(EventHandler<OperationFailedEventArgs> handler)
    {
        _operationManager.OperationFailed += handler;
        return this;
    }

    public OperationManagerBuilder OnRollbackStarted(EventHandler<EventArgs> handler)
    {
        _operationManager.RollbackStarted += handler;
        return this;
    }

    public OperationManagerBuilder OnProgressChanged(EventHandler<ProgressEventArgs> handler)
    {
        _operationManager.ProgressChanged += handler;
        return this;
    }

    public OperationManagerBuilder OnLog(EventHandler<LogEventArgs> handler)
    {
        _operationManager.Log += handler;
        return this;
    }

    public OperationManager Build()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(OperationManagerBuilder));

        return _operationManager;
    }
}
