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

namespace Gurux.Data.Relay.Shared;

/// <summary>Data Vault mapping details and its owning database.</summary>
public sealed class GXDataVaultMappingDetails
{
    /// <summary>Owning database identifier.</summary>
    public Guid Database { get; set; }
    /// <summary>The requested mapping.</summary>
    public GXDataVaultTableMapping? Mapping { get; set; }
    /// <summary>Description of the mapping's object type.</summary>
    public string? Description { get; set; }
}

/// <summary>The result of deleting event log entries.</summary>
public sealed class GXDeleteEventsResult
{
    /// <summary>Number of deleted entries.</summary>
    public int DeletedRows { get; set; }
}

/// <summary>The result of sending table schemas.</summary>
public sealed class GXSendSchemasResult
{
    /// <summary>Number of sent schemas.</summary>
    public int SentSchemas { get; set; }
}

/// <summary>Problem details returned by API error and validation responses.</summary>
public sealed class GXApiProblemDetails
{
    /// <summary>Short description of the error.</summary>
    public string? Title { get; set; }
    /// <summary>Detailed error description.</summary>
    public string? Detail { get; set; }
    /// <summary>Validation messages grouped by field name.</summary>
    public Dictionary<string, string[]>? Errors { get; set; }
}
