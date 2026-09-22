namespace Gurux.Data.Relay.Configuration;

public interface IGXServerConfigurationChanges
{
    /// <summary>Raised after server settings have committed. Handlers apply runtime changes before save returns.</summary>
    event Func<Task>? ServerConfigurationSaved;
}

