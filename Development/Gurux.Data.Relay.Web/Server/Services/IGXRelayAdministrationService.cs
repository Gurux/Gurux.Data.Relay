using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Log;
using Gurux.Data.Relay.Enums;
using Gurux.Service.Orm.Common.Model;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Shared;

namespace Gurux.Data.Relay.Web.Server.Services;

public interface IGXRelayAdministrationService
{
    Task<string> ExportDatabaseDiagramAsync(GXDatabaseConfiguration configuration, CancellationToken cancellationToken);
    Task<IReadOnlyList<GXDatabaseConfiguration>> GetDatabasesAsync(ApplicationMode mode, CancellationToken cancellationToken);
    Task<string?> SaveDatabasesAsync(ApplicationMode mode, IReadOnlyList<GXDatabaseConfiguration> databases, CancellationToken cancellationToken, string? concurrencyStamp = null);

    Task<GXClientConfiguration> GetClientSettingsAsync(CancellationToken cancellationToken, GXDatabase? filter = null);
    Task UpdateClientSettingsAsync(GXClientConfiguration configuration, CancellationToken cancellationToken);
    Task<GXClientState> GetClientStateAsync(CancellationToken cancellationToken);
    Task ResetClientStateAsync(CancellationToken cancellationToken);
    Task ResetClientTableCheckpointAsync(Guid databaseId, string stateTableName, string? concurrencyStamp, CancellationToken cancellationToken);
    Task<int> SendClientSchemaAsync(CancellationToken cancellationToken);

    Task<GXServerConfiguration> GetServerSettingsAsync(CancellationToken cancellationToken, GXDatabase? filter = null);
    Task UpdateServerSettingsAsync(GXServerConfiguration configuration, CancellationToken cancellationToken);
    Task<GXServerState> GetServerStateAsync(CancellationToken cancellationToken);

    Task<GXDataVaultConfiguration> GetDataVaultSettingsAsync(CancellationToken cancellationToken, GXDatabase? filter = null);
    Task UpdateDataVaultSettingsAsync(GXDataVaultConfiguration configuration, CancellationToken cancellationToken);

    Task<string> ExportSettingsJsonAsync(ApplicationMode mode, CancellationToken cancellationToken);
    Task ImportSettingsJsonAsync(ApplicationMode mode, string json, CancellationToken cancellationToken);

    Task<IReadOnlyList<GXEventLog>> GetEventsAsync(ApplicationMode mode, int top, LogLevel? minimumLevel, int? databaseIndex, CancellationToken cancellationToken);
    Task<int> ClearEventsAsync(ApplicationMode mode, int? databaseIndex, CancellationToken cancellationToken);
    Task<Shared.GXEventPage<GXEventLog>> GetEventPageAsync(ApplicationMode mode, Shared.GXEventPageRequest request, CancellationToken cancellationToken);

    Task TestConnectionAsync(GXDatabaseConfiguration configuration, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetTableNamesAsync(GXDatabaseConfiguration configuration, CancellationToken cancellationToken);
    Task<GXTableSchema> DescribeTableAsync(GXDatabaseConfiguration configuration, string tableName, CancellationToken cancellationToken);

    Task<IReadOnlyList<(int DatabaseIndex, GXDataVaultTableMapping Mapping)>> ListDataVaultMappingsAsync(CancellationToken cancellationToken);
    Task<(int DatabaseIndex, GXDataVaultTableMapping Mapping)?> GetDataVaultMappingAsync(Guid id, CancellationToken cancellationToken);
    Task<GXDataVaultTableMapping> CreateDataVaultMappingAsync(int databaseIndex, GXDataVaultTableMapping mapping, CancellationToken cancellationToken);
    Task<GXDataVaultTableMapping> UpdateDataVaultMappingAsync(Guid id, int databaseIndex, GXDataVaultTableMapping mapping, CancellationToken cancellationToken);
    Task<bool> DeleteDataVaultMappingAsync(Guid id, CancellationToken cancellationToken);
}

