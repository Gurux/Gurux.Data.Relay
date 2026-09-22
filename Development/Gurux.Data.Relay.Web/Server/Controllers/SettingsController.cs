using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Web.Server.Models;
using Gurux.Data.Relay.Web.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Gurux.Data.Relay.Web.Server.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly IGXSettingsService _settingsService;
    private readonly GXDataSourceSettingsService _dataSources;

    public SettingsController(
        IGXSettingsService settingsService, GXDataSourceSettingsService dataSources)
    {
        _settingsService = settingsService;
        _dataSources = dataSources;
    }

    /// <summary>Read the configuration-store connection settings.</summary>
    [HttpGet]
    public async Task<ActionResult<GXConfigurationStoreSettings>> Get(CancellationToken cancellationToken)
    {
        GXConfigurationStoreSettings settings = await _settingsService.GetAsync(cancellationToken);
        return Ok(settings);
    }

    /// <summary>Save the configuration-store connection settings.</summary>
    [HttpPut]
    public async Task<IActionResult> Save([FromBody] GXConfigurationStoreSettings settings, CancellationToken cancellationToken)
    {
        await _settingsService.SaveAsync(settings, cancellationToken);
        return NoContent();
    }

    /// <summary>Test the configuration-store database connection.</summary>
    [HttpPost("test")]
    public async Task<IActionResult> Test([FromBody] GXStoreSettingsTestRequest request, CancellationToken cancellationToken)
    {
        await _settingsService.TestAsync(request.Settings, cancellationToken);

        return Ok(new { Succeeded = true });
    }

    /// <summary>Read registered data-source providers and configured source instances.</summary>
    [HttpGet("data-sources")]
    public async Task<ActionResult<GXDataSourceOverview>> GetDataSources(CancellationToken cancellationToken)
        => Ok(await _dataSources.GetOverviewAsync(cancellationToken));

    [HttpPost("data-sources")]
    public async Task<ActionResult<GXDataSourceRequest>> CreateDataSource([FromBody] GXDataSourceRequest request, CancellationToken cancellationToken)
    {
        if (request.InstanceId == Guid.Empty) request.InstanceId = Guid.NewGuid();
        await _dataSources.SaveAsync(ToStored(request), cancellationToken);
        request.SecretChanges = [];
        return CreatedAtAction(nameof(GetDataSources), new { id = request.InstanceId }, request);
    }

    [HttpPut("data-sources/{id:guid}")]
    public async Task<ActionResult<GXDataSourceRequest>> UpdateDataSource(Guid id, [FromBody] GXDataSourceRequest request, CancellationToken cancellationToken)
    {
        if (request.InstanceId != Guid.Empty && request.InstanceId != id) return BadRequest("Data-source ID cannot be changed.");
        request.InstanceId = id;
        await _dataSources.SaveAsync(ToStored(request), cancellationToken);
        request.SecretChanges = [];
        return Ok(request);
    }

    [HttpDelete("data-sources/{id:guid}")]
    public async Task<IActionResult> DeleteDataSource(Guid id, CancellationToken cancellationToken)
    {
        await _dataSources.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
    private static Gurux.Data.Relay.Sources.GXStoredDataSource ToStored(GXDataSourceRequest request) => new(new()
    { InstanceId = request.InstanceId, Provider = request.Provider, Enabled = request.Enabled, Pull = request.Pull, Streaming = request.Streaming,
      Schedule = request.Schedule, RouteIds = request.RouteIds, DeliveryAttempts = request.DeliveryAttempts, RetryDelaySeconds = request.RetryDelaySeconds }, request.SettingsJson, new Dictionary<string, string>())
    { SecretChanges = request.SecretChanges };
}

