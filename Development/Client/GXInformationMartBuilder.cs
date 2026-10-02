//
// --------------------------------------------------------------------------
//  Gurux Ltd
// 
//
//
// Filename:        $HeadURL$
//
// Version:         $Revision$,
//                  $Date$
//                  $Author$
//
// Copyright (c) Gurux Ltd
//
//---------------------------------------------------------------------------
//
//  DESCRIPTION
//
// This file is a part of Gurux Device Framework.
//
// Gurux Device Framework is Open Source software; you can redistribute it
// and/or modify it under the terms of the GNU General Public License 
// as published by the Free Software Foundation; version 2 of the License.
// Gurux Device Framework is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of 
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. 
// See the GNU General Public License for more details.
//
// This code is licensed under the GNU General Public License v2. 
// Full text may be retrieved at http://www.gnu.org/licenses/gpl-2.0.txt
//---------------------------------------------------------------------------

using Gurux.Data.Relay.Database;
using E = System.Linq.Expressions.Expression;
using Gurux.Service.Orm;
using Gurux.Service.Orm.Common.Model;
using System.Data.Common;
using System.Text.RegularExpressions;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Shared;
using Gurux.Service.Orm.Common.Enums;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.DB;
namespace Gurux.Data.Relay.Client;

public sealed record GXInformationMartPlan(
    GXTableSchema TargetSchema, GXDataVaultTableMapping Mapping, GXSelectArgs Query,
    GXTableSchema Target, IReadOnlyList<string> TargetColumns, IReadOnlyList<GXSelectArgs> Checks, DatabaseType Provider)
{
    public string SelectSql
    {
        get
        {
            Query.UseQueryCache(new GXQueryCache(Provider));
            return Query.ToString(false);
        }
    }
}

