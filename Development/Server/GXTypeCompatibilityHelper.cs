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

using Gurux.Data.Relay.Enums;

namespace Gurux.Data.Relay.Server;

internal static class GXTypeCompatibilityHelper
{
    public static Type MapLogicalType(DataType dataType)
    {
        return dataType switch
        {
            DataType.Boolean => typeof(bool),
            DataType.SByte => typeof(sbyte),
            DataType.Byte => typeof(byte),
            DataType.Int16 => typeof(short),
            DataType.UInt16 => typeof(ushort),
            DataType.Int32 => typeof(int),
            DataType.UInt32 => typeof(uint),
            DataType.Int64 => typeof(long),
            DataType.UInt64 => typeof(ulong),
            DataType.Single => typeof(float),
            DataType.Double => typeof(double),
            DataType.Decimal => typeof(decimal),
            DataType.String => typeof(string),
            DataType.Guid => typeof(Guid),
            DataType.ByteArray => typeof(byte[]),
            DataType.DateTime => typeof(DateTime),
            DataType.DateTimeOffset => typeof(DateTimeOffset),
            DataType.DateOnly => typeof(DateOnly),
            DataType.TimeOnly => typeof(TimeOnly),
            _ => throw new NotSupportedException($"Logical data type '{dataType}' is not supported."),
        };
    }

    public static bool IsCompatible(Type? destinationType, Type logicalSourceType)
    {
        if (destinationType is null)
        {
            return false;
        }

        Type actualDestinationType = Nullable.GetUnderlyingType(destinationType) ?? destinationType;
        Type actualSourceType = Nullable.GetUnderlyingType(logicalSourceType) ?? logicalSourceType;
        if (actualDestinationType == actualSourceType)
        {
            return true;
        }

        if (actualDestinationType == typeof(object))
        {
            return true;
        }

        if (actualDestinationType == typeof(string))
        {
            return true;
        }

        if (IsNumericType(actualDestinationType) && IsNumericType(actualSourceType))
        {
            return true;
        }

        if (actualDestinationType == typeof(decimal) && actualSourceType == typeof(string))
        {
            return true;
        }

        if (actualDestinationType == typeof(Guid) && actualSourceType == typeof(string))
        {
            return true;
        }

        if (actualDestinationType == typeof(byte[]) && actualSourceType == typeof(Guid))
        {
            return true;
        }

        if (actualDestinationType == typeof(Guid) && actualSourceType == typeof(byte[]))
        {
            return true;
        }

        if (actualDestinationType == typeof(DateTimeOffset) && actualSourceType == typeof(DateTime))
        {
            return true;
        }

        if (actualDestinationType == typeof(DateTime) && actualSourceType == typeof(DateTimeOffset))
        {
            return true;
        }

        if (actualDestinationType == typeof(DateTime) && actualSourceType == typeof(string))
        {
            return true;
        }

        if (actualDestinationType == typeof(DateTimeOffset) && actualSourceType == typeof(string))
        {
            return true;
        }

        if (actualDestinationType == typeof(DateOnly) && actualSourceType == typeof(string))
        {
            return true;
        }

        if (actualDestinationType == typeof(TimeOnly) && actualSourceType == typeof(string))
        {
            return true;
        }

        if (actualDestinationType == typeof(bool) && IsNumericType(actualSourceType))
        {
            return true;
        }

        if (IsNumericType(actualDestinationType) && actualSourceType == typeof(bool))
        {
            return true;
        }

        return false;
    }

    private static bool IsNumericType(Type type)
    {
        return type == typeof(byte)
            || type == typeof(sbyte)
            || type == typeof(short)
            || type == typeof(ushort)
            || type == typeof(int)
            || type == typeof(uint)
            || type == typeof(long)
            || type == typeof(ulong)
            || type == typeof(float)
            || type == typeof(double)
            || type == typeof(decimal);
    }
}

