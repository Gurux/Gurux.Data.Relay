using System.Data;
using Gurux.Data.Relay.Shared;
using Gurux.Service.Orm;

namespace Gurux.Data.Relay.Configuration;

internal static class GXEntityPersistence
{
    public static void CopyMetadata(IGXEntityMetadata source, IGXEntityMetadata target)
    {
        target.CreationTime = source.CreationTime;
        target.Updated = source.Updated;
        target.ConcurrencyStamp = source.ConcurrencyStamp;
    }

    public static void Initialize(IGXEntityMetadata value)
    {
        value.CreationTime = DateTimeOffset.Now;
        value.Updated = value.CreationTime;
        value.ConcurrencyStamp = Guid.NewGuid().ToString();
    }

    public static async Task SaveAsync<T>(GXDbConnection connection, IDbTransaction transaction,
        T value, T? existing, CancellationToken cancellationToken, DateTimeOffset? importedAt = null) where T : class, IGXEntityMetadata
    {
        if (existing is null)
        {
            if (value.ConcurrencyStamp is not null)
            {
                throw Conflict<T>();
            }
            Initialize(value);
            if (importedAt is not null) value.CreationTime = value.Updated = importedAt;
            await connection.InsertAsync(transaction, GXInsertArgs.Insert(value), cancellationToken);
            return;
        }

        string? expected = value.ConcurrencyStamp;
        if (expected is null || !string.Equals(expected, existing.ConcurrencyStamp, StringComparison.Ordinal))
        {
            throw Conflict<T>();
        }
        value.CreationTime = importedAt ?? existing.CreationTime;
        value.Updated = importedAt ?? DateTimeOffset.UtcNow;
        value.ConcurrencyStamp = Guid.NewGuid().ToString();
        var update = GXUpdateArgs.Update(value);
        update.Where.And<T>(row => row.ConcurrencyStamp == expected);
        update.UseQueryCache(connection.QueryCache);
        int count = await connection.UpdateAsync(transaction, update, cancellationToken);
        if (count != 1)
        {
            throw Conflict<T>();
        }
    }

    private static DBConcurrencyException Conflict<T>() => new(
        $"{typeof(T).Name} has changed or was deleted since it was loaded. Reload the data before saving.");
}
