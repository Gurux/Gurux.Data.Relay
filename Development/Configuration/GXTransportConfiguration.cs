using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Shared.Enums;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Gurux.Data.Relay.Shared;
using Gurux.Service.Orm.Common;

namespace Gurux.Data.Relay.Configuration;

public sealed class GXTransportConfiguration : IUnique<Guid>,
    IGXEntityMetadata
{

    public Guid Id { get; set; } = Guid.NewGuid();

    [System.Runtime.Serialization.DataMember]
    public DateTimeOffset? CreationTime { get; set; }

    [System.Runtime.Serialization.DataMember]
    public DateTimeOffset? Updated { get; set; }

    [System.Runtime.Serialization.DataMember]
    [System.ComponentModel.DataAnnotations.StringLength(36)]
    [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
    public string? ConcurrencyStamp { get; set; }
    public Guid? Database { get; set; }

    public List<GXTransportTableConfiguration> Tables { get; set; } = [];


    [JsonIgnore]
    public int? DatabaseIndex { get; set; }

    public string? Description { get; set; }

    public TransportType Type { get; set; }

    public GXTransferConfiguration? Transfer { get; set; }

    public int MaximumMessageSize { get; set; } = 4 * 1024 * 1024;

    [DefaultValue(10)]
    public int ConnectTimeoutSeconds { get; set; } = 10;

    public int AcknowledgementTimeoutSeconds { get; set; } = 30;

    public string? Host { get; set; }

    public int Port { get; set; }

    public string? Broker { get; set; }

    public string? Topic { get; set; }

    public string? AcknowledgementTopic { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    public bool UseTls { get; set; }

    public override string? ToString()
    {
        if (!string.IsNullOrEmpty(Description))
        {
            return $"{Type} ({Description})";
        }
        return base.ToString();
    }
}


