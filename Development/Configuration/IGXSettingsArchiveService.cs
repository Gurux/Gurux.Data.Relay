using Gurux.Data.Relay.Shared;

namespace Gurux.Data.Relay.Configuration;

public interface IGXSettingsArchiveService
{
    Task<GXSettingsArchive> ExportAllSettingsAsync(CancellationToken cancellationToken);
    Task ImportAllSettingsAsync(GXSettingsArchive archive, CancellationToken cancellationToken);
}
