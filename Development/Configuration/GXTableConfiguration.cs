using Gurux.Data.Relay.Shared;

namespace Gurux.Data.Relay.Configuration;

public sealed class GXTableConfiguration : Gurux.Service.Orm.Common.IUnique<Guid>, IGXEntityMetadata
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
    public Guid Database { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<string> Columns { get; set; } = [];

    public List<string> Keys { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public List<GXTransportConfiguration> Transports { get; set; } = [];


    public bool DeleteSourceRowsAfterTransfer { get; set; }

    public string? IncrementalColumn { get; set; }
    [System.Runtime.Serialization.DataMember]
    [System.ComponentModel.DataAnnotations.StringLength(255)]
    public string? RecordSource { get; set; }

    public GXChangeTrackingConfiguration ChangeTracking { get; set; } = new();

    public GXSchedule? Schedule { get; set; }

    /// <summary>
    /// Runtime-only transfer timestamp populated from client state.
    /// </summary>
    public DateTimeOffset? LastTransferred { get; set; }

    /// <summary>Next scheduled execution, populated from runtime state for the web API.</summary>
    public DateTimeOffset? NextTransferTime { get; set; }

    public override string? ToString()
    {
        if (!string.IsNullOrEmpty(Name))
        {
            return Name;
        }
        return base.ToString();
    }
}



