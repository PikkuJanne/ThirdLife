using System.Buffers.Binary;

namespace ThirdLife.Broker.Protocol.Transport;

/// <summary>Little-endian length-prefixed broker messages with a bound before payload allocation.</summary>
public static class BrokerPipeFrames
{
    public static async Task<BrokerMessage> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[sizeof(int)];
        var headerBytes = await stream.ReadAsync(header, cancellationToken).ConfigureAwait(false);
        if (headerBytes == 0)
        {
            throw new EndOfStreamException("The broker connection has closed.");
        }

        try
        {
            await stream.ReadExactlyAsync(header.AsMemory(headerBytes), cancellationToken).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length is < 1 or > BrokerProtocolLimits.MaximumMessageBytes)
            {
                throw new InvalidDataException("The broker frame length is outside the permitted range.");
            }

            var payload = new byte[length];
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            return BrokerProtocolCodec.Parse(payload);
        }
        catch (EndOfStreamException)
        {
            throw new InvalidDataException("The broker frame ended before its declared size.");
        }
    }

    public static async Task WriteAsync(Stream stream, BrokerMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var payload = BrokerProtocolCodec.Serialize(message);
        var frame = new byte[sizeof(int) + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame.AsSpan(sizeof(int)));
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
    }
}
