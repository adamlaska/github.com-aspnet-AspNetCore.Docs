// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Features;

namespace SignalRConnectionHandlerSample
{
    public class MessagesConnectionHandler : ConnectionHandler
    {
        private ConcurrentDictionary<string, ConnectionContext> Connections { get; } = new ConcurrentDictionary<string, ConnectionContext>();

        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            var connectionName = Guid.NewGuid().ToString();
            var transportType = connection.Features.Get<IHttpTransportFeature>()?.TransportType;

            Connections.TryAdd(connectionName, connection);

            await Broadcast($"{connectionName} connected ({transportType})");

            try
            {
                while (true)
                {
                    var result = await connection.Transport.Input.ReadAsync();
                    var buffer = result.Buffer;

                    try
                    {
                        if (!buffer.IsEmpty)
                        {
                            // We can avoid the copy here but we'll deal with that later
                            var text = Encoding.UTF8.GetString(buffer.ToArray());
                            text = $"{connectionName}: {text}";
                            await Broadcast(Encoding.UTF8.GetBytes(text));
                        }
                        else if (result.IsCompleted)
                        {
                            break;
                        }
                    }
                    finally
                    {
                        connection.Transport.Input.AdvanceTo(buffer.End);
                    }
                }
            }
            finally
            {
                Connections.TryRemove(connectionName, out _);

                await Broadcast($"{connectionName} disconnected ({transportType})");
            }
        }

        private Task Broadcast(string text)
        {
            return Broadcast(Encoding.UTF8.GetBytes(text));
        }

        private Task Broadcast(byte[] payload)
        {
            var tasks = new List<Task>(Connections.Count);
            foreach (var pair in Connections)
            {
                var context = pair.Value;
                // TODO: Use a connection-level lock to avoid concurrent writes.
                // The default Pipe holds the lock between GetMemory and Advance,
                // so it's not technically necessary, but it's best practice.
                tasks.Add(context.Transport.Output.WriteAsync(payload).AsTask());
            }

            return Task.WhenAll(tasks);
        }
    }
}
