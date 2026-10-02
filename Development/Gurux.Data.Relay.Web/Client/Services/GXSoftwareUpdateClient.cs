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

using Gurux.Data.Relay.Shared;
using Gurux.UI.Components;
using Gurux.UI.Components.Enums;

namespace Gurux.Data.Relay.Web.Client.Services;

/// <summary>Shares update status between the notification and the update page.</summary>
public sealed class GXSoftwareUpdateClient(GXAdminApiClient api, IGXNotificationService notifications)
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    public GXSoftwareUpdateStatus Status { get; private set; } = new();
    public event Action? Changed;

    public async Task RefreshAsync(bool check, CancellationToken token)
    {
        await _refreshLock.WaitAsync(token);
        notifications.RemoveByKey("software-update-error");
        try
        {
            Status = await api.GetSoftwareUpdateStatusAsync(check, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            Status = Status with { Error = "Unable to retrieve software update status. Please try again later." };
        }
        finally
        {
            _refreshLock.Release();
        }
        if (!string.IsNullOrWhiteSpace(Status.Error))
            notifications.Add(new GXNotificationItem(NotificationLevel.Warning,
                Status.Error, string.Empty, "software-update-error", Closable: true));
        Changed?.Invoke();
    }
}
