namespace Gurux.Data.Relay.Web.Server.Models;

public sealed class GXEventQuery
{
    public int Top { get; set; } = 50;

    public LogLevel? LogLevel { get; set; }

    public int? DatabaseIndex { get; set; }
}

