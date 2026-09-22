using Gurux.Data.Relay.Realtime;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Database;
using Gurux.Service.Orm;
using Gurux.Service.Orm.Model;
using Microsoft.Extensions.Logging;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Shared;

namespace Gurux.Data.Relay.Log;

public sealed class GXEventLogService : IGXEventLogService
{
    private readonly IGXRelayChangePublisher? _changes;

    private readonly IGXDatabaseConnectionFactory _connectionFactory;
    private readonly GXConfigurationStoreSettings _storeSettings;
    private ConcurrentQueue<GXEventLog> _pendingEntries => State.Pending;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private sealed class ModeState
    {
        public readonly ConcurrentQueue<GXEventLog> Pending = new();
        public GXDatabaseConfiguration? Configuration;
        public LogLevel EventLogLevel = LogLevel.Information;
        public LogLevel CommunicationLogLevel = LogLevel.Information;
        public ApplicationMode Mode;
        public bool TableReady;
    }
    private readonly ModeState _defaultState = new();
    private readonly ConcurrentDictionary<ApplicationMode, ModeState> _modeStates = new();
    private ModeState State => GXRelayModeContext.Current.Value is { } mode
        ? _modeStates.GetOrAdd(mode, _ => new ModeState { Mode = mode }) : _defaultState;
    private GXDatabaseConfiguration? _configuration { get => State.Configuration; set => State.Configuration = value; }
    private LogLevel _eventLogLevel { get => _storeSettings.EventLogLevel ?? State.EventLogLevel; set => State.EventLogLevel = value; }
    private LogLevel _communicationLogLevel { get => _storeSettings.CommunicationLogLevel ?? State.CommunicationLogLevel; set => State.CommunicationLogLevel = value; }
    private ApplicationMode _mode { get => State.Mode; set => State.Mode = value; }
    private bool _tableReady { get => State.TableReady; set => State.TableReady = value; }
    public GXEventLogService(
        IGXDatabaseConnectionFactory connectionFactory,
        GXConfigurationStoreSettings storeSettings, IGXRelayChangePublisher? changes = null)
    {
        _connectionFactory = connectionFactory;
        _storeSettings = storeSettings; _changes = changes;
    }

