using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Gurux.Data.Relay.Log;

public interface IGXEventLogService
{
    Task InitializeAsync(Configuration.GXModeConfiguration configuration, CancellationToken cancellationToken);

    bool IsEnabled(LogLevel level);

    void Write(LogLevel level, string categoryName, EventId eventId, string message, Exception? exception, IReadOnlyDictionary<string, object?>? state);
}
