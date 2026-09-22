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

/// <summary>
/// Defines the aggregation operation applied to a column.
/// </summary>
public enum Aggregation
{
    /// <summary>
    /// No aggregation is applied.
    /// </summary>
    None,

    /// <summary>
    /// Counts the number of non-null values.
    /// </summary>
    Count,

    /// <summary>
    /// Counts the number of distinct non-null values.
    /// </summary>
    CountDistinct,

    /// <summary>
    /// Returns the minimum value.
    /// </summary>
    Min,

    /// <summary>
    /// Returns the maximum value.
    /// </summary>
    Max,

    /// <summary>
    /// Returns the sum of the values.
    /// </summary>
    Sum,

    /// <summary>
    /// Returns the average of the values.
    /// </summary>
    Average
}

