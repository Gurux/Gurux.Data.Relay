namespace Gurux.Data.Relay.Web.Client.Services;

/// <summary>The settings were committed, but applying them to the running server failed.</summary>
public sealed class GXSettingsActivationException(string message) : InvalidOperationException(message);
