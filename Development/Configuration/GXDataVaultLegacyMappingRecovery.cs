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

using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.Orm.Common.Model;

namespace Gurux.Data.Relay.Configuration;

/// <summary>Rebuilds fully blank Data Vault target-column bindings from exported target schemas.</summary>
public static class GXDataVaultLegacyMappingRecovery
{
    public static void Recover(IEnumerable<GXDataVaultTableMapping> mappings,
        IReadOnlyDictionary<Guid, IReadOnlyList<GXTableSchema>> schemas)
    {
        GXDataVaultTableMapping[] mappingList = mappings.ToArray();
        RecoverMartStageSources(mappingList);
        foreach (GXDataVaultTableMapping mapping in mappingList)
        {
            if (mapping.ObjectType == DataVaultObjectType.Staging && mapping.Columns.Count == 0)
            {
                string stagingTable = mapping.TargetTable?.Name ?? throw new ArgumentException("Legacy mapping has no target table.");
                GXTableSchema stagingSchema = schemas.TryGetValue(mapping.Database, out IReadOnlyList<GXTableSchema>? stagingSchemas)
                    ? stagingSchemas.SingleOrDefault(item => string.Equals(item.Name, stagingTable, StringComparison.OrdinalIgnoreCase))
                        ?? throw new ArgumentException($"No exported schema is available for target table '{stagingTable}'.")
                    : throw new ArgumentException($"No exported schema is available for target table '{stagingTable}'.");
                mapping.Columns = stagingSchema.Columns.Select(column => new GXDataVaultColumnMapping
                {
                    SourceColumn = column.Name,
                    TargetColumn = column.Name,
                    Role = string.Equals(column.Name, "LOAD_DATE", StringComparison.OrdinalIgnoreCase) ? DataVaultColumnRole.LoadDate :
                        string.Equals(column.Name, "RECORD_SOURCE", StringComparison.OrdinalIgnoreCase) ? DataVaultColumnRole.RecordSource :
                        DataVaultColumnRole.Attribute
                }).ToList();
                continue;
            }
            bool hasBlank = mapping.Columns.Any(column => string.IsNullOrWhiteSpace(column.TargetColumn));
            if (!hasBlank) continue;
            if (mapping.Columns.Any(column => !string.IsNullOrWhiteSpace(column.TargetColumn)))
                throw new ArgumentException($"Mapping for target table '{mapping.TargetTable?.Name}' has partially blank target columns.");

            string table = mapping.TargetTable?.Name ?? throw new ArgumentException("Legacy mapping has no target table.");
            GXTableSchema schema = schemas.TryGetValue(mapping.Database, out IReadOnlyList<GXTableSchema>? databaseSchemas)
                ? databaseSchemas.SingleOrDefault(item => string.Equals(item.Name, table, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException($"No exported schema is available for target table '{table}'.")
                : throw new ArgumentException($"No exported schema is available for target table '{table}'.");
            if (schema.Columns.Count == 0 || schema.Columns.Select(column => column.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != schema.Columns.Count)
                throw new ArgumentException($"Exported schema for target table '{table}' must contain uniquely named columns.");
            var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? TryTake(IEnumerable<string> candidates)
            {
                foreach (string candidate in candidates)
                {
                    GXColumnSchema[] matches = schema.Columns.Where(column => !assigned.Contains(column.Name) &&
                        string.Equals(candidate, column.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matches.Length == 1) { assigned.Add(matches[0].Name); return matches[0].Name; }
                    if (matches.Length > 1) break;
                }
                return null;
            }
            string Take(params string[] candidates)
            {
                string? target = TryTake(candidates);
                if (target is not null) return target;
                throw new ArgumentException($"Target column for source column '{candidates[0]}' could not be restored in target table '{table}'.");
            }
            var regular = mapping.Columns.Where(column =>
                !string.Equals(column.SourceColumn, "LOAD_DATE", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(column.SourceColumn, "RECORD_SOURCE", StringComparison.OrdinalIgnoreCase)).ToList();
            var bindings = new List<(GXDataVaultColumnMapping Column, string Target, DataVaultColumnRole Role)>();
            var unresolved = new List<(GXDataVaultColumnMapping Column, string[] Candidates, DataVaultColumnRole Role)>();
            for (int index = 0; index < regular.Count; ++index)
            {
                GXDataVaultColumnMapping column = regular[index];
                (string[] candidates, DataVaultColumnRole role) = mapping.ObjectType switch
                {
                    DataVaultObjectType.Hub when index == 0 => ([$"HK_{column.SourceColumn}"], DataVaultColumnRole.HashKey),
                    DataVaultObjectType.Hub => ([$"{column.SourceColumn}_BK", column.SourceColumn], DataVaultColumnRole.BusinessKey),
                    DataVaultObjectType.Link when index == 0 => ([$"HK_{table}"], DataVaultColumnRole.LinkHashKey),
                    DataVaultObjectType.Link => ([column.SourceColumn, $"HK_{column.SourceColumn}"], DataVaultColumnRole.ParentHashKey),
                    DataVaultObjectType.Satellite when index == 0 => ([$"HK_{column.SourceColumn}", column.SourceColumn], DataVaultColumnRole.ParentHashKey),
                    DataVaultObjectType.Satellite when index == 1 => (["HASH_DIFF"], DataVaultColumnRole.HashDiff),
                    _ => (ColumnCandidates(column.SourceColumn), DataVaultColumnRole.Attribute)
                };
                string? target = TryTake(candidates);
                if (target is null) unresolved.Add((column, candidates, role));
                else bindings.Add((column, target, role));
            }
            if (unresolved.Count != 0)
            {
                string[] remaining = schema.Columns.Where(column => !assigned.Contains(column.Name)).Select(column => column.Name).ToArray();
                if (unresolved.Count != 1 || remaining.Length != 1)
                    throw new ArgumentException($"Target column for source column '{unresolved[0].Candidates[0]}' could not be restored in target table '{table}'.");
                bindings.Add((unresolved[0].Column, remaining[0], unresolved[0].Role));
                assigned.Add(remaining[0]);
            }
            foreach (GXDataVaultColumnMapping column in mapping.Columns.Except(regular))
            {
                bool loadDate = string.Equals(column.SourceColumn, "LOAD_DATE", StringComparison.OrdinalIgnoreCase);
                bindings.Add((column, Take(loadDate ? "LOAD_DATE" : "RECORD_SOURCE"),
                    loadDate ? DataVaultColumnRole.LoadDate : DataVaultColumnRole.RecordSource));
            }
            foreach ((GXDataVaultColumnMapping column, string target, DataVaultColumnRole role) in bindings)
            {
                column.TargetColumn = target;
                column.Role = role;
            }
        }
    }

    private static string[] ColumnCandidates(string source)
    {
        string? businessKeyName = source.EndsWith("_BK", StringComparison.OrdinalIgnoreCase) ? source[..^3] : null;
        return [source, $"HK_{source}", $"{source}_BK", ..(businessKeyName is null ? [] : new[] { businessKeyName })];
    }

    private static void RecoverMartStageSources(IReadOnlyList<GXDataVaultTableMapping> mappings)
    {
        foreach (GXDataVaultTableMapping mart in mappings.Where(mapping =>
            mapping.ObjectType == DataVaultObjectType.InformationMart && mapping.SourceTable is not null))
        {
            GXTable[] sources = mappings.Where(mapping =>
                    mapping.ObjectType is not (DataVaultObjectType.Staging or DataVaultObjectType.InformationMart) &&
                    mapping.TargetTable is not null &&
                    string.Equals(mapping.TargetTable.Name, mart.SourceTable!.Name, StringComparison.OrdinalIgnoreCase) &&
                    mapping.SourceTable is not null)
                .Select(mapping => mapping.SourceTable!)
                .GroupBy(table => table.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
            if (sources.Length == 1) mart.SourceTable = sources[0];
        }
    }
}
