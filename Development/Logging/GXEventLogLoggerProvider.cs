using Microsoft.Extensions.Logging;

namespace Gurux.Data.Relay.Log;

public sealed class GXEventLogLoggerProvider : ILoggerProvider
{
    private readonly IGXEventLogService _eventLogService;

    public GXEventLogLoggerProvider(IGXEventLogService eventLogService)
    {
        _eventLogService = eventLogService;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new GXEventLogLogger(categoryName, _eventLogService);
    }

    public void Dispose()
    {
    }

    private sealed class GXEventLogLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly IGXEventLogService _eventLogService;

        public GXEventLogLogger(string categoryName, IGXEventLogService eventLogService)
        {
            _categoryName = categoryName;
            _eventLogService = eventLogService;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            // Reading live views produces HTTP/SignalR diagnostics. Persisting those
            // as events would trigger another read and create a notification loop.
            if (_categoryName.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                && logLevel < LogLevel.Warning) return false;
            return _eventLogService.IsEnabled(logLevel);
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            try
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                string message = formatter(state, exception);
                IReadOnlyDictionary<string, object?>? values = state is IReadOnlyList<KeyValuePair<string, object?>> pairs
                    ? pairs
                        .Where(item => item.Key is not null)
                        .GroupBy(item => item.Key)
                        .ToDictionary(item => item.Key, item => item.Last().Value)
                    : null;
                _eventLogService.Write(logLevel, _categoryName, eventId, message, exception, values);
            }
            catch
            {
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}

