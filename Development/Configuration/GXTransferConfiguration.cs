using Gurux.Data.Relay.Shared;

namespace Gurux.Data.Relay.Configuration;

public sealed class GXTransferConfiguration : Gurux.Service.Orm.Common.IUnique<Guid>,
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

    public int BatchSize { get; set; } = 1000;

    public int RetryCount { get; set; } = 5;

    public int RetryDelaySeconds { get; set; } = 10;

    public int PingAfterSeconds { get; set; } = 300;

    public bool AllowConcurrentRuns { get; set; }
}

