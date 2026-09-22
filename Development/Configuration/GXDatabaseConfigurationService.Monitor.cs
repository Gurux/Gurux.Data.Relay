using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.Orm;
using Gurux.Service.Orm.Model;
using Microsoft.Extensions.Logging;

namespace Gurux.Data.Relay.Configuration;

public sealed partial class GXDatabaseConfigurationService
{
    // Accessed while ConfigurationGate is held, including local saves.
    private readonly Dictionary<ApplicationMode, string> _configurationFingerprints = [];

    private static string ConfigurationFingerprint<T>(T configuration) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(configuration)));

    /// <summary>Monitors committed configuration changes, including writes from other processes.</summary>
    internal async Task<IAsyncDisposable> MonitorChangesAsync(ILogger logger, CancellationToken cancellationToken)
    {
        await ConfigurationGate.WaitAsync(cancellationToken);
        try { await EnsureConfigurationStoreAsync(cancellationToken); }
        finally { ConfigurationGate.Release(); }
        var monitor = new ConfigurationMonitor(this, logger, CreateConfigurationConnection(), cancellationToken);
        try
        {
            await monitor.StartAsync(cancellationToken);
            return monitor;
        }
        catch
        {
            await monitor.DisposeAsync();
            throw;
        }
    }

    private async Task CheckConfigurationChangesAsync(bool initialize, CancellationToken cancellationToken)
    {
        List<ApplicationMode> changed = [];
        await ConfigurationGate.WaitAsync(cancellationToken);
        try
        {
            var snapshots = new Dictionary<ApplicationMode, string>
            {
                [ApplicationMode.Client] = ConfigurationFingerprint(await ReadConfigurationAsync<GXClientConfiguration>(ApplicationMode.Client, cancellationToken)),
                [ApplicationMode.Server] = ConfigurationFingerprint(await ReadConfigurationAsync<GXServerConfiguration>(ApplicationMode.Server, cancellationToken)),
                [ApplicationMode.DataVault] = ConfigurationFingerprint(await ReadConfigurationAsync<GXDataVaultConfiguration>(ApplicationMode.DataVault, cancellationToken))
            };
            foreach (var (mode, fingerprint) in snapshots)
            {
                if (!initialize && _configurationFingerprints.GetValueOrDefault(mode) != fingerprint)
                    changed.Add(mode);
                _configurationFingerprints[mode] = fingerprint;
            }
        }
        finally { ConfigurationGate.Release(); }
        foreach (var mode in changed)
        {
            _changes?.Publish(new(mode.ToString().ToLowerInvariant(), Shared.GXRelayChangeKind.Configuration));
            var handlers = mode switch
            {
                ApplicationMode.Client => ClientConfigurationSaved,
                ApplicationMode.Server => ServerConfigurationSaved,
                ApplicationMode.DataVault => DataVaultConfigurationSaved,
                _ => null
            };
            if (handlers != null)
                foreach (Func<Task> handler in handlers.GetInvocationList()) await handler();
        }
    }

    private sealed class ConfigurationMonitor : IAsyncDisposable
    {
        private readonly GXDatabaseConfigurationService _owner;
        private readonly ILogger _logger;
        private readonly DbConnection _native;
        private readonly CancellationTokenSource _stop;
        private readonly Channel<bool> _signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        private GXDbConnection? _connection;
        private GXDatabaseChangeNotifier? _notifier;
        private Task? _worker;

        public ConfigurationMonitor(GXDatabaseConfigurationService owner, ILogger logger, DbConnection native, CancellationToken token)
        {
            _owner = owner;
            _logger = logger;
            _native = native;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        }

        public async Task StartAsync(CancellationToken token)
        {
            await _native.OpenAsync(token);
            _connection = new GXDbConnection(_native);
            var schema = new GXSchemaManager(_connection);
            var tables = GXRelationalConfigurationStore.EntityTypes.Select(type => schema.Describe(type))
                .Select(table => new DatabaseMonitor(table.Name, table.Columns.Select(column => column.Name).ToArray())).ToArray();
            _notifier = new GXDatabaseChangeNotifier(_connection, null, tables, null, TimeSpan.FromSeconds(1));
            _notifier.Changed += OnChanged;
            _notifier.Error += OnError;
            await _notifier.StartAsync(token);
            await _owner.CheckConfigurationChangesAsync(true, token);
            _worker = RunAsync(_stop.Token);
        }

        private void OnChanged(object? sender, GXDatabaseChangedEventArgs args) => _signals.Writer.TryWrite(true);

        private void OnError(object? sender, Exception error) => _logger.LogError(error, "Relay configuration monitoring failed.");

        private async Task RunAsync(CancellationToken token)
        {
            try
            {
                await foreach (var signal in _signals.Reader.ReadAllAsync(token))
                {
                    try { await _owner.CheckConfigurationChangesAsync(false, token); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception ex) { _logger.LogError(ex, "Could not reload changed relay configuration."); }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            if (_notifier != null)
            {
                _notifier.Changed -= OnChanged;
                _notifier.Error -= OnError;
                await _notifier.DisposeAsync();
            }
            if (_worker != null) await _worker;
            if (_connection != null) await _connection.DisposeAsync();
            await _native.DisposeAsync();
            _stop.Dispose();
        }
    }
}
