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

/// <summary>Predicates composed dynamically by Relay.</summary>
public static class GXWhereCollectionExtensions
{
    public static void And(this GXWhereCollection where, Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        where.And(Expression.Lambda<Func<object, bool>>(expression, Expression.Parameter(typeof(object), "row")));
    }

    public static GXSelectArgs Filter(this GXSelectArgs query, Expression predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        query.Where.Set(predicate);
        return query;
    }

    public static GXUpdateArgs Filter(this GXUpdateArgs query, Expression predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        query.Where.Set(predicate);
        return query;
    }

    public static GXSelectArgs WithDistinct(this GXSelectArgs query)
    {
        query.Distinct = true;
        return query;
    }

    public static void Set(this GXWhereCollection where, Expression? expression)
    {
        where.Clear();
        if (expression == null)
            return;
        var body = expression.Type == typeof(bool) ? expression : Expression.Convert(expression, typeof(bool));
        where.And(Expression.Lambda<Func<object, bool>>(body, Expression.Parameter(typeof(object), "row")));
    }
}
