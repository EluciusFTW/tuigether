using System.Collections.Concurrent;
using JasperFx;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Tuigether.Server.Specs;

/// <summary>
/// Keeps every error with an exception that one node logs. A handler that fails on a node that no tracked session
/// watches shows only here.
/// </summary>
public sealed class NodeErrors : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _errors = new();

    public IReadOnlyList<string> All => [.. _errors];

    public void Clear() => _errors.Clear();

    public ILogger CreateLogger(string categoryName) => new ErrorLogger(categoryName, _errors);

    public void Dispose() { }

    private sealed class ErrorLogger(string category, ConcurrentQueue<string> errors) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel) && exception is not null && !IsRetriedConflict(exception))
            {
                errors.Enqueue($"{category}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        // Two handlers that append to one stream at the same moment: Marten logs the conflict, and Wolverine runs
        // the second handler again. A spec of such a race checks the outcome, not the log.
        private static bool IsRetriedConflict(Exception exception) =>
            exception is ConcurrencyException
                or PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "pk_mt_events_stream_and_version" }
                or PostgresException { SqlState: MartenStreamVersionMismatch };

        // Marten's append function raises it when the expected version of a stream is not its current version.
        private const string MartenStreamVersionMismatch = "MT003";
    }
}
