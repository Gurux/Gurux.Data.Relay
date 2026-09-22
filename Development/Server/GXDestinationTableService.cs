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

using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Database;
using Gurux.Data.Relay.Shared.Protocol;
using Gurux.Service.Orm;
using Gurux.Service.Orm.Common;
using Gurux.Service.Orm.Common.Model;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using Gurux.Service.Orm.Model;
using Gurux.Service.Orm.Common.Enums;
using Gurux.Data.Relay.Shared.Server;

namespace Gurux.Data.Relay.Server;

public sealed class GXDestinationTableService : IDestinationTableService
{
    private readonly IGXConfigurationService _configurationService;
    private readonly IGXDatabaseConnectionFactory _connectionFactory;

    public GXDestinationTableService(
        IGXConfigurationService configurationService,
        IGXDatabaseConnectionFactory connectionFactory)
    {
        _configurationService = configurationService;
        _connectionFactory = connectionFactory;
    }

    public async Task EnsureReadyAsync(string destinationTable, GXDataMessage message, CancellationToken cancellationToken)
    {
        GXServerConfiguration configuration = await _configurationService.LoadServerAsync(cancellationToken)
            ?? throw new InvalidOperationException("Server configuration was not found.");

        IReadOnlyList<GXDatabaseConfiguration> databases = ResolveTargetDatabases(configuration);
        foreach (GXDatabaseConfiguration database in databases)
        {
            await EnsureReadyAsync(database, destinationTable, message, cancellationToken);
        }
    }

    private static IReadOnlyList<GXDatabaseConfiguration> ResolveTargetDatabases(GXServerConfiguration configuration)
    {
        IReadOnlyList<GXDatabaseConfiguration> databases = configuration.GetTargetDatabases();
        if (GXServerDatabaseContext.DatabaseIndex is not int index)
        {
            return databases;
        }

        if (index < 0 || index >= databases.Count)
        {
            throw new InvalidOperationException($"Server database index {index} is outside configured Databases.");
        }

        return [databases[index]];
    }

    public async Task EnsureReadyAsync(GXDatabaseConfiguration database, string destinationTable, GXDataMessage message, CancellationToken cancellationToken)
    {
        GXRecordSource.Prepare(message);
        await using var connection = _connectionFactory.CreateConnection(database);
        await connection.OpenAsync(cancellationToken);
        GXSchemaManager schemaManager = new(connection);

        if (!schemaManager.TableExist(destinationTable))
        {
            GXStageLoadDate.AddColumn(message.Table);
            await CreateTableAsync(schemaManager, database.Type, destinationTable, message, cancellationToken);
        }

        GXTableSchema table = schemaManager.Describe(destinationTable);
        ValidateColumns(table, message);
        ValidateKeys(table, message);
    }

    public async Task EnsureSchemaAsync(string destinationTable, GXDataMessage message, CancellationToken cancellationToken)
    {
        GXServerConfiguration configuration = await _configurationService.LoadServerAsync(cancellationToken)
            ?? throw new InvalidOperationException("Server configuration was not found.");

        IReadOnlyList<GXDatabaseConfiguration> databases = ResolveTargetDatabases(configuration);
        foreach (GXDatabaseConfiguration database in databases)
        {
            await EnsureSchemaAsync(database, destinationTable, message, cancellationToken);
        }
    }

    private async Task EnsureSchemaAsync(
        GXDatabaseConfiguration database,
        string destinationTable,
        GXDataMessage message,
        CancellationToken cancellationToken)
    {
        GXRecordSource.Prepare(message);
        GXTableSchema schema = message.Schema
            ?? throw new InvalidOperationException("Schema message must include a table schema.");

        await using var connection = _connectionFactory.CreateConnection(database);
        await connection.OpenAsync(cancellationToken);
        GXSchemaManager schemaManager = new(connection);

        if (!schemaManager.TableExist(destinationTable))
        {
            GXStageLoadDate.AddColumn(schema);
            PrepareDestinationSchema(schema, database.Type, destinationTable, message.Keys);
            await Task.Run(() => schemaManager.CreateTable(schema), cancellationToken);
        }

        if (!schemaManager.TableExist(destinationTable))
        {
            throw new InvalidOperationException($"Destination table '{destinationTable}' could not be created from schema.");
        }
    }

    private static async Task CreateTableAsync(
        GXSchemaManager schemaManager,
        DatabaseType databaseType,
        string destinationTable,
        GXDataMessage message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Type tableType = GXRuntimeTableTypeBuilder.Build(databaseType, destinationTable, message.Table, message.Keys);
        await Task.Run(() => schemaManager.CreateTable(tableType, relations: false, overwrite: false), cancellationToken);

        if (!schemaManager.TableExist(destinationTable))
        {
            throw new InvalidOperationException($"Destination table '{destinationTable}' could not be created.");
        }
    }

