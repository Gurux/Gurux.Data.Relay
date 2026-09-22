using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Gurux.Data.Relay.Web.Server.Models;

/// <summary>Values for one new row. Omitted columns use database defaults.</summary>
public sealed class GXInsertRowRequest
{
    /// <summary>Column names and scalar JSON values. Encode binary values as Base64 strings.</summary>
    [Required, MinLength(1)]
    public Dictionary<string, JsonElement> Values { get; set; } = [];
}

/// <summary>The complete primary key of one row.</summary>
public class GXRowKeyRequest
{
    /// <summary>Every primary-key column and its value, including all parts of a composite key.</summary>
    [Required, MinLength(1)]
    public Dictionary<string, JsonElement> Keys { get; set; } = [];
}

/// <summary>Changes to the non-key columns of one existing row.</summary>
public sealed class GXUpdateRowRequest : GXRowKeyRequest
{
    /// <summary>Only columns to change; omitted columns retain their existing values.</summary>
    [Required, MinLength(1)]
    public Dictionary<string, JsonElement> Values { get; set; } = [];
}

/// <summary>The number of rows changed by a successful request.</summary>
public sealed record GXRowWriteResult(int AffectedRows);
