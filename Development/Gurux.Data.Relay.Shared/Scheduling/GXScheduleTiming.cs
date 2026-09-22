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
using Gurux.Data.Relay.Shared.Enums;
using System.Globalization;

namespace Gurux.Data.Relay.Shared.Scheduling;

public static class GXScheduleTiming
{
    public static DateTimeOffset? Initial(GXSchedule schedule, DateTimeOffset now) => Calculate(schedule, now, true);
    public static DateTimeOffset? Next(GXSchedule schedule, DateTimeOffset now) => Calculate(schedule, now, false);
    private static DateTimeOffset? Calculate(GXSchedule schedule, DateTimeOffset now, bool initial)
    {
        switch (schedule.Type)
        {
            case ScheduleType.Manual: return null;
            case ScheduleType.Interval:
                if (schedule.IntervalSeconds is not > 0) throw new InvalidOperationException("IntervalSeconds must be positive.");
                return initial ? now : now.AddSeconds(schedule.IntervalSeconds.Value);
            case ScheduleType.Continuous: return initial ? now : now.AddSeconds(1);
            case ScheduleType.Daily:
                if (!TimeOnly.TryParse(schedule.Time, out var time)) throw new InvalidOperationException("Daily schedule requires a valid Time.");
                var candidate = new DateTimeOffset(now.Year, now.Month, now.Day, time.Hour, time.Minute, time.Second, now.Offset);
                return candidate < now || (!initial && candidate == now) ? candidate.AddDays(1) : candidate;
            case ScheduleType.Cron:
                if (string.IsNullOrWhiteSpace(schedule.Expression)) throw new InvalidOperationException("Cron schedule requires an Expression.");
                DateTime next = GXDateTime.GetNextScheduledDates(now.UtcDateTime,
                    new GXDateTime(schedule.Expression, CultureInfo.GetCultureInfo("en-US")), initial ? 1 : 2)
                    .Skip(initial ? 0 : 1)
                    .FirstOrDefault();
                return next == default
                    ? throw new InvalidOperationException("Cron has no next occurrence.")
                    : new DateTimeOffset(DateTime.SpecifyKind(next, DateTimeKind.Utc));
            default: throw new InvalidOperationException("DatabaseChange is not a generic provider schedule.");
        }
    }
}
