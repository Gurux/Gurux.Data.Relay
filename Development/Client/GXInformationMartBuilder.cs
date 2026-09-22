using Gurux.Service.Orm.Common.Model;
using System.Data.Common;
using System.Text.RegularExpressions;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Shared;
using Gurux.Service.Orm.Common.Enums;
using Gurux.Data.Relay.Shared.Enums;
namespace Gurux.Data.Relay.Client;

public sealed record GXInformationMartPlan(
    GXTableSchema TargetSchema,
    GXDataVaultTableMapping Mapping,
    string SelectSql, string TargetSql,
    string TargetColumnsSql,
    IReadOnlyList<string> CardinalityChecks);

/// <summary>Plans a current-state mart from mapped keys, without accepting SQL expressions.</summary>
public sealed class GXInformationMartBuilder(
    IReadOnlyList<GXDataVaultTableMapping> mappings, Func<string, GXTableSchema> describe, DatabaseType databaseType)
{
    private sealed record Edge(GXDataVaultTableMapping Parent, GXDataVaultTableMapping Child, GXDataVaultColumnMapping ParentKey, GXDataVaultColumnMapping ChildKey);
    private sealed record Node(GXDataVaultTableMapping GXDataVaultTableMapping, List<Edge> Path);
    private readonly Dictionary<string, GXTableSchema> _schemas = new(StringComparer.OrdinalIgnoreCase);
    private static GXTable Source(GXDataVaultTableMapping mapping) => mapping.SourceTable
        ?? throw new ArgumentException($"Mapping '{mapping.Id}' requires a source table.");
    private static GXTable Target(GXDataVaultTableMapping mapping) => mapping.TargetTable
        ?? throw new ArgumentException($"Mapping '{mapping.Id}' requires a target table.");
    private GXTableSchema Schema(GXDataVaultTableMapping mapping)
    {
        if (!_schemas.TryGetValue(Target(mapping).Name, out var schema))
            _schemas[Target(mapping).Name] = schema = GXMartIdentifiers.Normalize(describe(Target(mapping).Name));
        return schema;
    }
    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool SameTable(string a, string b) => Same(GXMartIdentifiers.Canonical(a), GXMartIdentifiers.Canonical(b));
    private static void Name(string value)
    {
        if (string.IsNullOrEmpty(value) || !Regex.IsMatch(value, @"\A[A-Za-z_][A-Za-z0-9_]*\z"))
            throw new ArgumentException("Use letters, digits and underscores for table and output column names.");
    }
    private string Q(string value)
    {
        char open = databaseType == DatabaseType.MSSQL ? '[' : databaseType is DatabaseType.MySQL or DatabaseType.MariaDB ? '`' : '"';
        char close = open == '[' ? ']' : open;
        return open + value.Replace(close.ToString(), new string(close, 2), StringComparison.Ordinal) + close;
    }
    private string Table(GXTableSchema schema) => string.IsNullOrEmpty(schema.Schema) ? Q(schema.Name) : Q(schema.Schema) + "." + Q(schema.Name);
    private GXColumnSchema Physical(GXDataVaultTableMapping mapping, string name) => Schema(mapping).Columns.FirstOrDefault(c => Same(c.Name, name))
        ?? throw new ArgumentException($"GXDataVaultColumnMapping '{name}' is missing from '{Target(mapping).Name}'.");

    private List<GXDataVaultTableMapping> Hubs(string source)
    {
        var selected = mappings.Where(m => SameTable(Target(m).Name, source) && m.ObjectType is DataVaultObjectType.Staging or DataVaultObjectType.Hub or DataVaultObjectType.Satellite).ToList();
        if (selected.Count != 1) throw new ArgumentException("Select a table with exactly one Staging, Hub or Satellite mapping in this database.");
        if (selected[0].ObjectType == DataVaultObjectType.Hub) return selected;
        var hubs = mappings.Where(m => m.ObjectType == DataVaultObjectType.Hub &&
            (SameTable(Source(m).Name, Source(selected[0]).Name) ||
             selected[0].ObjectType == DataVaultObjectType.Staging && SameTable(Source(m).Name, Target(selected[0]).Name))).ToList();
        if (selected[0].ObjectType == DataVaultObjectType.Satellite)
        {
            var parents = hubs.Where(hub => Edges(hub, selected[0]).Any()).ToList();
            // A Hub-owned satellite uses its parent; Link-owned satellites can offer multiple roots.
            return parents.Count != 0 ? parents : hubs.Where(hub =>
                ResolveNodes(hub, []).Any(node => node.GXDataVaultTableMapping.Id == selected[0].Id)).ToList();
        }
        return hubs;
    }

    public GXMartPreview Preview(string sourceTable, Guid? hubMappingId)
    {
        var hubs = Hubs(sourceTable);
        var preview = new GXMartPreview { Hubs = hubs.Select(h => new GXMartHubOption { MappingId = h.Id, Table = Target(h).Name }).ToList() };
        if (hubs.Count == 0) { preview.Messages.Add("No Hub mapping is associated with this source."); return preview; }
        var hub = hubMappingId is null || hubMappingId == Guid.Empty
            ? hubs.Count == 1 ? hubs[0] : null
            : hubs.FirstOrDefault(h => h.Id == hubMappingId) ?? throw new ArgumentException("The selected Hub does not belong to this source.");
        if (hub == null) { preview.Messages.Add("Select the Hub that defines one row in the mart."); return preview; }
        preview.HubMappingId = hub.Id;
        var nodes = ResolveNodes(hub, preview.Messages);
        var key = HubKey(hub);
        foreach (var node in nodes)
        {
            foreach (var column in node.GXDataVaultTableMapping.Columns)
            {
                DataVaultObjectType objectType = node.GXDataVaultTableMapping.ObjectType
                    ?? throw new ArgumentException($"Mapping '{node.GXDataVaultTableMapping.Id}' requires an object type.");
                Physical(node.GXDataVaultTableMapping, column.TargetColumn);
                preview.Columns.Add(new()
                {
                    MappingId = node.GXDataVaultTableMapping.Id,
                    ObjectType = objectType,
                    Table = Target(node.GXDataVaultTableMapping).Name,
                    Column = column.TargetColumn,
                    TargetColumn = node.GXDataVaultTableMapping == hub ? column.TargetColumn : Regex.Replace(Schema(node.GXDataVaultTableMapping).Name + "_" + column.TargetColumn, "[^A-Za-z0-9_]", "_"),
                    IsKey = node.GXDataVaultTableMapping == hub && Same(column.TargetColumn, key.TargetColumn),
                    RequiresAggregation = node.Path.Any(e => e.Child.ObjectType == DataVaultObjectType.Link),
                    JoinDescription = string.Join("; ", node.Path.Select(e => $"{Target(e.Parent).Name}.{e.ParentKey.TargetColumn} = {Target(e.Child).Name}.{e.ChildKey.TargetColumn}"))
                });
            }
        }
        return preview;
    }

    private GXDataVaultColumnMapping HubKey(GXDataVaultTableMapping hub)
    {
        var keys = hub.Columns.Where(c => c.Role == DataVaultColumnRole.HashKey).ToArray();
        if (keys.Length != 1 || Schema(hub).Columns.Count(c => c.IsPrimaryKey) != 1 || !Physical(hub, keys[0].TargetColumn).IsPrimaryKey)
            throw new ArgumentException($"Hub '{Target(hub).Name}' must have one mapped hash primary key.");
        return keys[0];
    }

    private List<Node> ResolveNodes(GXDataVaultTableMapping hub, List<string> messages)
    {
        HubKey(hub);
        var remaining = mappings.Where(m => Same(Source(m).Name, Source(hub).Name) &&
            m.ObjectType is DataVaultObjectType.Hub or DataVaultObjectType.Satellite or DataVaultObjectType.Link or DataVaultObjectType.Reference && m.Id != hub.Id).ToList();
        if (remaining.Append(hub).GroupBy(m => Target(m).Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() != 1))
            throw new ArgumentException("Each raw vault table must have one unambiguous mapping for this source.");
        var all = remaining.Prepend(hub).ToList();
        var edges = all.ToDictionary(m => m.Id, parent => all.Where(child => child.Id != parent.Id)
            .SelectMany(child => Edges(parent, child)).ToList());
        List<Edge>? FindPath(GXDataVaultTableMapping target, Edge? blocked)
        {
            var queue = new Queue<Node>();
            queue.Enqueue(new(hub, []));
            var visited = new HashSet<Guid> { hub.Id };
            while (queue.TryDequeue(out var node))
            {
                foreach (var edge in edges[node.GXDataVaultTableMapping.Id])
                {
                    if (edge == blocked || !visited.Add(edge.Child.Id)) continue;
                    List<Edge> path = [.. node.Path, edge];
                    if (edge.Child.Id == target.Id) return path;
                    queue.Enqueue(new(edge.Child, path));
                }
            }
            return null;
        }
        var nodes = new List<Node> { new(hub, []) };
        foreach (var candidate in remaining)
        {
            var path = FindPath(candidate, null);
            if (path == null)
                messages.Add($"{Target(candidate).Name} has no mapped join path to {Target(hub).Name}.");
            // Any competing simple path must omit at least one edge of this path.
            else if (path.Any(edge => FindPath(candidate, edge) != null))
                messages.Add($"{Target(candidate).Name} has ambiguous join keys or paths and is not available.");
            else nodes.Add(new(candidate, path));
        }
        return nodes;
    }

    private IEnumerable<Edge> Edges(GXDataVaultTableMapping parent, GXDataVaultTableMapping child)
    {
        foreach (var p in parent.Columns)
            foreach (var c in child.Columns)
            {
                bool sameSource = Same(p.SourceColumn, c.SourceColumn);
                bool ownsParentKey = OwnsParentKey(parent, p, child, c);
                bool hash = parent.ObjectType == DataVaultObjectType.Hub && p.Role == DataVaultColumnRole.HashKey && ownsParentKey;
                bool linkHub = parent.ObjectType == DataVaultObjectType.Link && p.Role == DataVaultColumnRole.ParentHashKey &&
                    child.ObjectType == DataVaultObjectType.Hub && c.Role == DataVaultColumnRole.HashKey &&
                    sameSource;
                bool linkSatellite = parent.ObjectType == DataVaultObjectType.Link && p.Role == DataVaultColumnRole.LinkHashKey &&
                    child.ObjectType == DataVaultObjectType.Satellite && ownsParentKey;
                bool reference = child.ObjectType == DataVaultObjectType.Reference && c.Role == DataVaultColumnRole.BusinessKey &&
                    parent.ObjectType != DataVaultObjectType.Reference && p.Role is DataVaultColumnRole.BusinessKey or DataVaultColumnRole.Attribute && sameSource;
                if (hash || linkHub || linkSatellite || reference) yield return new(parent, child, p, c);
            }
    }

    private bool OwnsParentKey(GXDataVaultTableMapping parent, GXDataVaultColumnMapping key, GXDataVaultTableMapping child, GXDataVaultColumnMapping reference)
    {
        if (child.ObjectType is not (DataVaultObjectType.Satellite or DataVaultObjectType.Link) || reference.Role != DataVaultColumnRole.ParentHashKey)
            return false;
        if (child.ObjectType == DataVaultObjectType.Link) return Same(key.SourceColumn, reference.SourceColumn);
        // A Link hash may carry its first parent's source lineage; the hash values are different.
        // Prefer the physical parent key identity for legacy satellites, symmetrically for Hub/Link.
        var namedOwners = mappings.Where(m => Same(Source(m).Name, Source(child).Name) && m.ObjectType is DataVaultObjectType.Hub or DataVaultObjectType.Link)
            .Where(m => m.Columns.Any(c => c.Role == (m.ObjectType == DataVaultObjectType.Hub ? DataVaultColumnRole.HashKey : DataVaultColumnRole.LinkHashKey) &&
                (Same(c.TargetColumn, reference.TargetColumn) || Same(c.TargetColumn, reference.SourceColumn)))).ToList();
        return namedOwners.Count != 0 ? namedOwners.Any(m => m.Id == parent.Id) : Same(key.SourceColumn, reference.SourceColumn);
    }

    public GXInformationMartPlan Build(GXCreateMartRequest request)
    {
        if (!request.UseExistingTable) Name(request.TargetTable);
        if (request.Definition == null || request.Definition.Columns == null || request.Definition.Columns.Count == 0)
            throw new ArgumentException("Select mart columns, including the Hub hash key.");
        var preview = Preview(request.SourceTable, request.Definition.HubMappingId);
        if (preview.HubMappingId == Guid.Empty) throw new ArgumentException("Select a Hub.");
        var hub = mappings.Single(m => m.Id == preview.HubMappingId);
        if (request.Schedule == null || request.Schedule.Type is not (ScheduleType.Manual or ScheduleType.Interval) ||
            request.Schedule.Type == ScheduleType.Interval && request.Schedule.IntervalSeconds is not > 0)
            throw new ArgumentException("Choose Manual or a positive Interval schedule.");
        var nodes = ResolveNodes(hub, []);
        var key = HubKey(hub);
        var actualTarget = request.UseExistingTable ? GXMartIdentifiers.Normalize(describe(request.TargetTable)) : null;
        var target = new GXTableSchema
        {
            Name = actualTarget?.Name ?? request.TargetTable,
            Schema = request.UseExistingTable ? actualTarget!.Schema : Schema(hub).Schema
        };
        var mapping = new GXDataVaultTableMapping
        {
            SourceTable = hub.TargetTable,
            TargetTable = new() { Id = Guid.Empty, Name = target.ToString() },
            ObjectType = DataVaultObjectType.InformationMart,
            Schedule = new() { Type = request.Schedule.Type, IntervalSeconds = request.Schedule.IntervalSeconds }
        };
        List<string> expressions = [], checks = [];
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        bool hasKey = false;
        foreach (var selection in request.Definition.Columns)
        {
            if (!request.UseExistingTable) Name(selection.TargetColumn);
            else if (actualTarget!.Columns.All(c => !Same(c.Name, selection.TargetColumn)))
                throw new ArgumentException($"Mart column '{selection.TargetColumn}' is missing from the target table.");
            if (!names.Add(selection.TargetColumn)) throw new ArgumentException("Mart output column names must be distinct.");
            var option = preview.Columns.SingleOrDefault(c => c.MappingId == selection.MappingId && Same(c.Column, selection.Column))
                ?? throw new ArgumentException($"GXDataVaultColumnMapping '{selection.Column}' has no unambiguous mapped path from the selected Hub.");
            if (selection.Aggregation is not (Aggregation.None or Aggregation.Min or Aggregation.Max or Aggregation.Count or Aggregation.CountDistinct or Aggregation.Average) || option.RequiresAggregation && selection.Aggregation == Aggregation.None)
                throw new ArgumentException("Choose Min, Max, Count, CountDistinct or Average for columns reached through a Link.");
            if (option.IsKey && selection.Aggregation != Aggregation.None) throw new ArgumentException("The Hub key cannot be aggregated.");
            hasKey |= option.IsKey;
            mapping.Columns.Add(new()
            {
                SourceMappingId = selection.MappingId,
                SourceColumn = selection.Column,
                TargetColumn = selection.TargetColumn,
                Aggregation = selection.Aggregation,
                Role = option.IsKey ? DataVaultColumnRole.HashKey : DataVaultColumnRole.Attribute
            });
            var node = nodes.Single(n => n.GXDataVaultTableMapping.Id == selection.MappingId);
            var source = Physical(node.GXDataVaultTableMapping, selection.Column);
            bool count = selection.Aggregation is Aggregation.Count or Aggregation.CountDistinct;
            bool average = selection.Aggregation == Aggregation.Average;
            if (average && Type.GetTypeCode(Nullable.GetUnderlyingType(source.Type!) ?? source.Type) is not
                (TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal))
                throw new ArgumentException("Average requires a numeric source column.");
            Type sourceType = source.Type ?? throw new ArgumentException($"Source column '{source.Name}' requires a CLR type.");
            target.Columns.Add(new()
            {
                Name = selection.TargetColumn,
                Type = count ? typeof(long) : average ? typeof(double) : sourceType,
                DbType = count || average ? null : source.DbType,
                MaxLength = count || average ? 0 : source.MaxLength,
                Precision = count || average ? null : source.Precision,
                Scale = count || average ? null : source.Scale,
                DateTimePrecision = count || average ? null : source.DateTimePrecision,
                IsPrimaryKey = option.IsKey,
                IsNullable = !option.IsKey && !count
            });
            expressions.Add(Expression(node, selection, hub, checks) + " AS " + Q(selection.TargetColumn));
        }
        if (!hasKey) throw new ArgumentException("Include the Hub hash key to preserve one row per Hub key.");
        return new(target, mapping, "SELECT " + string.Join(", ", expressions) + " FROM " + Table(Schema(hub)) + " r0", Table(target),
            string.Join(", ", target.Columns.Select(c => Q(c.Name))), checks);
    }

    public static bool HasColumnMappings(GXDataVaultTableMapping mapping) =>
        mapping.ObjectType == DataVaultObjectType.InformationMart && mapping.Columns.Any(c => c.SourceMappingId.HasValue);

    public GXInformationMartPlan Build(GXDataVaultTableMapping mapping)
    {
        var keys = mapping.Columns.Where(c => c.Role == DataVaultColumnRole.HashKey).ToList();
        if (!HasColumnMappings(mapping) || keys.Count != 1 || mapping.Columns.Any(c => c.SourceMappingId is null || c.SourceMappingId == Guid.Empty))
            throw new ArgumentException("The Mart requires source mappings for every column and one root Hub key.");
        var hub = mappings.SingleOrDefault(m => m.Id == keys[0].SourceMappingId && m.ObjectType == DataVaultObjectType.Hub)
            ?? throw new ArgumentException("The Mart's root Hub mapping no longer exists.");
        var existing = GXMartIdentifiers.Normalize(describe(Target(mapping).Name));
        var plan = Build(new GXCreateMartRequest
        {
            SourceTable = Target(hub).Name,
            TargetTable = existing.ToString(),
            UseExistingTable = true,
            Definition = new()
            {
                HubMappingId = hub.Id,
                Columns = mapping.Columns.Select(c => new GXMartColumnSelection
                {
                    MappingId = c.SourceMappingId!.Value,
                    Column = c.SourceColumn,
                    TargetColumn = c.TargetColumn,
                    Aggregation = c.Aggregation
                }).ToList()
            },
            Schedule = new() { Type = mapping.Schedule.Type, IntervalSeconds = mapping.Schedule.IntervalSeconds }
        });
        return ResolveTarget(plan) with { Mapping = mapping };
    }

    public GXInformationMartPlan ValidateExistingTarget(GXInformationMartPlan plan)
    {
        var existing = GXMartIdentifiers.Normalize(describe(plan.TargetSchema.ToString()));
        foreach (var expected in plan.TargetSchema.Columns)
        {
            var column = existing.Columns.SingleOrDefault(c => Same(c.Name, expected.Name))
                ?? throw new ArgumentException($"Mart column '{expected.Name}' is missing from '{existing}'.");
            if (column.IsGenerated || column.IsComputed || column.IsIdentity || column.IsAutoIncrement)
                throw new ArgumentException($"Mart column '{column.Name}' must be writable.");
            if (column.Type != expected.Type || expected.IsNullable && !column.IsNullable ||
                column.MaxLength is > 0 && (expected.MaxLength is not > 0 || column.MaxLength < expected.MaxLength) ||
                column.Precision is > 0 && expected.Precision is > 0 &&
                    (column.Precision - (column.Scale ?? 0) < expected.Precision - (expected.Scale ?? 0) ||
                        (column.Scale ?? 0) < (expected.Scale ?? 0)))
                throw new ArgumentException($"Mart column '{column.Name}' has an incompatible type, length or nullability.");
        }
        foreach (var column in existing.Columns.Where(c => plan.TargetSchema.Columns.All(e => !Same(e.Name, c.Name))))
            if (!column.IsNullable && column.DefaultValue == null && !column.IsGenerated && !column.IsComputed && !column.IsIdentity && !column.IsAutoIncrement)
                throw new ArgumentException($"Required target column '{column.Name}' is not mapped and has no default.");
        return ResolveTarget(plan);
    }

    public GXInformationMartPlan ResolveTarget(GXInformationMartPlan plan)
    {
        var existing = GXMartIdentifiers.Normalize(describe(plan.TargetSchema.ToString()));
        foreach (var column in plan.TargetSchema.Columns)
            if (!existing.Columns.Any(c => Same(c.Name, column.Name)))
                throw new ArgumentException($"Mart column '{column.Name}' is missing from '{existing}'.");
        Target(plan.Mapping).Name = existing.ToString();
        return plan with
        {
            TargetSchema = existing,
            TargetSql = Table(existing),
            TargetColumnsSql = string.Join(", ", plan.TargetSchema.Columns.Select(column =>
                Q(existing.Columns.Single(c => Same(c.Name, column.Name)).Name)))
        };
    }
    private string Expression(Node node, GXMartColumnSelection column, GXDataVaultTableMapping hub, List<string> checks)
    {
        if (node.Path.Count == 0)
        {
            if (column.Aggregation != Aggregation.None) throw new ArgumentException("Hub columns do not require aggregation.");
            return "r0." + Q(Physical(hub, column.Column).Name);
        }
        List<string> conditions = [];
        string from = "";
        for (int i = 0; i < node.Path.Count; i++)
        {
            var edge = node.Path[i];
            string parent = i == 0 ? "r0" : "t" + i;
            string alias = "t" + (i + 1);
            string join = parent + "." + Q(Physical(edge.Parent, edge.ParentKey.TargetColumn).Name) + " = " + alias + "." + Q(Physical(edge.Child, edge.ChildKey.TargetColumn).Name);
            if (i == 0) { from = Table(Schema(edge.Child)) + " " + alias; conditions.Add(join); }
            else from += " INNER JOIN " + Table(Schema(edge.Child)) + " " + alias + " ON " + join;
            if (edge.Child.ObjectType == DataVaultObjectType.Satellite)
            {
                var load = edge.Child.Columns.Where(c => c.Role == DataVaultColumnRole.LoadDate).ToArray();
                var parents = edge.Child.Columns.Where(c => c.Role == DataVaultColumnRole.ParentHashKey).ToArray();
                if (load.Length != 1 || parents.Length == 0) throw new ArgumentException($"Satellite '{Target(edge.Child).Name}' requires parent keys and one LoadDate mapping.");
                string date = Q(Physical(edge.Child, load[0].TargetColumn).Name);
                var keys = parents.Select(p => Q(Physical(edge.Child, p.TargetColumn).Name)).ToArray();
                conditions.Add($"{alias}.{date} = (SELECT MAX(latest.{date}) FROM {Table(Schema(edge.Child))} latest WHERE " +
                    string.Join(" AND ", keys.Select(k => $"latest.{k} = {alias}.{k}")) + ")");
            }
        }
        string expression = "t" + node.Path.Count + "." + Q(Physical(node.GXDataVaultTableMapping, column.Column).Name);
        string suffix = " FROM " + from + " WHERE " + string.Join(" AND ", conditions);
        if (column.Aggregation == Aggregation.None)
            checks.Add("SELECT COUNT(*) FROM " + Table(Schema(hub)) + " r0 WHERE (SELECT COUNT(*)" + suffix + ") > 1");
        else expression = column.Aggregation switch
        {
            Aggregation.Count => "COUNT(" + expression + ")",
            Aggregation.CountDistinct => "COUNT(DISTINCT " + expression + ")",
            Aggregation.Average => "AVG(CAST(" + expression + " AS FLOAT))",
            Aggregation.Min => "MIN(" + expression + ")",
            Aggregation.Max => "MAX(" + expression + ")",
            _ => throw new ArgumentException("Unsupported aggregation.")
        };
        return "(SELECT " + expression + suffix + ")";
    }

    public static async Task<int> PopulateAsync(DbConnection connection, DbTransaction transaction, GXInformationMartPlan plan, CancellationToken cancellationToken,
        bool replace = false)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        foreach (var sql in plan.CardinalityChecks)
        {
            command.CommandText = sql;
            if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 0)
                throw new InvalidOperationException("A mart column has multiple current rows per Hub key. Correct the mapping or choose an aggregation.");
        }
        if (replace)
        {
            command.CommandText = "DELETE FROM " + plan.TargetSql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        command.CommandText = "INSERT INTO " + plan.TargetSql + " (" + plan.TargetColumnsSql + ") " + plan.SelectSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
        command.CommandText = "SELECT COUNT(*) FROM " + plan.TargetSql;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }
}


