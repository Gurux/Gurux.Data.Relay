using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.Orm.Common.Model;

namespace Gurux.Data.Relay.Client;

/// <summary>Validates and encodes a generated hash for its physical destination column.</summary>
public static class GXDataVaultHashStorage
{
    public static bool IsHash(DataVaultColumnRole? role) => role is DataVaultColumnRole.HashKey or
        DataVaultColumnRole.ParentHashKey or DataVaultColumnRole.LinkHashKey or DataVaultColumnRole.HashDiff;

    public static void Validate(GXColumnSchema column, HashAlgorithmType algorithm)
    {
        int bytes = algorithm switch
        {
            HashAlgorithmType.SHA256 => 32,
            HashAlgorithmType.SHA512 => 64,
            HashAlgorithmType.MD5 => 16,
            _ => throw new ArgumentException($"Unsupported hash algorithm '{algorithm}'.")
        };
        bool binary = column.Type == typeof(byte[]);
        if (!binary && column.Type != typeof(string))
            throw new ArgumentException($"Hash column '{column.Name}' must be a binary or text column; actual type is {column.Type?.Name ?? column.DbType}.");
        int required = binary ? bytes : bytes * 2;
        if (column.MaxLength > 0 && column.MaxLength < required)
            throw new ArgumentException($"Hash column '{column.Name}' is too short for {algorithm}: requires {required} {(binary ? "bytes" : "characters")}, actual maximum is {column.MaxLength}.");
    }

    public static object Encode(string hex, GXColumnSchema column, HashAlgorithmType algorithm)
    {
        Validate(column, algorithm);
        return column.Type == typeof(byte[]) ? Convert.FromHexString(hex) : hex;
    }
}
