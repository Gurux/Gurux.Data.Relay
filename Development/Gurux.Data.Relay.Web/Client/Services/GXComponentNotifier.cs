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
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;

namespace Gurux.Data.Relay.Web.Client.Services;

public sealed class GXComponentNotifier : IGXNotifier, IDisposable
{
    private readonly NavigationManager _navigation;
    private readonly ILogger<GXComponentNotifier> _logger;
    private readonly IGXNotificationService _notifications;
    private readonly IDisposable _navigationRegistration;
    private readonly Dictionary<(object Listener, string Method), Delegate> _listeners = new();
    private readonly List<GXMenuItem> _menuItems = new();
    private EditContext? _editContext;

    public GXComponentNotifier(NavigationManager navigation, ILogger<GXComponentNotifier> logger,
        IGXNotificationService notifications)
    {
        _navigation = navigation;
        _logger = logger;
        _notifications = notifications;
        _navigationRegistration = navigation.RegisterLocationChangingHandler(context =>
        {
            LastUrl = navigation.Uri;
            OnPageChanging?.Invoke(context);
            return ValueTask.CompletedTask;
        });
        navigation.LocationChanged += LocationChanged;
    }

    public string? LastUrl { get; private set; }
    public string? Title { get; set; }
    public event Action? OnUpdateButtons;
    public event Action<LocationChangingContext>? OnPageChanging;
    public event Action? OnPageChanged;
    public event EventHandler<FieldChangedEventArgs>? OnDirty;

    public EditContext? EditContext
    {
        get => _editContext;
        set
        {
            if (_editContext != null) _editContext.OnFieldChanged -= FieldChanged;
            _editContext = value;
            if (_editContext != null) _editContext.OnFieldChanged += FieldChanged;
        }
    }

    public void ChangePage(string page) => _navigation.NavigateTo(page);
    public void AddMenuItem(GXMenuItem menu) => _menuItems.Add(menu);
    public void Clear() => _menuItems.Clear();
    public IDisposable ProgressStart(string? message)
    {
        _logger.LogInformation("{Message}", message);
        return new GXProgressScope(() => ProgressEnd(message));
    }
    public void ProgressEnd(string? message) => _logger.LogInformation("{Message}", message);
    public void ClearStatus() => UpdateButtons();
    public void ShowInformation(string info, bool closable = false) => _logger.LogInformation("{Message}", info);
    public void ProcessError(Exception ex) => _notifications.ReportError(ex);
    public void ProcessErrors(IEnumerable<object> errors)
    {
        foreach (object error in errors)
        {
            if (error is Exception exception)
                ProcessError(exception);
            else
                _notifications.Add(new GXNotificationItem(NotificationLevel.Error,
                    error?.ToString() ?? "Component operation failed.", string.Empty, Closable: true));
        }
    }
    public void UpdateButtons() => OnUpdateButtons?.Invoke();
    public void On(object listener, string methodName, Action handler) => _listeners[(listener, methodName)] = handler;
    public void On<T1>(object listener, string methodName, Action<T1> handler) => _listeners[(listener, methodName)] = handler;
    public void RemoveListener(object listener)
    {
        foreach (var key in _listeners.Keys.Where(key => ReferenceEquals(key.Listener, listener)).ToArray())
            _listeners.Remove(key);
    }
    private void LocationChanged(object? sender, LocationChangedEventArgs args) => OnPageChanged?.Invoke();
    private void FieldChanged(object? sender, FieldChangedEventArgs args) => OnDirty?.Invoke(sender, args);
    public void Dispose()
    {
        EditContext = null;
        _navigation.LocationChanged -= LocationChanged;
        _navigationRegistration.Dispose();
    }
}


