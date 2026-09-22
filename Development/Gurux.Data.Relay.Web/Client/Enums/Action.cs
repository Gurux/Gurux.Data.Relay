namespace Gurux.Data.Relay.Web.Client.Enums
{
    /// <summary>
    /// CRUD action.
    /// </summary>
    public enum CrudAction : byte
    {
        /// <summary>
        /// None
        /// </summary>
        None = 0,
        /// <summary>
        /// Create 
        /// </summary>
        Create = 0x1,
        /// <summary>
        /// Read object.
        /// </summary>
        Read = 0x2,
        /// <summary>
        /// Edit object.
        /// </summary>
        Update = 0x4,
        /// <summary>
        /// Delete object.
        /// </summary>
        Delete = 0x8,
    }
}

