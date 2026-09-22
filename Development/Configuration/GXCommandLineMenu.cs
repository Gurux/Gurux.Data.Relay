namespace Gurux.Data.Relay.Configuration;

/// <summary>Selects an existing command when launched interactively without arguments.</summary>
public static class GXCommandLineMenu
{
    public static string[]? SelectArguments(string[] args, bool interactive,
        Func<string, string?> readLine, Action<string> writeLine)
    {
        if (args.Length != 0 || !interactive) return args;

        writeLine("Gurux Data Relay");
        writeLine("1. Client");
        writeLine("2. Server");
        writeLine("3. Data Vault");
        writeLine("4. Update configuration table GXDataVaultTableMapping");
        writeLine("5. Help");
        writeLine("0. Exit");
        while (true)
        {
            string? selection = readLine("> ");
            if (selection == null) return null;
            switch (selection.Trim().ToLowerInvariant())
            {
                case "1": case "client": return ["client"];
                case "2": case "server": return ["server"];
                case "3": case "datavault": case "data vault": return ["datavault"];
                case "4": case "update": return ["update"];
                case "5": case "help": return ["--help"];
                case "0": case "exit": return null;
                default: writeLine("Select 0–5."); break;
            }
        }
    }
}
