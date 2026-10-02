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

using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.Orm.Common.Model;
using System.Text.Json.Serialization;

namespace Gurux.Data.Relay.Shared;

/// <summary>Portable settings for all relay modes, without runtime state or database data.</summary>
[JsonConverter(typeof(GXSettingsArchiveConverter))]
public sealed class GXSettingsArchive
{
    [JsonRequired] public int FormatVersion { get; set; } = 2;
    [JsonRequired] public List<GXDatabase> Databases { get; set; } = [];
    [JsonRequired] public GXSettings Client { get; set; } = new() { Mode = ApplicationMode.Client };
    [JsonRequired] public GXSettings Server { get; set; } = new() { Mode = ApplicationMode.Server };
    [JsonRequired] public GXSettings DataVault { get; set; } = new() { Mode = ApplicationMode.DataVault };
    public Dictionary<Guid, List<GXTableSchema>> Schemas { get; set; } = [];
}
