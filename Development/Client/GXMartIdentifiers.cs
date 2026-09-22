using Gurux.Service.Orm.Common.Model;
using System.Text;
using Gurux.Service.Orm.Model;

namespace Gurux.Data.Relay.Client;

internal static class GXMartIdentifiers
{
    public static string Canonical(string name) => string.Join('.', Parts(name));
    // Describe can return schema.table (or [schema].[table]) in Name with Schema unset.
    public static GXTableSchema Normalize(GXTableSchema source)
    {
        var parts = Parts(source.Name);
        if (parts.Count > 2) throw new ArgumentException("Cross-catalog mart tables are not supported.");
        var schema = new GXTableSchema
        {
            Name = parts[^1], Schema = parts.Count == 2 ? parts[0] : string.IsNullOrEmpty(source.Schema) ? null : Parts(source.Schema).Single(),
            Catalog = source.Catalog, Comment = source.Comment, TableType = source.TableType
        };
        schema.Columns.AddRange(source.Columns);
        return schema;
    }

    private static List<string> Parts(string value)
    {
        List<string> parts = [];
        var part = new StringBuilder();
        char closing = '\0';
        for (int i = 0; i < value.Length; ++i)
        {
            char c = value[i];
            if (closing != '\0')
            {
                if (c == closing)
                {
                    if (i + 1 < value.Length && value[i + 1] == closing) { part.Append(c); ++i; }
                    else closing = '\0';
                }
                else part.Append(c);
            }
            else if (c is '[' or '"' or '`') closing = c == '[' ? ']' : c;
            else if (c == '.') { parts.Add(part.ToString()); part.Clear(); }
            else part.Append(c);
        }
        parts.Add(part.ToString());
        if (closing != '\0' || parts.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Invalid qualified table name.");
        return parts;
    }
}
