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

using Gurux.Scheduling;
using System.Globalization;

namespace Gurux.Data.Relay.Shared;

public static class GXCronSchedule
{
    public const string DefaultExpression = "*/*/* *:*:00";

    public static string? Validate(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return "Cron expression is required.";
        try
        {
            GXDateTime schedule = new(expression, CultureInfo.GetCultureInfo("en-US"));
            return GXDateTime.GetNextScheduledDates(DateTime.UtcNow, schedule, 1).Length == 0
                ? "Cron expression has no future occurrence." : null;
        }
        catch (ArgumentException ex)
        {
            return $"Invalid schedule expression. Use weekday/month/year time. {ex.Message}";
        }
    }
}

