using System.Data.Common;
using Gurux.Service.Orm.Common.Enums;
using Gurux.Service.Orm.Model;

namespace Gurux.Data.Relay.Configuration;

/// <summary>Offline reset of the explicitly owned Relay and example-data tables.</summary>
public static class GXApplicationTableReset
{
    public static void Reset(DbConnection connection, DatabaseType provider, Action<string>? report = null)
    {
        if (connection.State != System.Data.ConnectionState.Open)
            throw new InvalidOperationException("Open the database before resetting tables.");
        var names = new HashSet<string>(GXRelationalConfigurationStore.EntityTypes.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
        names.UnionWith(new[] { "RelayRuntimeState", "GXClientTableState", "GXTableMapping", "GXProcessedMessageState",
            "GXEventLog", "GXTransportMessageLog", "GXDataVaultMappingState",
            "Customer", "STG_Customer", "Hub_Company", "HUB_Customer", "LINK_CustomerCompany",
            "MART_Customer", "MART_Customer2", "MART_Customer5", "REF_Country", "Sat_Company", "SAT_Customer" });
        var manager = new GXSchemaManager(connection);
        var tables = manager.GetTables();
        // No prefix matching, no cascade, and no removal in other schemas.
        var selected = tables.Where(names.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var descriptions = tables.ToDictionary(t => t, t => manager.Describe(t), StringComparer.OrdinalIgnoreCase);
        foreach (var (table, schema) in descriptions)
            if (!selected.Contains(table) && schema.ForeignKeys.Any(k => selected.Contains(k.ReferencedTable)))
                throw new InvalidOperationException($"Reset refused: unrelated table '{table}' references a selected table.");
        string Quote(string name) => provider switch
        {
            DatabaseType.MSSQL => "[" + name.Replace("]", "]]") + "]",
            DatabaseType.MySQL or DatabaseType.MariaDB => "`" + name.Replace("`", "``") + "`",
            _ => "\"" + name.Replace("\"", "\"\"") + "\""
        };
        // SQL Server enumeration is restricted to dbo; do not resolve a same-name
        // table through the login's possibly different default schema.
        string QuoteTable(string name) => provider == DatabaseType.MSSQL ? "[dbo]." + Quote(name) : Quote(name);
        void Run(string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
        bool sqlite = provider == DatabaseType.SqLite;
        object? enforcement = null;
        if (sqlite)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys";
            enforcement = command.ExecuteScalar();
        }
        foreach (var table in selected) report?.Invoke("Will remove table and all rows: " + table);
        try
        {
            if (sqlite) Run("PRAGMA foreign_keys=OFF");
            else
                foreach (var table in selected)
                    foreach (var key in descriptions[table].ForeignKeys)
                    {
                        string drop = provider is DatabaseType.MySQL or DatabaseType.MariaDB ? "DROP FOREIGN KEY " : "DROP CONSTRAINT ";
                        Run($"ALTER TABLE {QuoteTable(table)} {drop}{Quote(key.Name)}");
                    }
            foreach (var table in selected)
            {
                Run("DROP TABLE " + QuoteTable(table));
                report?.Invoke("Removed: " + table);
            }
        }
        finally
        {
            if (sqlite) Run("PRAGMA foreign_keys=" + (Convert.ToInt32(enforcement) != 0 ? "ON" : "OFF"));
        }
    }
}
