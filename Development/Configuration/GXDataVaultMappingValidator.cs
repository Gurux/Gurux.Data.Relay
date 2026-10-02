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

namespace Gurux.Data.Relay.Configuration;

/// <summary>Validates column bindings required to write Data Vault target tables.</summary>
public static class GXDataVaultMappingValidator
{
    public static void Validate(GXDataVaultTableMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        string table = mapping.TargetTable?.Name ?? "<unknown>";
        foreach (GXDataVaultColumnMapping column in mapping.Columns ?? [])
        {
            if (string.IsNullOrWhiteSpace(column.TargetColumn))
                throw new ArgumentException($"Mapping for target table '{table}' has no target column for source column '{column.SourceColumn}'.");
        }
    }

    public static void Validate(IEnumerable<GXDataVaultTableMapping> mappings)
    {
        GXDataVaultTableMapping[] all = mappings.ToArray();
        foreach (GXDataVaultTableMapping mapping in all)
        {
            Validate(mapping);
            if (mapping.ObjectType != DataVaultObjectType.InformationMart || !mapping.Columns.Any(c => c.SourceMappingId.HasValue)) continue;
            if (mapping.Columns.Any(c => !c.SourceMappingId.HasValue || c.SourceMappingId.Value == Guid.Empty))
                throw new ArgumentException($"Information Mart '{mapping.TargetTable?.Name}' requires a source mapping for every column.");
            GXDataVaultTableMapping[] sources = mapping.Columns.Select(c => c.SourceMappingId!.Value).Distinct()
                .Select(sourceId => all.SingleOrDefault(candidate => candidate.Id == sourceId && candidate.ObjectType != DataVaultObjectType.InformationMart)
                    ?? throw new ArgumentException($"Information Mart '{mapping.TargetTable?.Name}' references an unavailable source mapping '{sourceId}'."))
                .ToArray();
            foreach (GXDataVaultColumnMapping column in mapping.Columns)
            {
                GXDataVaultTableMapping source = sources.Single(candidate => candidate.Id == column.SourceMappingId!.Value);
                if (!source.Columns.Any(candidate => Same(candidate.SourceColumn, column.SourceColumn) || Same(candidate.TargetColumn, column.SourceColumn)))
                    throw new ArgumentException($"Information Mart '{mapping.TargetTable?.Name}' source column '{column.SourceColumn}' is not mapped by '{source.TargetTable?.Name}'.");
            }
            bool stage = sources.All(source => source.ObjectType == DataVaultObjectType.Staging);
            if (stage)
            {
                if (sources.Length != 1)
                    throw new ArgumentException($"Information Mart '{mapping.TargetTable?.Name}' must select exactly one Stage source mapping.");
                if (!mapping.Columns.Any(column => column.Role == DataVaultColumnRole.BusinessKey))
                    throw new ArgumentException($"Stage-direct Information Mart '{mapping.TargetTable?.Name}' requires a BusinessKey column.");
                if (!sources[0].Columns.Any(column => column.Role == DataVaultColumnRole.LoadDate))
                    throw new ArgumentException($"Stage source '{sources[0].TargetTable?.Name}' requires a LoadDate mapping.");
            }
            else if (sources.Any(source => source.ObjectType == DataVaultObjectType.Staging))
            {
                throw new ArgumentException($"Information Mart '{mapping.TargetTable?.Name}' cannot mix Stage and Raw Data Vault sources.");
            }
        }
    }

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
