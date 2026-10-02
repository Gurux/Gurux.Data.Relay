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

using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Web.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Gurux.Data.Relay.Web.Server.Controllers;

[ApiController]
[Route("api/update/software")]
public sealed class SoftwareUpdateController(GXSoftwareUpdateService updates) : ControllerBase
{
    /// <summary>Read the cached software update status without contacting GitHub.</summary>
    [HttpGet]
    public GXSoftwareUpdateStatus GetStatus() => updates.Status;

    /// <summary>Check for a newer application release. Does not download or install files.</summary>
    [HttpPost("check")]
    public Task<GXSoftwareUpdateStatus> Check(CancellationToken cancellationToken)
        => updates.CheckAsync(cancellationToken);
}
