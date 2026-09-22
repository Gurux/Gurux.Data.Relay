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

namespace Gurux.Data.Relay.Shared;

public sealed class GXDataSourceRequest
{
    public Guid InstanceId { get; set; }
    public string Provider { get; set; } = "modbus-tcp";
    public bool Enabled { get; set; } = true;
    public bool Pull { get; set; } = true;
    public bool Streaming { get; set; }
    public GXSchedule Schedule { get; set; } = new();
    public List<Guid> RouteIds { get; set; } = [];
    public int DeliveryAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 1;
    public string SettingsJson { get; set; } = "{}";
    /// <summary>Replacement or explicit removal requests. These are never returned by GET endpoints.</summary>
    public List<GXDataSourceSecretChange> SecretChanges { get; set; } = [];
}
