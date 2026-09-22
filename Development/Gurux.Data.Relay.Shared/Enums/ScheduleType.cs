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


namespace Gurux.Data.Relay.Shared.Enums;

/// <summary>
/// Represents the type of schedule for a task or job.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ScheduleType>))]
public enum ScheduleType : byte
{
    /// <summary>
    /// The task or job is scheduled to run manually.
    /// </summary>
    Manual,
    /// <summary>
    /// The task or job is scheduled to run at regular intervals.
    /// </summary>
    Interval,
    /// <summary>
    /// The task or job is scheduled to run a daily.
    /// </summary>
    Daily,
    /// <summary>
    /// The task or job is scheduled to run based on a cron expression.
    /// </summary>
    /// <remarks>Cron expressions are used to define time-based job schedules.</remarks>
    Cron,
    /// <summary>
    /// The task or job is scheduled to run continuously.
    /// </summary>
    Continuous,
    /// <summary>
    /// The task or job is scheduled to run when there is a change in the database.
    /// </summary>
    DatabaseChange
}
