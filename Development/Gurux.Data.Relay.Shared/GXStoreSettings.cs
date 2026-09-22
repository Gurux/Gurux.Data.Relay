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
using Gurux.Service.Orm.Common.Enums;

namespace Gurux.Data.Relay.Shared;

/// <summary>
/// Connection settings for the configuration store.
/// </summary>
public sealed class GXStoreSettings
{
    public DatabaseType Type { get; set; } = DatabaseType.SqLite;
    public string ConnectionString { get; set; } = string.Empty;
    public bool IncludeStackTrace { get; set; }
    public bool NotifyLogChanges { get; set; }
    public Microsoft.Extensions.Logging.LogLevel? EventLogLevel { get; set; }
    public Microsoft.Extensions.Logging.LogLevel? CommunicationLogLevel { get; set; }
}


