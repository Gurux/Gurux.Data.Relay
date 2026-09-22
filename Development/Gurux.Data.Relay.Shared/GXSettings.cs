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
using System.Text.Json.Serialization;

using System.Runtime.Serialization;
using Gurux.Service.Orm.Common;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Shared.Enums;
using System.ComponentModel.DataAnnotations;

namespace Gurux.Data.Relay.Shared;

/// <summary>
/// Client, server or data vault settings exchanged with the administration API.
/// </summary>
[DataContract]
public sealed class GXSettings : IUnique<Guid>, IGXEntityMetadata
{
    [DataMember]
    public Guid Id { get; set; } = Guid.NewGuid();

    [DataMember]
    public DateTimeOffset? CreationTime { get; set; }

    [DataMember]
    public DateTimeOffset? Updated { get; set; }

    [DataMember]
    [StringLength(36)]
    [ConcurrencyCheck]
    public string? ConcurrencyStamp { get; set; }
    public List<GXTransport> Transports { get; set; } = [];
   
    /// <summary>
    /// Are settings for the client, server or datavault.
    /// </summary>
    [DataMember]
    public ApplicationMode Mode { get; set; } = ApplicationMode.Client;
   
    /// <summary>
    /// Minimum event log level.
    /// </summary>
    [DataMember]
    [JsonConverter(typeof(JsonStringEnumConverter<Microsoft.Extensions.Logging.LogLevel>))]
    public Microsoft.Extensions.Logging.LogLevel? EventLogLevel { get; set; }
   
    /// <summary>
    /// Communication log level for the client, server or data vault. 
    /// Null uses the application's default log level.
    /// </summary>
    [DataMember]
    [JsonConverter(typeof(JsonStringEnumConverter<Microsoft.Extensions.Logging.LogLevel>))]
    public Microsoft.Extensions.Logging.LogLevel? CommunicationLogLevel { get; set; }
    /// <summary>
    /// Hash algorithm used for data integrity checks. Default is SHA256.
    /// </summary>
    [DataMember]
    public HashAlgorithmType HashAlgorithm { get; set; } = HashAlgorithmType.SHA256;

    /// <summary>Enables REST access and web administration for this mode.</summary>
    [DataMember, System.ComponentModel.DefaultValue(true)]
    public bool RestEnabled { get; set; } = true;

    /// <summary>Includes this mode in Swagger when REST is enabled.</summary>
    [DataMember, System.ComponentModel.DefaultValue(true)]
    public bool SwaggerEnabled { get; set; } = true;

    /// <summary>Enables cross-origin browser access to this mode's public REST endpoints.</summary>
    [DataMember, System.ComponentModel.DefaultValue(false)]
    public bool CorsEnabled { get; set; }

    /// <summary>Allows credentials from explicitly configured origins. Cannot be combined with the wildcard origin.</summary>
    [DataMember, System.ComponentModel.DefaultValue(false)]
    public bool CorsAllowCredentials { get; set; }

    /// <summary>Allowed origins such as https://app.example.com. An empty list permits no cross-origin access.</summary>
    public List<string> CorsAllowedOrigins { get; set; } = [];

    [JsonIgnore]
    public List<GXColumnAlias> ColumnAliases { get; set; } = GXColumnAlias.Defaults();

    /// <summary>Only Data Vault exposes column aliases in settings JSON.</summary>
    [JsonPropertyName("columnAliases")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)]
    public List<GXColumnAlias>? DataVaultColumnAliases
    {
        get => Mode == ApplicationMode.DataVault ? ColumnAliases : null;
        set => ColumnAliases = value!;
    }

    [DataMember, JsonIgnore]
    public string? ColumnAliasesJson
    {
        get => JsonSerializer.Serialize(ColumnAliases);
        set => ColumnAliases = string.IsNullOrWhiteSpace(value) ? GXColumnAlias.Defaults() : JsonSerializer.Deserialize<List<GXColumnAlias>>(value) ?? GXColumnAlias.Defaults();
    }

    /// <summary>Database representation of the allowed-origin list.</summary>
    [DataMember, JsonIgnore]
    public string? CorsAllowedOriginsJson
    {
        get => JsonSerializer.Serialize(CorsAllowedOrigins);
        set => CorsAllowedOrigins = string.IsNullOrWhiteSpace(value) ? [] : JsonSerializer.Deserialize<List<string>>(value) ?? [];
    }
    /// <summary>
    /// List of databases configured for the client, server or data vault.
    /// </summary>
    public List<GXDatabase> Databases { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<GXDataVaultTableMapping>? Mappings { get; set; }
}

