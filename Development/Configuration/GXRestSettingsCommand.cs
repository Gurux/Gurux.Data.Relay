using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Configuration;

/// <summary>Updates persisted REST availability without starting relay transports.</summary>
public static class GXRestSettingsCommand
{
    /// <summary>Changes only REST availability in an existing mode configuration.</summary>
    public static async Task ExecuteAsync(
        IGXConfigurationService service, 
        ApplicationMode mode, bool enabled, 
        CancellationToken cancellationToken)
    {
        switch (mode)
        {
            case ApplicationMode.Client:
                var client = await service.LoadClientAsync(cancellationToken) ?? throw Missing(mode);
                client.RestEnabled = enabled;
                await service.SaveClientAsync(client, cancellationToken);
                break;
            case ApplicationMode.Server:
                var server = await service.LoadServerAsync(cancellationToken) ?? throw Missing(mode);
                server.RestEnabled = enabled;
                await service.SaveServerAsync(server, cancellationToken);
                break;
            case ApplicationMode.DataVault:
                var vault = await service.LoadDataVaultAsync(cancellationToken) ?? throw Missing(mode);
                vault.RestEnabled = enabled;
                await service.SaveDataVaultAsync(vault, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    private static InvalidOperationException Missing(ApplicationMode mode) => new($"No {mode} configuration exists. Configure the mode first.");
}
