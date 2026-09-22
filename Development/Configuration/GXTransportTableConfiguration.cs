using Gurux.Data.Relay.Shared;

namespace Gurux.Data.Relay.Configuration;

/// <summary>A source table selection, or an incoming source mapped to a destination table.</summary>
public sealed class GXTransportTableConfiguration : Gurux.Service.Orm.Common.IUnique<Guid>,
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

    public Guid DatabaseId { get; set; }
    public Guid? TableId { get; set; }
    public string Table { get; set; } = string.Empty;
    public string? Source { get; set; }
}

