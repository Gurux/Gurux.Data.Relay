using System.Data.Common;
using Gurux.Data.Relay.Configuration;

namespace Gurux.Data.Relay.Database;

public interface IGXDatabaseConnectionFactory
{
    DbConnection CreateConnection(GXDatabaseConfiguration configuration);
}
