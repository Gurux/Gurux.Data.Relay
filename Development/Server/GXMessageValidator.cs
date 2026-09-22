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


using Gurux.Service.Orm.Common.Model;
using Gurux.Data.Relay.Shared.Protocol;
using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Server;

public sealed class GXMessageValidator : IGXMessageValidator
{
    public void Validate(GXDataMessage message, int maximumMessageSize)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Version != 2)
        {
            throw new InvalidOperationException($"Unsupported protocol version '{message.Version}'.");
        }

        if (message.MessageId == Guid.Empty)
        {
            throw new InvalidOperationException("MessageId is required.");
        }

        if (maximumMessageSize <= 0)
        {
            throw new InvalidOperationException("Maximum message size must be positive.");
        }

        if (message.MessageType == MessageType.Ping)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(message.Table.Name))
        {
            throw new InvalidOperationException("Table name is required.");
        }

        if (message.MessageType == MessageType.Schema)
        {
            ValidateSchemaMessage(message);
            return;
        }

        if (message.Table.Columns.Count == 0)
        {
            throw new InvalidOperationException("At least one table column is required.");
        }

        if (message.Changes.Count == 0)
        {
            throw new InvalidOperationException("At least one change is required.");
        }

        HashSet<string> columnNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (GXColumnSchema column in message.Table.Columns)
        {
            if (column.Type is null)
            {
                throw new InvalidOperationException($"Column type is required for '{column.Name}'.");
            }
            if (string.IsNullOrWhiteSpace(column.Name))
            {
                throw new InvalidOperationException("Column name is required.");
            }

            if (!columnNames.Add(column.Name))
            {
                throw new InvalidOperationException($"Duplicate column '{column.Name}' is not allowed.");
            }
        }

        foreach (GXDataChange change in message.Changes)
        {
            switch (change.Operation)
            {
                case DataOperation.Insert:
                case DataOperation.Update:
                    if (change.Values is null || change.Values.Count == 0)
                    {
                        throw new InvalidOperationException($"{change.Operation} change must include values.");
                    }
                    break;
                case DataOperation.Delete:
                    if (change.Keys is null || change.Keys.Count == 0)
                    {
                        throw new InvalidOperationException("Delete change must include keys.");
                    }
                    break;
            }
        }
    }

    private static void ValidateSchemaMessage(GXDataMessage message)
    {
        if (message.Schema is null)
        {
            throw new InvalidOperationException("Schema message must include a table schema.");
        }

        if (message.Schema.Columns.Count == 0)
        {
            throw new InvalidOperationException("Schema message must include at least one table column.");
        }

        HashSet<string> columnNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (var column in message.Schema.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.Name))
            {
                throw new InvalidOperationException("Schema column name is required.");
            }

            if (!columnNames.Add(column.Name))
            {
                throw new InvalidOperationException($"Duplicate schema column '{column.Name}' is not allowed.");
            }
        }
    }
}

