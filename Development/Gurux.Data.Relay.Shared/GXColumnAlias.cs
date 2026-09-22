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

namespace Gurux.Data.Relay.Shared;

/// <summary>A bidirectional column-name rule with an optional wildcard.</summary>
public sealed class GXColumnAlias
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;

    public static List<GXColumnAlias> Defaults() => [
        new() { Source = "LoadDate", Target = "LOAD_DATE" },
        new() { Source = "RecordSource", Target = "RECORD_SOURCE" },
        new() { Source = "*HashKey", Target = "HK_*" },
        new() { Source = "*HashDiff", Target = "*HASH_DIFF" },
        new() { Source = "*HashDiff", Target = "HASH_DIFF" },
        new() { Source = "HashDiff", Target = "*HASH_DIFF" }];

    public static bool IsValid(GXColumnAlias rule) => !string.IsNullOrWhiteSpace(rule.Source) &&
        !string.IsNullOrWhiteSpace(rule.Target) && rule.Source.Count(c => c == '*') <= 1 &&
        rule.Target.Count(c => c == '*') <= 1;

    public bool Matches(string source, string target) =>
        string.Equals(Translate(source, Source, Target), target, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Translate(source, Target, Source), target, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Translate(target, Source, Target), source, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Translate(target, Target, Source), source, StringComparison.OrdinalIgnoreCase);

    private static string? Translate(string value, string pattern, string replacement)
    {
        int star = pattern.IndexOf('*');
        if (star < 0) return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase) ? replacement : null;
        string prefix = pattern[..star], suffix = pattern[(star + 1)..];
        if (value.Length < prefix.Length + suffix.Length || !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;
        return replacement.Replace("*", value.Substring(prefix.Length, value.Length - prefix.Length - suffix.Length));
    }
}
