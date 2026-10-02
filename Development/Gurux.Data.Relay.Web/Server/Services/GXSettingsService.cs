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

using Gurux.Data.Relay.Realtime;
using System.Text.Json;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Database;
using Microsoft.Data.SqlClient;
using Gurux.Data.Relay.Shared;
using Gurux.Service.Orm.Common.Enums;

namespace Gurux.Data.Relay.Web.Server.Services;

public sealed class GXSettingsService : IGXSettingsService
{
    private readonly IGXRelayChangePublisher? _changes;

    private readonly GXConfigurationStoreSettings _currentSettings;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IGXDatabaseConnectionTestService _connectionTestService;
    private readonly string _settingsFilePath;

    public GXSettingsService(
        GXConfigurationStoreSettings currentSettings,
        IHostEnvironment hostEnvironment,
        IGXDatabaseConnectionTestService connectionTestService, IGXRelayChangePublisher? changes = null,
        GXCommandLineOptions? commandLine = null)
    {
        _currentSettings = currentSettings; _changes = changes;
        _hostEnvironment = hostEnvironment;
        _connectionTestService = connectionTestService;
        _settingsFilePath = Path.GetFullPath(
            commandLine?.SettingsFilePath ?? GXConfigurationStoreSettingsProvider.SettingsFileName,
            hostEnvironment.ContentRootPath);
    }

    public Task<GXConfigurationStoreSettings> GetAsync(CancellationToken cancellationToken)
    {
        GXConfigurationStoreSettings snapshot = new()
        {
            Type = _currentSettings.Type,
            ConnectionString = _currentSettings.ConnectionString,
            IncludeStackTrace = _currentSettings.IncludeStackTrace,
            NotifyLogChanges = _currentSettings.NotifyLogChanges,
            EventLogLevel = _currentSettings.EventLogLevel,
            CommunicationLogLevel = _currentSettings.CommunicationLogLevel,
        };
        return Task.FromResult(snapshot);
    }

    public async Task SaveAsync(GXConfigurationStoreSettings settings, CancellationToken cancellationToken)
    {
        ValidateSettings(settings);

        string path = _settingsFilePath;
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using (FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, settings, GXConfigurationStoreSettingsProvider.SerializerOptions, cancellationToken);
        }

        _currentSettings.IncludeStackTrace = settings.IncludeStackTrace;
        _currentSettings.NotifyLogChanges = settings.NotifyLogChanges;
        _currentSettings.EventLogLevel = settings.EventLogLevel;
        _currentSettings.CommunicationLogLevel = settings.CommunicationLogLevel;
        _currentSettings.Type = settings.Type;
        _currentSettings.ConnectionString = settings.ConnectionString;
        _changes?.Publish(new(null, GXRelayChangeKind.Store));
    }

    public async Task TestAsync(GXConfigurationStoreSettings settings, CancellationToken cancellationToken)
    {
        ValidateSettings(settings);
        await _connectionTestService.TestConnectionAsync(new GXDatabaseConfiguration
        {
            Type = settings.Type,
            ConnectionString = settings.ConnectionString,
        }, cancellationToken);
    }

    private void ValidateSettings(GXConfigurationStoreSettings settings)
    {
        if (settings.Type == DatabaseType.SqLite && string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            settings.ConnectionString = GXConfigurationStoreSettings.CreateDefault(_hostEnvironment.ContentRootPath).ConnectionString;
        }

        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            throw new InvalidOperationException("Configuration database connection string is required.");
        }

        if (settings.Type == DatabaseType.MSSQL)
        {
            try
            {
                SqlConnectionStringBuilder builder = new(settings.ConnectionString);
                if (string.IsNullOrWhiteSpace(builder.DataSource))
                {
                    throw new InvalidOperationException(
                        "MSSQL connection string must include Server (or Data Source). Example: Server=localhost;Database=Gurux.Data.Relay;User Id=sa;Password=YourPassword;TrustServerCertificate=True;Encrypt=False");
                }

                if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
                {
                    throw new InvalidOperationException(
                        "MSSQL connection string must include Database (or Initial Catalog). Example: Server=localhost;Database=Gurux.Data.Relay;User Id=sa;Password=YourPassword;TrustServerCertificate=True;Encrypt=False");
                }
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    "Invalid MSSQL connection string format. Example: Server=localhost;Database=Gurux.Data.Relay;User Id=sa;Password=YourPassword;TrustServerCertificate=True;Encrypt=False", ex);
            }
        }
    }
}


