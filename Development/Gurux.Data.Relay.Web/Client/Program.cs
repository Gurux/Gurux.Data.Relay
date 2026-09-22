using Gurux.Data.Relay.Web.Client;
using Gurux.Data.Relay.Web.Client.Services;
using Gurux.Data.Relay.Shared;
using Gurux.UI.Components;
using Gurux.UI.Components.Help;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<GXAdminApiClient>();
builder.Services.AddScoped<IGXDataSourceAdminApi>(provider =>
    provider.GetRequiredService<GXAdminApiClient>());
builder.Services.AddScoped<IGXProgress, GXProgressService>();
builder.Services.AddScoped<IGXTopMenu, GXTopMenuService>();
builder.Services.AddScoped<GXRealtimeClient>();
builder.Services.AddScoped<IGXNotifier, GXComponentNotifier>();
builder.Services.AddScoped<IGXLocalStorage, GXLocalStorage>();
builder.Services.AddScoped<GXHelpService>(sp =>
{
    return new GXHelpService
    {
        BaseAddress = "/",
        HiddenUrls = ["/"]
    };
});
await builder.Build().RunAsync();

