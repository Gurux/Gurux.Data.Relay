using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Database;
using Gurux.Data.Relay.Realtime;
using Gurux.Data.Relay.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Gurux.Data.Relay.Web.Server.Controllers;

[ApiController]
[Route("api/update")]
public sealed class UpdateController(
    GXConfigurationStoreSettings settings,
    IGXDatabaseConnectionFactory connectionFactory) : ControllerBase
{
    /// <summary>
    /// List configuration tables with pending schema changes.
    /// </summary>
    [HttpGet]
    public Task<List<GXConfigurationTableChange>> GetChanges(CancellationToken cancellationToken)
        => GXConfigurationTableUpdater.GetChangesAsync(settings, connectionFactory, cancellationToken);

    /// <summary>
    /// Update the configuration database schema.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Update(CancellationToken cancellationToken)
    {
        await GXConfigurationTableUpdater.UpdateAsync(settings, connectionFactory, cancellationToken);
        HttpContext?.RequestServices.GetService<IGXRelayChangePublisher>()?
            .Publish(new(null, GXRelayChangeKind.Store));
        return NoContent();
    }
}
