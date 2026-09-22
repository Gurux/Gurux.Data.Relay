using Gurux.Service.Orm.Settings;

namespace Gurux.Data.Relay.Database;

internal static class GXSqlIdentifierHelper
{
    public static string QuoteIdentifier(GXDBSettings settings, string identifier, bool isTable)
    {
        char openQuote = isTable && settings.TableNameQuoteCharacter != '\0'
            ? settings.TableNameQuoteCharacter
            : settings.ColumnNameQuoteCharacter;

        string[] parts = identifier.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (openQuote == '\0')
        {
            return string.Join('.', parts);
        }

        char closeQuote = GetClosingQuote(openQuote);
        return string.Join('.', parts.Select(part => $"{openQuote}{part}{closeQuote}"));
    }

    private static char GetClosingQuote(char openQuote)
    {
        return openQuote == '[' ? ']' : openQuote;
    }
}

