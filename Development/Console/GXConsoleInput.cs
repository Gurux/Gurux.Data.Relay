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

