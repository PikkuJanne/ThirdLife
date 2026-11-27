using ThirdLife.Core.Jobs;
using ThirdLife.Inventory.Windows;
using static ThirdLife.Inventory.Windows.WindowsColdBootEvidence;

namespace ThirdLife.Inventory.Tests;

public sealed class WindowsColdBootEvidenceTests
{
    private static readonly DateTimeOffset Requested = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(4u, ColdBootKind.OrderlyPowerOff)]
    [InlineData(5u, ColdBootKind.Restart)]
    [InlineData(6u, ColdBootKind.OrderlyPowerOff)]
    [InlineData(3u, ColdBootKind.Ambiguous)]
    public void DistinguishesFullPowerOffFromWarmRestartAndUnknownPowerActions(uint action, ColdBootKind kind)
    {
        var observation = Classify(Transition(action), 42, Requested, Requested.AddMinutes(1));

        Assert.Equal(new ColdBootObservation(42, Requested.AddSeconds(20), kind), observation);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    public void HybridOrResumeCannotReuseOlderFullBootEvidence(uint bootType)
    {
        var events = Transition();
        events.Insert(0, new(EventId.BootType, 71, Requested.AddSeconds(50), bootType));

        Assert.Equal(ColdBootKind.HybridOrResume, Classify(events, 42, Requested, Requested.AddMinutes(1))?.Kind);
    }

    [Fact]
    public void RefusesAbsentStaleOrFutureStartup()
    {
        Assert.Null(Classify([], 42, Requested, Requested.AddMinutes(1)));
        Assert.Null(Classify(Transition(), 42, Requested.AddSeconds(21), Requested.AddMinutes(1)));
        Assert.Null(Classify(Transition(), 42, Requested, Requested.AddSeconds(20)));
    }

    [Fact]
    public void RefusesMisboundOrUncleanPreviousBoot()
    {
        Assert.Equal(ColdBootKind.Ambiguous, Classify(Transition(), 43, Requested, Requested.AddMinutes(1))?.Kind);
        var events = Transition();
        events[1] = events[1] with { Good = false };
        Assert.Equal(ColdBootKind.Ambiguous, Classify(events, 42, Requested, Requested.AddMinutes(1))?.Kind);
    }

    [Fact]
    public void RefusesContradictoryOrOutOfOrderShutdownEvidence()
    {
        var events = Transition();
        events.Insert(0, new(EventId.UnexpectedShutdown, 71, Requested.AddSeconds(22)));
        Assert.Equal(ColdBootKind.Ambiguous, Classify(events, 42, Requested, Requested.AddMinutes(1))?.Kind);

        events = Transition();
        events.Insert(4, new(EventId.ShutdownAction, 19, Requested.AddSeconds(8), 5));
        Assert.Equal(ColdBootKind.Ambiguous, Classify(events, 42, Requested, Requested.AddMinutes(1))?.Kind);

        events = Transition();
        events[4] = events[4] with { Time = Requested.AddSeconds(-5) };
        Assert.Equal(ColdBootKind.Ambiguous, Classify(events, 42, Requested, Requested.AddMinutes(1))?.Kind);

        events = Transition();
        events.RemoveAt(3);
        Assert.Equal(ColdBootKind.Ambiguous, Classify(events, 42, Requested, Requested.AddMinutes(1))?.Kind);
    }

    private static List<PowerEvent> Transition(uint action = 6) =>
    [
        new(EventId.BootType, 70, Requested.AddSeconds(21), 0),
        new(EventId.PreviousShutdownStatus, 60, Requested.AddSeconds(20), 41, Good: true),
        new(EventId.Started, 50, Requested.AddSeconds(20)),
        new(EventId.Stopped, 30, Requested.AddSeconds(10)),
        new(EventId.ShutdownAction, 20, Requested.AddSeconds(9), action),
        new(EventId.BootType, 10, Requested.AddDays(-1), 0),
    ];
}
