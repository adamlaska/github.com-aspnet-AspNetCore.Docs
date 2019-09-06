// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
        // TODO: Use concurrent collection, lock, or remove connection list
        private List<ConnectionContext> Connections { get; } = new List<ConnectionContext>();

        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            Connections.Add(connection);

            var transportType = connection.Features.Get<IHttpTransportFeature>()?.TransportType;

            await Broadcast($"{connection.ConnectionId} connected ({transportType})");

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
                            text = $"{connection.ConnectionId}: {text}";
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
                Connections.Remove(connection);

                await Broadcast($"{connection.ConnectionId} disconnected ({transportType})");
            }
        }

        private Task Broadcast(string text)
        {
            return Broadcast(Encoding.UTF8.GetBytes(text));
        }

        private Task Broadcast(byte[] payload)
        {
            var tasks = new List<Task>(Connections.Count);
            foreach (var c in Connections)
            {
                tasks.Add(c.Transport.Output.WriteAsync(payload).AsTask());
            }

            return Task.WhenAll(tasks);
        }
    }
}
