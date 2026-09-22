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
using Gurux.Data.Relay.Client;
using Gurux.Data.Relay.Database;
using Gurux.Data.Relay.Hosting;
using Gurux.Data.Relay.Input;
using Gurux.Data.Relay.Log;
using Gurux.Data.Relay.Server;
using Gurux.Data.Relay.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Gurux.Data.Relay.Sources;
using Gurux.Data.Relay.Shared.Client;

string[]? selectedArguments = GXCommandLineMenu.SelectArguments(args, !Console.IsInputRedirected,
    GXConsoleInput.ReadLine, Console.WriteLine);
if (selectedArguments == null) return;
args = selectedArguments;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "GXDataRelay";
});
builder.Services.AddSystemd();

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});


GXCommandLineOptions commandLine = GXCommandLineOptions.Parse(args);
GXConfigurationStoreSettings configurationStoreSettings = commandLine.RequiresConfigurationStore()
    ? await GXConfigurationStoreSettingsProvider.EnsureCreatedAsync(builder.Environment, CancellationToken.None, commandLine.SettingsFilePath)
    : GXConfigurationStoreSettings.CreateDefault(builder.Environment.ContentRootPath);

builder.Services.AddSingleton(commandLine);
builder.Services.AddSingleton(configurationStoreSettings);
builder.Services.AddSingleton<IGXConfigurationService, GXDatabaseConfigurationService>();
builder.Services.AddSingleton<GXDataSourceSecretProtector>(_ => new GXDataSourceSecretProtector(
    () => Environment.GetEnvironmentVariable("GURUX_RELAY_SECRETS_KEY")));
builder.Services.AddSingleton<IGXDataSourceConfigurationService, GXDataSourceConfigurationService>();
builder.Services.AddSingleton<IGXDatabaseConnectionFactory, GXDatabaseConnectionFactory>();
builder.Services.AddSingleton<IGXEventLogService, GXEventLogService>();
builder.Services.AddSingleton<IGXTransportMessageLogService, GXTransportMessageLogService>();
builder.Services.AddSingleton<ILoggerProvider, GXEventLogLoggerProvider>();
builder.Services.AddSingleton<IGXDatabaseConnectionTestService, GXDatabaseConnectionTestService>();
builder.Services.AddSingleton<IGXDatabaseMetadataService, GXDatabaseMetadataService>();
builder.Services.AddSingleton<IClientBatchSender, GXClientBatchSender>();
builder.Services.AddSingleton<IClientSchemaSender, GXClientSchemaSender>();
builder.Services.AddSingleton<IDatabaseChangeNotifierFactory, GXDatabaseChangeNotifierFactory>();
builder.Services.AddSingleton<ITransferScheduler, GXTransferScheduler>();
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
builder.Services.AddHostedService<GXRelayHostedService>();
if (GXDataSourceServiceExtensions.ShouldRun(commandLine))
    builder.Services.AddRelayDataSources();

IHost host = builder.Build();
await host.RunAsync();

