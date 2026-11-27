using System.Diagnostics;
using System.Runtime.InteropServices;
using ThirdLife.Core.Jobs;
using Native = ThirdLife.Inventory.Windows.WindowsEventLogNative;

namespace ThirdLife.Inventory.Windows;

/// <summary>Correlates a requested checkpoint with the later local Windows power transition.</summary>
public static class WindowsColdBootEvidence
{
    private const string PowerTransitions = "*[System[(Provider[@Name='Microsoft-Windows-Kernel-Boot'] and (EventID=20 or EventID=27)) or (Provider[@Name='Microsoft-Windows-Kernel-General'] and (EventID=12 or EventID=13)) or (Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=41 or EventID=109))]]";
    private static readonly string[] Fields =
    [
        "Event/System/EventID",
        "Event/System/EventRecordID",
        "Event/System/TimeCreated/@SystemTime",
        "Event/EventData/Data[@Name='BootType']",
        "Event/EventData/Data[@Name='LastBootId']",
        "Event/EventData/Data[@Name='LastShutdownGood']",
        "Event/EventData/Data[@Name='ShutdownActionType']",
    ];

    private const int VariantBytes = 16;
    private const int TypeOffset = 12;
    private const int MaximumRecords = 32;
    private const int TimeoutMilliseconds = 1000;
    private const int NoMoreItems = 259;