    private static void ValidateColumns(GXTableSchema table, GXDataMessage message)
    {
        foreach (GXColumnSchema requiredColumn in message.Table.Columns)
        {
            GXColumnSchema? actualColumn = table.Columns.FirstOrDefault(column =>
                string.Equals(column.Name, requiredColumn.Name, StringComparison.OrdinalIgnoreCase));
            if (actualColumn is null)
            {
                if (requiredColumn.IsNullable)
                {
                    continue;
                }

                throw new InvalidOperationException($"Destination column '{requiredColumn.Name}' is missing from table '{table.Name}'.");
            }

            Type expectedType = requiredColumn.Type;
            if (!GXTypeCompatibilityHelper.IsCompatible(actualColumn.Type, expectedType))
            {
                throw new InvalidOperationException(
                    $"Destination column '{actualColumn.Name}' in table '{table.Name}' has incompatible type '{actualColumn.Type?.Name ?? actualColumn.DbType}'. Expected '{expectedType.Name}'.");
            }

            if (!requiredColumn.IsNullable && actualColumn.IsNullable)
            {
                continue;
            }
        }
    }

    private static void ValidateKeys(GXTableSchema table, GXDataMessage message)
    {
        foreach (string key in message.Keys)
        {
            GXColumnSchema? actualColumn = table.Columns.FirstOrDefault(column =>
                string.Equals(column.Name, key, StringComparison.OrdinalIgnoreCase));
            if (actualColumn is null)
            {
                throw new InvalidOperationException($"Destination key column '{key}' is missing from table '{table.Name}'.");
            }
        }
    }

    private static void PrepareDestinationSchema(GXTableSchema schema, DatabaseType databaseType, string destinationTable, List<string> keys)
    {
        schema.Catalog = null;
        schema.Schema = null;
        schema.Name = NormalizeDatabaseIdentifier(databaseType, destinationTable);
        NormalizeDestinationColumns(schema, databaseType, keys);
        ApplyConfiguredKeys(schema, keys);
    }

    private static void NormalizeDestinationColumns(GXTableSchema schema, DatabaseType databaseType, List<string> keys)
    {
        HashSet<string> keyNames = new(keys, StringComparer.OrdinalIgnoreCase);
        foreach (GXColumnSchema column in schema.Columns)
        {
            column.DbType = string.Empty;
            column.DefaultValue = null;
            column.ComputedExpression = null;
            column.Collation = null;
            column.IsUnique = false;
            column.IsGenerated = false;
            column.IsIdentity = false;
            column.IsAutoIncrement = false;
            column.IsComputed = false;

            if (databaseType == DatabaseType.DB2)
            {
                column.Name = column.Name.ToUpperInvariant();
            }

            if (keyNames.Contains(column.Name))
            {
                NormalizeKeyColumn(column);
            }
        }
    }

    private static void NormalizeKeyColumn(GXColumnSchema column)
    {
        column.Type ??= typeof(string);
        Type type = Nullable.GetUnderlyingType(column.Type) ?? column.Type;
        if (type == typeof(string) &&
            (column.MaxLength is null or <= 0 || column.MaxLength > 1024))
        {
            column.MaxLength = 450;
        }
        else if (type == typeof(byte[]) &&
            (column.MaxLength is null or <= 0 || column.MaxLength > 1024))
        {
            column.MaxLength = 256;
        }
    }

    private static void ApplyConfiguredKeys(GXTableSchema schema, List<string> keys)
    {
        if (keys.Count == 0)
        {
            return;
        }

        HashSet<string> keyNames = new(keys, StringComparer.OrdinalIgnoreCase);
        foreach (GXColumnSchema column in schema.Columns)
        {
            column.IsPrimaryKey = keyNames.Contains(column.Name);
        }
    }

    private static string NormalizeDatabaseIdentifier(DatabaseType databaseType, string value)
    {
        return databaseType == DatabaseType.DB2 ? value.ToUpperInvariant() : value;
    }

}

internal static class GXRuntimeTableTypeBuilder
{
    private static readonly AssemblyBuilder Assembly = AssemblyBuilder.DefineDynamicAssembly(
        new AssemblyName("GXDataRelay.RuntimeTables"),
        AssemblyBuilderAccess.Run);

    private static readonly ModuleBuilder Module = Assembly.DefineDynamicModule("GXDataRelay.RuntimeTables");
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Type> Types = new(StringComparer.Ordinal);

    public static Type Build(DatabaseType databaseType, string destinationTable, GXTableSchema definition, IReadOnlyList<string> transferKeys)
    {
        string cacheKey = CreateCacheKey(databaseType, destinationTable, definition, transferKeys);
        lock (Gate)
        {
            if (Types.TryGetValue(cacheKey, out Type? existing))
            {
                return existing;
            }

            Type type = BuildCore(databaseType, destinationTable, definition, transferKeys, Types.Count);
            Types.Add(cacheKey, type);
            return type;
        }
    }

    private static Type BuildCore(DatabaseType databaseType, string destinationTable, GXTableSchema definition, IReadOnlyList<string> transferKeys, int index)
    {
        string effectiveDestinationTable = NormalizeDatabaseIdentifier(databaseType, destinationTable);
        TypeBuilder typeBuilder = Module.DefineType(
            $"GXDataRelay.RuntimeTables.{SanitizeIdentifier(effectiveDestinationTable)}_{index}",
            TypeAttributes.Public | TypeAttributes.Class);
        typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);
        AddDataContractAttribute(typeBuilder, effectiveDestinationTable);

