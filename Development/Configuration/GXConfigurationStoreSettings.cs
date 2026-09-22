using System.Text.Json;
using System.Text.Json.Serialization;
using Gurux.Service.Orm.Enums;
using Gurux.Data.Relay.Input;
using Microsoft.Extensions.Hosting;
using Gurux.Service.Orm.Common.Enums;

namespace Gurux.Data.Relay.Configuration;

public sealed class GXConfigurationStoreSettings
{
    public DatabaseType Type { get; set; } = DatabaseType.SqLite;

    public string ConnectionString { get; set; } = string.Empty;
    public bool IncludeStackTrace { get; set; }
    public bool NotifyLogChanges { get; set; }
    public Microsoft.Extensions.Logging.LogLevel? EventLogLevel { get; set; }
    public Microsoft.Extensions.Logging.LogLevel? CommunicationLogLevel { get; set; }

    public static GXConfigurationStoreSettings CreateDefault(string contentRootPath)
    {
        return new GXConfigurationStoreSettings
        {
            Type = DatabaseType.SqLite,
            ConnectionString = $"Data Source={Path.Combine(contentRootPath, GXConfigurationStoreSettingsProvider.ConfigurationDatabaseFileName)}",
        };
    }
}

public static class GXConfigurationStoreSettingsProvider
{
    public const string SettingsFileName = "gurux.data.relay.settings.json";
    public const string ConfigurationDatabaseFileName = "Gurux.Data.Relay.db";

    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Task<GXConfigurationStoreSettings> EnsureCreatedAsync(
        IHostEnvironment hostEnvironment,
        CancellationToken cancellationToken,
        string? settingsFilePath = null)
    {
        return EnsureCreatedAsync(
            hostEnvironment,
            GXConsoleInput.ReadLine,
            System.Console.WriteLine,
            cancellationToken,
            settingsFilePath);
    }

    public static async Task<GXConfigurationStoreSettings> EnsureCreatedAsync(
        IHostEnvironment hostEnvironment,
        Func<string, string?> readLine,
        Action<string> writeLine,
        CancellationToken cancellationToken,
        string? settingsFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(hostEnvironment);
        ArgumentNullException.ThrowIfNull(readLine);
        ArgumentNullException.ThrowIfNull(writeLine);

        string path = Path.GetFullPath(settingsFilePath ?? SettingsFileName, hostEnvironment.ContentRootPath);
        if (File.Exists(path))
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            GXConfigurationStoreSettings loadedSettings = await JsonSerializer.DeserializeAsync<GXConfigurationStoreSettings>(
                stream,
                SerializerOptions,
                cancellationToken) ?? GXConfigurationStoreSettings.CreateDefault(hostEnvironment.ContentRootPath);
            return Validate(loadedSettings, hostEnvironment.ContentRootPath);
        }

        GXConfigurationStoreSettings settings = ReadSettings(hostEnvironment.ContentRootPath, readLine, writeLine);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using (FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
        }

        return settings;
    }

    private static GXConfigurationStoreSettings ReadSettings(
        string contentRootPath,
        Func<string, string?> readLine,
        Action<string> writeLine)
    {
        writeLine("Select where GXDataRelay configuration is stored:");
        writeLine("1. MSSQL");
        writeLine("2. PostgreSQL");
        writeLine("3. MySQL");
        writeLine("4. MariaDB");
        writeLine("5. SQLite");
        writeLine("6. Oracle");
        writeLine("7. DB2");
        writeLine("8. SAP HANA");

        DatabaseType type = ParseDatabaseType(readLine("Configuration database type [SQLite]: "));
        string connectionString = ReadConnectionString(contentRootPath, type, readLine, writeLine);

        return new GXConfigurationStoreSettings
        {
            Type = type,
            ConnectionString = connectionString,
        };
    }

    private static string ReadConnectionString(
        string contentRootPath,
        DatabaseType type,
        Func<string, string?> readLine,
        Action<string> writeLine)
    {
        string defaultConnectionString = GXConfigurationStoreSettings.CreateDefault(contentRootPath).ConnectionString;
        if (type == DatabaseType.SqLite)
        {
            string? sqliteConnectionString = readLine($"Configuration database connection string [{defaultConnectionString}]: ");
            return string.IsNullOrWhiteSpace(sqliteConnectionString) ? defaultConnectionString : sqliteConnectionString;
        }

        while (true)
        {
            string? connectionString = readLine("Configuration database connection string: ");
            if (connectionString is null)
            {
                throw new InvalidOperationException("Configuration database connection string is required.");
            }

            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                return connectionString;
            }

            writeLine("Connection string is required for the selected configuration database.");
        }
    }

    private static DatabaseType ParseDatabaseType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DatabaseType.SqLite;
        }

        return value.Trim() switch
        {
            "1" => DatabaseType.MSSQL,
            "2" => DatabaseType.PostgreSQL,
            "3" => DatabaseType.MySQL,
            "4" => DatabaseType.MariaDB,
            "5" => DatabaseType.SqLite,
            "6" => DatabaseType.Oracle,
            "7" => DatabaseType.DB2,
            "8" => DatabaseType.SapHana,
            var text when Enum.TryParse(text, ignoreCase: true, out DatabaseType databaseType) => databaseType,
            _ => throw new InvalidOperationException($"Unsupported configuration database type '{value}'."),
        };
    }

    private static GXConfigurationStoreSettings Validate(
        GXConfigurationStoreSettings settings,
        string contentRootPath)
    {
        if (settings.Type == DatabaseType.SqLite && string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            settings.ConnectionString = GXConfigurationStoreSettings.CreateDefault(contentRootPath).ConnectionString;
        }

        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            throw new InvalidOperationException("Configuration database connection string is required.");
        }

        return settings;
    }

}


