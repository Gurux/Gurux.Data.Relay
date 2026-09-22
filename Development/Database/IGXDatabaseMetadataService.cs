using Gurux.Data.Relay.Configuration;
using Gurux.Service.Orm.Common.Model;

namespace Gurux.Data.Relay.Database;

public interface IGXDatabaseMetadataService
{
    Task<IReadOnlyList<string>> GetTableNamesAsync(GXDatabaseConfiguration configuration, CancellationToken cancellationToken);

    Task<GXTableSchema> DescribeTableAsync(GXDatabaseConfiguration configuration, string tableName, CancellationToken cancellationToken);

    Task<IReadOnlyList<GXTableSchema>> GetTablesAsync(GXDatabaseConfiguration configuration, CancellationToken cancellationToken);
}

