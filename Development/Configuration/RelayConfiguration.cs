using Gurux.Service.Orm.Common;
using Gurux.Data.Relay.Enums;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Configuration;
/// <summary>
/// Configuration for the relay application.
/// </summary>
[DataContract]
internal sealed class RelayConfiguration : IUnique<Guid>, IGXEntityMetadata
{

    /// <summary>
    /// Unique identifier of the configuration record.
    /// </summary>
    [DataMember, DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [System.Runtime.Serialization.DataMember]
    public DateTimeOffset? CreationTime { get; set; }

    [System.Runtime.Serialization.DataMember]
    public DateTimeOffset? Updated { get; set; }

    [System.Runtime.Serialization.DataMember]
    [System.ComponentModel.DataAnnotations.StringLength(36)]
    [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
    public string? ConcurrencyStamp { get; set; }

    [DataMember]
    public ApplicationMode Mode { get; set; }

    /// <summary>
    /// Configuration graph loaded through the reference tables.
    /// </summary>
    [IgnoreDataMember]
    public GXSettings? Payload { get; set; }

    [DataMember(Name = "Payload"), Gurux.Service.Orm.Common.ForeignKey(typeof(GXSettings), OnDelete = Gurux.Service.Orm.Common.Enums.ForeignKeyDelete.Cascade)]
    public Guid PayloadId { get; set; }

}



