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

using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Shared;

namespace Gurux.Data.Relay.Configuration;

public abstract class GXModeConfiguration : IGXEntityMetadata, System.ComponentModel.DataAnnotations.IValidatableObject
{
    [System.Runtime.Serialization.DataMember]
    public DateTimeOffset? CreationTime { get; set; }

    [System.Runtime.Serialization.DataMember]
    public DateTimeOffset? Updated { get; set; }

    [System.Runtime.Serialization.DataMember]
    [System.ComponentModel.DataAnnotations.StringLength(36)]
    [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
    public string? ConcurrencyStamp { get; set; }

    public ApplicationMode Mode { get; init; }

    public LogLevel? EventLogLevel { get; set; }

    public LogLevel? CommunicationLogLevel { get; set; }

    public HashAlgorithmType HashAlgorithm { get; set; } = HashAlgorithmType.SHA256;

    /// <summary>Enables REST access and web administration for this mode.</summary>
    public bool RestEnabled { get; set; } = true;

    /// <summary>Includes this mode in Swagger when REST is enabled.</summary>
    public bool SwaggerEnabled { get; set; } = true;

    /// <summary>Enables cross-origin browser access to the public REST API.</summary>
    public bool CorsEnabled { get; set; }

    /// <summary>Allows browser credentials for explicitly allowed origins.</summary>
    public bool CorsAllowCredentials { get; set; }

    /// <summary>Allowed HTTP(S) origins. The wildcard cannot be used with credentials.</summary>
    public List<string> CorsAllowedOrigins { get; set; } = [];

    /// <inheritdoc/>
    public virtual IEnumerable<System.ComponentModel.DataAnnotations.ValidationResult> Validate(System.ComponentModel.DataAnnotations.ValidationContext validationContext)
    {
        if (CorsAllowedOrigins == null)
        {
            yield return new("CorsAllowedOrigins must be an array.", [nameof(CorsAllowedOrigins)]);
            yield break;
        }
        if (CorsAllowCredentials && CorsAllowedOrigins.Contains("*"))
            yield return new("Wildcard CORS origins cannot be combined with credentials.", [nameof(CorsAllowedOrigins), nameof(CorsAllowCredentials)]);
        foreach (string origin in CorsAllowedOrigins)
        {
            if (origin == "*") continue;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
                !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                origin.EndsWith('/') || origin != origin.Trim())
                yield return new("CORS origins must be HTTP(S) origins without credentials, paths, trailing slashes, queries or fragments.", [nameof(CorsAllowedOrigins)]);
        }
    }
}

