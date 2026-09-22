using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Shared;
using Gurux.Service.Orm.Common;

namespace Gurux.Data.Relay.Configuration;

public sealed class GXChangeTrackingConfiguration : IUnique<Guid>,
    IGXEntityMetadata
{

    public Guid Id { get; set; }

    [System.Runtime.Serialization.DataMember]
    public DateTimeOffset? CreationTime { get; set; }

    [System.Runtime.Serialization.DataMember]
    public DateTimeOffset? Updated { get; set; }

    [System.Runtime.Serialization.DataMember]
    [System.ComponentModel.DataAnnotations.StringLength(36)]
    [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
    public string? ConcurrencyStamp { get; set; }

    public ChangeTrackingType Type { get; set; } = ChangeTrackingType.None;

    public string? CreatedColumn { get; set; }

    public string? UpdatedColumn { get; set; }

    public string? DeletedColumn { get; set; }

    public string? Column { get; set; }

    /// <summary>Comma-separated source columns to hash; empty means all transferred columns.</summary>
    [System.Runtime.Serialization.DataMember]
    public string? HashColumns { get; set; }
}

