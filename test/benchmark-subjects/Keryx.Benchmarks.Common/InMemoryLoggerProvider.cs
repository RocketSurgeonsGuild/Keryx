using Microsoft.Extensions.Logging;

namespace Keryx.Benchmarks.Common;

/// <summary>
///     A logging provider that really formats every message (it calls the formatter, as a console or file provider does) and keeps the last
///     <see cref="Capacity" /> messages in a ring buffer, so it does not grow across benchmark iterations. Each formatted message is a
///     <see cref="Probe" /> hit, so <c>--verify</c> proves the logging behavior's messages were written.
/// </summary>
public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    /// <summary>How many messages the ring buffer keeps.</summary>
    public const int Capacity = 1024;

    private readonly string?[] _messages = new string?[Capacity];
    private long _written;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new Logger(this);

    /// <inheritdoc />
    public void Dispose() { }

    private void Write(string message)
    {
        var index = Interlocked.Increment(ref _written) - 1;
        _messages[index % Capacity] = message;
        Probe.Hit();
    }

    private sealed class Logger(InMemoryLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                provider.Write(formatter(state, exception));
            }
        }
    }
}
