using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TourDock.Api.IntegrationTests;

/// <summary>
/// Keeps every log entry: the formatted message, each structured value and the active scopes (which carry TraceId and
/// SpanId), much as the JSON console would write them.
/// </summary>
public sealed class LogCapture : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public ConcurrentQueue<string> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class Logger(string category, LogCapture owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(" ", pairs.Select(p => $"{p.Key}={p.Value}")) : string.Empty;
            var scopes = new List<string>();
            owner._scopes.ForEachScope((scope, list) =>
            {
                list.Add(scope is IEnumerable<KeyValuePair<string, object?>> kv ? string.Join(" ", kv.Select(p => $"{p.Key}={p.Value}")) : scope?.ToString() ?? string.Empty);
            }, scopes);
            owner.Entries.Enqueue($"{logLevel} {category}: {formatter(state, exception)} | {values} | scopes: {string.Join("; ", scopes)} | {exception}");
        }
    }
}
