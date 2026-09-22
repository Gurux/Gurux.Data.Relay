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

using System.Security.Cryptography;
using System.Text.Json;
using Gurux.Data.Relay.Shared.Protocol;
using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Client;

/// <summary>Prepares a checkpoint without mutating the last acknowledged checkpoint.</summary>
internal sealed class GXContentHashTracker
{
    private const string Prefix = "ContentHash:v1:";
    private readonly string[] _keys, _columns;
    private readonly Dictionary<string, string> _hashes;
    private readonly HashSet<string> _seen = [];
    private readonly string _signature;
    private readonly int _batchSize;
    public List<GXDataChange> Changes { get; } = [];

    public GXContentHashTracker(IEnumerable<string> selectedColumns, IEnumerable<string> keys,
        string? hashColumns, string? checkpoint, int batchSize)
    {
        var selected = selectedColumns.ToArray();
        string[] Resolve(IEnumerable<string> names) => names.Select(name =>
            selected.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"ContentHash column '{name}' must be selected for transfer."))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.Ordinal).ToArray();
        _keys = Resolve(keys);
        if (_keys.Length == 0) throw new InvalidOperationException("ContentHash requires unique key columns.");
        _columns = Resolve(string.IsNullOrWhiteSpace(hashColumns) ? selected : hashColumns.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        if (_columns.Length == 0) throw new InvalidOperationException("Select at least one ContentHash column.");
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
        _batchSize = batchSize;
        _signature = JsonSerializer.Serialize(new[] { _keys, _columns });
        _hashes = [];
        if (!string.IsNullOrEmpty(checkpoint))
        {
            if (!checkpoint.StartsWith(Prefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Reset the transfer checkpoint before enabling ContentHash.");
            var saved = JsonSerializer.Deserialize<Snapshot>(checkpoint[Prefix.Length..])
                ?? throw new InvalidOperationException("Invalid ContentHash checkpoint.");
            if (saved.Signature != _signature)
                throw new InvalidOperationException("ContentHash columns or keys changed. Reset the transfer checkpoint before transferring.");
            _hashes = new(saved.Hashes);
        }
    }

    public void Add(Dictionary<string, JsonElement> values)
    {
        var keys = GXChangeTrackingHelper.BuildKeyValues(values, _keys.ToList());
        if (keys.Values.Any(value => value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined))
            throw new InvalidOperationException("ContentHash key columns cannot contain NULL.");
        var key = Hash(keys, _keys);
        if (!_seen.Add(key)) throw new InvalidOperationException("ContentHash found duplicate keys in the source table. Select columns that uniquely identify each row.");
        var hash = Hash(values, _columns);
        var exists = _hashes.TryGetValue(key, out var previous);
        if (hash == previous || Changes.Count >= _batchSize) return;
        Changes.Add(new GXDataChange { Operation = exists ? DataOperation.Update : DataOperation.Insert, Values = values, Keys = keys });
        _hashes[key] = hash;
    }

    public string Checkpoint => Prefix + JsonSerializer.Serialize(new Snapshot { Signature = _signature, Hashes = _hashes });

    private static string Hash(Dictionary<string, JsonElement> values, string[] columns)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var column in columns)
            {
                if (!values.TryGetValue(column, out var value)) throw new InvalidOperationException($"ContentHash source column '{column}' is missing.");
                writer.WriteStartArray();
                writer.WriteStringValue(column);
                value.WriteTo(writer);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private sealed class Snapshot
    {
        public string Signature { get; set; } = string.Empty;
        public Dictionary<string, string> Hashes { get; set; } = [];
    }
}
