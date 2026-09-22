using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Shared.Enums;
using Microsoft.Extensions.Logging;

namespace Gurux.Data.Relay.Configuration;

public sealed class GXCommandLineOptions
{
    public ApplicationMode? Mode { get; init; }

    public bool RunClientAndServer { get; init; }

    /// <summary>Run configured relay modes without interactive setup or stopping the web host.</summary>
    public bool WebHost { get; init; }

    /// <summary>Persist REST and web administration availability for the selected mode, then exit.</summary>
    public bool? RestEnabled { get; init; }

    public bool Configure { get; init; }

    public ConfigureAction ConfigureAction { get; init; } = ConfigureAction.Edit;

    public bool ResetClientState { get; init; }

    public bool SendSchema { get; init; }

    /// <summary>Update the configuration store's Data Vault mapping table and exit.</summary>
    public bool Update { get; init; }

    public bool RefreshInformationMart { get; init; }

    public bool ShowEvents { get; init; }

    public bool FollowEvents { get; init; }

    public bool ShowSettings { get; init; }

    public Guid? DataVaultTableId { get; init; }

    public string? ExportSettingsPath { get; init; }

    public string? ImportSettingsPath { get; init; }

    public string? SettingsFilePath { get; init; }

    public int EventTop { get; init; } = 50;

    public LogLevel? EventMinimumLevel { get; init; }

    public LogLevel? EventLogLevel { get; init; }

    public LogLevel? CommunicationLogLevel { get; init; }

    public HashAlgorithmType HashAlgorithm { get; init; } = HashAlgorithmType.SHA256;

    public bool HashAlgorithmSpecified { get; init; }

    public bool ShowHelp { get; init; }

    public string? ParseError { get; init; }

