using Gurux.Service.Orm.Common.Model;

namespace Gurux.Data.Relay.Web.Client.Services;

/// <summary>
/// Provides schema queries used by the table description view.
/// </summary>
public static class GXTableSchemaExtensions
{
    /// <summary>
    /// Determines whether a column belongs to the primary key or an index.
    /// </summary>
    /// <param name="schema">The table schema containing the indexes.</param>
    /// <param name="column">The column to check.</param>
    /// <returns>Whether the column is a primary key or appears in an index.</returns>
    public static bool IsIndexed(this GXTableSchema schema, GXColumnSchema column) =>
        column.IsPrimaryKey || schema.Indexes.Any(index => index.Columns.Any(item =>
            string.Equals(item.Name, column.Name, StringComparison.OrdinalIgnoreCase)));
}
