using Gurux.Data.Relay.Database;
using Gurux.Data.Relay.Shared;
using Gurux.Service.Orm;
using Gurux.Service.Orm.Model;
using System.Runtime.Serialization;

namespace Gurux.Data.Relay.Configuration;

public static class GXConfigurationTableUpdater
{
    /// <summary>Reads pending configuration model changes without updating tables.</summary>
    public static async Task<List<Shared.GXConfigurationTableChange>> GetChangesAsync(GXConfigurationStoreSettings settings,
        IGXDatabaseConnectionFactory connectionFactory, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection(new GXDatabaseConfiguration { Type = settings.Type, ConnectionString = settings.ConnectionString });
        await connection.OpenAsync(cancellationToken);
        var schema = new GXSchemaManager(connection);
        var changes = new List<GXConfigurationTableChange>();
        foreach (var type in GXRelationalConfigurationStore.EntityTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pending = schema.GetTableChanges(type);
            if (pending.Any())
            {
                //TODO: This is not work with SQLite and fix is under construction.
                changes.Add(new() { TableName = type.Name, Changes = pending });
            }
        }
        return changes;
    }

    public static async Task UpdateAsync(GXConfigurationStoreSettings settings,
        IGXDatabaseConnectionFactory connectionFactory, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection(new GXDatabaseConfiguration
        {
            Type = settings.Type,
            ConnectionString = settings.ConnectionString
        });
        await connection.OpenAsync(cancellationToken);
        var schema = new GXSchemaManager(connection);

        foreach (var type in GXRelationalConfigurationStore.EntityTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!schema.TableExist(type)) schema.CreateTable(type, relations: false, overwrite: false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!schema.GetColumns(typeof(GXDataVaultTableMapping)).Contains("Database", StringComparer.OrdinalIgnoreCase))
        {
            // Add a nullable column first so populated stores can be upgraded.
            // Ownership is recovered from the existing ordered references.
            schema.UpdateTable(typeof(MappingDatabaseUpgrade), updateForeignKeys: false);
        }
        var orm = new GXDbConnection(connection);
        {
            var pending = orm.SelectAll<MappingDatabaseUpgrade>().Where(m => m.Database is null || m.Database == Guid.Empty).ToList();
            if (pending.Count > 0 && schema.TableExist(typeof(Shared.GXDatabaseMappingReference)))
            {
                var links = orm.SelectAll<Shared.GXDatabaseMappingReference>();
                var databases = orm.SelectAll<Shared.GXDatabase>();
                using var transaction = connection.BeginTransaction();
                foreach (var mapping in pending)
                {
                    var owners = links.Where(r => r.TargetId == mapping.Id).Select(r => r.OwnerId).Distinct().ToList();
                    if (!owners.Any())
                    {
                        throw new InvalidOperationException("Legacy database ownership is not supported. Create a new shared database catalog.");
                    }
                    if (owners.Count != 1)
                    {
                        throw new InvalidOperationException($"Cannot determine database for mapping {mapping.Id}. Assign a single owning database before updating.");
                    }
                    mapping.Database = owners[0];
                    await orm.UpdateAsync(transaction, GXUpdateArgs.Update(mapping, m => m.Database), cancellationToken);
                }
                transaction.Commit();
            }
        }

        if (!schema.TableExist(typeof(Shared.GXDataVaultColumnMapping)))
        {
            throw new InvalidOperationException("GXDataVaultColumnMapping was not found in the configuration store.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        schema.UpdateTable(typeof(Shared.GXDataVaultColumnMapping));

        if (schema.TableExist(typeof(Shared.GXTableColumn)))
        {
            var columnNames = schema.GetColumns(typeof(Shared.GXTableColumn));
            if (columnNames.Contains("TableId", StringComparer.OrdinalIgnoreCase))
                schema.RenameTableColumn<Shared.GXTableColumn>("TableId", "Table");
            if (columnNames.Contains("Name", StringComparer.OrdinalIgnoreCase))
                schema.RenameTableColumn<Shared.GXTableColumn>("Name", "ColumnName");
            schema.UpdateTable(typeof(Shared.GXTableColumn));
        }

        foreach (var type in GXRelationalConfigurationStore.EntityTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            schema.UpdateTable(type, updateForeignKeys: true, removeUnusedColumns: true);
        }

    }
  
    [DataContract(Name = "GXDataVaultTableMapping")]
    private sealed class MappingDatabaseUpgrade : Gurux.Service.Orm.Common.IUnique<Guid>
    {
        [DataMember] public Guid Id { get; set; }
        [DataMember] public Guid? Database { get; set; }
    }
}
