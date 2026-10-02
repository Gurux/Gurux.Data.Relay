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
