using Gurux.Service.Orm;
using Gurux.Service.Orm.Model;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Log;
using System.Data;

namespace Gurux.Data.Relay.Database;

public sealed class GXDatabaseConnectionTestService : IGXDatabaseConnectionTestService
{
    private readonly IGXDatabaseConnectionFactory _connectionFactory;

    public GXDatabaseConnectionTestService(IGXDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task TestConnectionAsync(GXDatabaseConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection(configuration);
        await connection.OpenAsync(cancellationToken);
        await using GXDbConnection guruxConnection = new(connection);
        if (guruxConnection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException("The database connection is not open.");
        }
    }
}
