namespace Gurux.Data.Relay.Configuration;

public interface IGXClientConfigurationChanges
{
    /// <summary>Raised after client settings have committed.</summary>
    event Func<Task>? ClientConfigurationSaved;
}
