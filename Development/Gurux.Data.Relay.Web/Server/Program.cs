//
// --------------------------------------------------------------------------
//  Gurux Ltd
// 
//
//
// Filename:        $HeadURL$
//
// Version:         $Revision$,
//                  $Date$
//                  $Author$
//
// Copyright (c) Gurux Ltd
//
//---------------------------------------------------------------------------
//
//  DESCRIPTION
//
// This file is a part of Gurux Device Framework.
//
// Gurux Device Framework is Open Source software; you can redistribute it
// and/or modify it under the terms of the GNU General Public License 
// as published by the Free Software Foundation; version 2 of the License.
// Gurux Device Framework is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of 
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. 
// See the GNU General Public License for more details.
//
// This code is licensed under the GNU General Public License v2. 
// Full text may be retrieved at http://www.gnu.org/licenses/gpl-2.0.txt
//---------------------------------------------------------------------------

using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Web.Server.Services;
using Gurux.Data.Relay.Sources;
using Gurux.Data.Relay.Log;
using Gurux.Data.Relay.Database;
using Gurux.Data.Relay.Client;
using Gurux.Data.Relay.Server;
using Gurux.Data.Relay.Transport;
using Gurux.Data.Relay.Shared.Client;
using Gurux.Data.Relay.Realtime;
using Gurux.Data.Relay.Hosting;
using Gurux.Data.Relay.Web.Server.Mcp;

