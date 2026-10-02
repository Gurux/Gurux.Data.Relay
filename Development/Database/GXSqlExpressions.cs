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
using Gurux.Service.Orm.Common.Model;

namespace Gurux.Data.Relay.Database;

/// <summary>Builds LINQ expressions for Gurux.Service's SQL marker methods.</summary>
public static class GXSqlExpressions
{
    private static Expression Box(Expression value) => value.Type == typeof(object)
        ? value : Expression.Convert(value, typeof(object));

    public static Expression Column(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Expression.Constant(new GXColumnSchema { Name = name });
    }

    public static Expression Value(object? value) => Expression.Constant(value == DBNull.Value ? null : value, typeof(object));
    public static Expression Integer(int value) => Expression.Constant(value);
    public static Expression Equal(Expression left, Expression right) => Expression.Equal(Box(left), Box(right));
    public static Expression NotEqual(Expression left, Expression right) => Expression.NotEqual(Box(left), Box(right));
    public static Expression IsNull(Expression value, bool negate = false) => negate
        ? NotEqual(value, Value(null)) : Equal(value, Value(null));
    public static Expression And(params Expression[] values) => values.Length == 0
        ? Expression.Constant(true) : values.Skip(1).Aggregate(values[0], Expression.AndAlso);
    public static Expression Or(params Expression[] values) => values.Length == 0
        ? Expression.Constant(false) : values.Skip(1).Aggregate(values[0], Expression.OrElse);

    public static Expression Greater(Expression left, Expression right) =>
        Expression.Call(typeof(GXSql), nameof(GXSql.Greater), [typeof(object)], Box(left), Box(right));
    public static Expression CountExpression(Expression? value = null) =>
        Expression.Call(typeof(GXSql), nameof(GXSql.Count), Type.EmptyTypes,
            Box(value ?? Expression.Property(null, typeof(GXSql), nameof(GXSql.One))));
    public static Expression CountDistinct(Expression value) =>
        Expression.Call(typeof(GXSql), nameof(GXSql.DistinctCount), Type.EmptyTypes, Box(value));
    public static Expression MaxExpression(Expression value) =>
        Expression.Call(typeof(GXSql), nameof(GXSql.Max), Type.EmptyTypes, Box(value));
    public static Expression MinExpression(Expression value) =>
        Expression.Call(typeof(GXSql), nameof(GXSql.Min), Type.EmptyTypes, Box(value));
    public static Expression Average(Expression value) =>
        Expression.Call(typeof(GXSql), nameof(GXSql.Avg), Type.EmptyTypes, Box(value));
    public static Expression ExistsExpression(GXSelectArgs query)
    {
        Expression<Func<bool>> expression = () => GXSql.Exists(query);
        return expression.Body;
    }
}
