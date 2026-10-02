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

/// <summary>
/// Audit fields and the version read by the caller of a save operation.
/// </summary>
public interface IGXEntityMetadata
{
    /// <summary>
    /// Gets or sets the date and time when the item was created.
    /// </summary>
    DateTimeOffset? CreationTime { get; set; }
    /// <summary>
    /// Gets or sets the date and time when the item was last updated.
    /// </summary>
    DateTimeOffset? Updated { get; set; }
    /// <summary>
    /// Gets or sets the concurrency stamp for the item, which is used to detect concurrent updates.
    /// </summary>
    string? ConcurrencyStamp { get; set; }
}
