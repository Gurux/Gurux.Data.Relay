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

using Microsoft.Extensions.Logging;

namespace Gurux.Data.Relay.Log;

public sealed class GXEventLogLoggerProvider : ILoggerProvider
{
    private readonly IGXEventLogService _eventLogService;

    public GXEventLogLoggerProvider(IGXEventLogService eventLogService)
    {
        _eventLogService = eventLogService;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new GXEventLogLogger(categoryName, _eventLogService);
    }

    public void Dispose()
    {
    }

    private sealed class GXEventLogLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly IGXEventLogService _eventLogService;

        public GXEventLogLogger(string categoryName, IGXEventLogService eventLogService)
        {
            _categoryName = categoryName;
            _eventLogService = eventLogService;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            // Reading live views produces HTTP/SignalR diagnostics. Persisting those
            // as events would trigger another read and create a notification loop.
            if (_categoryName.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                && logLevel < LogLevel.Warning) return false;
            return _eventLogService.IsEnabled(logLevel);
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            try
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                string message = formatter(state, exception);
                IReadOnlyDictionary<string, object?>? values = state is IReadOnlyList<KeyValuePair<string, object?>> pairs
                    ? pairs
                        .Where(item => item.Key is not null)
                        .GroupBy(item => item.Key)
                        .ToDictionary(item => item.Key, item => item.Last().Value)
                    : null;
                _eventLogService.Write(logLevel, _categoryName, eventId, message, exception, values);
            }
            catch
            {
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}

