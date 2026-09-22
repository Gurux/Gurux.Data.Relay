using Gurux.Service.Orm.Common.Model;
using System.Diagnostics;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Client;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gurux.Data.Relay.Database;
using Gurux.Data.Relay.Log;
using Gurux.Data.Relay.Server;
using Gurux.Data.Relay.Transport;
using Gurux.Data.Relay.Input;
using System.Reflection;
using Gurux.Service.Orm;
using Gurux.Service.Orm.Enums;
using Gurux.Service.Orm.Model;
using Gurux.Service.Orm.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.Orm.Common.Enums;
using Gurux.Data.Relay.Sources;
using Microsoft.Extensions.Configuration;

namespace Gurux.Data.Relay.Hosting;

public sealed class GXRelayHostedService : BackgroundService
{
    private static readonly JsonSerializerOptions SettingsSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
    private static readonly TimeSpan EventLogFollowPollInterval = TimeSpan.FromSeconds(1);

    private readonly GXCommandLineOptions _commandLine;
    private readonly IGXConfigurationService _configurationService;
    private readonly GXConfigurationStoreSettings _storeSettings;
    private readonly IGXDatabaseConnectionFactory _connectionFactory;
    private readonly IGXDatabaseConnectionTestService _connectionTestService;
    private readonly IGXDatabaseMetadataService _metadataService;
    private readonly ITransferScheduler _transferScheduler;
    private readonly IClientSchemaSender _clientSchemaSender;
    private readonly IGXTransportFactory _transportFactory;
    private readonly IGXMqttServerListener _mqttServerListener;
    private readonly IGXTcpServerListener _tcpServerListener;
    private readonly IGXEventLogService _eventLogService;
    private readonly IGXTransportMessageLogService _transportMessageLogService;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<GXRelayHostedService> _logger;
    private readonly bool _sourceLifetime;
    private readonly IConfiguration? _sourceConfiguration;

    public GXRelayHostedService(
        GXCommandLineOptions commandLine,
        IGXConfigurationService configurationService,
        GXConfigurationStoreSettings storeSettings,
        IGXDatabaseConnectionFactory connectionFactory,
        IGXDatabaseConnectionTestService connectionTestService,
        IGXDatabaseMetadataService metadataService,
        ITransferScheduler transferScheduler,
        IClientSchemaSender clientSchemaSender,
        IGXTransportFactory transportFactory,
        IGXMqttServerListener mqttServerListener,
        IGXTcpServerListener tcpServerListener,
        IGXEventLogService eventLogService,
        IGXTransportMessageLogService transportMessageLogService,
        IHostApplicationLifetime applicationLifetime,
        ILogger<GXRelayHostedService> logger,
        IConfiguration? sourceConfiguration = null)
    {
        _commandLine = commandLine;
        _configurationService = configurationService;
        _storeSettings = storeSettings;
        _connectionFactory = connectionFactory;
        _connectionTestService = connectionTestService;
        _metadataService = metadataService;
        _transferScheduler = transferScheduler;
        _clientSchemaSender = clientSchemaSender;
        _transportFactory = transportFactory;
        _mqttServerListener = mqttServerListener;
        _tcpServerListener = tcpServerListener;
        _eventLogService = eventLogService;
        _transportMessageLogService = transportMessageLogService;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
        _sourceConfiguration = sourceConfiguration;
        _sourceLifetime = GXDataSourceServiceExtensions.ShouldRun(commandLine) &&
            sourceConfiguration?.GetSection("DataSources").GetChildren().Any(s =>
                s["Enabled"] is null || bool.TryParse(s["Enabled"], out var enabled) && enabled) == true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (_commandLine.WebHost)
            {
                await using var monitor = _configurationService is GXDatabaseConfigurationService databaseConfiguration
                    ? await databaseConfiguration.MonitorChangesAsync(_logger, stoppingToken)
                    : null;
                await Task.WhenAll(
                    RunWebModeAsync(ApplicationMode.Server, RunServerAsync, stoppingToken),
                    RunWebModeAsync(ApplicationMode.Client, RunClientAsync, stoppingToken),
                    RunWebModeAsync(ApplicationMode.DataVault, RunDataVaultAsync, stoppingToken));
                return;
            }

            if (_commandLine.ShowHelp || (_commandLine.Mode is null && !_commandLine.RunClientAndServer && !_commandLine.Update) || _commandLine.ParseError is not null)
            {
                if (_commandLine.ParseError is not null)
                {
                    _logger.LogError("{Message}", _commandLine.ParseError);
                    Environment.ExitCode = 1;
                }

                PrintUsage();
                return;
            }

            if (_commandLine.Update)
            {
                await UpdateConfigurationMappingTableAsync(stoppingToken);
                return;
            }

            if (_commandLine.RunClientAndServer)
            {
                await RunClientAndServerAsync(stoppingToken);
                return;
            }

            ApplicationMode mode = _commandLine.Mode.GetValueOrDefault();

            if (_commandLine.RestEnabled is bool restEnabled)
            {
                await GXRestSettingsCommand.ExecuteAsync(_configurationService, mode, restEnabled, stoppingToken);
                _logger.LogInformation("{Mode} REST and web administration enabled: {Enabled}.", mode, restEnabled);
                return;
            }

            if (_commandLine.ImportSettingsPath is not null)
            {
                await ImportSettingsAsync(mode, _commandLine.ImportSettingsPath, stoppingToken);
                return;
            }

            if (_commandLine.ExportSettingsPath is not null)
            {
                await ExportSettingsAsync(mode, _commandLine.ExportSettingsPath, stoppingToken);
                return;
            }

            if (mode == ApplicationMode.Client)
            {
                await RunClientAsync(stoppingToken);
            }
            else if (mode == ApplicationMode.Server)
            {
                await RunServerAsync(stoppingToken);
            }
            else
            {
                await RunDataVaultAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Operation cancelled.");
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            _logger.LogError("{Message}", MaskSecrets(ex.Message));
        }
        finally
        {
            if (!_commandLine.WebHost && !_sourceLifetime) _applicationLifetime.StopApplication();
        }
    }

    private async Task UpdateConfigurationMappingTableAsync(CancellationToken cancellationToken)
    {
        await GXConfigurationTableUpdater.UpdateAsync(_storeSettings, _connectionFactory, cancellationToken);
        _logger.LogInformation("Configuration store tables updated.");
    }

    private async Task RunWebModeAsync(ApplicationMode mode, Func<CancellationToken, Task> run, CancellationToken cancellationToken)
    {
        GXRelayModeContext.Current.Value = mode;
        try
        {
            if (mode == ApplicationMode.Server && _configurationService is IGXServerConfigurationChanges changes)
            {
                await CreateServerRuntime(changes).RunAsync(cancellationToken);
                return;
            }
            if (mode == ApplicationMode.Client && _configurationService is IGXClientConfigurationChanges clientChanges)
            {
                await new GXScheduleRuntime(clientChanges, RunClientAsync, _logger).RunAsync(cancellationToken);
                return;
            }
            if (mode == ApplicationMode.DataVault && _configurationService is IGXDataVaultConfigurationChanges vaultChanges)
            {
                await new GXScheduleRuntime(vaultChanges, RunDataVaultAsync, _logger).RunAsync(cancellationToken);
                return;
            }
            GXModeConfiguration? configuration = mode switch
            {
                ApplicationMode.Client => await _configurationService.LoadClientAsync(cancellationToken),
                ApplicationMode.Server => await _configurationService.LoadServerAsync(cancellationToken),
                _ => await _configurationService.LoadDataVaultAsync(cancellationToken)
            };
            if (configuration == null)
            {
                _logger.LogInformation("{Mode} settings are missing. Configure the mode and restart the web host to start it.", mode);
                return;
            }
            await run(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError("{Mode} stopped: {Message}. The web host remains available.", mode, MaskSecrets(ex.Message));
        }
    }

    private async Task ExportSettingsAsync(ApplicationMode mode, string path, CancellationToken cancellationToken)
    {
        object configuration = mode switch
        {
            ApplicationMode.Client => NormalizeAndReturn(await _configurationService.LoadClientAsync(cancellationToken)
                ?? throw new InvalidOperationException("Client configuration was not found.")),
            ApplicationMode.Server => NormalizeAndReturn(await _configurationService.LoadServerAsync(cancellationToken)
                ?? throw new InvalidOperationException("Server configuration was not found.")),
            ApplicationMode.DataVault => NormalizeAndReturn(await _configurationService.LoadDataVaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Data Vault configuration was not found.")),
            _ => throw new NotSupportedException($"Mode '{mode}' is not supported."),
        };

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = Shared.GXDatabaseSelectionsJson.WithoutConcurrencyStamps(
            JsonSerializer.Serialize(configuration, configuration.GetType(), SettingsSerializerOptions), SettingsSerializerOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
        Console.WriteLine($"Exported {mode} settings to {path}.");
    }

    private async Task ImportSettingsAsync(ApplicationMode mode, string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Settings import file '{path}' was not found.", path);
        }

        await using FileStream stream = File.OpenRead(path);
        switch (mode)
        {
            case ApplicationMode.Client:
                GXClientConfiguration clientConfiguration = await JsonSerializer.DeserializeAsync<GXClientConfiguration>(stream, SettingsSerializerOptions, cancellationToken)
                    ?? throw new InvalidOperationException("Client settings import file was empty.");
                if (mode != clientConfiguration.Mode)
                {
                    throw new InvalidOperationException($"Client settings import file mode '{clientConfiguration.Mode}' does not match the specified mode '{mode}'.");
                }
                NormalizeClientConfiguration(clientConfiguration);
                await _configurationService.ImportClientAsync(clientConfiguration, cancellationToken);
                break;
            case ApplicationMode.Server:
                GXServerConfiguration serverConfiguration = await JsonSerializer.DeserializeAsync<GXServerConfiguration>(stream, SettingsSerializerOptions, cancellationToken)
                    ?? throw new InvalidOperationException("Server settings import file was empty.");
                if (mode != serverConfiguration.Mode)
                {
                    throw new InvalidOperationException($"Server settings import file mode '{serverConfiguration.Mode}' does not match the specified mode '{mode}'.");
                }
                NormalizeServerConfiguration(serverConfiguration);
                await _configurationService.ImportServerAsync(serverConfiguration, cancellationToken);
                break;
            case ApplicationMode.DataVault:
                GXDataVaultConfiguration dataVaultConfiguration = await JsonSerializer.DeserializeAsync<GXDataVaultConfiguration>(stream, SettingsSerializerOptions, cancellationToken)
                    ?? throw new InvalidOperationException("Data Vault settings import file was empty.");
                if (mode != dataVaultConfiguration.Mode)
                {
                    throw new InvalidOperationException($"Data Vault settings import file mode '{dataVaultConfiguration.Mode}' does not match the specified mode '{mode}'.");
                }
                NormalizeDataVaultConfiguration(dataVaultConfiguration);
                await _configurationService.ImportDataVaultAsync(dataVaultConfiguration, cancellationToken);
                break;
            default:
                throw new NotSupportedException($"Mode '{mode}' is not supported.");
        }

        Console.WriteLine($"Imported {mode} settings from {path}.");
    }

    private async Task RunClientAndServerAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task serverTask = RunServerAsync(linkedCancellation.Token);
        Task clientTask = RunClientAsync(linkedCancellation.Token);
        Task completedTask = await Task.WhenAny(serverTask, clientTask);

        if (completedTask == serverTask)
        {
            await linkedCancellation.CancelAsync();
            try
            {
                await serverTask;
            }
            finally
            {
                await ObserveExpectedCancellationAsync(clientTask, linkedCancellation);
            }
            return;
        }

        try
        {
            await clientTask;
        }
        finally
        {
            await linkedCancellation.CancelAsync();
            await ObserveExpectedCancellationAsync(serverTask, linkedCancellation);
        }
    }

    private static async Task ObserveExpectedCancellationAsync(
        Task task,
        CancellationTokenSource cancellation)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }

    private GXClientConfiguration NormalizeAndReturn(GXClientConfiguration configuration)
    {
        NormalizeClientConfiguration(configuration);
        return configuration;
    }

    private static GXServerConfiguration NormalizeAndReturn(GXServerConfiguration configuration)
    {
        NormalizeServerConfiguration(configuration);
        return configuration;
    }

    private static GXDataVaultConfiguration NormalizeAndReturn(GXDataVaultConfiguration configuration)
    {
        NormalizeDataVaultConfiguration(configuration);
        return configuration;
    }

    private async Task RunClientAsync(CancellationToken cancellationToken)
    {
        if (_commandLine.ShowSettings)
        {
            GXClientConfiguration settingsConfiguration = await _configurationService.LoadClientAsync(cancellationToken)
                ?? throw new InvalidOperationException("Client configuration was not found.");
            NormalizeClientConfiguration(settingsConfiguration);
            ShowClientSettings(settingsConfiguration);
            return;
        }

        if (_sourceLifetime)
        {
            var existing = await _configurationService.LoadClientAsync(cancellationToken);
            if (existing == null || existing.Databases.Count == 0)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return;
            }
        }
        GXClientConfiguration configuration = await EnsureClientConfigurationAsync(cancellationToken);
        ApplyCommandLineLogLevels(configuration);
        await _eventLogService.InitializeAsync(configuration, cancellationToken);
        WriteLifecycleEvent(configuration.Mode, "Application started.");
        try
        {
            if (_commandLine.ShowEvents)
            {
                await ShowEventLogAsync(configuration, cancellationToken);
                return;
            }

            await _transportMessageLogService.InitializeAsync(configuration, cancellationToken);
            _logger.LogInformation("GXDataRelay Client");
            await VerifyClientDatabaseConfigurationsAsync(configuration, cancellationToken);
            if (_commandLine.SendSchema)
            {
                int sentSchemas = await _clientSchemaSender.SendAsync(configuration, cancellationToken);
                _logger.LogInformation("Client schema transfer finished. Sent {MessageCount} schema messages.", sentSchemas);
                return;
            }

            if (_commandLine.ResetClientState)
            {
                await ResetClientStateAsync(cancellationToken);
            }

            int sentMessages = await _transferScheduler.RunAsync(configuration, cancellationToken);
            _logger.LogInformation("Client transfer finished. Sent {MessageCount} messages.", sentMessages);
            if (_sourceLifetime) await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        finally
        {
            WriteLifecycleEvent(configuration.Mode, "Application stopped.");
        }
    }