/// <summary>Plans a current-state mart from mapped keys, without accepting SQL expressions.</summary>
public sealed class GXInformationMartBuilder(
    IReadOnlyList<GXDataVaultTableMapping> mappings, Func<string, GXTableSchema> describe, DatabaseType databaseType,
    IReadOnlyList<GXMartReferenceJoin>? referenceJoins = null)
{
    private sealed record Edge(GXDataVaultTableMapping Parent, GXDataVaultTableMapping Child, GXDataVaultColumnMapping ParentKey, GXDataVaultColumnMapping ChildKey);
    private sealed record Node(GXDataVaultTableMapping GXDataVaultTableMapping, List<Edge> Path);
    private readonly Dictionary<string, GXTableSchema> _schemas = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<GXMartReferenceJoin> _referenceJoins = referenceJoins ?? [];
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
    private static GXSelectArgs Table(GXTableSchema schema, string alias)
    {
        var source = GXSelectArgs.Select(schema.Columns);
        return GXSelectArgs.From(() => GXSql.As(GXSql.Subquery<object>(source), alias));
    }
    private GXInformationMartPlan Plan(GXTableSchema target, GXDataVaultTableMapping mapping, GXSelectArgs query,
        IReadOnlyList<GXSelectArgs> checks, IReadOnlyList<string>? columns = null) =>
        new(target, mapping, query, target, columns ?? target.Columns.Select(c => c.Name).ToArray(), checks, databaseType);
    private GXColumnSchema Physical(GXDataVaultTableMapping mapping, string name) => Schema(mapping).Columns.FirstOrDefault(c => Same(c.Name, name))
        ?? throw new ArgumentException($"GXDataVaultColumnMapping '{name}' is missing from '{Target(mapping).Name}'.");

    private List<GXDataVaultTableMapping> Hubs(string source)
    {
        var selected = mappings.Where(m => SameTable(Target(m).Name, source) && m.ObjectType is DataVaultObjectType.Staging or DataVaultObjectType.Hub or DataVaultObjectType.Link or DataVaultObjectType.Satellite).ToList();
        if (selected.Count != 1) throw new ArgumentException("Select a table with exactly one Staging, Hub, Link or Satellite mapping in this database.");
        if (selected[0].ObjectType is DataVaultObjectType.Hub or DataVaultObjectType.Link) return selected;
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
        var role = hub.ObjectType == DataVaultObjectType.Link ? DataVaultColumnRole.LinkHashKey : DataVaultColumnRole.HashKey;
        var keys = hub.Columns.Where(c => c.Role == role).ToArray();
        if (keys.Length != 1 || Schema(hub).Columns.Count(c => c.IsPrimaryKey) != 1 || !Physical(hub, keys[0].TargetColumn).IsPrimaryKey)
            throw new ArgumentException($"{hub.ObjectType} '{Target(hub).Name}' must have one mapped primary key.");
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
                bool explicitReference = child.ObjectType == DataVaultObjectType.Reference && parent.ObjectType != DataVaultObjectType.Reference &&
                    _referenceJoins.Any(j => j.FromMappingId == parent.Id && j.ReferenceMappingId == child.Id &&
                        Same(j.FromColumn, p.TargetColumn) && Same(j.ReferenceColumn, c.TargetColumn));
                if (hash || linkHub || linkSatellite || reference || explicitReference) yield return new(parent, child, p, c);
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
        if (request.Definition?.Grain == GXMartGrain.Reference)
            return BuildReference(request);
        if (request.Definition?.Grain == GXMartGrain.Link)
            return BuildLinkGrain(request);
        if (request.Definition?.Grain is GXMartGrain.Custom)
            throw new ArgumentException("The selected Mart grain is not supported by this builder yet. Use Hub grain.");
        if (!request.UseExistingTable) Name(request.TargetTable);
        if (request.Definition == null || request.Definition.Columns == null || request.Definition.Columns.Count == 0)
            throw new ArgumentException("Select mart columns, including the Hub hash key.");
        var preview = Preview(request.SourceTable, request.Definition.HubMappingId);
        if (preview.HubMappingId == Guid.Empty) throw new ArgumentException("Select a Hub.");
        ValidateStageCoverage(request, preview);
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
        List<(System.Linq.Expressions.Expression Expression, string? Alias)> expressions = [];
        List<GXSelectArgs> checks = [];
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        bool hasKey = false;
        foreach (var selection in request.Definition.Columns)
        {
            var option = preview.Columns.SingleOrDefault(c => c.MappingId == selection.MappingId && Same(c.Column, selection.Column))
                ?? throw new ArgumentException($"GXDataVaultColumnMapping '{selection.Column}' has no unambiguous mapped path from the selected Hub.");
            if (!option.IsKey || selection.IncludeInOutput)
            {
                if (!request.UseExistingTable) Name(selection.TargetColumn);
                else if (actualTarget!.Columns.All(c => !Same(c.Name, selection.TargetColumn)))
                    throw new ArgumentException($"Mart column '{selection.TargetColumn}' is missing from the target table.");
                if (!names.Add(selection.TargetColumn)) throw new ArgumentException("Mart output column names must be distinct.");
            }
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
            if (option.IsKey && !selection.IncludeInOutput)
                continue;
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
            expressions.Add((Expression(node, selection, hub, checks), selection.TargetColumn));
        }
        if (!hasKey) throw new ArgumentException("Include the Hub hash key to preserve one row per Hub key.");
        var query = Table(Schema(hub), "r0");
        query.Columns.AddRange(expressions);
        return Plan(target, mapping, query, checks);
    }

    private void ValidateStageCoverage(GXCreateMartRequest request, GXMartPreview preview)
    {
        var stage = mappings.FirstOrDefault(m => m.ObjectType == DataVaultObjectType.Staging &&
            SameTable(Target(m).Name, request.SourceTable));
        if (stage is null) return;

        static bool Technical(GXDataVaultColumnMapping c) => c.Role is
            DataVaultColumnRole.LoadDate or DataVaultColumnRole.RecordSource or
            DataVaultColumnRole.HashDiff or DataVaultColumnRole.HashKey or
            DataVaultColumnRole.LinkHashKey or DataVaultColumnRole.ParentHashKey;

        var required = stage.Columns
            .Where(c => !Technical(c) && !string.IsNullOrWhiteSpace(c.SourceColumn))
            .Select(c => c.SourceColumn)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = request.Definition!.Columns
            .Select(s => mappings.FirstOrDefault(m => m.Id == s.MappingId)?.Columns
                .FirstOrDefault(c => Same(c.TargetColumn, s.Column) || Same(c.SourceColumn, s.Column))?.SourceColumn)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = required.Where(c => !selected.Contains(c)).OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray();
        var extra = selected.Where(c => !required.Contains(c)).OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length != 0 || extra.Length != 0)
        {
            var details = new List<string>();
            if (missing.Length != 0) details.Add("missing: " + string.Join(", ", missing));
            if (extra.Length != 0) details.Add("not in Stage: " + string.Join(", ", extra));
            throw new ArgumentException("Information Mart columns must exactly match the nontechnical Stage columns through the Data Vault model (" + string.Join("; ", details) + ").");
        }
    }

    private GXInformationMartPlan BuildReference(GXCreateMartRequest request)
    {
        var reference = mappings.SingleOrDefault(m => m.ObjectType == DataVaultObjectType.Reference &&
            Same(Target(m).Name, request.Definition.SourceReference));
        if (reference is null) throw new ArgumentException("Select an existing Reference mapping as the Mart source.");
        var schema = Schema(reference);
        var key = reference.Columns.SingleOrDefault(c => c.Role == DataVaultColumnRole.BusinessKey);
        if (key is null) throw new ArgumentException("The Reference mapping must have a Business Key.");
        var selected = request.Definition.Columns;
        if (selected.Count == 0) throw new ArgumentException("Select at least one Reference column.");
        var target = new GXTableSchema { Name = request.TargetTable, Schema = schema.Schema };
        var mapping = new GXDataVaultTableMapping { SourceTable = reference.TargetTable, TargetTable = new() { Name = request.TargetTable }, ObjectType = DataVaultObjectType.InformationMart, Schedule = request.Schedule };
        var selectedColumns = new List<GXColumnSchema>();
        foreach (var selection in selected)
        {
            var column = reference.Columns.SingleOrDefault(c => Same(c.TargetColumn, selection.Column));
            var physical = schema.Columns.SingleOrDefault(c => Same(c.Name, selection.Column));
            if (column is null || physical is null) throw new ArgumentException($"Reference column '{selection.Column}' was not found.");
            mapping.Columns.Add(new() { SourceMappingId = reference.Id, SourceColumn = selection.Column, TargetColumn = selection.TargetColumn, Role = column.Role == DataVaultColumnRole.BusinessKey ? DataVaultColumnRole.HashKey : DataVaultColumnRole.Attribute });
            target.Columns.Add(new() { Name = selection.TargetColumn, Type = physical.Type, DbType = physical.DbType, MaxLength = physical.MaxLength, IsPrimaryKey = column.Role == DataVaultColumnRole.BusinessKey, IsNullable = column.Role != DataVaultColumnRole.BusinessKey });
            selectedColumns.Add(physical);
        }
        if (!mapping.Columns.Any(c => c.Role == DataVaultColumnRole.HashKey)) throw new ArgumentException("Include the Reference Business Key.");
        var query = GXSelectArgs.Select(selectedColumns);
        query.Distinct = true;
        return Plan(target, mapping, query, []);
    }

    private GXInformationMartPlan BuildLinkGrain(GXCreateMartRequest request)
    {
        var link = mappings.SingleOrDefault(m => m.ObjectType == DataVaultObjectType.Link && Same(Target(m).Name, request.Definition.SourceLink));
        if (link is null) throw new ArgumentException("Select an existing Link mapping as the Mart source.");
        var schema = Schema(link);
        var key = link.Columns.SingleOrDefault(c => c.Role == DataVaultColumnRole.LinkHashKey);
        if (key is null) throw new ArgumentException("The Link mapping must have a Link Hash Key.");
        if (request.Definition.Columns.Count == 0) throw new ArgumentException("Select at least one Link column.");
        var target = new GXTableSchema { Name = request.TargetTable, Schema = schema.Schema };
        var mapping = new GXDataVaultTableMapping { SourceTable = link.TargetTable, TargetTable = new() { Name = request.TargetTable }, ObjectType = DataVaultObjectType.InformationMart, Schedule = request.Schedule };
        var selectedColumns = new List<GXColumnSchema>();
        foreach (var selection in request.Definition.Columns)
        {
            var column = link.Columns.SingleOrDefault(c => Same(c.TargetColumn, selection.Column));
            var physical = schema.Columns.SingleOrDefault(c => Same(c.Name, selection.Column));
            if (column is null || physical is null) throw new ArgumentException($"Link column '{selection.Column}' was not found.");
            mapping.Columns.Add(new() { SourceMappingId = link.Id, SourceColumn = selection.Column, TargetColumn = selection.TargetColumn, Role = column.Role == DataVaultColumnRole.LinkHashKey ? DataVaultColumnRole.HashKey : DataVaultColumnRole.Attribute });
            target.Columns.Add(new() { Name = selection.TargetColumn, Type = physical.Type, DbType = physical.DbType, MaxLength = physical.MaxLength, IsPrimaryKey = column.Role == DataVaultColumnRole.LinkHashKey, IsNullable = column.Role != DataVaultColumnRole.LinkHashKey });
            selectedColumns.Add(physical);
        }
        if (!mapping.Columns.Any(c => c.Role == DataVaultColumnRole.HashKey)) throw new ArgumentException("Include the Link Hash Key.");
        var query = GXSelectArgs.Select(selectedColumns);
        query.Distinct = true;
        return Plan(target, mapping, query, []);
    }

    public static bool HasColumnMappings(GXDataVaultTableMapping mapping) =>
        mapping.ObjectType == DataVaultObjectType.InformationMart && mapping.Columns.Any(c => c.SourceMappingId.HasValue);

    public GXInformationMartPlan Build(GXDataVaultTableMapping mapping)
    {
        var sourceIds = mapping.Columns.Where(c => c.SourceMappingId.HasValue && c.SourceMappingId != Guid.Empty)
            .Select(c => c.SourceMappingId!.Value).Distinct().ToArray();
        // Legacy Stage-direct mappings remain readable for refresh/migration. New Mart
        // definitions are validated by Build(GXCreateMartRequest) and must use the
        // Data Vault graph, so no new direct Stage-to-Mart path can be created.
        if (mapping.ObjectType == DataVaultObjectType.InformationMart &&
            mapping.Columns.Count != 0 && mapping.Columns.All(c => c.SourceMappingId.HasValue && c.SourceMappingId != Guid.Empty) &&
            sourceIds.Length == 1 && sourceIds[0] is Guid sourceId &&
            mappings.SingleOrDefault(m => m.Id == sourceId && m.ObjectType == DataVaultObjectType.Staging) is { } stage)
        {
            return BuildStageDirect(mapping, stage);
        }
        if (mapping.ObjectType == DataVaultObjectType.InformationMart &&
            mapping.Columns.Count != 0 && mapping.Columns.All(c => c.SourceMappingId.HasValue && c.SourceMappingId != Guid.Empty) &&
            sourceIds.Length == 1 && sourceIds[0] is Guid rawSourceId &&
            mappings.SingleOrDefault(m => m.Id == rawSourceId && m.ObjectType == DataVaultObjectType.Link) is { } link &&
            mapping.Columns.Any(c => c.Role == DataVaultColumnRole.LinkHashKey))
        {
            return BuildLinkDirect(mapping, link);
        }
        var keys = mapping.Columns.Where(c => c.Role is DataVaultColumnRole.HashKey or DataVaultColumnRole.LinkHashKey).ToList();
        if (!HasColumnMappings(mapping) || keys.Count != 1 || mapping.Columns.Any(c => c.SourceMappingId is null || c.SourceMappingId == Guid.Empty))
            throw new ArgumentException("The Mart requires source mappings for every column and one root Hub key.");
        var hub = mappings.SingleOrDefault(m => m.Id == keys[0].SourceMappingId && m.ObjectType is (DataVaultObjectType.Hub or DataVaultObjectType.Link))
            ?? throw new ArgumentException("The Mart's root Hub or Link mapping no longer exists.");
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

    private GXInformationMartPlan BuildStageDirect(GXDataVaultTableMapping mapping, GXDataVaultTableMapping stage)
    {
        var keys = mapping.Columns.Where(c => c.Role == DataVaultColumnRole.BusinessKey).ToList();
        if (keys.Count == 0) throw new ArgumentException("A Stage-direct Mart requires one or more BusinessKey columns.");
        GXDataVaultColumnMapping loadDate = stage.Columns.SingleOrDefault(c => c.Role == DataVaultColumnRole.LoadDate)
            ?? throw new ArgumentException($"Stage '{Target(stage).Name}' requires a LoadDate mapping.");
        GXTableSchema source = Schema(stage);
        GXTableSchema target = GXMartIdentifiers.Normalize(describe(Target(mapping).Name));
        var outputs = mapping.Columns.Select(c => new
        {
            Source = Physical(stage, stage.Columns.Single(sc => Same(sc.SourceColumn, c.SourceColumn)).TargetColumn).Name,
            Target = Physical(mapping, c.TargetColumn).Name
        }).ToList();
        string[] keysNames = keys.Select(c => outputs.Single(o => Same(o.Target, c.TargetColumn)).Source).ToArray();
        string date = Physical(stage, loadDate.TargetColumn).Name;
        var ranked = Table(source, "s");
        ranked.Columns.AddRange(outputs.Select(o => (GXMetadataQueries.Column(source.Columns.Single(c => c.Name == o.Source), "s"), o.Target)));
        var partitionColumns = keysNames.Select(k => (object)source.Columns.Single(c => c.Name == k)).ToArray();
        var dateColumn = source.Columns.Single(c => c.Name == date);
        System.Linq.Expressions.Expression<Func<bool>> rowNumber = () =>
            GXSql.RowNumber(GXSql.PartitionBy(partitionColumns), GXSql.OrderByDescending(dateColumn));
        ranked.Columns.Add(rowNumber.Body, "rn");
        var query = GXSelectArgs.From(() => GXSql.As(GXSql.Subquery<object>(ranked), "r"));
        var rankColumn = new GXColumnSchema { Name = "rn", Type = typeof(long) };
        query.Where.FilterBy(new (GXColumnSchema Column, object? Value)[] { (rankColumn, 1) }.AsEnumerable());
        var rankedSchema = new GXTableSchema { Name = "ranked" };
        foreach (var output in outputs)
            rankedSchema.Columns.Add(new GXColumnSchema
            {
                Name = output.Target, Parent = rankedSchema,
                Type = source.Columns.Single(c => c.Name == output.Source).Type
            });
        query.Columns.AddRange(rankedSchema.Columns.Select(c => (GXMetadataQueries.Column(c, "r"), (string?)null)));
        var latest = Table(source, "latest").Filter(GXSqlExpressions.And(keysNames.Select(k => GXSqlExpressions.Equal(GXMetadataQueries.Column(source.Columns.Single(c => c.Name == k), "latest"), GXMetadataQueries.Column(source.Columns.Single(c => c.Name == k), "s"))).ToArray()));
        latest.Columns.Add(GXSqlExpressions.MaxExpression(GXMetadataQueries.Column(source.Columns.Single(c => c.Name == date), "latest")));
        var conflicting = Table(source, "conflicting");
        conflicting.Columns.Add(GXSqlExpressions.Integer(1));
        var differences = outputs.Select(o => GXSqlExpressions.Or(
            GXSqlExpressions.NotEqual(GXMetadataQueries.Column(source.Columns.Single(c => c.Name == o.Source), "conflicting"), GXMetadataQueries.Column(source.Columns.Single(c => c.Name == o.Source), "s")),
            GXSqlExpressions.And(GXSqlExpressions.IsNull(GXMetadataQueries.Column(source.Columns.Single(c => c.Name == o.Source), "conflicting")), GXSqlExpressions.IsNull(GXMetadataQueries.Column(source.Columns.Single(c => c.Name == o.Source), "s"), true)),
            GXSqlExpressions.And(GXSqlExpressions.IsNull(GXMetadataQueries.Column(source.Columns.Single(c => c.Name == o.Source), "conflicting"), true), GXSqlExpressions.IsNull(GXMetadataQueries.Column(source.Columns.Single(c => c.Name == o.Source), "s"))))).ToArray();
        conflicting.Where.Set(GXSqlExpressions.And(GXSqlExpressions.And(keysNames.Select(k => GXSqlExpressions.Equal(GXMetadataQueries.Column(source.Columns.Single(c => c.Name == k), "conflicting"), GXMetadataQueries.Column(source.Columns.Single(c => c.Name == k), "s"))).ToArray()),
            GXSqlExpressions.Equal(GXMetadataQueries.Column(source.Columns.Single(c => c.Name == date), "conflicting"), GXMetadataQueries.Column(source.Columns.Single(c => c.Name == date), "s")), GXSqlExpressions.Or(differences)));
        var check = Table(source, "s").Filter(GXSqlExpressions.And(GXSqlExpressions.Equal(GXMetadataQueries.Column(source.Columns.Single(c => c.Name == date), "s"), GXSubqueryExpressions.Scalar<object?>(latest)), GXSqlExpressions.ExistsExpression(conflicting)));
        check.Columns.Add(GXSqlExpressions.CountExpression());
        return Plan(target, mapping, query, [check], outputs.Select(o => o.Target).ToArray());
    }

    private GXInformationMartPlan BuildLinkDirect(GXDataVaultTableMapping mapping, GXDataVaultTableMapping link)
    {
        GXTableSchema target = GXMartIdentifiers.Normalize(describe(Target(mapping).Name));
        var columns = mapping.Columns.Select(c => new
        {
            Source = Physical(link, link.Columns.Single(sc => Same(sc.SourceColumn, c.SourceColumn) || Same(sc.TargetColumn, c.SourceColumn)).TargetColumn).Name,
            Target = Physical(mapping, c.TargetColumn).Name
        }).ToList();
        var query = Table(Schema(link), "r0").WithDistinct();
        query.Columns.AddRange(columns.Select(c => (GXMetadataQueries.Column(Schema(link).Columns.Single(sc => sc.Name == c.Source), "r0"), c.Target)));
        return Plan(target, mapping, query, [], columns.Select(c => c.Target).ToArray());
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
            Target = existing,
            TargetColumns = plan.TargetColumns.Select(column => existing.Columns.Single(c => Same(c.Name, column)).Name).ToArray()
        };
    }
    private E Expression(Node node, GXMartColumnSelection column, GXDataVaultTableMapping hub, List<GXSelectArgs> checks)
    {
        if (node.Path.Count == 0)
        {
            if (column.Aggregation != Aggregation.None) throw new ArgumentException("Hub columns do not require aggregation.");
            return GXMetadataQueries.Column(Physical(hub, column.Column), "r0");
        }
        List<E> conditions = [];
        GXSelectArgs? query = null;
        for (int i = 0; i < node.Path.Count; ++i)
        {
            var edge = node.Path[i];
            string parent = i == 0 ? "r0" : "t" + i;
            string alias = "t" + (i + 1);
            var join = GXSqlExpressions.Equal(GXMetadataQueries.Column(Physical(edge.Parent, edge.ParentKey.TargetColumn), parent),
                GXMetadataQueries.Column(Physical(edge.Child, edge.ChildKey.TargetColumn), alias));
            if (i == 0) { query = Table(Schema(edge.Child), alias); conditions.Add(join); }
            else query!.Joins.AddInnerJoin(
                GXMetadataQueries.JoinColumn(Physical(edge.Parent, edge.ParentKey.TargetColumn), parent),
                GXMetadataQueries.JoinColumn(Physical(edge.Child, edge.ChildKey.TargetColumn), alias));
            if (edge.Child.ObjectType == DataVaultObjectType.Satellite)
            {
                var load = edge.Child.Columns.Where(c => c.Role == DataVaultColumnRole.LoadDate).ToArray();
                var parents = edge.Child.Columns.Where(c => c.Role == DataVaultColumnRole.ParentHashKey).ToArray();
                if (load.Length != 1 || parents.Length == 0) throw new ArgumentException($"Satellite '{Target(edge.Child).Name}' requires parent keys and one LoadDate mapping.");
                string date = Physical(edge.Child, load[0].TargetColumn).Name;
                var latest = Table(Schema(edge.Child), "latest").Filter(GXSqlExpressions.And(parents.Select(p => Physical(edge.Child, p.TargetColumn).Name)
                        .Select(k => GXSqlExpressions.Equal(GXMetadataQueries.Column(Schema(edge.Child).Columns.Single(c => c.Name == k), "latest"), GXMetadataQueries.Column(Schema(edge.Child).Columns.Single(c => c.Name == k), alias))).ToArray()));
                latest.Columns.Add(GXSqlExpressions.MaxExpression(GXMetadataQueries.Column(Schema(edge.Child).Columns.Single(c => c.Name == date), "latest")));
                conditions.Add(GXSqlExpressions.Equal(GXMetadataQueries.Column(Schema(edge.Child).Columns.Single(c => c.Name == date), alias), GXSubqueryExpressions.Scalar<object?>(latest)));
            }
        }
        query!.Where.Set(GXSqlExpressions.And(conditions.ToArray()));
        E expression = GXMetadataQueries.Column(Physical(node.GXDataVaultTableMapping, column.Column), "t" + node.Path.Count);
        if (column.Aggregation == Aggregation.None)
        {
            var count = Table(Schema(node.Path[0].Child), "t1");
            count.Where.Append(query.Where);
            count.Joins.AddRange(query.Joins);
            count.Columns.Add(GXSqlExpressions.CountExpression());
            var check = Table(Schema(hub), "r0").Filter(GXSqlExpressions.Greater(GXSubqueryExpressions.Scalar<long>(count), GXSqlExpressions.Integer(1)));
            check.Columns.Add(GXSqlExpressions.CountExpression());
            checks.Add(check);
        }
        else expression = column.Aggregation switch
        {
            Aggregation.Count => GXSqlExpressions.CountExpression(expression),
            Aggregation.CountDistinct => GXSqlExpressions.CountDistinct(expression),
            Aggregation.Average => GXSqlExpressions.Average(expression),
            Aggregation.Min => GXSqlExpressions.MinExpression(expression),
            Aggregation.Max => GXSqlExpressions.MaxExpression(expression),
            _ => throw new ArgumentException("Unsupported aggregation.")
        };
        query.Columns.Add(expression);
        return GXSubqueryExpressions.Scalar<object?>(query);
    }

    public static async Task<int> PopulateAsync(DbConnection connection, DbTransaction transaction, GXInformationMartPlan plan, CancellationToken cancellationToken,
        bool replace = false)
    {
        foreach (var check in plan.Checks)
        {
            if ((await new GXDbConnection(connection).SelectAsync<long>(transaction, check, cancellationToken)).Single() != 0)
                throw new InvalidOperationException("A mart column has multiple current rows per Hub key. Correct the mapping or choose an aggregation.");
        }
        if (replace)
        {
            await new GXDbConnection(connection).DeleteAsync(transaction, GXDeleteArgs.DeleteAll(plan.Target), cancellationToken);
        }
        await new GXDbConnection(connection).InsertAsync(transaction,
            GXInsertArgs.Insert(plan.Query, GXSchemaColumns.Columns(plan.Target, plan.TargetColumns)), cancellationToken);
        var count = GXSelectArgs.Select(plan.Target.Columns);
        count.Columns.Clear();
        count.Columns.Add(GXSqlExpressions.CountExpression());
        return checked((int)(await new GXDbConnection(connection).SelectAsync<long>(transaction, count, cancellationToken)).Single());
    }
}
