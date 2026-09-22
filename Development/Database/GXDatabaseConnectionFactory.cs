using System.Data.Common;
using System.Data.Odbc;
using Gurux.Data.Relay.Configuration;
using Gurux.Service.Orm.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using IBM.Data.Db2;
using Sap.Data.Hana;
using Gurux.Service.Orm.Common.Enums;

namespace Gurux.Data.Relay.Database;

/// <summary>
/// Represents a factory for creating database connections based on the provided configuration.
/// </summary>
public sealed class GXDatabaseConnectionFactory : IGXDatabaseConnectionFactory
{
    /// <summary>
    /// Creates a database connection based on the provided configuration.
    /// </summary>
    /// <param name="configuration">The database configuration.</param>
    /// <returns>A database connection.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the connection string is null or empty.</exception>
    /// <exception cref="NotSupportedException">Thrown when the database type is not supported.</exception>
    public DbConnection CreateConnection(GXDatabaseConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(configuration.ConnectionString))
        {
            throw new InvalidOperationException("Database connection string is required.");
        }

        return configuration.Type switch
        {
            DatabaseType.MSSQL => new SqlConnection(configuration.ConnectionString),
            DatabaseType.PostgreSQL => new NpgsqlConnection(configuration.ConnectionString),
            DatabaseType.MySQL => new MySqlConnection(configuration.ConnectionString),
            DatabaseType.MariaDB => new MySqlConnection(configuration.ConnectionString),
            DatabaseType.SqLite => new SqliteConnection(configuration.ConnectionString),
            DatabaseType.Oracle => new OracleConnection(configuration.ConnectionString),
            DatabaseType.DB2 => new DB2Connection(configuration.ConnectionString),
            DatabaseType.SapHana => new HanaConnection(configuration.ConnectionString),
            _ => throw new NotSupportedException($"Database type '{configuration.Type}' is not supported."),
        };
    }
}
