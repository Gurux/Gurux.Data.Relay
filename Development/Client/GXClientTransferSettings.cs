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

using Gurux.Data.Relay.Configuration;

namespace Gurux.Data.Relay.Client;

internal static class GXClientTransferSettings
{
    public static GXTransferConfiguration GetEffective(GXClientConfiguration configuration)
    {
        configuration.EnsureRoutingIds();
        return GetEffective(configuration.Transports);
    }

    public static GXTransferConfiguration GetEffective(IEnumerable<GXTransportConfiguration> transports)
    {
        List<GXTransferConfiguration> settings = transports
            .Select(transport => transport.Transfer ?? new GXTransferConfiguration())
            .ToList();
        if (settings.Count == 0)
        {
            return new GXTransferConfiguration();
        }

        int pingAfterSeconds = settings
            .Where(item => item.PingAfterSeconds > 0)
            .Select(item => item.PingAfterSeconds)
            .DefaultIfEmpty(0)
            .Min();

        return new GXTransferConfiguration
        {
            BatchSize = settings.Min(item => Math.Max(1, item.BatchSize)),
            RetryCount = settings.Max(item => Math.Max(1, item.RetryCount)),
            RetryDelaySeconds = settings.Min(item => Math.Max(1, item.RetryDelaySeconds)),
            PingAfterSeconds = pingAfterSeconds,
            AllowConcurrentRuns = settings.All(item => item.AllowConcurrentRuns),
        };
    }
}