    public async Task InitializeAsync(GXModeConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _mode = configuration.Mode;
            _eventLogLevel = configuration.EventLogLevel ?? LogLevel.Information;
            _communicationLogLevel = configuration.CommunicationLogLevel ?? _eventLogLevel;
            _configuration = new GXDatabaseConfiguration
            {
                Description = "RelayConfigurationStore",
                Type = _storeSettings.Type,
                ConnectionString = _storeSettings.ConnectionString,
            };

            try
            {
                await EnsureTableAsync(cancellationToken);
                while (_pendingEntries.TryDequeue(out GXEventLog? entry))
                {
                    entry.Mode = _mode;
                    await InsertAsync(entry, cancellationToken);
                }
            }
            catch
            {
                _configuration = null;
                _tableReady = false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool IsEnabled(LogLevel level)
    {
        LogLevel logLevel = level;
        return IsLogEnabled(logLevel, _eventLogLevel) ||
            IsLogEnabled(logLevel, _communicationLogLevel);
    }

    public void Write(
        LogLevel level,
        string categoryName,
        EventId eventId,
        string message,
        Exception? exception,
        IReadOnlyDictionary<string, object?>? state)
    {
        LogLevel logLevel = level;
        LogLevel configuredLevel = IsCommunicationCategory(categoryName) ? _communicationLogLevel : _eventLogLevel;
        if (!IsLogEnabled(logLevel, configuredLevel))
        {
            return;
        }

        GXEventLog entry = new()
        {
            Timestamp = DateTime.UtcNow,
            Level = logLevel,
            Mode = _mode,
            DatabaseIndex = GXEventLogContext.DatabaseIndex ??
                (state != null && state.TryGetValue("DatabaseIndex", out object? index) && index is int value ? value : null),
            Source = $"{level}:{categoryName}",
            Message = string.IsNullOrWhiteSpace(message) ? exception?.Message : message,
            Data = CreateData(eventId, exception, state),
        };

        _gate.Wait();
        try
        {
            if (_configuration is null)
            {
                _pendingEntries.Enqueue(entry);
                return;
            }

            try
            {
                EnsureTableAsync(CancellationToken.None).GetAwaiter().GetResult();
                InsertAsync(entry, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch
            {
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        if (_tableReady)
        {
            return;
        }

        await using DbConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using GXDbConnection guruxConnection = new(connection);
        GXSchemaManager schemaManager = new(guruxConnection);
        if (!schemaManager.TableExist(nameof(GXEventLog)))
        {
            schemaManager.CreateTable<GXEventLog>();
        }

        _tableReady = true;
    }

    private async Task InsertAsync(GXEventLog entry, CancellationToken cancellationToken)
    {
        await using DbConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using GXDbConnection guruxConnection = new(connection);
        entry.Settings = await GXLogParent.ResolveAsync(guruxConnection, entry.Mode, cancellationToken);
        GXEntityPersistence.Initialize(entry);
        await guruxConnection.InsertAsync(GXInsertArgs.Insert(entry));
        if (_storeSettings?.NotifyLogChanges == true)
            _changes?.Publish(new(entry.Mode.ToString().ToLowerInvariant(), GXRelayChangeKind.Events));
    }

    private DbConnection CreateConnection()
    {
        return _connectionFactory.CreateConnection(_configuration ?? throw new InvalidOperationException("Event log service is not initialized."));
    }

    private static bool IsLogEnabled(LogLevel level, LogLevel configuredLevel)
    {
        return level != LogLevel.None &&
            configuredLevel != LogLevel.None &&
            level >= configuredLevel;
    }

    private static bool IsCommunicationCategory(string categoryName)
    {
        return categoryName.Contains("Transport", StringComparison.OrdinalIgnoreCase) ||
            categoryName.Contains("GXTcp", StringComparison.OrdinalIgnoreCase) ||
            categoryName.Contains("GXMqtt", StringComparison.OrdinalIgnoreCase) ||
            categoryName.Contains("GXClientBatchSender", StringComparison.OrdinalIgnoreCase) ||
            categoryName.Contains("GXClientSchemaSender", StringComparison.OrdinalIgnoreCase);
    }

    private string? CreateData(
        EventId eventId,
        Exception? exception,
        IReadOnlyDictionary<string, object?>? state)
    {
        Dictionary<string, object?> payload = [];
        if (eventId.Id != 0)
        {
            payload["EventId"] = eventId.Id;
        }

        if (!string.IsNullOrWhiteSpace(eventId.Name))
        {
            payload["EventName"] = eventId.Name;
        }

        if (exception is not null)
        {
            payload["Exception"] = _storeSettings.IncludeStackTrace ? exception.ToString() : exception.Message;
        }

        if (state is not null)
        {
            foreach ((string key, object? value) in state)
            {
                if (string.Equals(key, "{OriginalFormat}", StringComparison.Ordinal))
                {
                    continue;
                }

                payload[key] = ConvertLogValue(value);
            }
        }

        if (payload.Count == 0)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Serialize(payload);
        }
        catch
        {
            return JsonSerializer.Serialize(payload.ToDictionary(
                item => item.Key,
                item => Convert.ToString(item.Value, System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    private static object? ConvertLogValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        Type type = value.GetType();
        Type simpleType = Nullable.GetUnderlyingType(type) ?? type;
        if (simpleType.IsPrimitive ||
            simpleType.IsEnum ||
            value is string ||
            value is decimal ||
            value is DateTime ||
            value is DateTimeOffset ||
            value is DateOnly ||
            value is TimeOnly ||
            value is Guid)
        {
            return value;
        }

        if (value is byte[] bytes)
        {
            return Convert.ToBase64String(bytes);
        }

        try
        {
            return value.ToString();
        }
        catch
        {
            return type.FullName ?? type.Name;
        }
    }
}




