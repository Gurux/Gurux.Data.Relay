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

using Gurux.Data.Relay.Client;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Shared.Mcp;
using Gurux.Data.Relay.Web.Server.Services;
using Gurux.Service.Orm.Common.Model;

namespace Gurux.Data.Relay.Web.Server.Mcp;

/// <summary>Validates the entire batch before one atomic configuration save. Never executes DDL.</summary>
public sealed class GXDataVaultWriteService(IGXDataVaultWriteStore store)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private sealed class InvalidModel(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
    private static void Require(bool condition, string code, string message)
    {
        if (!condition) throw new InvalidModel(code, message);
    }

    public Task<GXDataVaultMcpApplyResult> CreateAsync(GXDataVaultMcpMappingRequest request, CancellationToken token)
        => ExecuteAsync(request.Database, async (configuration, database) =>
        {
            var prepared = await PrepareAsync(configuration, database, request, token);
            return new List<GXDataVaultTableMapping> { prepared };
        }, token);

    public Task<GXDataVaultMcpApplyResult> ApplyAsync(GXDataVaultModel model, CancellationToken token)
        => ExecuteAsync(model.Database, (configuration, database) => CompileAsync(configuration, database, model, token), token);

    public Task<GXDataVaultMcpApplyResult> CreateMartAsync(Guid databaseId, GXDataVaultMartDefinition definition, CancellationToken token)
        => ExecuteAsync(databaseId, async (configuration, database) =>
        {
            Require(definition is not null && !string.IsNullOrWhiteSpace(definition.Name) && !string.IsNullOrWhiteSpace(definition.SourceHub), "INVALID_MART", "Mart name and sourceHub are required.");
            var hub = (database.Mappings ?? []).FirstOrDefault(m => m.ObjectType == DataVaultObjectType.Hub && Same(m.TargetTable?.Name, definition.SourceHub));
            Require(hub is not null, "MART_HUB_NOT_FOUND", "The selected source Hub mapping was not found.");
            Require(!(database.Mappings ?? []).Any(m => Same(m.TargetTable?.Name, definition.Name)), "MAPPING_ALREADY_EXISTS", "The Mart target already has a mapping.");
            var source = string.IsNullOrWhiteSpace(definition.SourceTable) ? hub!.SourceTable!.Name! : definition.SourceTable;
            var tables = await store.TablesAsync(database, token);
            var schemas = new Dictionary<string, GXTableSchema>(StringComparer.OrdinalIgnoreCase);
            foreach (var mapping in database.Mappings ?? [])
                if (mapping.TargetTable?.Name is { } table && tables.Any(t => Same(t, table))) schemas[table] = await store.DescribeAsync(database, table, token);
            var builder = new GXInformationMartBuilder(database.Mappings ?? [], name => schemas.TryGetValue(name, out var schema) ? schema : throw new ArgumentException($"Schema '{name}' was not found."), database.Type, definition.ReferenceJoins);
            var preview = builder.Preview(source, hub.Id);
            var selected = preview.Columns.Where(c => definition.Columns is null || definition.Columns.Count == 0 || definition.Columns.Contains(c.TargetColumn, StringComparer.OrdinalIgnoreCase) || definition.Columns.Contains(c.Column, StringComparer.OrdinalIgnoreCase)).ToList();
            Require(selected.Count > 0, "MART_COLUMNS_EMPTY", "Select at least one Mart column.");
            var request = new GXCreateMartRequest { SourceTable = source, TargetTable = definition.Name, Definition = new GXInformationMartDefinition { HubMappingId = hub.Id, Columns = selected.Select(c => new GXMartColumnSelection { MappingId = c.MappingId, Column = c.Column, TargetColumn = definition.TargetColumns.TryGetValue(c.TargetColumn, out var alias) ? alias : c.TargetColumn, Aggregation = c.RequiresAggregation ? Aggregation.CountDistinct : Aggregation.None }).ToList() } };
            var plan = builder.Build(request);
            if (!tables.Any(t => Same(t, plan.TargetSchema.Name))) await store.CreateTableAsync(database, plan.TargetSchema, token);
            plan.Mapping.Id = Guid.NewGuid(); plan.Mapping.Database = databaseId; database.Mappings ??= []; database.Mappings.Add(plan.Mapping);
            GXDataVaultMappingValidator.Validate(configuration.GetMappings()); await store.SaveAsync(configuration, token);
            return new List<GXDataVaultTableMapping> { plan.Mapping };
        }, token);

    private async Task<GXDataVaultMcpApplyResult> ExecuteAsync(Guid databaseId,
        Func<GXDataVaultConfiguration, GXDatabaseConfiguration, Task<List<GXDataVaultTableMapping>>> prepare,
        CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var configuration = await store.LoadAsync(token);
            var database = configuration.Databases.SingleOrDefault(d => d.Id == databaseId);
            Require(database is not null, "DATABASE_NOT_FOUND", "Select a database configured for Data Vault.");
            var mappings = await prepare(configuration, database!);
            Require(mappings.Count > 0, "EMPTY_MODEL", "Select at least one Data Vault object.");
            foreach (var mapping in mappings)
                foreach (var parent in mapping.Columns.Where(c => c.Role == DataVaultColumnRole.ParentHashKey))
                {
                    var candidates = (database!.Mappings ?? []).Concat(mappings).Where(m => m.ObjectType == DataVaultObjectType.Hub &&
                        Same(m.SourceTable?.Name, mapping.SourceTable?.Name) && m.Columns.Any(c => c.Role == DataVaultColumnRole.HashKey && Same(c.SourceColumn, parent.SourceColumn))).ToArray();
                    Require(candidates.Length == 1, "PARENT_MAPPING_NOT_FOUND", "Each parent key must resolve to exactly one Hub mapping with the same Stage source lineage.");
                }
            foreach (var mapping in mappings)
            {
                mapping.Id = Guid.NewGuid();
                mapping.Database = databaseId;
            }
            database!.Mappings ??= [];
            database.Mappings.AddRange(mappings);
            GXDataVaultMappingValidator.Validate(configuration.GetMappings());
            token.ThrowIfCancellationRequested();
            await store.SaveAsync(configuration, token);
            return new() { Applied = true, Mappings = mappings.Select(m => new GXDataVaultMcpCreatedMapping
                { Id = m.Id, Table = m.TargetTable!.Name! }).ToList() };
        }
        catch (InvalidModel ex)
        {
            return new() { Errors = [new() { Code = ex.Code, Message = ex.Message }] };
        }
        // Provider exceptions can contain connection strings. Let MCP report its generic tool error.
        finally { gate.Release(); }
    }

    private async Task<GXDataVaultTableMapping> PrepareAsync(GXDataVaultConfiguration configuration,
        GXDatabaseConfiguration database, GXDataVaultMcpMappingRequest request, CancellationToken token)
    {
        Require(request.ObjectType is DataVaultObjectType.Hub or DataVaultObjectType.Link or DataVaultObjectType.Satellite or DataVaultObjectType.Reference,
            "UNSUPPORTED_OBJECT_TYPE", "Select Hub, Link, Satellite or Reference.");
        Require(!string.IsNullOrWhiteSpace(request.SourceTable) && !string.IsNullOrWhiteSpace(request.TargetTable) && !Same(request.SourceTable, request.TargetTable),
            "INVALID_TABLE", "Source and target must be distinct, nonempty table names.");
        var tables = await store.TablesAsync(database, token);
        Require(tables.Any(t => Same(t, request.SourceTable)), "SOURCE_TABLE_NOT_FOUND", "Source table was not found.");
        bool targetExists = tables.Any(t => Same(t, request.TargetTable));
        Require(targetExists || request.CreateIfMissing, "TARGET_TABLE_NOT_FOUND", "Target table was not found. Set createIfMissing to create it.");
        var stages = (database.Mappings ?? []).Where(m => m.ObjectType == DataVaultObjectType.Staging && Same(m.TargetTable?.Name, request.SourceTable)).ToArray();
        Require(stages.Length == 1, "STAGING_MAPPING_NOT_FOUND", "Select a source table with exactly one Stage mapping.");
        Require(!(database.Mappings ?? []).Any(m => Same(m.TargetTable?.Name, request.TargetTable)), "MAPPING_ALREADY_EXISTS", "The target already has a mapping. Existing mappings are not overwritten.");
        var source = await store.DescribeAsync(database, request.SourceTable, token);
        var target = targetExists ? await store.DescribeAsync(database, request.TargetTable, token) : null;
        Require(target is not null || request.CreateIfMissing, "TARGET_TABLE_NOT_FOUND", "Target table was not found.");
        if (target is null) throw new InvalidModel("CREATE_REQUIRES_MODEL_SCHEMA", "createIfMissing for a single mapping requires apply_model, which supplies the generated target schema.");
        Require(request.Columns is { Count: > 0 } && request.Columns.All(c => c is not null), "MISSING_BINDINGS", "Explicit column bindings are required.");
        Require(request.Columns!.Select(c => c.TargetColumn).Distinct(StringComparer.OrdinalIgnoreCase).Count() == request.Columns.Count,
            "DUPLICATE_BINDING", "Each target column must have exactly one binding.");
        var mapping = new GXDataVaultTableMapping { ObjectType = request.ObjectType, SourceTable = stages[0].SourceTable,
            TargetTable = new() { Id = Guid.Empty, Name = target.ToString() } };
        foreach (var binding in request.Columns)
        {
            Require(Enum.IsDefined(binding.Role), "INVALID_ROLE", "Unknown column role.");
            var destination = target.Columns.SingleOrDefault(c => Same(c.Name, binding.TargetColumn));
            Require(destination is not null && !destination.IsComputed && !destination.IsGenerated && !destination.IsIdentity && !destination.IsAutoIncrement,
                "TARGET_CONFLICT", "A target column is missing or is generated by the database.");
            var origin = source.Columns.SingleOrDefault(c => Same(c.Name, binding.SourceColumn));
            bool metadata = binding.Role is DataVaultColumnRole.LoadDate or DataVaultColumnRole.RecordSource;
            Require(metadata || origin is not null, "SOURCE_COLUMN_NOT_FOUND", "A source column was not found in the Stage table.");
            var stageBindings = stages[0].Columns.Where(c => Same(c.TargetColumn, binding.SourceColumn)).ToArray();
            Require(metadata || stageBindings.Length == 1, "STAGING_COLUMN_NOT_FOUND", "Every source column must resolve to exactly one Stage binding.");
            if (GXDataVaultHashStorage.IsHash(binding.Role))
            {
                try { GXDataVaultHashStorage.Validate(destination!, configuration.HashAlgorithm); }
                catch (ArgumentException) { throw new InvalidModel("TARGET_CONFLICT", "Target hash storage is incompatible with the configured algorithm."); }
            }
            else if (!metadata)
                Require(origin!.Type == destination!.Type && (!origin.IsNullable || destination.IsNullable) &&
                    !(origin.MaxLength > destination.MaxLength && destination.MaxLength > 0), "TARGET_CONFLICT", "Source and target column types, lengths or nullability are incompatible.");
            if (binding.Role == DataVaultColumnRole.BusinessKey)
                Require(origin is not null && !origin.IsNullable, "BUSINESS_KEY_NULLABLE", "Business Key columns must be non-nullable.");
            if (binding.Role == DataVaultColumnRole.LoadDate)
                Require(destination!.Type == typeof(DateTimeOffset) || destination.Type == typeof(DateTime), "TARGET_CONFLICT", "LoadDate must target a date/time column.");
            if (binding.Role == DataVaultColumnRole.RecordSource)
                Require(destination!.Type == typeof(string), "TARGET_CONFLICT", "RecordSource must target a text column.");
            mapping.Columns.Add(new() { SourceColumn = stageBindings.SingleOrDefault()?.SourceColumn ?? binding.SourceColumn,
                TargetColumn = destination!.Name, Role = binding.Role });
        }
        foreach (var column in target.Columns)
            Require(mapping.Columns.Any(c => Same(c.TargetColumn, column.Name)) || (!column.IsPrimaryKey && (column.IsNullable || column.DefaultValue is not null || column.IsComputed || column.IsGenerated || column.IsIdentity || column.IsAutoIncrement)),
                "TARGET_CONFLICT", "Every required target column and primary key must be mapped.");
        int Count(DataVaultColumnRole role) => mapping.Columns.Count(c => c.Role == role);
        Require(Count(DataVaultColumnRole.LoadDate) == 1 && Count(DataVaultColumnRole.RecordSource) == 1, "MISSING_METADATA", "Map one LoadDate and one RecordSource column.");
        Require(request.ObjectType switch
        {
            DataVaultObjectType.Hub => Count(DataVaultColumnRole.HashKey) == 1 && Count(DataVaultColumnRole.BusinessKey) == 1,
            DataVaultObjectType.Link => Count(DataVaultColumnRole.LinkHashKey) == 1 && Count(DataVaultColumnRole.ParentHashKey) >= 2,
            DataVaultObjectType.Satellite => Count(DataVaultColumnRole.ParentHashKey) == 1 && Count(DataVaultColumnRole.HashDiff) == 1 && Count(DataVaultColumnRole.Attribute) > 0,
            DataVaultObjectType.Reference => Count(DataVaultColumnRole.BusinessKey) == 1,
            _ => false
        }, "INVALID_ROLES", "The selected roles do not define the requested Data Vault object.");
        var allowed = request.ObjectType switch
        {
            DataVaultObjectType.Hub => new[] { DataVaultColumnRole.HashKey, DataVaultColumnRole.BusinessKey },
            DataVaultObjectType.Link => new[] { DataVaultColumnRole.LinkHashKey, DataVaultColumnRole.ParentHashKey },
            DataVaultObjectType.Satellite => new[] { DataVaultColumnRole.ParentHashKey, DataVaultColumnRole.HashDiff, DataVaultColumnRole.Attribute },
            _ => new[] { DataVaultColumnRole.BusinessKey, DataVaultColumnRole.Attribute }
        };
        Require(mapping.Columns.All(c => allowed.Contains(c.Role!.Value) || c.Role is DataVaultColumnRole.LoadDate or DataVaultColumnRole.RecordSource),
            "INVALID_ROLES", "A column role is not supported for this object type.");
        var keyRole = request.ObjectType switch
        {
            DataVaultObjectType.Hub => DataVaultColumnRole.HashKey,
            DataVaultObjectType.Link => DataVaultColumnRole.LinkHashKey,
            DataVaultObjectType.Satellite => DataVaultColumnRole.ParentHashKey,
            _ => DataVaultColumnRole.BusinessKey
        };
        var expectedKeys = mapping.Columns.Where(c => c.Role == keyRole || request.ObjectType == DataVaultObjectType.Satellite && c.Role == DataVaultColumnRole.LoadDate)
            .Select(c => c.TargetColumn).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(expectedKeys.SetEquals(target.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name)), "TARGET_CONFLICT", "Target primary key does not match the selected Data Vault roles.");
        if (request.ObjectType == DataVaultObjectType.Hub)
            Require(Same(mapping.Columns.Single(c => c.Role == DataVaultColumnRole.HashKey).SourceColumn,
                mapping.Columns.Single(c => c.Role == DataVaultColumnRole.BusinessKey).SourceColumn), "INVALID_BUSINESS_KEY", "Hub HashKey and BusinessKey must use the same source column.");
        return mapping;
    }

    private async Task<List<GXDataVaultTableMapping>> CompileAsync(GXDataVaultConfiguration configuration,
        GXDatabaseConfiguration database, GXDataVaultModel model, CancellationToken token)
    {
        Require(model.Hubs is not null && model.Links is not null && model.Satellites is not null && model.References is not null &&
            model.Hubs.All(h => h is not null) && model.Links.All(l => l is not null) && model.Satellites.All(s => s is not null) && model.References.All(r => r is not null), "INVALID_MODEL", "Object collections and entries must not be null.");
        var names = model.Hubs!.Select(h => h.Name).Concat(model.Links!.Select(l => l.Name)).Concat(model.Satellites!.Select(s => s.Name)).Concat(model.References!.Select(r => r.Name)).ToArray();
        Require(names.All(n => !string.IsNullOrWhiteSpace(n)) && names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length,
            "DUPLICATE_OBJECT_NAME", "Object names must be nonempty and unique.");
        Require(model.Marts is null || model.Marts.Count == 0, "MART_NOT_SUPPORTED", "Information Mart creation is not part of apply_model yet; create the vault mappings first.");
        var requests = new List<GXCreateVaultTableRequest>();
        foreach (var hub in model.Hubs)
        {
            Require(hub.BusinessKeys is { Count: 1 } && hub.BusinessKeys[0] is not null && !string.IsNullOrWhiteSpace(hub.BusinessKeys[0].Table) && !string.IsNullOrWhiteSpace(hub.BusinessKeys[0].Column), "UNSUPPORTED_BUSINESS_KEY", "The existing Hub builder supports exactly one source-qualified Business Key column.");
            requests.Add(new() { ObjectType = DataVaultObjectType.Hub, SourceTable = hub.BusinessKeys[0].Table,
                TargetTable = hub.Name, BusinessKeyColumn = hub.BusinessKeys[0].Column, TargetColumns = hub.TargetColumns });
        }
        GXDataVaultHubDefinition Hub(string name) => model.Hubs.SingleOrDefault(h => Same(h.Name, name))
            ?? throw new InvalidModel("LINK_ENDPOINT_MISSING", "Every Link endpoint must reference a Hub in the supplied model.");
        foreach (var link in model.Links)
        {
            Require(link.Hubs is { Count: >= 2 } && link.Hubs.Distinct(StringComparer.OrdinalIgnoreCase).Count() == link.Hubs.Count,
                "LINK_ENDPOINT_MISSING", "A Link needs at least two distinct Hub endpoints.");
            var hubs = link.Hubs.Select(Hub).ToArray();
            Require(hubs.All(h => Same(h.BusinessKeys[0].Table, hubs[0].BusinessKeys[0].Table)), "UNSUPPORTED_SOURCE_JOIN", "Link keys must belong to the same Stage table.");
            requests.Add(new() { ObjectType = DataVaultObjectType.Link, SourceTable = hubs[0].BusinessKeys[0].Table,
                TargetTable = link.Name, TargetColumns = link.TargetColumns,
                Parents = hubs.Select(h => new GXVaultParentSelection { Table = h.Name, SourceColumn = h.BusinessKeys[0].Column,
                    Column = "HK_" + h.BusinessKeys[0].Column }).ToList() });
        }
        foreach (var satellite in model.Satellites)
        {
            var parent = model.Hubs.SingleOrDefault(h => Same(h.Name, satellite.Parent));
            Require(parent is not null, "SATELLITE_PARENT_UNSUPPORTED", "This builder supports Hub-parent Satellites; supply a Hub parent in the model.");
            Require(satellite.Attributes is { Count: > 0 } && satellite.Attributes.All(a => a is not null && Same(a.Table, parent!.BusinessKeys[0].Table)),
                "INVALID_ATTRIBUTES", "Satellite attributes must belong to the parent's Stage table.");
            requests.Add(new() { ObjectType = DataVaultObjectType.Satellite, SourceTable = parent!.BusinessKeys[0].Table,
                TargetTable = satellite.Name, TargetColumns = satellite.TargetColumns, Columns = satellite.Attributes.Select(a => a.Column).ToList(),
                Parents = [new() { Table = parent.Name, SourceColumn = parent.BusinessKeys[0].Column, Column = "HK_" + parent.BusinessKeys[0].Column }] });
        }
        foreach (var reference in model.References)
        {
            Require(reference.BusinessKey is not null && !string.IsNullOrWhiteSpace(reference.BusinessKey.Table) && !string.IsNullOrWhiteSpace(reference.BusinessKey.Column), "INVALID_REFERENCE", "A Reference requires one source-qualified Business Key.");
            Require(reference.Attributes is not null && reference.Attributes.All(a => a is not null && Same(a.Table, reference.BusinessKey.Table)), "INVALID_REFERENCE", "Reference attributes must belong to the Business Key source table.");
            requests.Add(new() { ObjectType = DataVaultObjectType.Reference, SourceTable = reference.BusinessKey.Table,
                TargetTable = reference.Name, BusinessKeyColumn = reference.BusinessKey.Column, TargetColumns = reference.TargetColumns,
                Columns = reference.Attributes.Select(a => a.Column).ToList() });
        }
        var schemas = new Dictionary<string, GXTableSchema>(StringComparer.OrdinalIgnoreCase);
        var tables = await store.TablesAsync(database, token);
        foreach (var name in requests.Select(r => r.SourceTable).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Require(tables.Any(t => Same(t, name)), "SOURCE_TABLE_NOT_FOUND", "The Stage table does not exist.");
            schemas[name] = await store.DescribeAsync(database, name, token);
        }
        foreach (var name in requests.Select(r => r.TargetTable).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (tables.Any(t => Same(t, name)))
                schemas[name] = await store.DescribeAsync(database, name, token);
        }
        var result = new List<GXDataVaultTableMapping>();
        foreach (var request in requests)
        {
            if (!schemas.ContainsKey(request.TargetTable))
                Require(model.CreateIfMissing, "TABLE_NOT_FOUND", "Target table was not found. Set model.createIfMissing to create it.");
            GXDataVaultTableMapping generated;
            try
            {
                var expected = GXVaultTableSchemaBuilder.Build(request, schemas[request.SourceTable], name => schemas[name],
                    name => requests.SingleOrDefault(r => Same(r.TargetTable, name))?.ObjectType);
                if (!schemas.ContainsKey(request.TargetTable))
                {
                    await store.CreateTableAsync(database, expected, token);
                    schemas[request.TargetTable] = expected;
                }
                generated = GXVaultTableMappingBuilder.Build(request, expected, null);
            }
            catch (ArgumentException) { throw new InvalidModel("INVALID_MODEL", "The supplied definition cannot be built. Check keys, parent keys and column names."); }
            result.Add(await PrepareAsync(configuration, database, new()
            {
                Database = model.Database, SourceTable = request.SourceTable, TargetTable = request.TargetTable, ObjectType = request.ObjectType, CreateIfMissing = model.CreateIfMissing,
                Columns = generated.Columns.Select(c => new GXDataVaultMcpColumnBinding { SourceColumn = c.SourceColumn!, TargetColumn = c.TargetColumn!, Role = c.Role!.Value }).ToList()
            }, token));
        }
        return result;
    }
}
