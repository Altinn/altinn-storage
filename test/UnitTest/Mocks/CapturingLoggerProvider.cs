using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.UnitTest.Mocks;

/// <summary>
/// A logger provider that keeps every log entry its loggers write, for asserting on logs.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<Entry> _entries = new();

    /// <summary>
    /// Gets the entries written so far, oldest first.
    /// </summary>
    public IReadOnlyList<Entry> Entries => [.. _entries];

    /// <summary>
    /// Creates a typed logger that writes to this provider.
    /// </summary>
    public ILogger<T> CreateLogger<T>() => new Logger<T>(new LoggerFactory([this]));

    /// <summary>
    /// Gets the messages of the entries with the given level.
    /// </summary>
    public IReadOnlyList<string> Messages(LogLevel level) =>
        [.. Entries.Where(entry => entry.Level == level).Select(entry => entry.Message)];

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    /// <inheritdoc/>
    public void Dispose() { }

    /// <summary>
    /// A log entry.
    /// </summary>
    /// <param name="Category">The logger category.</param>
    /// <param name="Level">The log level.</param>
    /// <param name="Message">The formatted message.</param>
    public sealed record Entry(string Category, LogLevel Level, string Message);

    private sealed class CapturingLogger(string category, ConcurrentQueue<Entry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            entries.Enqueue(new Entry(category, logLevel, formatter(state, exception)));
        }
    }
}
