using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Web.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Gurux.Data.Relay.Web.Server.Controllers;

[ApiController]
[Route("api/settings/archive")]
public sealed class SettingsArchiveController(IGXSettingsArchiveService archives) : ControllerBase
{
    /// <summary>Export all client, server and Data Vault settings, including credentials.</summary>
    [HttpGet("export")]
    public async Task<FileContentResult> Export(CancellationToken cancellationToken)
    {
        var options = new JsonOptions();
        GXApiJsonOptions.Configure(options);
        options.JsonSerializerOptions.WriteIndented = true;
        var archive = await archives.ExportAllSettingsAsync(cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return File(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(archive, options.JsonSerializerOptions)),
            "application/json", "Gurux.Data.Relay.settings.json");
    }

    /// <summary>Replace all three modes' settings from one archive atomically.</summary>
    [HttpPost("import")]
    [Consumes("application/json")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Import([FromBody] GXSettingsArchive archive, CancellationToken cancellationToken)
    {
        try
        {
            await archives.ImportAllSettingsAsync(archive, cancellationToken);
            return NoContent();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ValidationException)
        {
            return BadRequest(ex.Message);
        }
    }
}
