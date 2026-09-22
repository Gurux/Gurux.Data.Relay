namespace Gurux.Data.Relay.Configuration;

public interface IGXDataVaultConfigurationChanges
{
    /// <summary>Raised when committed Data Vault settings change.</summary>
    event Func<Task>? DataVaultConfigurationSaved;
}
