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

namespace Gurux.Data.Relay.Shared.Enums;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<DataVaultObjectType>))]
public enum DataVaultObjectType
{
    /// <summary>
    /// Hub table, which contains the unique business keys of the source system.
    /// </summary>
    Hub,
    /// <summary>
    /// Link table, which contains the relationships between the hubs.
    /// </summary>
    Link,
    /// <summary>
    /// Satellite table, which contains the descriptive attributes of the hubs and links.
    /// </summary>
    Satellite,
    /// <summary>
    /// Reference table, which contains the reference data used by the hubs and links.
    /// </summary>
    Reference,
    /// <summary>
    /// Staging table, which is used to stage the data before it is loaded into the data vault.
    /// </summary>
    Staging,
    /// <summary>
    /// Information mart table, which is used to store the data for reporting and analysis.
    /// </summary>
    InformationMart,
}

