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

namespace Gurux.Data.Relay.Server;

internal static class GXDeleteMarkerHelper
{
    public static object GetSoftDeleteMarkerValue(Type? targetType)
    {
        Type actualType = Nullable.GetUnderlyingType(targetType ?? typeof(DateTimeOffset)) ?? targetType ?? typeof(DateTimeOffset);
        if (actualType == typeof(DateTimeOffset))
        {
            return DateTimeOffset.UtcNow;
        }
        if (actualType == typeof(DateTime))
        {
            return DateTime.UtcNow;
        }
        if (actualType == typeof(DateOnly))
        {
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }
        if (actualType == typeof(string))
        {
            return DateTimeOffset.UtcNow.ToString("O");
        }
        if (actualType == typeof(bool))
        {
            return true;
        }

        throw new InvalidOperationException($"Soft delete column type '{actualType.Name}' is not supported.");
    }
}
