﻿// File: LogEventArgs.cs
// Description: Defines the event arguments for logging messages.

namespace DropBear.Codex.Operations.StandardOperationManager;

/// <summary>
///     Provides data for the log event.
/// </summary>
public class LogEventArgs : EventArgs
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="LogEventArgs" /> class.
    /// </summary>
    /// <param name="message">The log message.</param>
    public LogEventArgs(string message) => Message = message;

    /// <summary>
    ///     Gets the log message.
    /// </summary>
    public string Message { get; }
}
