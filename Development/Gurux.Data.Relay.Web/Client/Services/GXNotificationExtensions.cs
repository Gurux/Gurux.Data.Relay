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

using Gurux.UI.Components;
using Gurux.UI.Components.Enums;

namespace Gurux.Data.Relay.Web.Client.Services;

internal static class GXNotificationExtensions
{
    // Keep state used by the view while displaying the message through the shared service.
    public static void SetMessage(this IGXNotificationService notifications, ref string? state,
        string? message, string key, NotificationLevel level)
    {
        if (state == message && (string.IsNullOrWhiteSpace(message) ||
            notifications.Notifications.Any(item => item.Key == key && item.Title == message && item.Level == level))) return;
        state = message;
        notifications.RemoveByKey(key);
        if (!string.IsNullOrWhiteSpace(message))
            notifications.Add(new GXNotificationItem(level, message, string.Empty, key, Closable: true));
    }
}
