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

using System.Text.Json;
using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Client;

internal static class GXChangeTrackingHelper
{
    public static DataOperation GetVersionOperation(bool hasCheckpoint)
    {
        return hasCheckpoint ? DataOperation.Update : DataOperation.Insert;
    }

    public static DataOperation? DetectTimestampOperation(
        Dictionary<string, JsonElement> values,
        DateTimeOffset checkpoint,
        string? createdColumn,
        string? updatedColumn,
        string? deletedColumn)
    {
        if (!string.IsNullOrWhiteSpace(deletedColumn) && TryGetTimestamp(values, deletedColumn, out DateTimeOffset deleted) && deleted > checkpoint)
        {
            return DataOperation.Delete;
        }
        if (!string.IsNullOrWhiteSpace(updatedColumn) && TryGetTimestamp(values, updatedColumn, out DateTimeOffset updated) && updated > checkpoint)
        {
            return DataOperation.Update;
        }
        if (!string.IsNullOrWhiteSpace(createdColumn) && TryGetTimestamp(values, createdColumn, out DateTimeOffset created) && created > checkpoint)
        {
            return DataOperation.Insert;
        }

        return null;
    }

    public static Dictionary<string, JsonElement> BuildKeyValues(Dictionary<string, JsonElement> values, List<string> keys)
    {
        Dictionary<string, JsonElement> keyValues = new(StringComparer.OrdinalIgnoreCase);
        foreach (string key in keys)
        {
            if (!values.TryGetValue(key, out JsonElement value))
            {
                throw new InvalidOperationException($"Key column '{key}' is missing from the selected source values.");
            }

            keyValues[key] = value;
        }

        return keyValues;
    }

    public static DateTimeOffset? GetMaximumTrackedTimestamp(
        Dictionary<string, JsonElement> values,
        string? createdColumn,
        string? updatedColumn,
        string? deletedColumn)
    {
        DateTimeOffset? max = null;
        if (!string.IsNullOrWhiteSpace(createdColumn) && TryGetTimestamp(values, createdColumn, out DateTimeOffset created))
        {
            max = Max(max, created);
        }
        if (!string.IsNullOrWhiteSpace(updatedColumn) && TryGetTimestamp(values, updatedColumn, out DateTimeOffset updated))
        {
            max = Max(max, updated);
        }
        if (!string.IsNullOrWhiteSpace(deletedColumn) && TryGetTimestamp(values, deletedColumn, out DateTimeOffset deleted))
        {
            max = Max(max, deleted);
        }

        return max;
    }

    public static string? GetCheckpointValue(Dictionary<string, JsonElement> values, string? columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return null;
        }

        return values.TryGetValue(columnName, out JsonElement value) ? value.ToString() : null;
    }

    private static bool TryGetTimestamp(Dictionary<string, JsonElement> values, string columnName, out DateTimeOffset value)
    {
        value = default;
        if (!values.TryGetValue(columnName, out JsonElement element) || element.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string? text = element.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (DateTimeOffset.TryParse(text, out DateTimeOffset dto))
        {
            value = dto;
            return true;
        }

        if (DateTime.TryParse(text, out DateTime dt))
        {
            value = new DateTimeOffset(dt);
            return true;
        }

        if (DateOnly.TryParse(text, out DateOnly date))
        {
            value = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue));
            return true;
        }

        return false;
    }

    private static DateTimeOffset? Max(DateTimeOffset? current, DateTimeOffset? candidate)
    {
        if (candidate is null)
        {
            return current;
        }
        if (current is null || candidate > current)
        {
            return candidate;
        }

        return current;
    }
}
