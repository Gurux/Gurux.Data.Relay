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

using System.Linq.Expressions;
using Gurux.Service.Orm;

namespace Gurux.Data.Relay.Database;

/// <summary>Column projections composed dynamically by Relay.</summary>
public static class GXColumnCollectionExtensions
{
    public static void Add(this GXColumnCollection columns, Expression expression, string? alias = null)
    {
        var parameter = Expression.Parameter(typeof(object), "row");
        var body = expression.Type == typeof(object) ? expression : Expression.Convert(expression, typeof(object));
        var projection = Expression.Lambda<Func<object, object>>(body, parameter);
        if (alias == null)
            columns.Add(projection);
        else
            columns.Add(projection, Expression.Lambda<Func<object, object>>(Expression.Constant(alias, typeof(object)), parameter));
    }

    public static void AddRange(this GXColumnCollection columns, IEnumerable<(Expression Expression, string? Alias)> projections)
    {
        foreach (var projection in projections)
            columns.Add(projection.Expression, projection.Alias);
    }
}
