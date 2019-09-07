// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Features;

namespace SignalRConnectionHandlerSample
{
    public class EchoConnectionHandler : ConnectionHandler
    {
        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            var transportType = connection.Features.Get<IHttpTransportFeature>()?.TransportType;
            var welcomeString = $"Welcome. A connection has been established with the '{transportType}' transport.\n\n";

            Write(connection.Transport.Output, welcomeString, Encoding.UTF8);
            await connection.Transport.Output.FlushAsync();

            while (true)
            {
                var result = await connection.Transport.Input.ReadAsync();
                var buffer = result.Buffer;

                try
                {
                    if (!buffer.IsEmpty)
                    {
                        if (buffer.IsSingleSegment)
                        {
                            await connection.Transport.Output.WriteAsync(buffer.First);
                        }
                        else
                        {
                            foreach (var segment in buffer)
                            {
                                await connection.Transport.Output.WriteAsync(segment);
                            }
                        }
                    }

                    if (result.IsCompleted)
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

        private static void Write(PipeWriter pipeWriter, string text, Encoding encoding)
        {
            var minimumByteSize = encoding.GetMaxByteCount(1);
            var encodedLength = encoding.GetByteCount(text);
            var destination = pipeWriter.GetSpan(minimumByteSize);

            if (encodedLength <= destination.Length)
            {
                // Just call Encoding.GetBytes if everything will fit into a single segment.
                var bytesWritten = encoding.GetBytes(text, destination);
                pipeWriter.Advance(bytesWritten);
            }
            else
            {
                WriteMultiSegmentEncoded(pipeWriter, text, encoding, destination, encodedLength, minimumByteSize);
            }
        }

        private static void WriteMultiSegmentEncoded(PipeWriter writer, string text, Encoding encoding, Span<byte> destination, int encodedLength, int minimumByteSize)
        {
            var encoder = encoding.GetEncoder();
            var source = text.AsSpan();
            var completed = false;
            var totalBytesUsed = 0;

            // This may be a bug, but encoder.Convert returns completed = true for UTF7 too early.
            // Therefore, we check encodedLength - totalBytesUsed too.
            while (!completed || encodedLength - totalBytesUsed != 0)
            {
                encoder.Convert(source, destination, flush: source.Length == 0, out var charsUsed, out var bytesUsed, out completed);
                totalBytesUsed += bytesUsed;

                writer.Advance(bytesUsed);
                source = source.Slice(charsUsed);

                destination = writer.GetSpan(minimumByteSize);
            }
        }
    }
}
