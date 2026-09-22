using System.Text.Json;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Shared;

namespace Gurux.Data.Relay.Web.Client.Services;

/// <summary>
/// Keeps transport edits separate from loaded settings until the user saves.
/// </summary>
public sealed class GXTransportEditSession
{
    private readonly GXTable? _table;
    private readonly GXSettings? _settings;
    private readonly int? _transportIndex;

    public GXTransportEditSession(GXSettings settings, int? transportIndex)
    {
        if (transportIndex is int index && (index < 0 || index >= settings.Transports.Count))
            throw new InvalidOperationException("The selected transport was not found.");
        _settings = Clone(settings);
        _transportIndex = transportIndex;
        Draft = transportIndex is int selected ? Clone(settings.Transports[selected]) : new GXTransport();
        Draft.Transfer ??= new();
    }

    public GXSettings CreateSaveSettings(bool delete = false)
    {
        GXSettings settings = Clone(_settings ?? throw new InvalidOperationException("Root settings are required."));
        if (delete)
        {
            if (_transportIndex is not int removed) throw new InvalidOperationException("A new transport cannot be deleted.");
            settings.Transports.RemoveAt(removed);
        }
        else
        {
            GXTransport transport = CreateTransport();
            if (_transportIndex is int index) settings.Transports[index] = transport;
            else settings.Transports.Add(transport);
        }
        return settings;
    }

    public GXTransportEditSession(GXTable table, int? transportIndex)
    {
        if (transportIndex is int index && (index < 0 || index >= table.Transports.Count))
            throw new InvalidOperationException("The selected transport was not found.");
        _table = Clone(table);
        _transportIndex = transportIndex;
        Draft = transportIndex is int selected ? Clone(table.Transports[selected]) : new GXTransport();
        Draft.Transfer ??= new();
    }
    public GXTransport Draft { get; }
    public string TableName => _table?.Name ?? string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool IsNew => _transportIndex == null;

    public GXTable CreateSaveTable()
    {
        GXTable table = Clone(_table!);
        GXTransport transport = CreateTransport();
        if (_transportIndex is int index) table.Transports[index] = transport;
        else table.Transports.Add(transport);
        return table;
    }

    private GXTransport CreateTransport()
    {
        GXTransport transport = Clone(Draft);
        if (transport.Type == TransportType.Tcp)
        {
            transport.Broker = null;
            transport.Topic = null;
            transport.AcknowledgementTopic = null;
            transport.Username = null;
            transport.Password = null;
            transport.UseTls = false;
        }
        else
        {
            transport.Host = null;
            if (IsNew || !string.IsNullOrEmpty(Password))
                transport.Password = Password;
        }
        return transport;
    }

    public GXTable CreateDeleteTable()
    {
        if (_transportIndex is not int index)
            throw new InvalidOperationException("A new transport cannot be deleted.");
        GXTable table = Clone(_table!);
        table.Transports.RemoveAt(index);
        return table;
    }

    private static T Clone<T>(T value)
        => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}



