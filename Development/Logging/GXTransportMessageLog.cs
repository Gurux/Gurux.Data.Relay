using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Gurux.Service.Orm.Common;
using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Shared;

namespace Gurux.Data.Relay.Log;

public sealed class GXTransportMessageLog : IUnique<long>, 
    IGXEntityMetadata
{
    [AutoIncrement]
    public long Id { get; set; }

    [System.Runtime.Serialization.DataMember]
    public DateTimeOffset? CreationTime { get; set; }

    [System.Runtime.Serialization.DataMember]
    public DateTimeOffset? Updated { get; set; }

    [System.Runtime.Serialization.DataMember]
    [System.ComponentModel.DataAnnotations.StringLength(36)]
    [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
    public string? ConcurrencyStamp { get; set; }

    [System.Runtime.Serialization.DataMember, System.Text.Json.Serialization.JsonIgnore,
     ForeignKey(typeof(GXSettings), OnDelete = Gurux.Service.Orm.Common.Enums.ForeignKeyDelete.Cascade)]
    public Guid Settings { get; set; }

    public DateTime? Timestamp { get; set; }

    public LogLevel Level { get; set; }

    public ApplicationMode Mode { get; set; }

    public TransportMessageDirection Direction { get; set; }

    public TransportType TransportType { get; set; }

    public Guid? MessageId { get; set; }

    public MessageType? MessageType { get; set; }

    public AcknowledgementStatus? AcknowledgementStatus { get; set; }

    public string? Endpoint { get; set; }

    public string? Message { get; set; }
}

