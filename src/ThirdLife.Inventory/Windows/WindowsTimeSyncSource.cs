using System.Globalization;
using System.Runtime.InteropServices;
using Native = ThirdLife.Inventory.Windows.WindowsEventLogNative;
using SyncTime = ThirdLife.Inventory.Windows.SystemInventorySourceValue<long>;

namespace ThirdLife.Inventory.Windows;

internal static class WindowsTimeSyncSource
{
    private const string Channel = "Microsoft-Windows-Time-Service/Operational";
    private const string LatestStatus = "*[System[Provider[@Name='Microsoft-Windows-Time-Service'] and EventID=260]]";
    private static readonly string[] Fields =
    [
        "Event/System/TimeCreated/@SystemTime",
        "Event/EventData/Data[@Name='LastSuccessfulSyncTime']",
        "Event/EventData/Data[@Name='LastSyncError']",
    ];

    private const int BufferBytes = 256;
    private const int VariantBytes = 16;
    private const int TypeOffset = 12;
    private const int MaximumUtcCharacters = 28;

    internal static SyncTime Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) { return SyncTime.Unavailable(); }
        bool clean = true;
        try
        {
            var value = ReadLatest(cancellationToken, ref clean);
            return clean ? value : throw new SecuritySourceCleanupException();
        }
        catch (Exception) when (!clean)
        {
            throw new SecuritySourceCleanupException();
        }
    }

    private static SyncTime ReadLatest(CancellationToken cancellationToken, ref bool clean)
    {
        nint query = 0, record = 0, context = 0;
        try
        {
            // Local channel, reverse record order. Do not skip a newer status reporting an error.
            query = Native.EvtQuery(0, Channel, LatestStatus, 0x201);
            if (query == 0) { return MapFailure(Marshal.GetLastPInvokeError()); }
            if (!Native.EvtNext(query, 1, out record, 500, 0, out var count)) { return MapFailure(Marshal.GetLastPInvokeError()); }
            if (count != 1 || record == 0) { return SyncTime.InvalidData(); }
            cancellationToken.ThrowIfCancellationRequested();
            context = Native.EvtCreateRenderContext(Fields.Length, Fields, 0);
            if (context == 0) { return MapFailure(Marshal.GetLastPInvokeError()); }
            return ReadFields(context, record);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return SyncTime.Unavailable();
        }
        finally
        {
            clean &= Close(context);
            clean &= Close(record);
            clean &= Close(query);
        }
    }

    private static SyncTime ReadFields(nint context, nint record)
    {
        // Only the three selected EVT_VARIANTs and their bounded scalar data are rendered.
        var buffer = Marshal.AllocHGlobal(BufferBytes);
        try
        {
            return Native.EvtRender(context, record, 0, BufferBytes, buffer, out var used, out var count)
                ? DecodeFields(buffer, used, count, DateTimeOffset.UtcNow)
                : MapFailure(Marshal.GetLastPInvokeError());
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static SyncTime DecodeFields(nint buffer, int used, int count, DateTimeOffset now)
    {
        var headerBytes = Fields.Length * VariantBytes;
        if (buffer == 0 || count != Fields.Length || used < headerBytes || used > BufferBytes) { return SyncTime.InvalidData(); }
        int recordType = Marshal.ReadInt32(buffer, TypeOffset);
        int timestampType = Marshal.ReadInt32(buffer, VariantBytes + TypeOffset);
        int errorType = Marshal.ReadInt32(buffer, 2 * VariantBytes + TypeOffset);
        if (recordType == 0 || timestampType == 0 || errorType == 0) { return SyncTime.Unavailable(); }
        // The Windows Time event contract supplies FILETIME, Unicode string and UInt64 respectively.
        if (recordType != 17 || timestampType != 1 || errorType != 10) { return SyncTime.InvalidData(); }
        var timestamp = Marshal.ReadIntPtr(buffer, VariantBytes);
        long offset = timestamp - buffer;
        if (offset < headerBytes || offset > used - sizeof(char) || offset % sizeof(char) != 0)
        {
            return SyncTime.InvalidData();
        }
        int availableCharacters = (used - (int)offset) / sizeof(char);
        for (int length = 0; length <= MaximumUtcCharacters && length < availableCharacters; length++)
        {
            if (Marshal.ReadInt16(timestamp, length * sizeof(char)) == 0)
            {
                return ParseStatus(Marshal.PtrToStringUni(timestamp, length), Marshal.ReadInt64(buffer),
                    Marshal.ReadInt64(buffer, 2 * VariantBytes), now);
            }
        }
        return SyncTime.InvalidData();
    }

    internal static SyncTime ParseStatus(string? lastSuccessfulSync, long eventFileTime, long lastError, DateTimeOffset now)
    {
        if (lastError != 0) { return SyncTime.Unavailable(); }
        const string utcFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";
        if (!DateTimeOffset.TryParseExact(lastSuccessfulSync, utcFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var synced) ||
            synced <= DateTimeOffset.UnixEpoch || eventFileTime > now.ToFileTime() || synced.ToFileTime() > eventFileTime)
        {
            return SyncTime.InvalidData();
        }
        return SyncTime.Available(synced.ToUnixTimeSeconds());
    }

    internal static SyncTime MapFailure(int error) => error switch
    {
        5 => SyncTime.AccessDenied(),
        122 => SyncTime.InvalidData(), // Selected fields exceed the fixed render buffer.
        _ => SyncTime.Unavailable(),
    };

    private static bool Close(nint handle) => handle == 0 || Native.EvtClose(handle);
}
