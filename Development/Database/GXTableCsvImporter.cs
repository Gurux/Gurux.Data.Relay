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

using E = System.Linq.Expressions.Expression;
using Gurux.Service.Orm;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Gurux.Data.Relay.Configuration;
using Gurux.Service.Orm.Common.Enums;
using Gurux.Service.Orm.Enums;
using Microsoft.VisualBasic.FileIO;

namespace Gurux.Data.Relay.Database;

public sealed class GXTableCsvImporter(IGXDatabaseConnectionFactory connections, IGXDatabaseMetadataService metadata)
{
    public Task<int> ImportJsonAsync(GXDatabaseConfiguration database, string tableName, string json,
        CancellationToken cancellationToken) => ImportCoreAsync(database, tableName, json, ",", false, true, cancellationToken);

    public Task<int> ImportAsync(GXDatabaseConfiguration database, string tableName, string csv,
        string delimiter, bool hasHeader, CancellationToken cancellationToken) =>
        ImportCoreAsync(database, tableName, csv, delimiter, hasHeader, false, cancellationToken);

    private async Task<int> ImportCoreAsync(GXDatabaseConfiguration database, string tableName, string csv,
        string delimiter, bool hasHeader, bool json, CancellationToken cancellationToken)
    {
        return await ImportRowsAsync(database, tableName,
            columns => json ? ParseJson(csv, columns, cancellationToken) : ParseCsv(csv, delimiter, hasHeader, columns.Count, cancellationToken),
            json ? "JSON" : "CSV", !json, hasHeader, cancellationToken);
    }

    private static List<string?[]> ParseCsv(string csv,
        string delimiter, bool hasHeader, int columnCount, CancellationToken cancellationToken)
    {
        if (delimiter is not ("," or ";" or "\t")) throw new ArgumentException("Unsupported CSV delimiter.");
        List<string?[]> rows = [];
        using var parser = new TextFieldParser(new StringReader(csv.TrimStart('\uFEFF')))
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(delimiter);
        int record = 0;
        try
        {
            while (!parser.EndOfData)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fields = parser.ReadFields()!;
                ++record;
                if (fields.Length != columnCount)
                    throw new ArgumentException($"CSV record {record}: expected {columnCount} columns, found {fields.Length}.");
                if (!hasHeader || record != 1) rows.Add(fields);
            }
        }
        catch (MalformedLineException ex) { throw new ArgumentException($"Invalid CSV at line {ex.LineNumber}: {ex.Message}", ex); }
        return rows;
    }

    private static List<string?[]> ParseJson(string json, List<string> columns, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(json.TrimStart('\uFEFF'));
            if (document.RootElement.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
                throw new ArgumentException("JSON must contain a row object or an array of row objects.");
            List<string?[]> rows = [];
            IEnumerable<JsonElement> records = document.RootElement.ValueKind == JsonValueKind.Object
                ? new[] { document.RootElement } : document.RootElement.EnumerateArray();
            foreach (var row in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int record = rows.Count + 1;
                if (row.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException($"JSON record {record}: expected a row object.");
                var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in row.EnumerateObject())
                    if (!fields.TryAdd(property.Name, property.Value))
                        throw new ArgumentException($"JSON record {record}: duplicate column '{property.Name}'.");
                if (fields.Count != columns.Count)
                    throw new ArgumentException($"JSON record {record}: expected {columns.Count} columns, found {fields.Count}. " +
                        $"Missing: {string.Join(", ", columns.Except(fields.Keys, StringComparer.OrdinalIgnoreCase))}. Unknown: {string.Join(", ", fields.Keys.Except(columns, StringComparer.OrdinalIgnoreCase))}.");
                string?[] values = new string?[columns.Count];
                for (int c = 0; c < columns.Count; ++c)
                {
                    if (!fields.TryGetValue(columns[c], out var value))
                        throw new ArgumentException($"JSON record {record}: missing column '{columns[c]}'.");
                    values[c] = value.ValueKind switch
                    {
                        JsonValueKind.Null => null,
                        JsonValueKind.String => value.GetString(),
                        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
                        _ => throw new ArgumentException($"JSON record {record}, column '{columns[c]}': expected a scalar value or null.")
                    };
                }
                rows.Add(values);
            }
            return rows;
        }
        catch (JsonException ex) { throw new ArgumentException($"Invalid JSON: {ex.Message}", ex); }
    }

    private async Task<int> ImportRowsAsync(GXDatabaseConfiguration database, string tableName,
        Func<List<string>, List<string?[]>> parse, string format, bool csv,
        bool hasHeader, CancellationToken cancellationToken)
    {
        if (!(await metadata.GetTableNamesAsync(database, cancellationToken)).Contains(tableName, StringComparer.Ordinal))
            throw new ArgumentException("The selected table does not exist.");
        var table = await metadata.DescribeTableAsync(database, tableName, cancellationToken);
        await using var connection = connections.CreateConnection(database);
        await connection.OpenAsync(cancellationToken);
        List<string> columns = table.Columns.Select(column => column.Name).ToList();
        List<Type> types = table.Columns.Select(column => column.Type ?? typeof(object)).ToList();
        // Validate the complete file before executing any INSERT.
        var rows = parse(columns);
        if (rows.Count == 0) throw new ArgumentException($"The {format} file contains no data rows.");

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        for (int r = 0; r < rows.Count; ++r)
        {
            try
            {
                object?[] values = Enumerable.Range(0, columns.Count).Select(c => ConvertValue(rows[r][c], types[c], csv)).ToArray();
                await new GXDbConnection(connection).InsertAsync(transaction, GXInsertArgs.Insert(values, GXSchemaColumns.Columns(table, columns)), cancellationToken);
            }
            catch (Exception ex) when (ex is DbException or FormatException or OverflowException or InvalidCastException)
            {
                throw new ArgumentException($"{format} record {r + (hasHeader ? 2 : 1)} could not be inserted. No rows were imported. {ex.Message}", ex);
            }
        }
        await transaction.CommitAsync(cancellationToken);
        return rows.Count;
    }

    private static object ConvertValue(string? value, Type type, bool csv)
    {
        if (value is null || (csv && value == "NULL")) return DBNull.Value;
        if (type == typeof(string) || type == typeof(object)) return value;
        if (value.Length == 0) return DBNull.Value;
        if (type == typeof(Guid)) return Guid.Parse(value);
        if (type == typeof(byte[])) return Convert.FromBase64String(value);
        if (type == typeof(DateTimeOffset)) return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(TimeSpan)) return TimeSpan.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(bool) && value is "0" or "1") return value == "1";
        // SQLite and several other providers expose boolean columns as integer fields.
        if (!csv && value is "true" or "false" &&
            (type == typeof(long) || type == typeof(int) || type == typeof(short) || type == typeof(byte)))
            return Convert.ChangeType(value == "true" ? 1 : 0, type, CultureInfo.InvariantCulture);
        return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
    }
}

