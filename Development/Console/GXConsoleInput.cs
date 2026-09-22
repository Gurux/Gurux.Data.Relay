using System.Text;

namespace Gurux.Data.Relay.Input;

public static class GXConsoleInput
{
    public static string? ReadLine(string prompt)
    {
        System.Console.Write(prompt);
        if (System.Console.IsInputRedirected || System.Console.IsOutputRedirected)
        {
            return System.Console.ReadLine();
        }

        StringBuilder value = new();
        while (true)
        {
            ConsoleKeyInfo key = System.Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                System.Console.WriteLine();
                return value.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length == 0)
                {
                    continue;
                }

                value.Length -= 1;
                System.Console.Write("\b \b");
                continue;
            }

            if (key.Key == ConsoleKey.Delete)
            {
                value.Clear();
                Clear();
                System.Console.Write(prompt);
                continue;
            }

            if (key.KeyChar == '\0' || char.IsControl(key.KeyChar))
            {
                continue;
            }

            value.Append(key.KeyChar);
            System.Console.Write(key.KeyChar);
        }
    }

    private static void Clear()
    {
        try
        {
            System.Console.Clear();
        }
        catch (IOException)
        {
            System.Console.WriteLine();
        }
    }
}