    public static GXCommandLineOptions Parse(string[] args)
    {
        ApplicationMode? mode = null;
        bool runClientAndServer = false;
        bool configure = false;
        ConfigureAction configureAction = ConfigureAction.Edit;
        bool resetClientState = false;
        bool sendSchema = false;
        bool update = false;
        bool? restEnabled = null;
        bool refreshInformationMart = false;
        bool showEvents = false;
        bool followEvents = false;
        bool showSettings = false;
        Guid? dataVaultTableId = null;
        string? exportSettingsPath = null;
        string? importSettingsPath = null;
        string? settingsFilePath = null;
        int eventTop = 50;
        LogLevel? eventMinimumLevel = null;
        LogLevel? eventLogLevel = null;
        LogLevel? communicationLogLevel = null;
        HashAlgorithmType hashAlgorithm = HashAlgorithmType.SHA256;
        bool hashAlgorithmSpecified = false;
        bool showHelp = false;
        string? parseError = null;

        for (int pos = 0; pos < args.Length; ++pos)
        {
            string current = args[pos];
            if (string.Equals(current, "client", StringComparison.OrdinalIgnoreCase))
            {
                parseError = TrySetMode(ref mode, ApplicationMode.Client);
            }
            else if (string.Equals(current, "both", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "--client-server", StringComparison.OrdinalIgnoreCase))
            {
                runClientAndServer = true;
            }
            else if (string.Equals(current, "server", StringComparison.OrdinalIgnoreCase))
            {
                parseError = TrySetMode(ref mode, ApplicationMode.Server);
            }
            else if (string.Equals(current, "datavault", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "data-vault", StringComparison.OrdinalIgnoreCase))
            {
                parseError = TrySetMode(ref mode, ApplicationMode.DataVault);
            }
            else if (string.Equals(current, "--mode", StringComparison.OrdinalIgnoreCase))
            {
                if (pos + 1 >= args.Length)
                {
                    parseError = "Missing mode value after --mode.";
                    break;
                }

                ++pos;
                if (!Enum.TryParse<ApplicationMode>(args[pos], true, out ApplicationMode parsedMode))
                {
                    parseError = $"Unsupported mode '{args[pos]}'.";
                    break;
                }

                parseError = TrySetMode(ref mode, parsedMode);
            }
            else if (string.Equals(current, "--rest", StringComparison.OrdinalIgnoreCase))
            {
                if (restEnabled.HasValue || pos + 1 >= args.Length || !bool.TryParse(args[++pos], out bool enabled))
                {
                    parseError = "Specify --rest once with true or false.";
                    break;
                }
                restEnabled = enabled;
            }
            else if (string.Equals(current, "--configure", StringComparison.OrdinalIgnoreCase))
            {
                configure = true;
                if (pos + 1 < args.Length && TryParseConfigureAction(args[pos + 1], out ConfigureAction parsedAction))
                {
                    configureAction = parsedAction;
                    pos += 1;
                }
            }
            else if (string.Equals(current, "--reset", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "--reset-client-state", StringComparison.OrdinalIgnoreCase))
            {
                resetClientState = true;
            }
            else if (string.Equals(current, "--send-schema", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "--send-table-schema", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "--sync-schema", StringComparison.OrdinalIgnoreCase))
            {
                sendSchema = true;
            }
            else if (string.Equals(current, "update", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "--update", StringComparison.OrdinalIgnoreCase))
            {
                update = true;
            }
            else if (string.Equals(current, "--refresh", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "--refresh-information-mart", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "--regenerate-information-mart", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "--rebuild-information-mart", StringComparison.OrdinalIgnoreCase))
            {
                refreshInformationMart = true;
            }
            else if (string.Equals(current, "--events", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "--show-events", StringComparison.OrdinalIgnoreCase))
            {
                showEvents = true;
                if (pos + 1 < args.Length && TryParseEventFollow(args[pos + 1]))
                {
                    followEvents = true;
                    pos += 1;
                }
            }
            else if (string.Equals(current, "--follow", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "-f", StringComparison.OrdinalIgnoreCase))
            {
                followEvents = true;
            }
            else if (string.Equals(current, "--settings", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "--show-settings", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "--connections", StringComparison.OrdinalIgnoreCase))
            {
                showSettings = true;
            }
            else if (string.Equals(current, "--data-vault-table", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "--datavault-table", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "--table-id", StringComparison.OrdinalIgnoreCase))
            {
                if (pos + 1 >= args.Length)
                {
                    parseError = "Missing value after --data-vault-table.";
                    break;
                }

                ++pos;
                if (!Guid.TryParse(args[pos], out Guid parsedTableId))
                {
                    parseError = $"Invalid Data Vault table ID '{args[pos]}'.";
                    break;
                }

                dataVaultTableId = parsedTableId;
                showSettings = true;
            }
            else if (string.Equals(current, "--settings-file", StringComparison.OrdinalIgnoreCase))
            {
                if (settingsFilePath is not null)
                {
                    parseError = "Specify --settings-file only once.";
                    break;
                }
                if (pos + 1 >= args.Length || string.IsNullOrWhiteSpace(args[pos + 1]) ||
                    args[pos + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    parseError = "Missing value after --settings-file.";
                    break;
                }
                settingsFilePath = args[++pos];
            }
            else if (string.Equals(current, "--export-settings", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "--export", StringComparison.OrdinalIgnoreCase))
            {
                if (pos + 1 >= args.Length)
                {
                    parseError = "Missing value after --export-settings.";
                    break;
                }

                ++pos;
                exportSettingsPath = args[pos];
            }
            else if (string.Equals(current, "--import-settings", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "--import", StringComparison.OrdinalIgnoreCase))
            {
                if (pos + 1 >= args.Length)
                {
                    parseError = "Missing value after --import-settings.";
                    break;
                }

                ++pos;
                importSettingsPath = args[pos];
            }
            else if (string.Equals(current, "--top", StringComparison.OrdinalIgnoreCase))
            {
                if (pos + 1 >= args.Length)
                {
                    parseError = "Missing value after --top.";
                    break;
                }

                ++pos;
                if (!int.TryParse(args[pos], out eventTop) || eventTop <= 0)
                {
                    parseError = "--top value must be a positive integer.";
                    break;
                }
            }
            else if (string.Equals(current, "--log-level", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "--level", StringComparison.OrdinalIgnoreCase))
            {
                if (pos + 1 >= args.Length)
                {
                    parseError = "Missing value after --log-level.";
                    break;
                }

                ++pos;
                if (!Enum.TryParse(args[pos], true, out LogLevel parsedLevel) || parsedLevel == LogLevel.None)
                {
                    parseError = $"Unsupported log level '{args[pos]}'.";
                    break;
                }

                eventMinimumLevel = parsedLevel;
            }
            else if (string.Equals(current, "--event-log-level", StringComparison.OrdinalIgnoreCase))
            {
                if (pos + 1 >= args.Length)
                {
                    parseError = "Missing value after --event-log-level.";
                    break;
                }

                ++pos;
                if (!TryParseLogLevel(args[pos], out LogLevel parsedLevel))
                {
                    parseError = $"Unsupported event log level '{args[pos]}'.";
                    break;
                }

                eventLogLevel = parsedLevel;
            }
            else if (string.Equals(current, "--communication-log-level", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "--communication-log-log-level", StringComparison.OrdinalIgnoreCase))
            {
                if (pos + 1 >= args.Length)
                {
                    parseError = "Missing value after --communication-log-level.";
                    break;
                }

                ++pos;
                if (!TryParseLogLevel(args[pos], out LogLevel parsedLevel))
                {
                    parseError = $"Unsupported communication log level '{args[pos]}'.";
                    break;
                }

                communicationLogLevel = parsedLevel;
            }
            else if (string.Equals(current, "--hash-algorithm", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "--HashAlgorithm", StringComparison.OrdinalIgnoreCase))
            {
                if (pos + 1 >= args.Length)
                {
                    parseError = "Missing value after --hash-algorithm.";
                    break;
                }

                ++pos;
                if (!Enum.TryParse(args[pos], ignoreCase: true, out HashAlgorithmType parsedAlgorithm))
                {
                    parseError = $"Unsupported hash algorithm '{args[pos]}'.";
                    break;
                }

                hashAlgorithm = parsedAlgorithm;
                hashAlgorithmSpecified = true;
            }
            else if (string.Equals(current, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "-h", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "/?", StringComparison.OrdinalIgnoreCase))
            {
                showHelp = true;
            }
            else
            {
                parseError = $"Unknown argument '{current}'.";
                break;
            }

            if (parseError is not null)
            {
                break;
            }
        }

        if (parseError is null && restEnabled.HasValue &&
            (mode is not (ApplicationMode.Client or ApplicationMode.Server or ApplicationMode.DataVault) ||
             runClientAndServer || configure || update || resetClientState || sendSchema || refreshInformationMart ||
             showEvents || followEvents || showSettings || importSettingsPath is not null || exportSettingsPath is not null ||
             eventLogLevel is not null || communicationLogLevel is not null || hashAlgorithmSpecified))
            parseError = "--rest requires client, server or datavault mode and cannot be combined with other actions.";

        if (parseError is null && update &&
            (runClientAndServer || configure || resetClientState || sendSchema || refreshInformationMart ||
             showEvents || followEvents || showSettings || dataVaultTableId is not null ||
             importSettingsPath is not null || exportSettingsPath is not null))
        {
            parseError = "--update cannot be combined with other actions.";
        }
        if (parseError is null && resetClientState && mode == ApplicationMode.Server)
        {
            parseError = "--reset can only be used in client or data vault mode.";
        }
        if (parseError is null && runClientAndServer && mode is not null)
        {
            parseError = "Client/server mode cannot be combined with another mode.";
        }
        if (parseError is null && runClientAndServer &&
            (showSettings || showEvents || importSettingsPath is not null || exportSettingsPath is not null || refreshInformationMart || dataVaultTableId is not null))
        {
            parseError = "Client/server mode can only be used with runtime client and server options.";
        }
        if (parseError is null && sendSchema && mode == ApplicationMode.Server)
        {
            parseError = "--send-schema can only be used in client or data vault mode.";
        }
        if (parseError is null && refreshInformationMart && mode != ApplicationMode.DataVault)
        {
            parseError = "--refresh-information-mart can only be used in data vault mode.";
        }
        if (parseError is null && refreshInformationMart && dataVaultTableId is null)
        {
            parseError = "--refresh-information-mart requires --data-vault-table ID.";
        }
        if (parseError is null && (eventTop != 50 || eventMinimumLevel is not null) && !showEvents)
        {
            parseError = "--top and --log-level can only be used with --events.";
        }
        if (parseError is null && followEvents && !showEvents)
        {
            parseError = "--follow can only be used with --events.";
        }
        if (parseError is null && dataVaultTableId is not null && mode != ApplicationMode.DataVault)
        {
            parseError = "--data-vault-table can only be used in data vault mode.";
        }
        if (parseError is null && exportSettingsPath is not null && importSettingsPath is not null)
        {
            parseError = "--export-settings and --import-settings cannot be used together.";
        }
        if (parseError is null && string.IsNullOrWhiteSpace(exportSettingsPath) && exportSettingsPath is not null)
        {
            parseError = "--export-settings value cannot be empty.";
        }
        if (parseError is null && string.IsNullOrWhiteSpace(importSettingsPath) && importSettingsPath is not null)
        {
            parseError = "--import-settings value cannot be empty.";
        }

        return new GXCommandLineOptions
        {
            Mode = mode,
            RestEnabled = restEnabled,
            RunClientAndServer = runClientAndServer,
            Configure = configure,
            ConfigureAction = configureAction,
            ResetClientState = resetClientState,
            SendSchema = sendSchema,
            Update = update,
            RefreshInformationMart = refreshInformationMart,
            ShowEvents = showEvents,
            FollowEvents = followEvents,
            ShowSettings = showSettings,
            DataVaultTableId = dataVaultTableId,
            ExportSettingsPath = exportSettingsPath,
            ImportSettingsPath = importSettingsPath,
            SettingsFilePath = settingsFilePath,
            EventTop = eventTop,
            EventMinimumLevel = eventMinimumLevel,
            EventLogLevel = eventLogLevel,
            CommunicationLogLevel = communicationLogLevel,
            HashAlgorithm = hashAlgorithm,
            HashAlgorithmSpecified = hashAlgorithmSpecified,
            ShowHelp = showHelp,
            ParseError = parseError,
        };
    }

    public bool RequiresConfigurationStore()
    {
        return !ShowHelp && ParseError is null && (Mode is not null || RunClientAndServer || Update);
    }

    private static bool TryParseConfigureAction(string value, out ConfigureAction action)
    {
        if (string.Equals(value, "add", StringComparison.OrdinalIgnoreCase))
        {
            action = ConfigureAction.Add;
            return true;
        }
        if (string.Equals(value, "remove", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "delete", StringComparison.OrdinalIgnoreCase))
        {
            action = ConfigureAction.Remove;
            return true;
        }
        if (string.Equals(value, "edit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "update", StringComparison.OrdinalIgnoreCase))
        {
            action = ConfigureAction.Edit;
            return true;
        }

        action = ConfigureAction.Edit;
        return false;
    }

    private static bool TryParseEventFollow(string value)
    {
        return string.Equals(value, "follow", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "tail", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "-f", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseLogLevel(string value, out LogLevel level)
    {
        return Enum.TryParse(value, ignoreCase: true, out level) && Enum.IsDefined(level);
    }

    private static string? TrySetMode(ref ApplicationMode? currentMode, ApplicationMode newMode)
    {
        if (currentMode is not null && currentMode != newMode)
        {
            return "Only one mode can be specified.";
        }

        currentMode = newMode;
        return null;
    }
}

