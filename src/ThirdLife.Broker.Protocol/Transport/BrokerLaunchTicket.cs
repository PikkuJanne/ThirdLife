using System.Globalization;
using System.Security.Cryptography;

namespace ThirdLife.Broker.Protocol.Transport;

/// <summary>Opaque bootstrap for a single explicitly launched no-op session.</summary>
public sealed class BrokerLaunchTicket
{
    private const string Mode = "--thirdlife-broker-v1";

    private BrokerLaunchTicket(Guid pipeId, int clientProcessId, Guid correlationId,
        DateTimeOffset expiresAtUtc, string nonceSha256, string requestSha256)
    {
        PipeId = pipeId;
        ClientProcessId = clientProcessId;
        CorrelationId = correlationId;
        ExpiresAtUtc = expiresAtUtc;
        NonceSha256 = nonceSha256;
        RequestSha256 = requestSha256;
    }

    public Guid PipeId { get; }
    public int ClientProcessId { get; }
    public Guid CorrelationId { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public string NonceSha256 { get; }
    public string RequestSha256 { get; }

    public static BrokerLaunchTicket Create(int clientProcessId, BrokerRequest request)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(clientProcessId);
        ArgumentNullException.ThrowIfNull(request);
        var envelope = request.Envelope;
        return new BrokerLaunchTicket(Guid.NewGuid(), clientProcessId, envelope.CorrelationId, envelope.ExpiresAtUtc,
            Convert.ToHexStringLower(NonceDigest(envelope.Nonce)), BrokerProtocolCodec.ComputeRequestSha256(request));
    }

    public string[] CreateArguments() =>
    [
        Mode,
        PipeId.ToString("D"),
        ClientProcessId.ToString(CultureInfo.InvariantCulture),
        CorrelationId.ToString("D"),
        ExpiresAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
        NonceSha256,
        RequestSha256,
    ];

    public static BrokerLaunchTicket? TryParse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count != 7 || arguments[0] != Mode || !TryParseGuid(arguments[1], out var pipeId) ||
            !TryParseNumber(arguments[2], int.MaxValue, out var pid) || !TryParseGuid(arguments[3], out var correlation) ||
            !TryParseNumber(arguments[4], DateTimeOffset.MaxValue.UtcTicks, out var expiry) ||
            !IsLowerHexDigest(arguments[5]) || !IsLowerHexDigest(arguments[6]))
        {
            return null;
        }

        return new BrokerLaunchTicket(pipeId, (int)pid, correlation, new DateTimeOffset(expiry, TimeSpan.Zero),
            arguments[5], arguments[6]);
    }

    public bool MatchesEnvelope(BrokerEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return envelope.CorrelationId == CorrelationId && envelope.ExpiresAtUtc == ExpiresAtUtc &&
            CryptographicOperations.FixedTimeEquals(NonceDigest(envelope.Nonce), Convert.FromHexString(NonceSha256));
    }

    public bool MatchesRequest(BrokerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return MatchesEnvelope(request.Envelope) && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(RequestSha256),
            Convert.FromHexString(BrokerProtocolCodec.ComputeRequestSha256(request)));
    }

    private static bool TryParseGuid(string? text, out Guid id) =>
        Guid.TryParseExact(text, "D", out id) && id != Guid.Empty && text == id.ToString("D");

    private static bool TryParseNumber(string? text, long maximum, out long number) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0 &&
        number <= maximum && text == number.ToString(CultureInfo.InvariantCulture);

    private static bool IsLowerHexDigest(string? value) => value is { Length: 64 } &&
        value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static byte[] NonceDigest(BrokerNonce nonce) => SHA256.HashData(Convert.FromHexString(nonce.Value));
}