public partial class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddRelayDataSources();

        // Add services to the container.

        builder.Services
            .AddControllersWithViews(options => options.Filters.Add<GXConcurrencyExceptionFilter>())
            .AddJsonOptions(GXApiJsonOptions.Configure);
        builder.Services.AddRazorPages();
        builder.Services.AddRelayOpenApi();
        builder.Services.AddRelayRestAccess();
        builder.Services.AddSingleton<GXDataVaultDiscoveryService>();
        builder.Services.AddSingleton<IGXDataVaultWriteStore, GXDataVaultWriteStore>();
        builder.Services.AddSingleton<GXDataVaultWriteService>();
        builder.Services.AddSingleton<GXTableDataReader>();
        builder.Services.AddMcpServer().WithHttpTransport().WithTools<GXDataVaultMcpTools>();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<GXRelayChangeFeed>();
        builder.Services.AddSingleton<IGXRelayChangePublisher>(sp => sp.GetRequiredService<GXRelayChangeFeed>());
        builder.Services.AddSingleton<GXEditRegistry>();
        builder.Services.AddHostedService<GXRelayNotificationService>();
        builder.Services.AddHttpClient(GXSoftwareUpdateService.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Gurux.Data.Relay");
            client.Timeout = TimeSpan.FromSeconds(30);
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });
        builder.Services.AddSingleton<GXSoftwareUpdateService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<GXSoftwareUpdateService>());

        // ASP.NET arguments belong to the web host; all configured relay modes run alongside it.
        string? settingsFilePath = builder.Configuration["settings-file"];
        if (settingsFilePath is not null && string.IsNullOrWhiteSpace(settingsFilePath))
        {
            throw new ArgumentException("--settings-file value cannot be empty.");
        }
        GXCommandLineOptions commandLine = new() { WebHost = true, SettingsFilePath = settingsFilePath };

        GXConfigurationStoreSettings configurationStoreSettings = await GXConfigurationStoreSettingsProvider.EnsureCreatedAsync(
            builder.Environment,
            _ => string.Empty,
            _ => { },
            CancellationToken.None,
            commandLine.SettingsFilePath);

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });

        builder.Services.AddSingleton(configurationStoreSettings);
        builder.Services.AddSingleton(commandLine);
        builder.Services.AddSingleton<IGXConfigurationService, GXDatabaseConfigurationService>();
        builder.Services.AddSingleton<IGXDatabaseCatalogService>(services =>
            (IGXDatabaseCatalogService)services.GetRequiredService<IGXConfigurationService>());
        builder.Services.AddSingleton<IGXSettingsArchiveService>(services =>
            (IGXSettingsArchiveService)services.GetRequiredService<IGXConfigurationService>());
        builder.Services.AddSingleton<IGXDataVaultRuntimeStateStore>(services =>
            (IGXDataVaultRuntimeStateStore)services.GetRequiredService<IGXConfigurationService>());
        builder.Services.AddSingleton<IGXDatabaseConnectionFactory, GXDatabaseConnectionFactory>();
        builder.Services.AddSingleton<IGXDatabaseConnectionTestService, GXDatabaseConnectionTestService>();
        builder.Services.AddSingleton<IGXDatabaseMetadataService, GXDatabaseMetadataService>();
        builder.Services.AddSingleton<IGXEventLogService, GXEventLogService>();
        builder.Services.AddSingleton<IGXTransportMessageLogService, GXTransportMessageLogService>();
        builder.Services.AddSingleton<ILoggerProvider, GXEventLogLoggerProvider>();
        builder.Services.AddSingleton<IClientSchemaSender, GXClientSchemaSender>();
        builder.Services.AddSingleton<GXClientTableActions>();
        builder.Services.AddSingleton<IClientBatchSender, GXClientBatchSender>();
        builder.Services.AddSingleton<IDatabaseChangeNotifierFactory, GXDatabaseChangeNotifierFactory>();
        builder.Services.AddSingleton<GXTransferScheduler>();
        builder.Services.AddSingleton<ITransferScheduler>(services => services.GetRequiredService<GXTransferScheduler>());
        builder.Services.AddSingleton<IDestinationTableService, GXDestinationTableService>();
        builder.Services.AddSingleton<IDataWriterService, GXDataWriterService>();
        builder.Services.AddSingleton<IGXMessageSerializer, GXJsonMessageSerializer>();
        builder.Services.AddSingleton<IGXTransportFactory, GXTransportFactory>();
        builder.Services.AddSingleton<ITableMappingService, GXTableMappingService>();
        builder.Services.AddSingleton<IProcessedMessageService, GXProcessedMessageService>();
        builder.Services.AddSingleton<IGXMessageValidator, GXMessageValidator>();
        builder.Services.AddSingleton<IGXIncomingMessageHandler, GXIncomingMessageHandler>();
        builder.Services.AddSingleton<IGXMqttServerListener, GXMqttServerListener>();
        builder.Services.AddSingleton<IGXTcpServerListener, GXTcpServerListener>();
        builder.Services.AddSingleton<IGXRelayAdministrationService, GXRelayAdministrationService>();
        builder.Services.AddSingleton<IGXSettingsService, GXSettingsService>();
        builder.Services.AddSingleton<GXDataSourceSecretProtector>(_ => new GXDataSourceSecretProtector(
            () => Environment.GetEnvironmentVariable("GURUX_RELAY_SECRETS_KEY")));
        builder.Services.AddSingleton<IGXDataSourceConfigurationService, GXDataSourceConfigurationService>();
        builder.Services.AddSingleton<GXDataSourceSettingsService>();
        builder.Services.AddHostedService<GXRelayHostedService>();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseWebAssemblyDebugging();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }
        if (app.Environment.IsDevelopment())
        {
            app.UseWhen(context => !context.Request.Path.StartsWithSegments("/mcp"), branch => branch.UseHttpsRedirection());
        }
        else
        {
            app.UseHttpsRedirection();
        }

        app.UseBlazorFrameworkFiles();
        app.UseStaticFiles();


        app.UseRouting();
        app.UseMiddleware<GXDatabaseConnectionExceptionMiddleware>();
        app.UseRelayRestAccess();
        app.MapMcp("/mcp");
        app.MapRelayOpenApi();

        app.MapRazorPages();
        app.MapControllers();
        app.MapHub<GXRelayHub>(GXRelayHub.Path);
        app.MapFallbackToFile("index.html");

        app.Run();
    }
}

