using Gurux.UI.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;

namespace Gurux.Data.Relay.Web.Client.Services;

public sealed class GXComponentNotifier : IGXNotifier, IDisposable
{
    private readonly NavigationManager _navigation;
    private readonly ILogger<GXComponentNotifier> _logger;
    private readonly IDisposable _navigationRegistration;
    private readonly Dictionary<(object Listener, string Method), Delegate> _listeners = new();
    private readonly List<GXMenuItem> _menuItems = new();
    private EditContext? _editContext;

    public GXComponentNotifier(NavigationManager navigation, ILogger<GXComponentNotifier> logger)
    {
        _navigation = navigation;
        _logger = logger;
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
    public void ProcessError(Exception ex) => _logger.LogError(ex, "Component operation failed.");
    public void ProcessErrors(IEnumerable<object> errors)
    {
        foreach (object error in errors) _logger.LogError("{Error}", error);
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