        HashSet<string> keys = new(transferKeys, StringComparer.OrdinalIgnoreCase);
        foreach (GXColumnSchema column in definition.Columns)
        {
            DefineProperty(typeBuilder, databaseType, column, keys.Contains(column.Name));
        }

        return typeBuilder.CreateType()
            ?? throw new InvalidOperationException($"Failed to build runtime table type for '{destinationTable}'.");
    }

    private static void DefineProperty(TypeBuilder typeBuilder, DatabaseType databaseType, GXColumnSchema column, bool isPrimaryKey)
    {
        string effectiveColumnName = NormalizeDatabaseIdentifier(databaseType, column.Name);
        Type propertyType = GetPropertyType(column);
        FieldBuilder fieldBuilder = typeBuilder.DefineField($"_{SanitizeIdentifier(effectiveColumnName)}", propertyType, FieldAttributes.Private);
        PropertyBuilder propertyBuilder = typeBuilder.DefineProperty(effectiveColumnName, PropertyAttributes.HasDefault, propertyType, null);

        MethodBuilder getMethod = typeBuilder.DefineMethod(
            $"get_{effectiveColumnName}",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            propertyType,
            Type.EmptyTypes);
        ILGenerator getIl = getMethod.GetILGenerator();
        getIl.Emit(OpCodes.Ldarg_0);
        getIl.Emit(OpCodes.Ldfld, fieldBuilder);
        getIl.Emit(OpCodes.Ret);

        MethodBuilder setMethod = typeBuilder.DefineMethod(
            $"set_{effectiveColumnName}",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            null,
            [propertyType]);
        ILGenerator setIl = setMethod.GetILGenerator();
        setIl.Emit(OpCodes.Ldarg_0);
        setIl.Emit(OpCodes.Ldarg_1);
        setIl.Emit(OpCodes.Stfld, fieldBuilder);
        setIl.Emit(OpCodes.Ret);

        propertyBuilder.SetGetMethod(getMethod);
        propertyBuilder.SetSetMethod(setMethod);
        AddDataMemberAttribute(propertyBuilder, effectiveColumnName);
        if (isPrimaryKey)
        {
            propertyBuilder.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(PrimaryKeyAttribute).GetConstructor(Type.EmptyTypes)!,
                []));
            // Use bounded key types, matching schema-message defaults,
            // so SQL Server can index them.
            int? keyLength = propertyType == typeof(string) ? 450
                : propertyType == typeof(byte[]) ? 256 : null;
            if (keyLength.HasValue)
            {
                propertyBuilder.SetCustomAttribute(new CustomAttributeBuilder(
                    typeof(System.ComponentModel.DataAnnotations.MaxLengthAttribute).GetConstructor([typeof(int)])!,
                    [keyLength.Value]));
            }
        }
        if (!column.IsNullable)
        {
            propertyBuilder.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(IsRequiredAttribute).GetConstructor(Type.EmptyTypes)!,
                []));
        }
    }

    private static Type GetPropertyType(GXColumnSchema column)
    {
        Type type = Nullable.GetUnderlyingType(column.Type) ?? column.Type;
        if (column.IsNullable && type.IsValueType)
        {
            return typeof(Nullable<>).MakeGenericType(type);
        }

        return type;
    }

    private static void AddDataContractAttribute(TypeBuilder typeBuilder, string tableName)
    {
        typeBuilder.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(DataContractAttribute).GetConstructor(Type.EmptyTypes)!,
            [],
            [typeof(DataContractAttribute).GetProperty(nameof(DataContractAttribute.Name))!],
            [tableName]));
    }

    private static void AddDataMemberAttribute(PropertyBuilder propertyBuilder, string columnName)
    {
        propertyBuilder.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(DataMemberAttribute).GetConstructor(Type.EmptyTypes)!,
            [],
            [typeof(DataMemberAttribute).GetProperty(nameof(DataMemberAttribute.Name))!],
            [columnName]));
    }

    private static string CreateCacheKey(DatabaseType databaseType, string destinationTable, GXTableSchema definition, IReadOnlyList<string> transferKeys)
    {
        string columns = string.Join(
            "|",
            definition.Columns.Select(column => $"{column.Name}:{column.Type}:{column.IsNullable}"));
        string keys = string.Join(",", transferKeys.Order(StringComparer.OrdinalIgnoreCase));
        return $"{databaseType}|{destinationTable}|{keys}|{columns}";
    }

    private static string NormalizeDatabaseIdentifier(DatabaseType databaseType, string value)
    {
        return databaseType == DatabaseType.DB2 ? value.ToUpperInvariant() : value;
    }

    private static string SanitizeIdentifier(string value)
    {
        char[] chars = value.Select(static ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        string sanitized = new(chars);
        if (string.IsNullOrWhiteSpace(sanitized) || !char.IsLetter(sanitized[0]))
        {
            sanitized = "Table_" + sanitized;
        }

        return sanitized;
    }
}

