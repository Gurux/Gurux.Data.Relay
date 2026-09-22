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

using System.Collections;

namespace Gurux.Data.Relay.Shared;

public static class GXEntityMetadata
{
    /// <summary>Applies versions returned by a successful save to the caller's graph.</summary>
    public static void ApplySaved(object saved, object target)
    {
        if (saved is IGXEntityMetadata source && target is IGXEntityMetadata destination)
        {
            destination.CreationTime = source.CreationTime;
            destination.Updated = source.Updated;
            destination.ConcurrencyStamp = source.ConcurrencyStamp;
        }
        foreach (var property in saved.GetType().GetProperties())
        {
            var current = property.GetValue(saved);
            var original = property.GetValue(target);
            if (current is IGXEntityMetadata && original is IGXEntityMetadata)
                ApplySaved(current, original);
            else if (current is IList list && original is IList originals)
            {
                for (int i = 0; i < list.Count; ++i)
                {
                    if (list[i] is not IGXEntityMetadata) continue;
                    if (i >= originals.Count) originals.Add(list[i]);
                    else if (originals[i] is IGXEntityMetadata) ApplySaved(list[i]!, originals[i]!);
                }
            }
            else if (property.CanWrite && (property.Name == "Id" || original is null && current is IList))
                property.SetValue(target, current);
        }
    }
}