    /// <summary>Reads bounded, structured evidence from the local System event log.</summary>
    public static ColdBootObservation? Read(uint bootSession, DateTimeOffset checkpointRequestedUtc)
    {
        if (!OperatingSystem.IsWindows() || bootSession == 0) { return null; }

        try
        {
            var records = ReadRecentTransitions();
            return records is null ? null : Classify(records, bootSession, checkpointRequestedUtc, DateTimeOffset.UtcNow);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static List<PowerEvent>? ReadRecentTransitions()
    {
        var elapsed = Stopwatch.StartNew();
        using var query = new EventHandle();
        using var context = new EventHandle();
        using var entry = new EventHandle();
        // Local System channel in reverse record order. No rendered event text or identifiers.
        query.Value = Native.EvtQuery(0, "System", PowerTransitions, 0x201);
        if (query.Value == 0) { return null; }
        context.Value = Native.EvtCreateRenderContext(Fields.Length, Fields, 0);
        if (context.Value == 0) { return null; }

        int bufferBytes = Fields.Length * VariantBytes;
        var buffer = Marshal.AllocHGlobal(bufferBytes);
        try
        {
            var events = new List<PowerEvent>(MaximumRecords);
            int startups = 0;
            while (events.Count < MaximumRecords && elapsed.ElapsedMilliseconds < TimeoutMilliseconds)
            {
                int timeout = Math.Max(1, TimeoutMilliseconds - (int)elapsed.ElapsedMilliseconds);
                if (!Native.EvtNext(query.Value, 1, out entry.Value, timeout, 0, out var count))
                {
                    return Marshal.GetLastPInvokeError() == NoMoreItems ? events : null;
                }

                if (count != 1 || entry.Value == 0 ||
                    !Native.EvtRender(context.Value, entry.Value, 0, bufferBytes, buffer, out var used, out var properties) ||
                    used != bufferBytes || properties != Fields.Length || Decode(buffer) is not { } item || !entry.Close())
                {
                    return null;
                }

                events.Add(item);
                if (item.Id == EventId.BootType && ++startups == 2) { return events; }
            }

            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static PowerEvent? Decode(nint buffer)
    {
        const int UInt16 = 6;
        const int UInt32 = 8;
        const int UInt64 = 10;
        const int Boolean = 13;
        const int FileTime = 17;
        int Type(int index) => Marshal.ReadInt32(buffer, index * VariantBytes + TypeOffset);
        uint ReadUInt32(int index) => unchecked((uint)Marshal.ReadInt32(buffer, index * VariantBytes));

        if (Type(0) != UInt16 || Type(1) != UInt64 || Type(2) != FileTime) { return null; }
        var id = (EventId)Marshal.ReadInt16(buffer);
        long record = Marshal.ReadInt64(buffer, VariantBytes);
        long fileTime = Marshal.ReadInt64(buffer, 2 * VariantBytes);
        if (record <= 0 || fileTime < DateTimeOffset.UnixEpoch.ToFileTime() || fileTime > DateTimeOffset.MaxValue.ToFileTime())
        {
            return null;
        }

        var time = new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime));
        return id switch
        {
            EventId.BootType when Type(3) == UInt32 => new(id, record, time, ReadUInt32(3)),
            EventId.PreviousShutdownStatus when Type(4) == UInt32 && Type(5) == Boolean =>
                new(id, record, time, ReadUInt32(4), ReadUInt32(5) == 1),
            EventId.ShutdownAction when Type(6) == UInt32 => new(id, record, time, ReadUInt32(6)),
            EventId.Started or EventId.Stopped or EventId.UnexpectedShutdown => new(id, record, time),
            _ => null,
        };
    }

    internal static ColdBootObservation? Classify(
        IReadOnlyList<PowerEvent> events, uint bootSession, DateTimeOffset requested, DateTimeOffset now)
    {
        var bootType = events.FirstOrDefault(item => item.Id == EventId.BootType);
        if (bootType is null || bootType.Time <= requested || bootType.Time > now) { return null; }
        if (bootType.Value is 1 or 2) { return new(bootSession, bootType.Time, ColdBootKind.HybridOrResume); }

        var transition = events.SkipWhile(item => item.Record > bootType.Record).Skip(1)
            .TakeWhile(item => item.Id != EventId.BootType).ToArray();
        var started = SingleEvent(transition, EventId.Started);
        var stopped = SingleEvent(transition, EventId.Stopped);
        var action = SingleEvent(transition, EventId.ShutdownAction);
        var priorStatus = SingleEvent(transition, EventId.PreviousShutdownStatus);
        if (bootType.Value != 0 || started is null || stopped is null || action is null || priorStatus is null ||
            bootSession < 2 || priorStatus.Value != bootSession - 1 || !priorStatus.Good ||
            action.Record >= stopped.Record || stopped.Record >= started.Record || started.Record >= priorStatus.Record ||
            priorStatus.Record >= bootType.Record || action.Time <= requested || action.Time > stopped.Time ||
            stopped.Time >= started.Time || started.Time > priorStatus.Time || priorStatus.Time > bootType.Time ||
            events.Any(item => item.Id == EventId.UnexpectedShutdown && item.Record > stopped.Record))
        {
            return new(bootSession, started?.Time ?? bootType.Time, ColdBootKind.Ambiguous);
        }

        // Windows POWER_ACTION values: Shutdown (S5), ShutdownReset, ShutdownOff (S5).
        var kind = action.Value switch
        {
            4 or 6 => ColdBootKind.OrderlyPowerOff,
            5 => ColdBootKind.Restart,
            _ => ColdBootKind.Ambiguous,
        };
        return new(bootSession, started.Time, kind);
    }

    private static PowerEvent? SingleEvent(IEnumerable<PowerEvent> events, EventId id)
    {
        var matching = events.Where(item => item.Id == id).Take(2).ToArray();
        return matching.Length == 1 ? matching[0] : null;
    }

    internal enum EventId
    {
        Started = 12,
        Stopped = 13,
        PreviousShutdownStatus = 20,
        BootType = 27,
        UnexpectedShutdown = 41,
        ShutdownAction = 109,
    }

    internal sealed record PowerEvent(EventId Id, long Record, DateTimeOffset Time, uint Value = 0, bool Good = false);

    private sealed class EventHandle : IDisposable
    {
        internal nint Value;

        internal bool Close()
        {
            if (Value == 0) { return true; }
            var value = Value;
            Value = 0;
            return Native.EvtClose(value);
        }

        public void Dispose() => Close();
    }
}