    private async Task ResetClientStateAsync(CancellationToken cancellationToken)
    {
        GXClientState state = await _configurationService.LoadClientStateAsync(cancellationToken) ?? new();
        state.Tables.Clear();
        await _configurationService.SaveClientStateAsync(state, cancellationToken);
        _logger.LogInformation(
            "Client read state reset. All configured source data will be read again during this run.");
    }

    private void ApplyCommandLineLogLevels(GXModeConfiguration configuration)
    {
        if (_commandLine.EventLogLevel is { } eventLogLevel)
        {
            configuration.EventLogLevel = eventLogLevel;
        }

        if (_commandLine.CommunicationLogLevel is { } communicationLogLevel)
        {
            configuration.CommunicationLogLevel = communicationLogLevel;
        }

        if (_commandLine.HashAlgorithmSpecified)
        {
            configuration.HashAlgorithm = _commandLine.HashAlgorithm;
        }
    }

    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        if (_commandLine.ShowSettings)
        {
            GXServerConfiguration settingsConfiguration = await _configurationService.LoadServerAsync(cancellationToken)
                ?? throw new InvalidOperationException("Server configuration was not found.");
            ShowServerSettings(settingsConfiguration);
            return;
        }

        GXServerConfiguration configuration = await EnsureServerConfigurationAsync(cancellationToken);
        ApplyCommandLineLogLevels(configuration);
        await _eventLogService.InitializeAsync(configuration, cancellationToken);
        WriteLifecycleEvent(configuration.Mode, "Application started.");
        try
        {
            if (_commandLine.ShowEvents)
            {
                await ShowEventLogAsync(configuration, cancellationToken);
                return;
            }

            await _transportMessageLogService.InitializeAsync(configuration, cancellationToken);
            _logger.LogInformation("GXDataRelay Server");
            await VerifyServerDatabaseConfigurationsAsync(configuration, cancellationToken);
            await RunServerDatabaseTransportsAsync(configuration, cancellationToken);
        }
        finally
        {
            WriteLifecycleEvent(configuration.Mode, "Application stopped.");
        }
    }

    private async Task<GXClientConfiguration> EnsureClientConfigurationAsync(CancellationToken cancellationToken)
    {
        GXClientConfiguration? existing = await _configurationService.LoadClientAsync(cancellationToken);
        if (_commandLine.WebHost && existing == null)
            throw new InvalidOperationException("Client configuration was not found.");
        if (existing is not null && !_commandLine.Configure)
        {
            NormalizeClientConfiguration(existing);
            _logger.LogInformation("Loaded existing client configuration from {Path}.", _configurationService.GetConfigurationPath(ApplicationMode.Client));
            return existing;
        }

        if (existing is not null)
        {
            _logger.LogInformation("Loaded existing client configuration from {Path}.", _configurationService.GetConfigurationPath(ApplicationMode.Client));
        }

        if (_commandLine.ConfigureAction == ConfigureAction.Add)
        {
            GXClientConfiguration updatedConfiguration = existing ?? new GXClientConfiguration();
            updatedConfiguration.Databases.AddRange(await PromptForClientDatabaseConfigurationsAsync(null, cancellationToken));
            updatedConfiguration.EnsureRoutingIds();
            updatedConfiguration.Transports = await PromptForModeTransportsAsync(false, updatedConfiguration.Databases, updatedConfiguration.Transports, cancellationToken);
            await _configurationService.SaveClientAsync(updatedConfiguration, cancellationToken);
            _logger.LogInformation("Saved client configuration to {Path}.", _configurationService.GetConfigurationPath(ApplicationMode.Client));
            return updatedConfiguration;
        }

        if (_commandLine.ConfigureAction == ConfigureAction.Remove)
        {
            GXClientConfiguration updatedConfiguration = existing ?? throw new InvalidOperationException("Client configuration was not found.");
            updatedConfiguration.EnsureRoutingIds();
            RemoveConfiguredDatabases(updatedConfiguration.Databases, "Client source databases", transports: updatedConfiguration.Transports);
            await _configurationService.SaveClientAsync(updatedConfiguration, cancellationToken);
            _logger.LogInformation("Saved client configuration to {Path}.", _configurationService.GetConfigurationPath(ApplicationMode.Client));
            return updatedConfiguration;
        }

        GXClientConfiguration configuration = new();

        configuration.Databases = await PromptForClientDatabaseConfigurationsAsync(existing, cancellationToken);
        configuration.EnsureRoutingIds();
        existing?.EnsureRoutingIds();
        configuration.Transports = await PromptForModeTransportsAsync(false, configuration.Databases, existing?.Transports ?? [], cancellationToken);

        await _configurationService.SaveClientAsync(configuration, cancellationToken);
        _logger.LogInformation("Saved client configuration to {Path}.", _configurationService.GetConfigurationPath(ApplicationMode.Client));
        return configuration;
    }

    private void NormalizeClientConfiguration(GXClientConfiguration configuration)
    {
        configuration.EnsureRoutingIds();
        var providerRoutes = GXDataSourceServiceExtensions.GetProviderRouteIds(_sourceConfiguration);
        if (configuration.Databases.Count == 0 && !configuration.Transports.Any(t => providerRoutes.Contains(t.Id)))
            throw new InvalidOperationException("Client configuration must contain at least one source database in Databases.");
        GXTransportRouting.Validate(configuration.Databases, configuration.Transports, false, providerRoutes);
    }
    private async Task<GXServerConfiguration> EnsureServerConfigurationAsync(CancellationToken cancellationToken)
    {
        GXServerConfiguration? existing = await _configurationService.LoadServerAsync(cancellationToken);
        if (_commandLine.WebHost && existing == null)
            throw new InvalidOperationException("Server configuration was not found.");
        if (existing is not null && !_commandLine.Configure)
        {
            NormalizeServerConfiguration(existing);
            _logger.LogInformation("Loaded existing server configuration from {Path}.", _configurationService.GetConfigurationPath(ApplicationMode.Server));
            return existing;
        }

        if (existing is not null)
        {
            _logger.LogInformation("Loaded existing server configuration from {Path}.", _configurationService.GetConfigurationPath(ApplicationMode.Server));
        }

        if (_commandLine.ConfigureAction == ConfigureAction.Add)
        {
            GXServerConfiguration updatedConfiguration = existing ?? new GXServerConfiguration();
            updatedConfiguration.Databases.AddRange(await PromptForServerDatabaseConfigurationsAsync(null, cancellationToken));
            updatedConfiguration.EnsureRoutingIds();
            updatedConfiguration.Transports = await PromptForModeTransportsAsync(true, updatedConfiguration.Databases, updatedConfiguration.Transports, cancellationToken);
            await _configurationService.SaveServerAsync(updatedConfiguration, cancellationToken);
            _logger.LogInformation("Saved server configuration to {Path}.", _configurationService.GetConfigurationPath(ApplicationMode.Server));
            return updatedConfiguration;
        }

        if (_commandLine.ConfigureAction == ConfigureAction.Remove)
        {
            GXServerConfiguration updatedConfiguration = existing ?? throw new InvalidOperationException("Server configuration was not found.");
            updatedConfiguration.EnsureRoutingIds();
            RemoveConfiguredDatabases(updatedConfiguration.Databases, "Server destination databases", showDeleteSettings: true, transports: updatedConfiguration.Transports);
            await _configurationService.SaveServerAsync(updatedConfiguration, cancellationToken);
            _logger.LogInformation("Saved server configuration to {Path}.", _configurationService.GetConfigurationPath(ApplicationMode.Server));
            return updatedConfiguration;
        }

        GXServerConfiguration configuration = new();
        configuration.Databases = await PromptForServerDatabaseConfigurationsAsync(existing, cancellationToken);
        configuration.EnsureRoutingIds();
        existing?.EnsureRoutingIds();
        configuration.Transports = await PromptForModeTransportsAsync(true, configuration.Databases, existing?.Transports ?? [], cancellationToken);

        await _configurationService.SaveServerAsync(configuration, cancellationToken);
        _logger.LogInformation("Saved server configuration to {Path}.", _configurationService.GetConfigurationPath(ApplicationMode.Server));
        return configuration;
    }

    private static void NormalizeServerConfiguration(GXServerConfiguration configuration)
    {
        configuration.EnsureRoutingIds();
        if (configuration.Databases.Count == 0)
            throw new InvalidOperationException("Server configuration must contain at least one destination database in Databases.");
        if (configuration.Transports.Count == 0)
            throw new InvalidOperationException("Server configuration must contain at least one transport in Transports.");
        GXTransportRouting.Validate(configuration.Databases, configuration.Transports, true);
    }

    private async Task RunServerDatabaseTransportsAsync(GXServerConfiguration configuration, CancellationToken cancellationToken)
    {
        if (_configurationService is IGXServerConfigurationChanges changes)
        {
            await CreateServerRuntime(changes).RunAsync(cancellationToken);
            return;
        }
        NormalizeServerConfiguration(configuration);
        List<Task> listeners = [];
        for (int index = 0; index < configuration.Transports.Count; ++index)
        {
            GXTransportConfiguration transport = configuration.Transports[index];
            string name = transport.Description ?? $"Transport {index + 1}";
            listeners.Add(RunServerTransportAsync(transport, name, index, configuration.Transports.Count, cancellationToken));
        }
        await Task.WhenAll(listeners);
    }

    private GXServerTransportRuntime CreateServerRuntime(IGXServerConfigurationChanges changes) => new(
        _configurationService, changes,
        (transport, token) => RunServerTransportAsync(transport, transport.Description ?? "Transport", 0, 1, token),
        _logger, async (configuration, token) =>
        {
            GXRelayModeContext.Current.Value = ApplicationMode.Server;
            ApplyCommandLineLogLevels(configuration);
            await _eventLogService.InitializeAsync(configuration, token);
            await _transportMessageLogService.InitializeAsync(configuration, token);
        });
    private async Task RunServerTransportAsync(
        GXTransportConfiguration transport,
        string databaseName,
        int transportIndex,
        int transportCount,
        CancellationToken cancellationToken)
    {
        using var databaseContext = GXEventLogContext.BeginDatabase(transport.DatabaseIndex);
        if (transport.Type == TransportType.Tcp)
        {
            _logger.LogInformation(
                "Starting TCP listener for destination database {databaseName}, transport {TransportIndex}/{TransportCount}, on port {Port}.",
                databaseName,
                transportIndex + 1,
                transportCount,
                transport.Port);
            await _tcpServerListener.RunAsync(transport, cancellationToken);
            return;
        }

        if (transport.Type == TransportType.Mqtt)
        {
            _logger.LogInformation(
                "Subscribing to MQTT topic {Topic} for destination database {databaseName}, transport {TransportIndex}/{TransportCount}.",
                transport.Topic,
                databaseName,
                transportIndex + 1,
                transportCount);
            await _mqttServerListener.RunAsync(transport, cancellationToken);
            return;
        }

        throw new NotSupportedException($"Transport type '{transport.Type}' is not supported.");
    }

    private async Task RunDataVaultAsync(CancellationToken cancellationToken)
    {
        if (_commandLine.RefreshInformationMart)
        {
            GXDataVaultConfiguration refreshConfiguration = await _configurationService.LoadDataVaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Data Vault configuration was not found.");
            NormalizeDataVaultConfiguration(refreshConfiguration);
            ApplyCommandLineLogLevels(refreshConfiguration);
            await VerifyDataVaultDatabaseConfigurationsAsync(refreshConfiguration, cancellationToken);
            GXDataVaultMappingSelection selection = ResolveDataVaultMapping(
                refreshConfiguration,
                _commandLine.DataVaultTableId ?? throw new InvalidOperationException("Data Vault table ID is required."));
            GXDataVaultScheduler scheduler = new(
                _connectionFactory,
                new GXDatabaseChangeNotifierFactory(_connectionFactory, _logger),
                NullLogger<GXDataVaultScheduler>.Instance)
            { RuntimeStateStore = _configurationService as IGXDataVaultRuntimeStateStore };
            if (GXInformationMartBuilder.HasColumnMappings(selection.Mapping))
            {
                int rows = await scheduler.RefreshInformationMartAsync(selection.Database, selection.Mapping.Id, cancellationToken);
                _logger.LogInformation("Information Mart {TargetTable} refreshed from column mappings. Copied {RowCount} rows.", selection.Mapping.TargetTable.Name, rows);
                return;
            }
            int clearedTables = await ClearDataVaultMappingsExceptStagingAsync(
                selection.Database,
                selection.Mapping.SourceTable.Name,
                cancellationToken);
            int copiedRows = await scheduler.RefreshInformationMartsAsync(
                selection.Database,
                selection.Mapping.SourceTable.Name,
                cancellationToken,
                refreshConfiguration.HashAlgorithm);
            _logger.LogInformation(
                "Data Vault Information Mart refresh finished. Cleared {TableCount} target tables and copied {RowCount} rows.",
                clearedTables,
                copiedRows);
            return;
        }

        if (_commandLine.ShowSettings)
        {
            GXDataVaultConfiguration settingsConfiguration = await _configurationService.LoadDataVaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Data Vault configuration was not found.");
            NormalizeDataVaultConfiguration(settingsConfiguration);
            ShowDataVaultSettings(settingsConfiguration, _commandLine.DataVaultTableId);
            return;
        }

        if (_commandLine.ShowEvents)
        {
            GXDataVaultConfiguration eventLogConfiguration = await _configurationService.LoadDataVaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Data Vault configuration was not found.");
            NormalizeDataVaultConfiguration(eventLogConfiguration);
            await ShowEventLogAsync(eventLogConfiguration, cancellationToken);
            return;
        }

        GXDataVaultConfiguration configuration = await EnsureDataVaultConfigurationAsync(cancellationToken);
        ApplyCommandLineLogLevels(configuration);
        await _eventLogService.InitializeAsync(configuration, cancellationToken);
        WriteLifecycleEvent(configuration.Mode, "Application started.");
        try
        {
            await _transportMessageLogService.InitializeAsync(configuration, cancellationToken);
            await VerifyDataVaultDatabaseConfigurationsAsync(configuration, cancellationToken);
            if (_commandLine.ResetClientState)
            {
                int resetTables = await ResetDataVaultSchemaAsync(configuration, cancellationToken);
                _logger.LogInformation("Data Vault schema reset finished. Dropped {TableCount} target tables.", resetTables);
            }
            if (_commandLine.SendSchema)
            {
                int createdTables = await SendDataVaultSchemaAsync(configuration, cancellationToken);
                _logger.LogInformation("Data Vault schema transfer finished. Created {TableCount} target tables.", createdTables);
                return;
            }

            if (IsDefaultDataVaultRun() || HasScheduledDataVaultInformationMarts(configuration))
            {
                GXDataVaultScheduler scheduler = new(
                    _connectionFactory,
                    new GXDatabaseChangeNotifierFactory(_connectionFactory, _logger),
                    NullLogger<GXDataVaultScheduler>.Instance)
                { RuntimeStateStore = _configurationService as IGXDataVaultRuntimeStateStore };
                int copiedRows = await scheduler.RunAsync(configuration, cancellationToken);
                _logger.LogInformation("Data Vault schedule stopped. Copied {RowCount} rows to Information Mart tables.", copiedRows);
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"Data Vault mappings saved to {_configurationService.GetConfigurationPath(ApplicationMode.DataVault)}.");
            PrintDataVaultMappings(configuration);
        }
        finally
        {
            WriteLifecycleEvent(configuration.Mode, "Application stopped.");
        }
    }

    private bool IsDefaultDataVaultRun()
    {
        return !_commandLine.Configure &&
            !_commandLine.ResetClientState &&
            !_commandLine.SendSchema &&
            !_commandLine.RefreshInformationMart &&
            !_commandLine.ShowSettings &&
            !_commandLine.ShowEvents &&
            _commandLine.DataVaultTableId is null;
    }

    private static bool HasScheduledDataVaultInformationMarts(GXDataVaultConfiguration configuration)
    {
        return configuration.GetMappings().Any(mapping =>
            (mapping.ObjectType is DataVaultObjectType.InformationMart or DataVaultObjectType.Staging) &&
            mapping.Schedule.Type != ScheduleType.Manual);
    }

    private sealed record GXDataVaultMappingSelection(
        GXDatabaseConfiguration Database,
        GXDataVaultTableMapping Mapping);

    private async Task<int> ResetDataVaultSchemaAsync(
        GXDataVaultConfiguration configuration,
        CancellationToken cancellationToken)
    {
        int dropped = 0;
        foreach (GXDatabaseConfiguration database in configuration.GetDatabases())
        {
            await using DbConnection connection = _connectionFactory.CreateConnection(database);
            await connection.OpenAsync(cancellationToken);
            await using GXDbConnection guruxConnection = new(connection);
            GXSchemaManager schemaManager = new(guruxConnection);
            foreach (string targetTable in (database.Mappings ?? [])
                .Select(mapping => mapping.TargetTable.Name)
                .Where(table => !string.IsNullOrWhiteSpace(table))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (schemaManager.TableExist(targetTable))
                {
                    schemaManager.DropTable(targetTable);
                    ++dropped;
                }
            }
        }

        return dropped;
    }

    /// <summary>
    /// Removes data vault target table mappings for the specified source table, 
    /// excluding those of type Staging.
    /// </summary>
    /// <param name="database">The database configuration containing the data vault mappings.</param>
    /// <param name="sourceTable">The source table whose non-staging target mappings will be cleared.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The number of target mappings cleared.</returns>
    private async Task<int> ClearDataVaultMappingsExceptStagingAsync(
        GXDatabaseConfiguration database,
        string sourceTable,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = _connectionFactory.CreateConnection(database);
        await connection.OpenAsync(cancellationToken);
        await using GXDbConnection guruxConnection = new(connection);
        int cleared = 0;
        foreach (string targetTable in (database.Mappings ?? [])
            .Where(mapping => mapping.ObjectType != DataVaultObjectType.Staging &&
                string.Equals(mapping.SourceTable.Name, sourceTable, StringComparison.OrdinalIgnoreCase))
            .Select(mapping => mapping.TargetTable.Name)
            .Where(table => !string.IsNullOrWhiteSpace(table))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!guruxConnection.TableExist(targetTable))
            {
                continue;
            }
            guruxConnection.Truncate(targetTable);
            ++cleared;
        }
        return cleared;
    }

    private async Task<int> SendDataVaultSchemaAsync(
        GXDataVaultConfiguration configuration,
        CancellationToken cancellationToken)
    {
        int created = 0;
        foreach (GXDatabaseConfiguration database in configuration.GetDatabases())
        {
            await using DbConnection connection = _connectionFactory.CreateConnection(database);
            await connection.OpenAsync(cancellationToken);
            await using GXDbConnection guruxConnection = new(connection);
            GXSchemaManager schemaManager = new(guruxConnection);
            Dictionary<string, GXTableSchema> sourceSchemas = [];

            foreach (GXDataVaultTableMapping mapping in database.Mappings ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (schemaManager.TableExist(mapping.TargetTable.Name))
                {
                    continue;
                }

                if (!sourceSchemas.TryGetValue(mapping.SourceTable.Name, out GXTableSchema? sourceSchema))
                {
                    sourceSchema = schemaManager.Describe(mapping.SourceTable.Name);
                    sourceSchema.Columns.Sort(static (left, right) => left.Ordinal.CompareTo(right.Ordinal));
                    sourceSchemas.Add(mapping.SourceTable.Name, sourceSchema);
                }

                GXTableSchema targetSchema = BuildDataVaultTargetSchema(mapping, sourceSchema, configuration.HashAlgorithm);
                schemaManager.CreateTable(targetSchema);
                ++created;
            }
        }

        return created;
    }

    private static GXTableSchema BuildDataVaultTargetSchema(
        GXDataVaultTableMapping mapping,
        GXTableSchema sourceSchema,
        HashAlgorithmType hashAlgorithm)
    {
        GXTableSchema targetSchema = new()
        {
            Name = mapping.TargetTable.Name,
            TableType = "BASE TABLE",
        };

        HashSet<string> addedColumns = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> keyColumns = mapping.Columns
            .Where(column => IsDataVaultKeyColumn(mapping.ObjectType.Value, column.Role))
            .Select(column => column.TargetColumn)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool usePrimaryKeyConstraint = keyColumns.Count == 1;
        int ordinal = 1;
        foreach (GXDataVaultColumnMapping columnMapping in mapping.Columns)
        {
            if (!addedColumns.Add(columnMapping.TargetColumn))
            {
                continue;
            }

            GXColumnSchema sourceColumn = sourceSchema.Columns.FirstOrDefault(column =>
                IsSameName(column.Name, columnMapping.SourceColumn))
                ?? throw new InvalidOperationException(
                    $"Data Vault source column '{columnMapping.SourceColumn}' was not found in table '{mapping.SourceTable.Name}'.");

            targetSchema.Columns.Add(new GXColumnSchema
            {
                Name = columnMapping.TargetColumn,
                Ordinal = ordinal++,
                Type = sourceColumn.Type,
                MaxLength = GetDataVaultColumnMaxLength(columnMapping.Role.Value, sourceColumn.MaxLength, hashAlgorithm),
                Precision = sourceColumn.Precision,
                Scale = sourceColumn.Scale,
                DateTimePrecision = sourceColumn.DateTimePrecision,
                IsNullable = !keyColumns.Contains(columnMapping.TargetColumn),
                IsPrimaryKey = usePrimaryKeyConstraint && keyColumns.Contains(columnMapping.TargetColumn),
            });
        }

        if (targetSchema.Columns.Count == 0)
        {
            throw new InvalidOperationException($"Data Vault mapping for '{mapping.TargetTable.Name}' must contain at least one column.");
        }

        return targetSchema;
    }

    private static long? GetDataVaultColumnMaxLength(DataVaultColumnRole role, long? maxLength)
    {
        return GetDataVaultColumnMaxLength(role, maxLength, HashAlgorithmType.SHA256);
    }

    private static long? GetDataVaultColumnMaxLength(
        DataVaultColumnRole role,
        long? maxLength,
        HashAlgorithmType hashAlgorithm)
    {
        return role is DataVaultColumnRole.HashKey or DataVaultColumnRole.LinkHashKey or DataVaultColumnRole.ParentHashKey or DataVaultColumnRole.HashDiff
            ? Math.Max(maxLength ?? 0, GetDataVaultHashLength(hashAlgorithm))
            : maxLength;
    }

    private static int GetDataVaultHashLength(HashAlgorithmType hashAlgorithm)
    {
        return hashAlgorithm switch
        {
            HashAlgorithmType.MD5 => 32,
            HashAlgorithmType.SHA512 => 128,
            _ => 64,
        };
    }

    private static GXDataVaultMappingSelection ResolveDataVaultMapping(
        GXDataVaultConfiguration configuration,
        Guid tableId)
    {
        foreach (GXDatabaseConfiguration database in configuration.GetDatabases())
        {
            GXDataVaultTableMapping? mapping = (database.Mappings ?? []).FirstOrDefault(item => item.Id == tableId);
            if (mapping is not null)
            {
                return new GXDataVaultMappingSelection(database, mapping);
            }
        }

        throw new InvalidOperationException($"Data Vault table ID '{tableId}' was not found.");
    }

    private static bool IsDataVaultKeyColumn(DataVaultObjectType objectType,
        DataVaultColumnRole? role)
    {
        return objectType switch
        {
            DataVaultObjectType.Hub => role is DataVaultColumnRole.HashKey,
            DataVaultObjectType.Link => role is DataVaultColumnRole.LinkHashKey,
            DataVaultObjectType.Satellite => role is DataVaultColumnRole.ParentHashKey or DataVaultColumnRole.LoadDate,
            DataVaultObjectType.Reference => role is DataVaultColumnRole.BusinessKey,
            DataVaultObjectType.Staging => false,
            DataVaultObjectType.InformationMart => role is DataVaultColumnRole.BusinessKey,
            _ => false,
        };
    }

    private async Task<GXDataVaultConfiguration> EnsureDataVaultConfigurationAsync(CancellationToken cancellationToken)
    {
        GXDataVaultConfiguration? existing = await _configurationService.LoadDataVaultAsync(cancellationToken);
        if (_commandLine.WebHost && existing == null)
            throw new InvalidOperationException("DataVault configuration was not found.");
        if (existing is not null && !_commandLine.Configure)
        {
            NormalizeDataVaultConfiguration(existing);
            _logger.LogInformation("Loaded existing Data Vault configuration from {Path}.", _configurationService.GetConfigurationPath(ApplicationMode.DataVault));
            return existing;
        }

        if (existing is not null)
        {
            _logger.LogInformation("Loaded existing Data Vault configuration from {Path}.", _configurationService.GetConfigurationPath(ApplicationMode.DataVault));
        }

        if (_commandLine.ConfigureAction == ConfigureAction.Add)
        {
            GXDataVaultConfiguration updatedConfiguration = existing ?? new GXDataVaultConfiguration();
            updatedConfiguration.Databases.AddRange(await PromptForDataVaultDatabaseConfigurationsAsync(null, cancellationToken));
            if (updatedConfiguration.Databases.Any(database => database.Mappings is null or { Count: 0 }))
            {
                foreach (GXDatabaseConfiguration database in updatedConfiguration.Databases
                    .Where(database => database.Mappings is null or { Count: 0 }))
                {
                    database.Mappings = await PromptForDataVaultMappingsAsync(database, database.Mappings, cancellationToken);
                }
            }
            await _configurationService.SaveDataVaultAsync(updatedConfiguration, cancellationToken);
            _logger.LogInformation("Saved Data Vault configuration to {Path}.", _configurationService.GetConfigurationPath(ApplicationMode.DataVault));
            return updatedConfiguration;
        }

        if (_commandLine.ConfigureAction == ConfigureAction.Remove)
        {
            GXDataVaultConfiguration updatedConfiguration = existing ?? throw new InvalidOperationException("Data Vault configuration was not found.");
            RemoveConfiguredDatabases(updatedConfiguration.Databases, "Data Vault databases");
            await _configurationService.SaveDataVaultAsync(updatedConfiguration, cancellationToken);
            _logger.LogInformation("Saved Data Vault configuration to {Path}.", _configurationService.GetConfigurationPath(ApplicationMode.DataVault));
            return updatedConfiguration;
        }

        GXDataVaultConfiguration configuration = new();

        configuration.Databases = await PromptForDataVaultDatabaseConfigurationsAsync(existing, cancellationToken);
        foreach (GXDatabaseConfiguration database in configuration.Databases)
        {
            GXDatabaseConfiguration? existingDatabase = existing?.Databases.FirstOrDefault(item =>
                string.Equals(item.ConnectionString, database.ConnectionString, StringComparison.OrdinalIgnoreCase) &&
                item.Type == database.Type);
            database.Mappings = await PromptForDataVaultMappingsAsync(database, existingDatabase?.Mappings, cancellationToken);
        }
        await _configurationService.SaveDataVaultAsync(configuration, cancellationToken);
        _logger.LogInformation("Saved Data Vault configuration to {Path}.", _configurationService.GetConfigurationPath(ApplicationMode.DataVault));
        return configuration;
    }

    private static void NormalizeDataVaultConfiguration(GXDataVaultConfiguration configuration)
    {
        if (configuration.Databases.Count == 0)
        {
            throw new InvalidOperationException("Data Vault configuration must contain at least one database in Databases.");
        }


    }

    private async Task<List<GXDataVaultTableMapping>> PromptForDataVaultMappingsAsync(
        GXDatabaseConfiguration databaseConfiguration,
        IReadOnlyList<GXDataVaultTableMapping>? defaults,
        CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine("Reading database metadata...");
        IReadOnlyList<string> tableNames = await _metadataService.GetTableNamesAsync(databaseConfiguration, cancellationToken);
        if (tableNames.Count == 0)
        {
            throw new InvalidOperationException("No tables were found in the configured database.");
        }

        Console.WriteLine("Select source tables to map to Data Vault objects:");
        for (int pos = 0; pos < tableNames.Count; ++pos)
        {
            Console.WriteLine($"{pos + 1}. {tableNames[pos]}");
        }

        List<int> defaultTableIndexes = GetDefaultDataVaultTableIndexes(tableNames, defaults);
        IReadOnlyList<int> selectedTableIndexes = PromptSelection("Tables", tableNames.Count, allowMultiple: true, defaultSelection: defaultTableIndexes);
        List<GXDataVaultTableMapping> mappings = [];
        foreach (int selectedTableIndex in selectedTableIndexes)
        {
            string tableName = tableNames[selectedTableIndex];
            GXDataVaultTableMapping? tableDefaults = defaults?.FirstOrDefault(item => IsSameName(item.SourceTable.Name, tableName));
            Console.WriteLine();
            Console.WriteLine($"Data Vault mapping for source table: {tableName}");
            GXTableSchema table = await _metadataService.DescribeTableAsync(databaseConfiguration, tableName, cancellationToken);
            table.Columns.Sort(static (left, right) => left.Ordinal.CompareTo(right.Ordinal));
            PrintColumns(table);

            DataVaultObjectType objectType = PromptDataVaultObjectType(tableDefaults?.ObjectType);
            string targetTable = PromptRequiredValue("Target table", tableDefaults?.TargetTable.Name ?? GetDefaultDataVaultTargetTable(tableName, objectType));
            List<GXDataVaultColumnMapping> columns = PromptDataVaultColumnMappings(table, objectType, tableDefaults?.Columns);
            GXSchedule schedule = PromptScheduleConfiguration(tableDefaults?.Schedule);
            mappings.Add(new GXDataVaultTableMapping
            {
                Id = tableDefaults?.Id ?? Guid.NewGuid(),
                SourceTable = new() { Id = Guid.Empty, Name = tableName },
                TargetTable = new() { Id = Guid.Empty, Name = targetTable },
                ObjectType = objectType,
                Columns = columns,
                Schedule = schedule,
                Updated = DateTimeOffset.UtcNow,
            });
        }

        return mappings;
    }

    private static void PrintColumns(GXTableSchema table)
    {
        for (int pos = 0; pos < table.Columns.Count; ++pos)
        {
            GXColumnSchema column = table.Columns[pos];
            string keyLabel = column.IsPrimaryKey ? " [PK]" : string.Empty;
            Console.WriteLine($"{pos + 1}. {column.Name} ({column.DbType}){keyLabel}");
        }
    }

    private static DataVaultObjectType PromptDataVaultObjectType(DataVaultObjectType? defaultValue = null)
    {
        Console.WriteLine("Data Vault object type:");
        Console.WriteLine("1. Hub");
        Console.WriteLine("2. Link");
        Console.WriteLine("3. Satellite");
        Console.WriteLine("4. Reference");
        Console.WriteLine("5. Staging");
        Console.WriteLine("6. Information Mart");
        DataVaultObjectType effectiveDefault = defaultValue ?? DataVaultObjectType.Hub;

        while (true)
        {
            string? input = GXConsoleInput.ReadLine(FormatPrompt(">", effectiveDefault.ToString()));
            ThrowIfInputClosed(input);
            string effectiveInput = string.IsNullOrWhiteSpace(input) ? effectiveDefault.ToString() : input.Trim();
            if (effectiveInput == "1" || string.Equals(effectiveInput, "hub", StringComparison.OrdinalIgnoreCase))
            {
                return DataVaultObjectType.Hub;
            }
            if (effectiveInput == "2" || string.Equals(effectiveInput, "link", StringComparison.OrdinalIgnoreCase))
            {
                return DataVaultObjectType.Link;
            }
            if (effectiveInput == "3" || string.Equals(effectiveInput, "satellite", StringComparison.OrdinalIgnoreCase) || string.Equals(effectiveInput, "sat", StringComparison.OrdinalIgnoreCase))
            {
                return DataVaultObjectType.Satellite;
            }
            if (effectiveInput == "4" || string.Equals(effectiveInput, "reference", StringComparison.OrdinalIgnoreCase) || string.Equals(effectiveInput, "ref", StringComparison.OrdinalIgnoreCase))
            {
                return DataVaultObjectType.Reference;
            }
            if (effectiveInput == "5" || string.Equals(effectiveInput, "staging", StringComparison.OrdinalIgnoreCase) || string.Equals(effectiveInput, "stage", StringComparison.OrdinalIgnoreCase) || string.Equals(effectiveInput, "stg", StringComparison.OrdinalIgnoreCase))
            {
                return DataVaultObjectType.Staging;
            }
            if (effectiveInput == "6" || string.Equals(effectiveInput, "informationmart", StringComparison.OrdinalIgnoreCase) || string.Equals(effectiveInput, "information-mart", StringComparison.OrdinalIgnoreCase) || string.Equals(effectiveInput, "mart", StringComparison.OrdinalIgnoreCase))
            {
                return DataVaultObjectType.InformationMart;
            }

            Console.WriteLine("Select a valid Data Vault object type.");
        }
    }

    private static List<GXDataVaultColumnMapping> PromptDataVaultColumnMappings(
        GXTableSchema table,
        DataVaultObjectType objectType,
        IReadOnlyList<GXDataVaultColumnMapping>? defaults)
    {
        List<GXDataVaultColumnMapping> mappings = [];
        switch (objectType)
        {
            case DataVaultObjectType.Hub:
                AddOptionalRoleMapping(mappings, table, DataVaultColumnRole.HashKey, "Hash key column", defaults);
                AddRoleMappings(mappings, table, DataVaultColumnRole.BusinessKey, "Business key columns", required: true, defaults);
                AddOptionalRoleMapping(mappings, table, DataVaultColumnRole.LoadDate, "Load date column", defaults);
                AddOptionalRoleMapping(mappings, table, DataVaultColumnRole.RecordSource, "Record source column", defaults);
                break;
            case DataVaultObjectType.Link:
                AddOptionalRoleMapping(mappings, table, DataVaultColumnRole.LinkHashKey, "Link hash key column", defaults);
                AddRoleMappings(mappings, table, DataVaultColumnRole.ParentHashKey, "Parent hash key columns", required: true, defaults);
                AddOptionalRoleMapping(mappings, table, DataVaultColumnRole.LoadDate, "Load date column", defaults);
                AddOptionalRoleMapping(mappings, table, DataVaultColumnRole.RecordSource, "Record source column", defaults);
                break;
            case DataVaultObjectType.Satellite:
                AddRoleMappings(mappings, table, DataVaultColumnRole.ParentHashKey, "Parent hash key columns", required: true, defaults);
                AddRoleMappings(mappings, table, DataVaultColumnRole.Attribute, "Descriptive attribute columns", required: true, defaults);
                AddOptionalRoleMapping(mappings, table, DataVaultColumnRole.LoadDate, "Load date column", defaults);
                AddOptionalRoleMapping(mappings, table, DataVaultColumnRole.RecordSource, "Record source column", defaults);
                AddOptionalRoleMapping(mappings, table, DataVaultColumnRole.EffectivityStart, "Effectivity start column", defaults);
                AddOptionalRoleMapping(mappings, table, DataVaultColumnRole.EffectivityEnd, "Effectivity end column", defaults);
                break;
            case DataVaultObjectType.Reference:
                AddRoleMappings(mappings, table, DataVaultColumnRole.BusinessKey, "Reference key columns", required: true, defaults);
                AddRoleMappings(mappings, table, DataVaultColumnRole.Attribute, "Reference attribute columns", required: false, defaults);
                AddOptionalRoleMapping(mappings, table, DataVaultColumnRole.LoadDate, "Load date column", defaults);
                AddOptionalRoleMapping(mappings, table, DataVaultColumnRole.RecordSource, "Record source column", defaults);
                break;
            case DataVaultObjectType.Staging:
                AddRoleMappings(mappings, table, DataVaultColumnRole.BusinessKey, "Staging key columns", required: false, defaults);
                AddRoleMappings(mappings, table, DataVaultColumnRole.Attribute, "Staging payload columns", required: true, defaults);
                AddOptionalRoleMapping(mappings, table, DataVaultColumnRole.LoadDate, "Load date column", defaults);
                AddOptionalRoleMapping(mappings, table, DataVaultColumnRole.RecordSource, "Record source column", defaults);
                break;
            case DataVaultObjectType.InformationMart:
                AddRoleMappings(mappings, table, DataVaultColumnRole.BusinessKey, "Information Mart key columns", required: true, defaults);
                AddRoleMappings(mappings, table, DataVaultColumnRole.Attribute, "Information Mart measure and attribute columns", required: true, defaults);
                AddOptionalRoleMapping(mappings, table, DataVaultColumnRole.LoadDate, "Load date column", defaults);
                AddOptionalRoleMapping(mappings, table, DataVaultColumnRole.RecordSource, "Record source column", defaults);
                break;
        }

        return mappings;
    }

    private static void AddRoleMappings(
        List<GXDataVaultColumnMapping> mappings,
        GXTableSchema table,
        DataVaultColumnRole role,
        string label,
        bool required,
        IReadOnlyList<GXDataVaultColumnMapping>? defaults)
    {
        IReadOnlyList<int> defaultSelection = GetDefaultRoleColumnIndexes(table, role, defaults);
        IReadOnlyList<int> selected = PromptColumnSelection(label, table, required, allowMultiple: true, defaultSelection);
        foreach (int index in selected)
        {
            GXColumnSchema column = table.Columns[index];
            mappings.Add(CreateDataVaultColumnMapping(column.Name, role, defaults));
        }
    }

    private static void AddOptionalRoleMapping(
        List<GXDataVaultColumnMapping> mappings,
        GXTableSchema table,
        DataVaultColumnRole role,
        string label,
        IReadOnlyList<GXDataVaultColumnMapping>? defaults)
    {
        IReadOnlyList<int> defaultSelection = GetDefaultRoleColumnIndexes(table, role, defaults);
        IReadOnlyList<int> selected = PromptColumnSelection(label, table, required: false, allowMultiple: false, defaultSelection);
        foreach (int index in selected)
        {
            GXColumnSchema column = table.Columns[index];
            mappings.Add(CreateDataVaultColumnMapping(column.Name, role, defaults));
        }
    }

    private static IReadOnlyList<int> PromptColumnSelection(
        string label,
        GXTableSchema table,
        bool required,
        bool allowMultiple,
        IReadOnlyList<int> defaultSelection)
    {
        Console.WriteLine($"{label}:");
        if (!required)
        {
            Console.WriteLine("0. None");
        }
        PrintColumns(table);
        Console.WriteLine(allowMultiple ? "Select column numbers separated with commas." : "Select one column number.");
        while (true)
        {
            string? defaultText = FormatSelectionDefault(defaultSelection, table.Columns.Count, allowAll: false);
            string? input = GXConsoleInput.ReadLine(FormatPrompt(">", defaultText));
            string inputText = input ?? throw new InvalidOperationException("Input stream closed before configuration was completed.");
            if (string.IsNullOrWhiteSpace(inputText))
            {
                if (defaultSelection.Count != 0)
                {
                    return defaultSelection;
                }
                if (!required)
                {
                    return [];
                }
            }
            if (!required && inputText.Trim() == "0")
            {
                return [];
            }

            string[] parts = inputText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (!allowMultiple && parts.Length > 1)
            {
                Console.WriteLine($"Select one {label.ToLowerInvariant()} or 0 for none.");
                continue;
            }

            List<int> values = [];
            bool invalid = false;
            foreach (string part in parts)
            {
                if (!int.TryParse(part, out int parsed) || parsed < 1 || parsed > table.Columns.Count)
                {
                    invalid = true;
                    break;
                }

                int zeroBased = parsed - 1;
                if (!values.Contains(zeroBased))
                {
                    values.Add(zeroBased);
                }
            }

            if (!invalid && (values.Count != 0 || !required))
            {
                return values;
            }

            Console.WriteLine(required ? "Select at least one valid column number." : "Select valid column numbers or 0 for none.");
        }
    }

    private static GXDataVaultColumnMapping CreateDataVaultColumnMapping(
        string sourceColumn,
        DataVaultColumnRole role,
        IReadOnlyList<GXDataVaultColumnMapping>? defaults)
    {
        string? defaultTarget = defaults?.FirstOrDefault(item =>
            item.Role == role && IsSameName(item.SourceColumn, sourceColumn))?.TargetColumn;
        return new GXDataVaultColumnMapping
        {
            SourceColumn = sourceColumn,
            TargetColumn = PromptRequiredValue($"Target column for {sourceColumn} ({role})", defaultTarget ?? sourceColumn),
            Role = role,
        };
    }

    private static List<int> GetDefaultDataVaultTableIndexes(IReadOnlyList<string> tableNames, IReadOnlyList<GXDataVaultTableMapping>? defaults)
    {
        List<int> indexes = [];
        if (defaults is null)
        {
            return indexes;
        }

        for (int index = 0; index < tableNames.Count; ++index)
        {
            if (defaults.Any(item => IsSameName(item.SourceTable.Name, tableNames[index])))
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    private static IReadOnlyList<int> GetDefaultRoleColumnIndexes(
        GXTableSchema table,
        DataVaultColumnRole role,
        IReadOnlyList<GXDataVaultColumnMapping>? defaults)
    {
        if (defaults is null)
        {
            return [];
        }

        List<int> indexes = [];
        for (int index = 0; index < table.Columns.Count; ++index)
        {
            string columnName = table.Columns[index].Name;
            if (defaults.Any(item => item.Role == role && IsSameName(item.SourceColumn, columnName)))
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    private static string GetDefaultDataVaultTargetTable(string sourceTable, DataVaultObjectType objectType)
    {
        string prefix = objectType switch
        {
            DataVaultObjectType.Hub => "HUB",
            DataVaultObjectType.Link => "LNK",
            DataVaultObjectType.Satellite => "SAT",
            DataVaultObjectType.Reference => "REF",
            DataVaultObjectType.Staging => "STG",
            DataVaultObjectType.InformationMart => "MART",
            _ => "DV",
        };
        return $"{prefix}_{sourceTable}";
    }

    private static void PrintDataVaultMappings(GXDataVaultConfiguration configuration)
    {
        if (configuration.GetMappings().Count == 0)
        {
            Console.WriteLine("No Data Vault mappings are configured.");
            return;
        }

        foreach (GXDatabaseConfiguration database in configuration.GetDatabases())
        {
            Console.WriteLine(database.ToString());
            foreach (GXDataVaultTableMapping mapping in database.Mappings ?? [])
            {
                Console.WriteLine($"  {mapping.ObjectType}: {mapping.SourceTable.Name} -> {mapping.TargetTable.Name}");
                Console.WriteLine($"    Schedule: {FormatSchedule(mapping.Schedule)}");
                foreach (GXDataVaultColumnMapping column in mapping.Columns)
                {
                    Console.WriteLine($"    {column.Role}: {column.SourceColumn} -> {column.TargetColumn}");
                }
            }
        }
    }

    private static string FormatSchedule(GXSchedule schedule)
    {
        return schedule.Type switch
        {
            ScheduleType.Interval => $"{schedule.Type} every {schedule.IntervalSeconds} seconds",
            ScheduleType.Daily => $"{schedule.Type} at {schedule.Time}",
            ScheduleType.Cron => $"{schedule.Type} ({schedule.Expression})",
            ScheduleType.DatabaseChange => $"{schedule.Type} every {schedule.IntervalSeconds ?? 1} seconds",
            _ => schedule.Type.ToString(),
        };
    }

    private static void RemoveConfiguredDatabases(List<GXDatabaseConfiguration> databases, string label, bool showDeleteSettings = false,
        IReadOnlyList<GXTransportConfiguration>? transports = null)
    {
        if (databases.Count == 0)
        {
            throw new InvalidOperationException($"{label} list is empty.");
        }

        Console.WriteLine(label + ":");
        for (int index = 0; index < databases.Count; ++index)
        {
            PrintDatabaseConfiguration(databases[index], index + 1, databases.Count, showDeleteSettings);
        }

        IReadOnlyList<int> selected = PromptSelection("Databases to remove", databases.Count, allowMultiple: true);
        if (selected.Any(index => transports?.Any(t => t.Tables.Any(route => route.DatabaseId == databases[index].Id)) == true))
            throw new InvalidOperationException("Remove the database's transport table mappings before deleting the database.");
        foreach (int index in selected.OrderDescending())
        {
            databases.RemoveAt(index);
        }
    }

    private static void ShowClientSettings(GXClientConfiguration configuration)
    {
        configuration.EnsureRoutingIds();
        PrintRootTransportConfigurations(configuration.Transports);
        Console.WriteLine("Client settings");
        Console.WriteLine();
        Console.WriteLine("Source databases:");
        IReadOnlyList<GXDatabaseConfiguration> databases = configuration.GetSourceDatabases();
        for (int index = 0; index < databases.Count; ++index)
        {
            PrintDatabaseConfiguration(databases[index], index + 1, databases.Count);

            PrintDatabaseTableConfigurations(databases[index]);
        }
    }

    private static void ShowServerSettings(GXServerConfiguration configuration)
    {
        configuration.EnsureRoutingIds();
        PrintRootTransportConfigurations(configuration.Transports);
        Console.WriteLine("Server settings");
        Console.WriteLine();
        Console.WriteLine("Destination databases:");
        IReadOnlyList<GXDatabaseConfiguration> databases = configuration.GetTargetDatabases();
        for (int index = 0; index < databases.Count; ++index)
        {
            PrintDatabaseConfiguration(databases[index], index + 1, databases.Count, showDeleteSettings: true);

        }
    }

    private static void ShowDataVaultSettings(GXDataVaultConfiguration configuration, Guid? tableId)
    {
        Console.WriteLine("Data Vault settings");
        Console.WriteLine();
        Console.WriteLine("Databases:");
        IReadOnlyList<GXDatabaseConfiguration> databases = configuration.GetDatabases();
        for (int index = 0; index < databases.Count; ++index)
        {
            PrintDatabaseConfiguration(databases[index], index + 1, databases.Count);
        }
        Console.WriteLine();

        if (tableId is not null)
        {
            GXDataVaultTableMapping? mapping = configuration.GetMappings().FirstOrDefault(item => item.Id == tableId.Value);
            if (mapping is null)
            {
                Console.WriteLine($"Data Vault table ID '{tableId}' was not found.");
                return;
            }

            PrintDataVaultTableDescription(mapping);
            return;
        }

        Console.WriteLine("Data Vault tables:");
        if (configuration.GetMappings().Count == 0)
        {
            Console.WriteLine("No Data Vault mappings are configured.");
            return;
        }

        foreach (GXDatabaseConfiguration database in configuration.GetDatabases())
        {
            Console.WriteLine(database.ToString());
            foreach (GXDataVaultTableMapping mapping in database.Mappings ?? [])
            {
                Console.WriteLine($"  {mapping.Id} | {mapping.ObjectType} | {mapping.SourceTable.Name} -> {mapping.TargetTable.Name} | {FormatSchedule(mapping.Schedule)}");
            }
        }
    }

    private static void PrintDataVaultTableDescription(GXDataVaultTableMapping mapping)
    {
        Console.WriteLine("Data Vault table:");
        Console.WriteLine($"ID: {mapping.Id}");
        Console.WriteLine($"Type: {mapping.ObjectType}");
        Console.WriteLine($"Source table: {mapping.SourceTable.Name}");
        Console.WriteLine($"Target table: {mapping.TargetTable.Name}");
        Console.WriteLine($"Schedule: {FormatSchedule(mapping.Schedule)}");
        Console.WriteLine($"Updated at UTC: {mapping.Updated:O}");
        Console.WriteLine();
        Console.WriteLine("Description:");
        Console.WriteLine(GetDataVaultObjectDescription(mapping.ObjectType));
        Console.WriteLine();
        Console.WriteLine("Column mappings:");
        if (mapping.Columns.Count == 0)
        {
            Console.WriteLine("No column mappings are configured.");
            return;
        }

        foreach (GXDataVaultColumnMapping column in mapping.Columns)
        {
            Console.WriteLine($"{column.Role}: {column.SourceColumn} -> {column.TargetColumn}");
        }
    }

    private static string GetDataVaultObjectDescription(
        DataVaultObjectType? objectType)
    {
        return objectType switch
        {
            DataVaultObjectType.Hub => "Hub stores a unique business key and its hash key. It represents a core business entity independently from descriptive attributes.",
            DataVaultObjectType.Link => "Link stores relationships between two or more hubs. It identifies associations using parent hash keys and an optional link hash key.",
            DataVaultObjectType.Satellite => "Satellite stores descriptive, historized attributes for a parent hub or link. It is driven by parent hash key, load date, record source, and attribute columns.",
            DataVaultObjectType.Reference => "Reference stores relatively static lookup or code-set data. It maps reference keys and optional descriptive attributes.",
            DataVaultObjectType.Staging => "Staging stores raw or lightly standardized source rows before they are transformed into core Data Vault structures.",
            DataVaultObjectType.InformationMart => "Information Mart stores presentation-ready analytical tables built from Data Vault entities for reporting and consumption.",
            _ => "Data Vault mapping defines how source table columns are stored in a target Data Vault table.",
        };
    }

    private static void PrintDatabaseConfiguration(GXDatabaseConfiguration configuration, int index, int count, bool showDeleteSettings = false)
    {
        Console.WriteLine($"[{index}/{count}] {FormatOptional(configuration.Description, "No description")}");
        Console.WriteLine($"  Type: {configuration.Type}");
        Console.WriteLine($"  ConnectionString: {MaskSecrets(configuration.ConnectionString)}");
        if (showDeleteSettings)
        {
            Console.WriteLine($"  DeleteMode: {configuration.DeleteMode}");
            if (!string.IsNullOrWhiteSpace(configuration.DeletedColumn))
            {
                Console.WriteLine($"  DeletedColumn: {configuration.DeletedColumn}");
            }
        }
    }

    private static void PrintRootTransportConfigurations(IReadOnlyList<GXTransportConfiguration> transports)
    {
        Console.WriteLine("Transports:");
        for (int index = 0; index < transports.Count; ++index)
        {
            PrintTransportConfiguration(transports[index], index + 1, transports.Count);
            foreach (GXTransportTableConfiguration route in transports[index].Tables)
                Console.WriteLine($"    {route.Source ?? route.Table} -> {route.DatabaseId}/{route.Table}");
        }
    }
    private static void PrintDatabaseTableConfigurations(GXDatabaseConfiguration configuration)
    {
        if (configuration.Tables is not { Count: > 0 })
        {
            Console.WriteLine("  Tables: none");
            return;
        }

        Console.WriteLine("  Tables:");
        for (int index = 0; index < configuration.Tables.Count; ++index)
        {
            GXTableConfiguration table = configuration.Tables[index];
            Console.WriteLine($"    [{index + 1}/{configuration.Tables.Count}] {table.Name}");
            if (table.Columns.Count > 0)
            {
                Console.WriteLine($"      Columns: {string.Join(", ", table.Columns)}");
            }
            if (table.Keys.Count > 0)
            {
                Console.WriteLine($"      Keys: {string.Join(", ", table.Keys)}");
            }
            if (!string.IsNullOrWhiteSpace(table.IncrementalColumn))
            {
                Console.WriteLine($"      IncrementalColumn: {table.IncrementalColumn}");
            }
            Console.WriteLine($"      ChangeTracking: {table.ChangeTracking.Type}");
            Console.WriteLine($"      Schedule: {table.Schedule.Type}");
        }
    }

    private static void PrintTransportConfiguration(GXTransportConfiguration configuration, int index, int count, string indent = "  ")
    {
        string description = string.IsNullOrWhiteSpace(configuration.Description)
            ? configuration.Type.ToString()
            : $"{configuration.Description} ({configuration.Type})";
        Console.WriteLine($"{indent}[{index}/{count}] {description}");
        if (!string.IsNullOrWhiteSpace(configuration.Host))
        {
            Console.WriteLine($"{indent}  Host: {configuration.Host}");
        }
        if (!string.IsNullOrWhiteSpace(configuration.Broker))
        {
            Console.WriteLine($"{indent}  Broker: {configuration.Broker}");
        }
        if (configuration.Port > 0)
        {
            Console.WriteLine($"{indent}  Port: {configuration.Port}");
        }
        if (!string.IsNullOrWhiteSpace(configuration.Topic))
        {
            Console.WriteLine($"{indent}  Topic: {configuration.Topic}");
        }
        if (!string.IsNullOrWhiteSpace(configuration.AcknowledgementTopic))
        {
            Console.WriteLine($"{indent}  AcknowledgementTopic: {configuration.AcknowledgementTopic}");
        }
        if (!string.IsNullOrWhiteSpace(configuration.Username))
        {
            Console.WriteLine($"{indent}  Username: {configuration.Username}");
        }
        Console.WriteLine($"{indent}  UseTls: {configuration.UseTls}");
        if (configuration.ConnectTimeoutSeconds > 0)
        {
            Console.WriteLine($"{indent}  ConnectTimeoutSeconds: {configuration.ConnectTimeoutSeconds}");
        }
        if (configuration.AcknowledgementTimeoutSeconds > 0)
        {
            Console.WriteLine($"{indent}  AcknowledgementTimeoutSeconds: {configuration.AcknowledgementTimeoutSeconds}");
        }
        if (configuration.MaximumMessageSize > 0)
        {
            Console.WriteLine($"{indent}  MaximumMessageSize: {configuration.MaximumMessageSize}");
        }
        if (configuration.Transfer is not null)
        {
            Console.WriteLine($"{indent}  Transfer:");
            Console.WriteLine($"{indent}    BatchSize: {configuration.Transfer.BatchSize}");
            Console.WriteLine($"{indent}    RetryCount: {configuration.Transfer.RetryCount}");
            Console.WriteLine($"{indent}    RetryDelaySeconds: {configuration.Transfer.RetryDelaySeconds}");
            Console.WriteLine($"{indent}    PingAfterSeconds: {configuration.Transfer.PingAfterSeconds}");
            Console.WriteLine($"{indent}    AllowConcurrentRuns: {configuration.Transfer.AllowConcurrentRuns}");
        }
    }

    private static string FormatOptional(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private async Task ShowEventLogAsync(GXModeConfiguration configuration, CancellationToken cancellationToken)
    {
        await _eventLogService.InitializeAsync(configuration, cancellationToken);
        GXDatabaseConfiguration eventLogDatabase = GetEventLogDatabaseConfiguration();
        long lastSeenId = await ShowCurrentEventLogRowsAsync(eventLogDatabase, cancellationToken);
        if (_commandLine.FollowEvents)
        {
            await FollowEventLogAsync(eventLogDatabase, lastSeenId, cancellationToken);
        }
    }

    private void WriteLifecycleEvent(ApplicationMode mode, string message)
    {
        _eventLogService.Write(
            LogLevel.Information,
            nameof(GXRelayHostedService),
            new EventId(1000, "ApplicationLifecycle"),
            $"{mode}: {message}",
            null,
            null);
    }

    private async Task<long> ShowCurrentEventLogRowsAsync(
        GXDatabaseConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = _connectionFactory.CreateConnection(configuration);
        await connection.OpenAsync(cancellationToken);
        long lastSeenId = 0;
        bool sawAnyRows = false;

        int shown = 0;
        await using DbCommand command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = "SELECT Id, Timestamp, Source, Message, Data FROM GXEventLog ORDER BY Id DESC;";
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (shown < _commandLine.EventTop && await reader.ReadAsync(cancellationToken))
        {
            long id = Convert.ToInt64(reader["Id"]);
            if (!sawAnyRows)
            {
                lastSeenId = id;
                sawAnyRows = true;
            }

            string? source = Convert.ToString(reader["Source"]);
            if (!MatchesLogLevel(source, _commandLine.EventMinimumLevel))
            {
                continue;
            }

            Console.WriteLine(FormatEventLogRow(reader));
            shown += 1;
        }

        if (shown == 0)
        {
            Console.WriteLine("No GXEventLog events matched the selected filters.");
        }

        return lastSeenId;
    }

    private async Task FollowEventLogAsync(
        GXDatabaseConfiguration configuration,
        long lastSeenId,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(EventLogFollowPollInterval, cancellationToken);

            await using DbConnection connection = _connectionFactory.CreateConnection(configuration);
            await connection.OpenAsync(cancellationToken);
            await using DbCommand command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = """
                SELECT Id, Timestamp, Source, Message, Data
                FROM GXEventLog
                WHERE Id > @lastSeenId
                ORDER BY Id ASC;
                """;
            DbParameter lastSeenParameter = command.CreateParameter();
            lastSeenParameter.ParameterName = "@lastSeenId";
            lastSeenParameter.Value = lastSeenId;
            command.Parameters.Add(lastSeenParameter);

            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                lastSeenId = Convert.ToInt64(reader["Id"]);
                string? source = Convert.ToString(reader["Source"]);
                if (!MatchesLogLevel(source, _commandLine.EventMinimumLevel))
                {
                    continue;
                }

                Console.WriteLine(FormatEventLogRow(reader));
            }
        }
    }

    private GXDatabaseConfiguration GetEventLogDatabaseConfiguration()
    {
        return new GXDatabaseConfiguration
        {
            Description = "RelayConfigurationStore",
            Type = _storeSettings.Type,
            ConnectionString = _storeSettings.ConnectionString,
        };
    }

    private async Task<bool> EventLogTableExistsAsync(
        GXDatabaseConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = _connectionFactory.CreateConnection(configuration);
        await connection.OpenAsync(cancellationToken);
        await using GXDbConnection guruxConnection = new(connection);
        GXSchemaManager schemaManager = new(guruxConnection);
        return schemaManager.TableExist(nameof(GXEventLog));
    }

    private static bool MatchesLogLevel(string? source, LogLevel? minimumLevel)
    {
        if (minimumLevel is null)
        {
            return true;
        }

        string? levelText = source?.Split(':', 2)[0];
        return Enum.TryParse(levelText, true, out LogLevel level) && level >= minimumLevel.Value;
    }

    private static string FormatEventLogRow(DbDataReader reader)
    {
        string id = Convert.ToString(reader["Id"]) ?? string.Empty;
        string timestamp = Convert.ToString(reader["Timestamp"]) ?? string.Empty;
        string source = Convert.ToString(reader["Source"]) ?? string.Empty;
        string message = Convert.ToString(reader["Message"]) ?? string.Empty;
        string? data = reader["Data"] is DBNull ? null : Convert.ToString(reader["Data"]);
        string suffix = string.IsNullOrWhiteSpace(data) ? string.Empty : $" | {data}";
        return $"{id} | {timestamp} | {source} | {message}{suffix}";
    }

    private async Task<GXDatabaseConfiguration> PromptForDatabaseConfigurationAsync(
        CancellationToken cancellationToken,
        GXDatabaseConfiguration? defaults = null,
        bool configureDeleteMode = false)
    {
        var catalog = _configurationService as IGXDatabaseCatalogService
            ?? throw new InvalidOperationException("Shared database catalog is required.");
        var databases = await catalog.GetDatabasesAsync(cancellationToken);
        if (databases.Count == 0)
            throw new InvalidOperationException("Add a database in the web application's Databases page before configuring a mode.");
        Console.WriteLine("Select a database from the shared catalog:");
        for (int index = 0; index < databases.Count; ++index)
            Console.WriteLine($"{index + 1}. {databases[index].Description ?? databases[index].Id.ToString()} ({databases[index].Type})");
        int defaultIndex = defaults == null ? -1 : databases.FindIndex(d => d.Id == defaults.Id);
        int selected = PromptSelection("Database", databases.Count, allowMultiple: false,
            defaultSelection: defaultIndex < 0 ? [] : [defaultIndex]).Single();
        var database = databases[selected];
        return new GXDatabaseConfiguration
        {
            Id = database.Id,
            Description = database.Description,
            Type = (DatabaseType)database.Type,
            ConnectionString = database.ConnectionString,
            RecordSource = database.RecordSource,
            DeleteMode = database.DeleteMode,
            DeletedColumn = database.DeletedColumn,
            Tables = defaults?.Id == database.Id ? defaults.Tables : null,
            Mappings = defaults?.Id == database.Id ? defaults.Mappings : null
        };
    }

    private async Task<List<GXDatabaseConfiguration>> PromptForServerDatabaseConfigurationsAsync(
        GXServerConfiguration? defaults,
        CancellationToken cancellationToken)
    {
        List<GXDatabaseConfiguration> defaultDatabases = defaults?.Databases is { Count: > 0 }
            ? defaults.Databases
            : [];
        List<GXDatabaseConfiguration> databases = [];
        int index = 1;

        do
        {
            GXDatabaseConfiguration? databaseDefaults = index <= defaultDatabases.Count ? defaultDatabases[index - 1] : null;
            Console.WriteLine();
            Console.WriteLine($"Server destination database {index}:");
            GXDatabaseConfiguration database = await PromptForDatabaseConfigurationAsync(cancellationToken, databaseDefaults, configureDeleteMode: true);
            databases.Add(database);
            index += 1;
        }
        while (PromptYesNo("Add another destination database", index <= defaultDatabases.Count));

        return databases;
    }

    private async Task<List<GXTransportConfiguration>> PromptForModeTransportsAsync(bool server,
        IReadOnlyList<GXDatabaseConfiguration> databases, IReadOnlyList<GXTransportConfiguration> defaults,
        CancellationToken cancellationToken)
    {
        List<GXTransportConfiguration> transports = [];
        int index = 0;
        do
        {
            GXTransportConfiguration? previous = defaults.ElementAtOrDefault(index);
            Console.WriteLine($"Transport {index + 1}:");
            GXTransportConfiguration transport = PromptForTransportConfiguration(server, previous);
            transport.Id = previous?.Id ?? Guid.NewGuid();
            if (previous != null && PromptYesNo("Keep existing table selections and mappings", true))
            {
                transport.Tables = previous.Tables;
            }
            else if (server)
            {
                do
                {
                    string source = PromptRequiredValue("Incoming source table name");
                    for (int dbIndex = 0; dbIndex < databases.Count; ++dbIndex)
                        Console.WriteLine($"{dbIndex + 1}: {databases[dbIndex].Description ?? databases[dbIndex].Type.ToString()}");
                    GXDatabaseConfiguration database = databases[PromptSelection("Destination database", databases.Count, false)[0]];
                    IReadOnlyList<string> names = await _metadataService.GetTableNamesAsync(database, cancellationToken);
                    if (names.Count == 0)
                        throw new InvalidOperationException("Create a destination table in the database before configuring its transport mapping.");
                    for (int i = 0; i < names.Count; ++i) Console.WriteLine($"{i + 1}: {names[i]}");
                    string table = names[PromptSelection("Destination table", names.Count, false)[0]];
                    transport.Tables.Add(new() { Source = source, DatabaseId = database.Id, Table = table });
                }
                while (PromptYesNo("Add another table mapping", false));
            }
            else
            {
                var tables = databases.SelectMany(db => (db.Tables ?? []).Select(t => (Database: db, Table: t))).ToList();
                if (tables.Count == 0) throw new InvalidOperationException("Configure source tables before adding a transport.");
                for (int i = 0; i < tables.Count; ++i)
                    Console.WriteLine($"{i + 1}: {tables[i].Database.Description ?? tables[i].Database.Type.ToString()} / {tables[i].Table.Name}");
                foreach (int selected in PromptSelection("Source tables to send", tables.Count, true))
                    transport.Tables.Add(new() { DatabaseId = tables[selected].Database.Id, Table = tables[selected].Table.Name });
            }
            transports.Add(transport);
            ++index;
        }
        while (PromptYesNo("Add another transport", index < defaults.Count));
        GXTransportRouting.Validate(databases, transports, server);
        return transports;
    }

    private async Task<List<GXDatabaseConfiguration>> PromptForClientDatabaseConfigurationsAsync(
        GXClientConfiguration? defaults,
        CancellationToken cancellationToken)
    {
        List<GXDatabaseConfiguration> defaultDatabases = defaults?.Databases is { Count: > 0 }
            ? defaults.Databases
            : [];
        List<GXDatabaseConfiguration> databases = [];
        int index = 1;

        do
        {
            GXDatabaseConfiguration? databaseDefaults = index <= defaultDatabases.Count ? defaultDatabases[index - 1] : null;
            Console.WriteLine();
            Console.WriteLine($"Client source database {index}:");
            GXDatabaseConfiguration database = await PromptForDatabaseConfigurationAsync(cancellationToken, databaseDefaults);
            database.Tables = await PromptForClientTablesAsync(database, databaseDefaults?.Tables, cancellationToken);
            databases.Add(database);
            index += 1;
        }
        while (PromptYesNo("Add another source database", index <= defaultDatabases.Count));

        return databases;
    }

    private async Task<List<GXDatabaseConfiguration>> PromptForDataVaultDatabaseConfigurationsAsync(
        GXDataVaultConfiguration? defaults,
        CancellationToken cancellationToken)
    {
        List<GXDatabaseConfiguration> defaultDatabases = defaults?.Databases is { Count: > 0 }
            ? defaults.Databases
            : [];
        List<GXDatabaseConfiguration> databases = [];
        int index = 1;

        do
        {
            GXDatabaseConfiguration? databaseDefaults = index <= defaultDatabases.Count ? defaultDatabases[index - 1] : null;
            Console.WriteLine();
            Console.WriteLine($"Data Vault database {index}:");
            databases.Add(await PromptForDatabaseConfigurationAsync(cancellationToken, databaseDefaults));
            index += 1;
        }
        while (PromptYesNo("Add another Data Vault database", index <= defaultDatabases.Count));

        return databases;
    }

    private async Task<List<GXTableConfiguration>> PromptForClientTablesAsync(
        GXDatabaseConfiguration databaseConfiguration,
        IReadOnlyList<GXTableConfiguration>? defaults,
        CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine("Reading database metadata...");
        IReadOnlyList<string> tableNames = await _metadataService.GetTableNamesAsync(databaseConfiguration, cancellationToken);
        if (tableNames.Count == 0)
        {
            throw new InvalidOperationException("No tables were found in the configured database.");
        }

        Console.WriteLine("Select source tables:");
        for (int pos = 0; pos < tableNames.Count; ++pos)
        {
            Console.WriteLine($"{pos + 1}. {tableNames[pos]}");
        }

        List<int> defaultTableIndexes = GetDefaultTableIndexes(tableNames, defaults);
        IReadOnlyList<int> selectedTableIndexes = PromptSelection("Tables", tableNames.Count, allowMultiple: true, defaultSelection: defaultTableIndexes);
        List<GXTableConfiguration> selectedTables = [];
        foreach (int selectedTableIndex in selectedTableIndexes)
        {
            string tableName = tableNames[selectedTableIndex];
            Console.WriteLine($"Reading table metadata: {tableName}");
            GXTableSchema table = await _metadataService.DescribeTableAsync(databaseConfiguration, tableName, cancellationToken);
            GXTableConfiguration? tableDefaults = defaults?.FirstOrDefault(item => IsSameName(item.Name, table.Name));
            Console.WriteLine();
            Console.WriteLine($"Table: {table}");
            string[] keyColumns = table.Columns
                .Where(static column => column.IsPrimaryKey)
                .Select(static column => column.Name)
                .ToArray();

            if (keyColumns.Length == 0)
            {
                Console.WriteLine("Primary key: none detected. Update and delete replication cannot be guaranteed.");
            }
            else
            {
                Console.WriteLine($"Primary key: {string.Join(", ", keyColumns)}");
            }

            for (int columnIndex = 0; columnIndex < table.Columns.Count; ++columnIndex)
            {
                GXColumnSchema column = table.Columns[columnIndex];
                string keyLabel = column.IsPrimaryKey ? " [PK]" : string.Empty;
                Console.WriteLine($"{columnIndex + 1}. {column.Name} ({column.DbType}){keyLabel}");
            }

            List<int> defaultColumnIndexes = GetDefaultColumnIndexes(table, tableDefaults);
            IReadOnlyList<int> selectedColumnIndexes = PromptSelection("Columns", table.Columns.Count, allowMultiple: true, allowAll: true, defaultSelection: defaultColumnIndexes);
            List<string> selectedColumns = selectedColumnIndexes
                .Select(index => table.Columns[index].Name)
                .ToList();

            string? incrementalColumn = PromptOptionalColumnSelection(table, selectedColumns, tableDefaults?.IncrementalColumn);
            GXChangeTrackingConfiguration changeTracking = PromptChangeTrackingConfiguration(table, selectedColumns, tableDefaults?.ChangeTracking);
            GXSchedule schedule = PromptScheduleConfiguration(tableDefaults?.Schedule);
            selectedTables.Add(new GXTableConfiguration
            {
                Name = table.Name,
                Keys = keyColumns.ToList(),
                Columns = selectedColumns,
                IncrementalColumn = incrementalColumn,
                ChangeTracking = changeTracking,
                Schedule = schedule,
            });
        }

        return selectedTables;
    }

    private async Task VerifyConfigurationAsync(
        ApplicationMode mode,
        GXDatabaseConfiguration configuration,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Testing {Mode} database connection...", mode.ToString().ToLowerInvariant());
        await _connectionTestService.TestConnectionAsync(configuration, cancellationToken);
        _logger.LogInformation("Connection successful.");
    }

    private async Task VerifyServerDatabaseConfigurationsAsync(
        GXServerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<GXDatabaseConfiguration> databases = configuration.GetTargetDatabases();
        for (int index = 0; index < databases.Count; ++index)
        {
            if (string.IsNullOrEmpty(databases[index].Description))
            {
                _logger.LogInformation("Testing server destination database {DatabaseIndex}/{DatabaseCount} connection...", index + 1, databases.Count);
            }
            else
            {
                _logger.LogInformation("Testing server destination database {0} connection...", databases[index].Description);
            }
            try
            {
                await _connectionTestService.TestConnectionAsync(databases[index], cancellationToken);
                if (string.IsNullOrEmpty(databases[index].Description))
                {
                    _logger.LogInformation("Server destination database {DatabaseIndex}/{DatabaseCount} connection successful.", index + 1, databases.Count);
                }
                else
                {
                    _logger.LogInformation("Server destination database {0} connection successful.", databases[index].Description);
                }
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(databases[index].Description))
                {
                    _logger.LogWarning(
                        ex,
                        "Server destination database {DatabaseIndex}/{DatabaseCount} connection failed. Listener startup will continue.",
                        index + 1,
                        databases.Count);
                }
                else
                {
                    _logger.LogWarning(
                        ex,
                        "Server destination database {Description} connection failed. Listener startup will continue.",
                        databases[index].Description);
                }
            }
        }
    }

    private async Task VerifyClientDatabaseConfigurationsAsync(
        GXClientConfiguration configuration,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<GXDatabaseConfiguration> databases = configuration.GetSourceDatabases();
        for (int index = 0; index < databases.Count; ++index)
        {
            _logger.LogInformation("Testing client source database {DatabaseIndex}/{DatabaseCount} connection...", index + 1, databases.Count);
            await _connectionTestService.TestConnectionAsync(databases[index], cancellationToken);
            _logger.LogInformation("Client source database {DatabaseIndex}/{DatabaseCount} connection successful.", index + 1, databases.Count);
        }
    }

    private async Task VerifyDataVaultDatabaseConfigurationsAsync(
        GXDataVaultConfiguration configuration,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<GXDatabaseConfiguration> databases = configuration.GetDatabases();
        for (int index = 0; index < databases.Count; ++index)
        {
            _logger.LogInformation("Testing Data Vault database {DatabaseIndex}/{DatabaseCount} connection...", index + 1, databases.Count);
            await _connectionTestService.TestConnectionAsync(databases[index], cancellationToken);
            _logger.LogInformation("Data Vault database {DatabaseIndex}/{DatabaseCount} connection successful.", index + 1, databases.Count);
        }
    }

    private async Task VerifyTransportConnectionAsync(
        GXTransportConfiguration configuration,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Testing {TransportType} transport connection...", configuration.Type);
        await using IDataTransport transport = _transportFactory.Create(configuration);
        await transport.ConnectAsync(cancellationToken);
        _logger.LogInformation("Transport connection successful.");
    }

    private static GXTransportConfiguration PromptForTransportConfiguration(bool isServer, GXTransportConfiguration? defaults = null)
    {
        Console.WriteLine();
        Console.WriteLine("Transport:");
        string? description = PromptOptionalValue("Transport description", defaults?.Description);
        Console.WriteLine("1. TCP/IP");
        Console.WriteLine("2. MQTT");
        TransportType? defaultType = defaults?.Type;

        while (true)
        {
            string? input = GXConsoleInput.ReadLine(FormatPrompt(">", defaultType?.ToString()));
            ThrowIfInputClosed(input);
            if (string.IsNullOrWhiteSpace(input) && defaultType is not null)
            {
                return defaultType == TransportType.Tcp
                    ? PromptForTcpTransportConfiguration(isServer, description, defaults)
                    : PromptForMqttTransportConfiguration(isServer, description, defaults);
            }

            if (input == "1" || string.Equals(input, "tcp", StringComparison.OrdinalIgnoreCase))
            {
                return PromptForTcpTransportConfiguration(isServer, description, defaults?.Type == TransportType.Tcp ? defaults : null);
            }

            if (input == "2" || string.Equals(input, "mqtt", StringComparison.OrdinalIgnoreCase))
            {
                return PromptForMqttTransportConfiguration(isServer, description, defaults?.Type == TransportType.Mqtt ? defaults : null);
            }

            Console.WriteLine("Select a valid transport type.");
        }
    }

    private static GXTransportConfiguration PromptForTcpTransportConfiguration(
        bool isServer,
        string? description,
        GXTransportConfiguration? defaults = null)
    {
        GXTransportConfiguration configuration = new()
        {
            Description = description,
            Type = TransportType.Tcp,
            Port = PromptPort(isServer ? "Listen port" : "Server port", defaults?.Port),
            MaximumMessageSize = PromptPositiveIntWithDefault("Maximum message size bytes", defaults?.MaximumMessageSize > 0 ? defaults.MaximumMessageSize : 4 * 1024 * 1024),
        };

        if (!isServer)
        {
            configuration.Host = PromptRequiredValue("Server host", defaults?.Host);
            configuration.ConnectTimeoutSeconds = PromptPositiveIntWithDefault("Connect timeout seconds", defaults?.ConnectTimeoutSeconds > 0 ? defaults.ConnectTimeoutSeconds : 10);
            configuration.AcknowledgementTimeoutSeconds = PromptPositiveIntWithDefault("Acknowledgement timeout seconds", defaults?.AcknowledgementTimeoutSeconds > 0 ? defaults.AcknowledgementTimeoutSeconds : 30);
            configuration.Transfer = PromptForTransferConfiguration(defaults?.Transfer);
        }

        return configuration;
    }

    private static GXTransportConfiguration PromptForMqttTransportConfiguration(
        bool isServer,
        string? description,
        GXTransportConfiguration? defaults = null)
    {
        GXTransportConfiguration configuration = new()
        {
            Description = description,
            Type = TransportType.Mqtt,
            Broker = PromptRequiredValue("Broker", defaults?.Broker),
            Port = PromptPort("Port", defaults?.Port),
            Topic = PromptRequiredValue(isServer ? "Subscription topic" : "Topic", defaults?.Topic),
            AcknowledgementTopic = PromptRequiredValue("Acknowledgement topic", defaults?.AcknowledgementTopic),
            Username = PromptOptionalValue("Username", defaults?.Username),
            Password = PromptOptionalValue("Password", defaults?.Password, maskDefault: true),
            UseTls = PromptYesNo("Use TLS", defaults?.UseTls ?? true),
            MaximumMessageSize = PromptPositiveIntWithDefault("Maximum message size bytes", defaults?.MaximumMessageSize > 0 ? defaults.MaximumMessageSize : 4 * 1024 * 1024),
            AcknowledgementTimeoutSeconds = isServer ? defaults?.AcknowledgementTimeoutSeconds ?? 30 : PromptPositiveIntWithDefault("Acknowledgement timeout seconds", defaults?.AcknowledgementTimeoutSeconds > 0 ? defaults.AcknowledgementTimeoutSeconds : 30),
        };
        if (!isServer)
        {
            configuration.Transfer = PromptForTransferConfiguration(defaults?.Transfer);
        }

        return configuration;
    }

    private static void ConfigureDeleteMode(GXDatabaseConfiguration configuration, GXDatabaseConfiguration? defaults = null)
    {
        Console.WriteLine("Delete mode:");
        Console.WriteLine("1. PhysicalDelete");
        Console.WriteLine("2. SoftDelete");
        DeleteMode defaultMode = defaults?.DeleteMode ?? DeleteMode.PhysicalDelete;

        while (true)
        {
            string? input = GXConsoleInput.ReadLine(FormatPrompt(">", defaultMode.ToString()));
            ThrowIfInputClosed(input);
            if (input == "1" ||
                string.Equals(input, "physicaldelete", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(input, "physical", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(input) && defaultMode == DeleteMode.PhysicalDelete)
            {
                configuration.DeleteMode = DeleteMode.PhysicalDelete;
                configuration.DeletedColumn = null;
                return;
            }

            if (input == "2" ||
                string.Equals(input, "softdelete", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(input, "soft", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(input) && defaultMode == DeleteMode.SoftDelete)
            {
                configuration.DeleteMode = DeleteMode.SoftDelete;
                configuration.DeletedColumn = PromptRequiredValue("Deleted column", defaults?.DeletedColumn);
                return;
            }

            Console.WriteLine("Select a valid delete mode.");
        }
    }

    private static DatabaseType PromptDatabaseType(DatabaseType? defaultValue = null)
    {
        DatabaseType[] values = Enum.GetValues<DatabaseType>();
        Console.WriteLine("Database type:");
        for (int pos = 0; pos < values.Length; ++pos)
        {
            Console.WriteLine($"{pos + 1}. {values[pos]}");
        }

        while (true)
        {
            string? input = GXConsoleInput.ReadLine(FormatPrompt(">", defaultValue?.ToString()));
            ThrowIfInputClosed(input);
            if (string.IsNullOrWhiteSpace(input) && defaultValue is not null)
            {
                return defaultValue.Value;
            }

            if (int.TryParse(input, out int index) && index >= 1 && index <= values.Length)
            {
                return values[index - 1];
            }

            if (!string.IsNullOrWhiteSpace(input) && Enum.TryParse(input, true, out DatabaseType parsed))
            {
                return parsed;
            }

            Console.WriteLine("Select a valid database type.");
        }
    }

    private static string PromptRequiredValue(string label, string? defaultValue = null, bool maskDefault = false)
    {
        while (true)
        {
            string? value = GXConsoleInput.ReadLine(FormatPrompt(label, defaultValue, maskDefault));
            ThrowIfInputClosed(value);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }

            if (!string.IsNullOrWhiteSpace(defaultValue))
            {
                return defaultValue;
            }

            Console.WriteLine($"{label} is required.");
        }
    }

    private static string? PromptOptionalValue(string label, string? defaultValue = null, bool maskDefault = false)
    {
        string? value = GXConsoleInput.ReadLine(FormatPrompt(label, defaultValue, maskDefault));
        ThrowIfInputClosed(value);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }

    private static int PromptPort(string label, int? defaultValue = null)
    {
        while (true)
        {
            string? value = GXConsoleInput.ReadLine(FormatPrompt(label, defaultValue is > 0 ? defaultValue.Value.ToString() : null));
            ThrowIfInputClosed(value);
            if (string.IsNullOrWhiteSpace(value) && defaultValue is > 0 and <= 65535)
            {
                return defaultValue.Value;
            }

            if (int.TryParse(value, out int port) && port is > 0 and <= 65535)
            {
                return port;
            }

            Console.WriteLine($"{label} must be between 1 and 65535.");
        }
    }

    private static bool PromptYesNo(string label, bool defaultValue)
    {
        string suffix = defaultValue ? "[Y/n]" : "[y/N]";
        while (true)
        {
            string? value = GXConsoleInput.ReadLine($"{label} {suffix}: ");
            ThrowIfInputClosed(value);
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            if (string.Equals(value, "y", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(value, "n", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Console.WriteLine("Answer yes or no.");
        }
    }

    private static IReadOnlyList<int> PromptSelection(
        string label,
        int count,
        bool allowMultiple,
        bool allowAll = false,
        IReadOnlyList<int>? defaultSelection = null)
    {
        while (true)
        {
            string? defaultText = FormatSelectionDefault(defaultSelection, count, allowAll);
            string allHint = allowAll ? " or ALL" : string.Empty;
            string? input = GXConsoleInput.ReadLine(FormatPrompt($"{label}{allHint}", defaultText));
            ThrowIfInputClosed(input);
            if (string.IsNullOrWhiteSpace(input))
            {
                if (defaultSelection is { Count: > 0 })
                {
                    return defaultSelection;
                }

                Console.WriteLine($"{label} selection is required.");
                continue;
            }

            if (allowAll && string.Equals(input.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
            {
                return Enumerable.Range(0, count).ToArray();
            }

            string[] parts = input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (!allowMultiple && parts.Length != 1)
            {
                Console.WriteLine($"Select exactly one {label.ToLowerInvariant()}.");
                continue;
            }

            List<int> values = [];
            bool invalid = false;
            foreach (string part in parts)
            {
                if (!int.TryParse(part, out int parsed) || parsed < 1 || parsed > count)
                {
                    invalid = true;
                    break;
                }

                int zeroBased = parsed - 1;
                if (!values.Contains(zeroBased))
                {
                    values.Add(zeroBased);
                }
            }

            if (invalid || values.Count == 0)
            {
                Console.WriteLine($"Select valid {label.ToLowerInvariant()} numbers.");
                continue;
            }

            return values;
        }
    }

    private static void ThrowIfInputClosed(string? input)
    {
        if (input is null)
        {
            throw new InvalidOperationException("Input stream closed before configuration was completed.");
        }
    }

    private static string? PromptOptionalColumnSelection(GXTableSchema table, List<string> selectedColumns, string? defaultColumn = null)
    {
        List<GXColumnSchema> eligibleColumns = table.Columns
            .Where(column => selectedColumns.Contains(column.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        string? validDefaultColumn = FindColumnName(eligibleColumns, defaultColumn);

        Console.WriteLine("Incremental column (optional):");
        Console.WriteLine("0. None");
        for (int pos = 0; pos < eligibleColumns.Count; ++pos)
        {
            Console.WriteLine($"{pos + 1}. {eligibleColumns[pos].Name} ({eligibleColumns[pos].DbType})");
        }

        while (true)
        {
            string? input = GXConsoleInput.ReadLine(FormatPrompt(">", validDefaultColumn ?? "None"));
            ThrowIfInputClosed(input);
            if (string.IsNullOrWhiteSpace(input) || input == "0")
            {
                return string.IsNullOrWhiteSpace(input) ? validDefaultColumn : null;
            }

            if (int.TryParse(input, out int index) && index >= 1 && index <= eligibleColumns.Count)
            {
                return eligibleColumns[index - 1].Name;
            }

            string? match = eligibleColumns
                .Select(column => column.Name)
                .FirstOrDefault(name => string.Equals(name, input, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }

            Console.WriteLine("Select a valid incremental column or 0 for none.");
        }
    }

    private static GXChangeTrackingConfiguration PromptChangeTrackingConfiguration(
        GXTableSchema table,
        List<string> selectedColumns,
        GXChangeTrackingConfiguration? defaults = null)
    {
        Console.WriteLine("Change tracking:");
        Console.WriteLine("1. None");
        Console.WriteLine("2. LastRow");
        Console.WriteLine("3. Timestamp");
        Console.WriteLine("4. Version");
        ChangeTrackingType defaultType = defaults?.Type ?? ChangeTrackingType.None;

        while (true)
        {
            string? input = GXConsoleInput.ReadLine(FormatPrompt(">", defaultType.ToString()));
            ThrowIfInputClosed(input);
            if (input == "1" ||
                string.Equals(input, "none", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(input) && defaultType == ChangeTrackingType.None)
            {
                return new GXChangeTrackingConfiguration();
            }

            if (input == "2" ||
                string.Equals(input, "lastrow", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(input, "last row", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(input) && defaultType == ChangeTrackingType.LastRow)
            {
                string lastRowColumn = PromptTrackingColumn("Last row column", table, selectedColumns, required: true, defaults?.Column)!;
                return new GXChangeTrackingConfiguration
                {
                    Type = ChangeTrackingType.LastRow,
                    Column = lastRowColumn,
                };
            }

            if (input == "3" ||
                string.Equals(input, "timestamp", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(input) && defaultType == ChangeTrackingType.Timestamp)
            {
                string? created = PromptTrackingColumn("Created column", table, selectedColumns, required: false, defaults?.CreatedColumn);
                string? updated = PromptTrackingColumn("Updated column", table, selectedColumns, required: false, defaults?.UpdatedColumn);
                string? deleted = PromptTrackingColumn("Deleted column", table, selectedColumns, required: false, defaults?.DeletedColumn);
                if (created is null && updated is null && deleted is null)
                {
                    Console.WriteLine("Select at least one timestamp tracking column.");
                    continue;
                }

                return new GXChangeTrackingConfiguration
                {
                    Type = ChangeTrackingType.Timestamp,
                    CreatedColumn = created,
                    UpdatedColumn = updated,
                    DeletedColumn = deleted,
                };
            }

            if (input == "4" ||
                string.Equals(input, "version", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(input) && defaultType == ChangeTrackingType.Version)
            {
                string versionColumn = PromptTrackingColumn("Version column", table, selectedColumns, required: true, defaults?.Column)!;
                return new GXChangeTrackingConfiguration
                {
                    Type = ChangeTrackingType.Version,
                    Column = versionColumn,
                };
            }

            Console.WriteLine("Select a valid change tracking type.");
        }
    }

    private static string? PromptTrackingColumn(
        string label,
        GXTableSchema table,
        List<string> selectedColumns,
        bool required,
        string? defaultColumn = null)
    {
        string? validDefaultColumn = FindColumnName(table.Columns, defaultColumn);
        Console.WriteLine($"{label}:");
        Console.WriteLine("0. None");
        for (int pos = 0; pos < table.Columns.Count; ++pos)
        {
            Console.WriteLine($"{pos + 1}. {table.Columns[pos].Name} ({table.Columns[pos].DbType})");
        }

        while (true)
        {
            string? input = GXConsoleInput.ReadLine(FormatPrompt(">", validDefaultColumn ?? "None"));
            ThrowIfInputClosed(input);
            if (string.IsNullOrWhiteSpace(input) || input == "0")
            {
                if (string.IsNullOrWhiteSpace(input) && validDefaultColumn is not null)
                {
                    EnsureTrackedColumnSelected(selectedColumns, validDefaultColumn);
                    return validDefaultColumn;
                }

                if (!required)
                {
                    return null;
                }

                Console.WriteLine($"{label} is required.");
                continue;
            }

            if (int.TryParse(input, out int index) && index >= 1 && index <= table.Columns.Count)
            {
                string name = table.Columns[index - 1].Name;
                EnsureTrackedColumnSelected(selectedColumns, name);
                return name;
            }

            GXColumnSchema? match = table.Columns.FirstOrDefault(column => string.Equals(column.Name, input, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                EnsureTrackedColumnSelected(selectedColumns, match.Name);
                return match.Name;
            }

            Console.WriteLine($"Select a valid {label.ToLowerInvariant()}.");
        }
    }

    private static void EnsureTrackedColumnSelected(List<string> selectedColumns, string name)
    {
        if (!selectedColumns.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            selectedColumns.Add(name);
        }
    }

    private static GXSchedule PromptScheduleConfiguration(GXSchedule? defaults = null)
    {
        Console.WriteLine("Schedule:");
        Console.WriteLine("1. Manual");
        Console.WriteLine("2. Interval");
        Console.WriteLine("3. Daily");
        Console.WriteLine("4. Cron");
        Console.WriteLine("5. Continuous");
        Console.WriteLine("6. Database change");
        ScheduleType defaultType = defaults?.Type ?? ScheduleType.Manual;

        while (true)
        {
            string? input = GXConsoleInput.ReadLine(FormatPrompt(">", defaultType.ToString()));
            ThrowIfInputClosed(input);
            string effectiveInput = (string.IsNullOrWhiteSpace(input) ? defaultType.ToString() : input).Trim().ToLowerInvariant();
            switch (effectiveInput)
            {
                case "1":
                case "manual":
                    return new GXSchedule { Type = ScheduleType.Manual };
                case "2":
                case "interval":
                    return new GXSchedule
                    {
                        Type = ScheduleType.Interval,
                        IntervalSeconds = PromptPositiveInt("Interval seconds", defaults?.IntervalSeconds),
                    };
                case "3":
                case "daily":
                    return new GXSchedule
                    {
                        Type = ScheduleType.Daily,
                        Time = PromptRequiredValue("Daily time (HH:mm:ss)", defaults?.Time),
                    };
                case "4":
                case "cron":
                    return new GXSchedule
                    {
                        Type = ScheduleType.Cron,
                        Expression = PromptRequiredValue("Cron expression", defaults?.Expression),
                    };
                case "5":
                case "continuous":
                    return new GXSchedule { Type = ScheduleType.Continuous };
                case "6":
                case "databasechange":
                case "database-change":
                case "change":
                    return new GXSchedule
                    {
                        Type = ScheduleType.DatabaseChange,
                        IntervalSeconds = PromptPositiveInt("Polling interval seconds", defaults?.IntervalSeconds),
                    };
            }

            Console.WriteLine("Select a valid schedule type.");
        }
    }

    private static GXTransferConfiguration PromptForTransferConfiguration(GXTransferConfiguration? defaults = null)
    {
        Console.WriteLine("Transfer settings:");
        return new GXTransferConfiguration
        {
            BatchSize = PromptPositiveIntWithDefault("Batch size", defaults?.BatchSize ?? 1000),
            RetryCount = PromptPositiveIntWithDefault("Retry count", defaults?.RetryCount ?? 5),
            RetryDelaySeconds = PromptPositiveIntWithDefault("Retry delay seconds", defaults?.RetryDelaySeconds ?? 10),
            PingAfterSeconds = PromptNonNegativeIntWithDefault("Ping after idle seconds (0 disables)", defaults?.PingAfterSeconds ?? 300),
            AllowConcurrentRuns = PromptYesNo("Allow concurrent runs", defaults?.AllowConcurrentRuns ?? false),
        };
    }

    private static LogLevel PromptForEventLogMinimumLevel(LogLevel? defaultValue = null)
    {
        Console.WriteLine("Event log minimum level:");
        Console.WriteLine("1. Trace");
        Console.WriteLine("2. Debug");
        Console.WriteLine("3. Information");
        Console.WriteLine("4. Warning");
        Console.WriteLine("5. Error");
        Console.WriteLine("6. Critical");

        LogLevel effectiveDefault = defaultValue ?? LogLevel.Information;
        while (true)
        {
            string? input = GXConsoleInput.ReadLine(FormatPrompt(">", effectiveDefault.ToString()));
            ThrowIfInputClosed(input);
            switch (input?.Trim().ToLowerInvariant())
            {
                case "":
                    return effectiveDefault;
                case "1":
                case "trace":
                    return LogLevel.Trace;
                case "2":
                case "debug":
                    return LogLevel.Debug;
                case "3":
                case "information":
                case "info":
                    return LogLevel.Information;
                case "4":
                case "warning":
                case "warn":
                    return LogLevel.Warning;
                case "5":
                case "error":
                    return LogLevel.Error;
                case "6":
                case "critical":
                    return LogLevel.Critical;
            }

            Console.WriteLine("Select a valid event log level.");
        }
    }

    private static int PromptPositiveInt(string label, int? defaultValue = null)
    {
        while (true)
        {
            string? value = GXConsoleInput.ReadLine(FormatPrompt(label, defaultValue is > 0 ? defaultValue.Value.ToString() : null));
            ThrowIfInputClosed(value);
            if (string.IsNullOrWhiteSpace(value) && defaultValue is > 0)
            {
                return defaultValue.Value;
            }

            if (int.TryParse(value, out int parsed) && parsed > 0)
            {
                return parsed;
            }

            Console.WriteLine($"{label} must be a positive integer.");
        }
    }

    private static int PromptPositiveIntWithDefault(string label, int defaultValue)
    {
        while (true)
        {
            string? value = GXConsoleInput.ReadLine($"{label} [{defaultValue}]: ");
            ThrowIfInputClosed(value);
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            if (int.TryParse(value, out int parsed) && parsed > 0)
            {
                return parsed;
            }

            Console.WriteLine($"{label} must be a positive integer.");
        }
    }

    private static int PromptNonNegativeIntWithDefault(string label, int defaultValue)
    {
        while (true)
        {
            string? value = GXConsoleInput.ReadLine($"{label} [{defaultValue}]: ");
            ThrowIfInputClosed(value);
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            if (int.TryParse(value, out int parsed) && parsed >= 0)
            {
                return parsed;
            }

            Console.WriteLine($"{label} must be zero or a positive integer.");
        }
    }

    private static string FormatPrompt(string label, string? defaultValue, bool maskDefault = false)
    {
        if (string.IsNullOrWhiteSpace(defaultValue))
        {
            return label == ">" ? "> " : $"{label}: ";
        }

        string displayValue = maskDefault ? FormatMaskedDefault(defaultValue) : defaultValue;
        return label == ">" ? $"> [{displayValue}]: " : $"{label} [{displayValue}]: ";
    }

    private static string FormatMaskedDefault(string defaultValue)
    {
        string masked = MaskSecrets(defaultValue);
        return string.Equals(masked, defaultValue, StringComparison.Ordinal) ? "***" : masked;
    }

    private static string? FormatSelectionDefault(IReadOnlyList<int>? defaultSelection, int count, bool allowAll)
    {
        if (defaultSelection is not { Count: > 0 })
        {
            return null;
        }

        if (allowAll && defaultSelection.Count == count)
        {
            return "ALL";
        }

        return string.Join(", ", defaultSelection.Select(index => (index + 1).ToString()));
    }

    private static List<int> GetDefaultTableIndexes(IReadOnlyList<string> tableNames, IReadOnlyList<GXTableConfiguration>? defaults)
    {
        List<int> indexes = [];
        if (defaults is null)
        {
            return indexes;
        }

        for (int index = 0; index < tableNames.Count; ++index)
        {
            if (defaults.Any(item => IsSameName(item.Name, tableNames[index])))
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    private static List<int> GetDefaultColumnIndexes(GXTableSchema table, GXTableConfiguration? defaults)
    {
        List<int> indexes = [];
        if (defaults is null)
        {
            return indexes;
        }

        for (int index = 0; index < table.Columns.Count; ++index)
        {
            GXColumnSchema column = table.Columns[index];
            if (defaults.Columns.Any(name => IsSameName(name, column.Name)))
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    private static string? FindColumnName(IEnumerable<GXColumnSchema> columns, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return columns
            .Select(column => column.Name)
            .FirstOrDefault(columnName => IsSameName(columnName, name));
    }

    private static bool IsSameName(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  client|server|datavault --rest true|false   Save REST/web administration availability and exit.");
        Console.WriteLine("  GXDataRelay update");
        Console.WriteLine("  GXDataRelay client [--configure [add|edit|remove]] [--reset] [--send-schema] [--settings] [--import-settings FILE] [--export-settings FILE] [--events [--top N] [--log-level LEVEL] [--follow|-f]]");
        Console.WriteLine("  GXDataRelay server [--configure [add|edit|remove]] [--settings] [--import-settings FILE] [--export-settings FILE] [--events [--top N] [--log-level LEVEL] [--follow|-f]]");
        Console.WriteLine("  GXDataRelay both [--configure [add|edit|remove]] [--reset] [--send-schema]");
        Console.WriteLine("  GXDataRelay --client-server [--configure [add|edit|remove]] [--reset] [--send-schema]");
        Console.WriteLine("  GXDataRelay datavault [--configure [add|edit|remove]] [--reset] [--send-schema] [--settings] [--data-vault-table ID] [--refresh-information-mart] [--import-settings FILE] [--export-settings FILE] [--events [--top N] [--log-level LEVEL] [--follow|-f]]");
        Console.WriteLine("  GXDataRelay --mode client [--configure [add|edit|remove]] [--reset] [--send-schema] [--settings] [--import-settings FILE] [--export-settings FILE] [--events [--top N] [--log-level LEVEL] [--follow|-f]]");
        Console.WriteLine("  GXDataRelay --mode server [--configure [add|edit|remove]] [--settings] [--import-settings FILE] [--export-settings FILE] [--events [--top N] [--log-level LEVEL] [--follow|-f]]");
        Console.WriteLine("  GXDataRelay --mode datavault [--configure [add|edit|remove]] [--reset] [--send-schema] [--settings] [--data-vault-table ID] [--refresh-information-mart] [--import-settings FILE] [--export-settings FILE] [--events [--top N] [--log-level LEVEL] [--follow|-f]]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  update, --update              Update configuration store tables from their models and exit.");
        Console.WriteLine("  --configure [add|edit|remove] Create, append, edit, or remove saved database settings.");
        Console.WriteLine("  --reset, --reset-client-state  Reset client checkpoints or drop Data Vault target tables.");
        Console.WriteLine("  --send-schema                  Send client table schemas or create Data Vault target table schemas and exit.");
        Console.WriteLine("  both, --client-server          Run client transfer and server listeners in the same process.");
        Console.WriteLine("  --events, --show-events        Show GXEventLog rows and exit.");
        Console.WriteLine("  --follow, -f                   Keep following new GXEventLog rows after the initial --events output.");
        Console.WriteLine("  --settings, --show-settings    Show saved connection settings and exit.");
        Console.WriteLine("  --data-vault-table ID          Show one Data Vault table mapping description.");
        Console.WriteLine("  --refresh-information-mart     Regenerate Data Vault Information Mart tables for the selected --data-vault-table source.");
        Console.WriteLine("  --export-settings FILE         Export saved settings to a JSON file and exit.");
        Console.WriteLine("  --settings-file FILE           Configuration database settings file (default: gurux.data.relay.settings.json).");
        Console.WriteLine("                                 Relative paths use the application content root; missing files prompt for setup.");
        Console.WriteLine("  --import-settings FILE         Import settings from a JSON file and exit.");
        Console.WriteLine("  --top N                        Limit shown GXEventLog rows. Default: 50.");
        Console.WriteLine("  --log-level LEVEL              Show GXEventLog rows at LEVEL or above.");
        Console.WriteLine("  --event-log-level LEVEL      Write event log rows at LogLevel LEVEL or above.");
        Console.WriteLine("  --communication-log-level LEVEL");
        Console.WriteLine("                                  Write communication log rows at LogLevel LEVEL or above.");
    }

    private static string MaskSecrets(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        string[] secretKeys = ["Password", "Pwd", "User ID", "UID"];
        string masked = input;
        foreach (string key in secretKeys)
        {
            masked = ReplaceValue(masked, key);
        }

        return masked;
    }

    private static string ReplaceValue(string input, string key)
    {
        int searchStart = 0;
        while (searchStart < input.Length)
        {
            int keyIndex = input.IndexOf(key + "=", searchStart, StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0)
            {
                break;
            }

            int valueStart = keyIndex + key.Length + 1;
            int valueEnd = input.IndexOf(';', valueStart);
            if (valueEnd < 0)
            {
                valueEnd = input.Length;
            }

            input = string.Concat(input.AsSpan(0, valueStart), "***", input.AsSpan(valueEnd));
            searchStart = valueStart + 3;
        }

        return input;
    }
}








