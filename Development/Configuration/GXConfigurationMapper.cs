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

using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Gurux.Service.Orm.Common;
using Gurux.Service.Orm.Enums;
using Gurux.Data.Relay.Enums;
using Shared = Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.Orm.Common.Enums;

namespace Gurux.Data.Relay.Configuration;

/// <summary>Maps the persisted configuration graph without a JSON conversion.</summary>
public static class GXConfigurationMapper
{
    internal static void CopySavedMetadata(object saved, object target) => Shared.GXEntityMetadata.ApplySaved(saved, target);

    public static Shared.GXSettings ToEntities(GXModeConfiguration configuration, Guid settingsId)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        List<GXDatabaseConfiguration> databases;
        List<GXTransportConfiguration> transports = [];
        List<GXDataVaultTableMapping>? mappings = null;
        switch (configuration)
        {
            case GXClientConfiguration client:
                client.EnsureRoutingIds();
                databases = client.Databases;
                transports = client.Transports;
                break;
            case GXServerConfiguration server:
                server.EnsureRoutingIds();
                databases = server.Databases;
                transports = server.Transports;
                break;
            case GXDataVaultConfiguration vault:
                databases = vault.Databases;
                mappings = vault.Mappings;
                GXTransportRouting.EnsureDatabaseIds(databases);
                break;
            default:
                throw new ArgumentException("Unsupported configuration type.", nameof(configuration));
        }
        foreach (var database in databases)
        {
            foreach (var table in database.Tables ?? []) table.Database = database.Id;
            foreach (var mapping in database.Mappings ?? []) mapping.Database = database.Id;
        }
        foreach (var transport in transports)
        {
            var ids = transport.Tables.Select(route => route.DatabaseId).Distinct().ToList();
            transport.Database = ids.Count == 1 ? ids[0] : ids.Count > 1 ? null : transport.Database;
        }
        var settings = new Shared.GXSettings
        {
            CreationTime = configuration.CreationTime,
            Updated = configuration.Updated,
            ConcurrencyStamp = configuration.ConcurrencyStamp,
            Id = settingsId,
            Mode = configuration.Mode,
            EventLogLevel = configuration.EventLogLevel,
            CommunicationLogLevel = configuration.CommunicationLogLevel,
            HashAlgorithm = configuration.HashAlgorithm,
            RestEnabled = configuration.RestEnabled,
            SwaggerEnabled = configuration.SwaggerEnabled,
            CorsEnabled = configuration.CorsEnabled,
            CorsAllowCredentials = configuration.CorsAllowCredentials,
            CorsAllowedOrigins = [.. configuration.CorsAllowedOrigins],
            ColumnAliases = configuration is GXDataVaultConfiguration aliasVault ? aliasVault.ColumnAliases.Select(c => new Shared.GXColumnAlias { Source = c.Source, Target = c.Target }).ToList() : [],
            Databases = databases.Select(ToEntity).ToList(),
            Transports = transports.Select(ToEntity).ToList(),
            Mappings = mappings?.Select(ToEntity).ToList()
        };
        NormalizeDatabaseReferences(settings);
        return settings;
    }

    internal static bool NormalizeDatabaseReferences(Shared.GXSettings settings)
    {
        bool changed = false;
        foreach (var database in settings.Databases)
        {
            foreach (var table in database.Tables ?? [])
            {
                changed |= table.Database != database.Id;
                table.Database = database.Id;
                changed |= table.ChangeTracking.Table != table.Id || table.Schedule.Table != table.Id;
                changed |= table.Schedule.Settings != settings.Id;
                table.Schedule.Settings = settings.Id;
                table.ChangeTracking.Table = table.Id;
                table.Schedule.Table = table.Id;
            }
            foreach (var mapping in database.Mappings ?? [])
            {
                changed |= mapping.Database != database.Id;
                mapping.Database = database.Id;
                NormalizeMapping(mapping);
            }
        }
        foreach (var mapping in settings.Mappings ?? []) NormalizeMapping(mapping);
        void NormalizeMapping(GXDataVaultTableMapping mapping)
        {
            changed |= mapping.Schedule.Mapping != mapping.Id;
            changed |= mapping.Schedule.Settings != settings.Id;
            mapping.Schedule.Settings = settings.Id;
            mapping.Schedule.Mapping = mapping.Id;
            foreach (var column in mapping.Columns)
            {
                changed |= column.Mapping != mapping.Id;
                column.Mapping = mapping.Id;
            }
        }
        foreach (var transport in settings.Transports)
        {
            changed |= transport.Settings != settings.Id;
            transport.Settings = settings.Id;
            if (transport.Transfer is not null)
            {
                changed |= transport.Transfer.Transport != transport.Id;
                transport.Transfer.Transport = transport.Id;
            }
            foreach (var route in transport.Tables)
            {
                changed |= route.Transport != transport.Id;
                route.Transport = transport.Id;
            }
            var ids = transport.Tables.Select(t => t.DatabaseId).Distinct().ToList();
            Guid? database = ids.Count == 1 ? ids[0] : ids.Count > 1 ? null : transport.Database;
            changed |= transport.Database != database;
            transport.Database = database;
        }
        return changed;
    }

    public static T FromEntities<T>(Shared.GXSettings settings) where T : GXModeConfiguration
    {
        GXModeConfiguration configuration = FromEntities(settings);
        return configuration as T ?? throw new InvalidOperationException(
            $"Settings mode '{settings.Mode}' does not match configuration type '{typeof(T).Name}'.");
    }

    public static GXModeConfiguration FromEntities(Shared.GXSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        List<GXDatabaseConfiguration> databases = settings.Databases.Select(FromEntity).ToList();
        GXModeConfiguration configuration = Parse<ApplicationMode>(settings.Mode) switch
        {
            ApplicationMode.Client => new GXClientConfiguration
            {
                Databases = databases,
                Transports = settings.Transports.Select(FromEntity).ToList()
            },
            ApplicationMode.Server => new GXServerConfiguration
            {
                Databases = databases,
                Transports = settings.Transports.Select(FromEntity).ToList()
            },
            ApplicationMode.DataVault => new GXDataVaultConfiguration
            {
                Databases = databases,
                Mappings = settings.Mappings?.Select(FromEntity).ToList()
            },
            _ => throw new InvalidOperationException($"Unsupported settings mode '{settings.Mode}'.")
        };
        GXEntityPersistence.CopyMetadata(settings, configuration);
        configuration.EventLogLevel = settings.EventLogLevel is null ? null : Parse<LogLevel>(settings.EventLogLevel.Value);
        configuration.CommunicationLogLevel = settings.CommunicationLogLevel is null ? null : Parse<LogLevel>(settings.CommunicationLogLevel.Value);
        configuration.HashAlgorithm = Parse<HashAlgorithmType>(settings.HashAlgorithm);
        configuration.RestEnabled = settings.RestEnabled;
        configuration.SwaggerEnabled = settings.SwaggerEnabled;
        configuration.CorsEnabled = settings.CorsEnabled;
        configuration.CorsAllowCredentials = settings.CorsAllowCredentials;
        configuration.CorsAllowedOrigins = [.. settings.CorsAllowedOrigins];
        if (configuration is GXDataVaultConfiguration vault)
            vault.ColumnAliases = settings.ColumnAliases.Select(c => new Shared.GXColumnAlias { Source = c.Source, Target = c.Target }).ToList();
        return configuration;
    }

    private static Guid EnsureId(IUnique<Guid> value, Action<Guid> setId)
    {
        if (value.Id == Guid.Empty) setId(Guid.NewGuid());
        return value.Id;
    }

    private static T Parse<T>(T value) where T : struct, Enum
    {
        if (Enum.IsDefined(value)) return value;
        throw new InvalidOperationException($"Unsupported {typeof(T).Name} value '{value}'.");
    }

    private static Shared.GXDatabase ToEntity(GXDatabaseConfiguration value) => new()
    {
        CreationTime = value.CreationTime,
        Updated = value.Updated,
        ConcurrencyStamp = value.ConcurrencyStamp,
        Id = EnsureId(value, id => value.Id = id),
        Description = value.Description,
        RecordSource = value.RecordSource,
        ConnectionString = value.ConnectionString,
        DeletedColumn = value.DeletedColumn,
        Type = value.Type,
        DeleteMode = value.DeleteMode,
        Tables = value.Tables?.Select(ToEntity).ToList(),
        Mappings = value.Mappings?.Select(ToEntity).ToList()
    };

    private static GXDatabaseConfiguration FromEntity(Shared.GXDatabase value) => new()
    {
        CreationTime = value.CreationTime,
        Updated = value.Updated,
        ConcurrencyStamp = value.ConcurrencyStamp,
        Id = value.Id,
        Description = value.Description,
        RecordSource = value.RecordSource,
        ConnectionString = value.ConnectionString,
        DeletedColumn = value.DeletedColumn,
        Type = value.Type.Value,
        DeleteMode = value.DeleteMode,
        Tables = value.Tables?.Select(FromEntity).ToList(),
        Mappings = value.Mappings?.Select(FromEntity).ToList()
    };

    private static Shared.GXTable ToEntity(GXTableConfiguration value) => new()
    {
        Database = value.Database,
        CreationTime = value.CreationTime,
        Updated = value.Updated,
        ConcurrencyStamp = value.ConcurrencyStamp,
        Id = EnsureId(value, id => value.Id = id),
        Name = value.Name,
        RecordSource = value.RecordSource,
        IncrementalColumn = value.IncrementalColumn,
        LastTransferred = value.LastTransferred,
        DeleteSourceRowsAfterTransfer = value.DeleteSourceRowsAfterTransfer,
        NextTransferTime = value.NextTransferTime,
        Columns = [.. value.Columns],
        Keys = [.. value.Keys],
        Transports = value.Transports.Select(ToEntity).ToList(),
        ChangeTracking = ToEntity(value.ChangeTracking),
        Schedule = ToEntity(value.Schedule)
    };

    private static GXTableConfiguration FromEntity(Shared.GXTable value) => new()
    {
        Database = value.Database,
        CreationTime = value.CreationTime,
        Updated = value.Updated,
        ConcurrencyStamp = value.ConcurrencyStamp,
        Id = value.Id,
        Name = value.Name,
        RecordSource = value.RecordSource,
        IncrementalColumn = value.IncrementalColumn,
        LastTransferred = value.LastTransferred,
        DeleteSourceRowsAfterTransfer = value.DeleteSourceRowsAfterTransfer,
        Columns = [.. value.Columns],
        Keys = [.. value.Keys],
        Transports = value.Transports.Select(FromEntity).ToList(),
        ChangeTracking = FromEntity(value.ChangeTracking),
        Schedule = FromEntity(value.Schedule)
    };

    private static Shared.GXTransport ToEntity(GXTransportConfiguration value) => new()
    {
        Database = value.Database,
        CreationTime = value.CreationTime,
        Updated = value.Updated,
        ConcurrencyStamp = value.ConcurrencyStamp,
        Id = EnsureId(value, id => value.Id = id),
        Description = value.Description,
        MaximumMessageSize = value.MaximumMessageSize,
        ConnectTimeoutSeconds = value.ConnectTimeoutSeconds,
        AcknowledgementTimeoutSeconds = value.AcknowledgementTimeoutSeconds,
        Host = value.Host,
        Port = value.Port,
        Broker = value.Broker,
        Topic = value.Topic,
        AcknowledgementTopic = value.AcknowledgementTopic,
        Username = value.Username,
        Password = value.Password,
        UseTls = value.UseTls,
        Type = value.Type,
        Tables = value.Tables.Select(ToEntity).ToList(),
        Transfer = value.Transfer is null ? null : ToEntity(value.Transfer)
    };

    private static GXTransportConfiguration FromEntity(Shared.GXTransport value) => new()
    {
        Database = value.Database,
        CreationTime = value.CreationTime,
        Updated = value.Updated,
        ConcurrencyStamp = value.ConcurrencyStamp,
        Id = value.Id,
        Description = value.Description,
        MaximumMessageSize = value.MaximumMessageSize,
        ConnectTimeoutSeconds = value.ConnectTimeoutSeconds,
        AcknowledgementTimeoutSeconds = value.AcknowledgementTimeoutSeconds,
        Host = value.Host,
        Port = value.Port,
        Broker = value.Broker,
        Topic = value.Topic,
        AcknowledgementTopic = value.AcknowledgementTopic,
        Username = value.Username,
        Password = value.Password,
        UseTls = value.UseTls,
        Type = Parse<TransportType>(value.Type),
        Tables = value.Tables.Select(FromEntity).ToList(),
        Transfer = value.Transfer is null ? null : FromEntity(value.Transfer)
    };

    private static Shared.GXTransportTable ToEntity(GXTransportTableConfiguration value) => new()
    {
        CreationTime = value.CreationTime,
        Updated = value.Updated,
        ConcurrencyStamp = value.ConcurrencyStamp,
        Id = EnsureId(value, id => value.Id = id),
        DatabaseId = value.DatabaseId,
        TableId = value.TableId,
        Table = value.Table,
        Source = value.Source
    };

    private static GXTransportTableConfiguration FromEntity(Shared.GXTransportTable value) => new()
    {
        CreationTime = value.CreationTime,
        Updated = value.Updated,
        ConcurrencyStamp = value.ConcurrencyStamp,
        Id = value.Id,
        DatabaseId = value.DatabaseId,
        TableId = value.TableId,
        Table = value.Table,
        Source = value.Source
    };

    private static GXSchedule? ToEntity(GXSchedule? value)
    {
        if (value is null)
        {
            return new GXSchedule();
        }
        return new()
        {
            CreationTime = value.CreationTime,
            Updated = value.Updated,
            ConcurrencyStamp = value.ConcurrencyStamp,
            Id = EnsureId(value, id => value.Id = id),
            Database = value.Database,
            IntervalSeconds = value.IntervalSeconds,
            Time = value.Time,
            Expression = value.Expression,
            Type = value.Type
        };
    }
    private static GXSchedule? FromEntity(GXSchedule? value)
    {
        if (value is null)
        {
            return null;
        }
        return new()
        {
            CreationTime = value.CreationTime,
            Updated = value.Updated,
            ConcurrencyStamp = value.ConcurrencyStamp,
            Id = value.Id,
            Database = value.Database,
            IntervalSeconds = value.IntervalSeconds,
            Time = value.Time,
            Expression = value.Expression,
            Type = Parse<ScheduleType>(value.Type)
        };
    }

    private static Shared.GXChangeTracking ToEntity(GXChangeTrackingConfiguration value) => new()
    {
        CreationTime = value.CreationTime,
        Updated = value.Updated,
        ConcurrencyStamp = value.ConcurrencyStamp,
        Id = EnsureId(value, id => value.Id = id),
        CreatedColumn = value.CreatedColumn,
        UpdatedColumn = value.UpdatedColumn,
        DeletedColumn = value.DeletedColumn,
        Column = value.Column,
        HashColumns = value.HashColumns,
        Type = value.Type
    };

    private static GXChangeTrackingConfiguration FromEntity(Shared.GXChangeTracking value) => new()
    {
        CreationTime = value.CreationTime,
        Updated = value.Updated,
        ConcurrencyStamp = value.ConcurrencyStamp,
        Id = value.Id,
        CreatedColumn = value.CreatedColumn,
        UpdatedColumn = value.UpdatedColumn,
        DeletedColumn = value.DeletedColumn,
        Column = value.Column,
        HashColumns = value.HashColumns,
        Type = Parse<ChangeTrackingType>(value.Type)
    };

    private static Shared.GXTransfer ToEntity(GXTransferConfiguration value) => new()
    {
        CreationTime = value.CreationTime,
        Updated = value.Updated,
        ConcurrencyStamp = value.ConcurrencyStamp,
        Id = EnsureId(value, id => value.Id = id),
        BatchSize = value.BatchSize,
        RetryCount = value.RetryCount,
        RetryDelaySeconds = value.RetryDelaySeconds,
        PingAfterSeconds = value.PingAfterSeconds,
        AllowConcurrentRuns = value.AllowConcurrentRuns
    };

    private static GXTransferConfiguration FromEntity(Shared.GXTransfer value) => new()
    {
        CreationTime = value.CreationTime,
        Updated = value.Updated,
        ConcurrencyStamp = value.ConcurrencyStamp,
        Id = value.Id,
        BatchSize = value.BatchSize,
        RetryCount = value.RetryCount,
        RetryDelaySeconds = value.RetryDelaySeconds,
        PingAfterSeconds = value.PingAfterSeconds,
        AllowConcurrentRuns = value.AllowConcurrentRuns
    };

    public static GXDataVaultTableMapping ToEntity(GXDataVaultTableMapping value) => new()
    {
        CreationTime = value.CreationTime,
        Updated = value.Updated,
        ConcurrencyStamp = value.ConcurrencyStamp,
        Id = EnsureId(value, id => value.Id = id),
        Database = value.Database,
        SourceTable = value.SourceTable,
        TargetTable = value.TargetTable,
        UpdatedAtUtc = value.UpdatedAtUtc,
        ObjectType = value.ObjectType,
        Columns = value.Columns.Select(ToEntity).ToList(),
        Schedule = ToEntity(value.Schedule)
    };

    public static GXDataVaultTableMapping FromEntity(GXDataVaultTableMapping value) => new()
    {
        CreationTime = value.CreationTime,
        Updated = value.Updated,
        ConcurrencyStamp = value.ConcurrencyStamp,
        Id = value.Id,
        Database = value.Database,
        SourceTable = value.SourceTable,
        TargetTable = value.TargetTable,
        UpdatedAtUtc = value.UpdatedAtUtc,
        ObjectType = value.ObjectType.Value,
        Columns = value.Columns.Select(FromEntity).ToList(),
        Schedule = FromEntity(value.Schedule)
    };

    private static Shared.GXDataVaultColumnMapping ToEntity(GXDataVaultColumnMapping value) => new()
    {
        CreationTime = value.CreationTime,
        Updated = value.Updated,
        ConcurrencyStamp = value.ConcurrencyStamp,
        Id = EnsureId(value, id => value.Id = id),
        SourceColumn = value.SourceColumn,
        TargetColumn = value.TargetColumn,
        Role = value.Role,
        SourceMappingId = value.SourceMappingId,
        Aggregation = value.Aggregation,
    };

    private static GXDataVaultColumnMapping? FromEntity(GXDataVaultColumnMapping? value)
    {
        if (value is null)
        {
            return null;
        }
        return new()
        {
            CreationTime = value.CreationTime,
            Updated = value.Updated,
            ConcurrencyStamp = value.ConcurrencyStamp,
            Id = value.Id,
            SourceColumn = value.SourceColumn,
            TargetColumn = value.TargetColumn,
            Role = value.Role,
            SourceMappingId = value.SourceMappingId,
            Aggregation = value.Aggregation,
        };
    }
}




