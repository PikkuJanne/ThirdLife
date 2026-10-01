using System.Runtime.InteropServices;
using System.Text.Json;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Inventory.Tests;

public sealed class DeviceClassSourceTests
{
    [Theory]
    [InlineData(0, false, 0)]
    [InlineData(1, true, 1)]
    [InlineData(2, true, 1)]
    [InlineData(256, true, 128)]
    public void CompleteCameraVariantsRetainPresenceAndDeviceProblems(int count, bool present, uint problems)
    {
        var native = new Native { DeviceCount = count, Problem = node => new(node % 2 == 0) };
        var result = new WindowsDeviceClassSource(native).Read(DeviceClass.Camera, CancellationToken.None);

        Assert.Equal(DeviceEnumerationStatus.Available, result.Status);
        Assert.Equal(DeviceClass.Camera, result.Class);
        Assert.Equal(present, result.Present);
        Assert.Equal(problems, result.ProblemCount);
        Assert.Null(result.NativeErrorCode);
        Assert.True(result.CleanupComplete);
        Assert.Equal(count + 1, native.NextCalls);
        Assert.Equal(count, native.ProblemCalls);
        Assert.Equal(1, native.CloseCalls);
    }

    [Fact]
    public void OversizedSetFailsClosedAfterOneSentinelWithoutQueryingItsProblem()
    {
        var native = new Native { DeviceCount = 1000 };
        var result = new WindowsDeviceClassSource(native).Read(DeviceClass.Camera, CancellationToken.None);

        Assert.Equal(DeviceEnumerationStatus.InvalidData, result.Status);
        Assert.Null(result.Present);
        Assert.Null(result.ProblemCount);
        Assert.Equal(257, native.NextCalls);
        Assert.Equal(256, native.ProblemCalls);
        Assert.Equal(1, native.CloseCalls);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(1314, true)]
    [InlineData(50, false)]
    [InlineData(31, false)]
    public void EnumerationErrorAfterAnObservedDeviceCannotPublishPresence(uint error, bool denied)
    {
        var native = new Native { DeviceCount = 3, Read = index => index == 1 ? new(error) : new(0, index) };
        var result = new WindowsDeviceClassSource(native).Read(DeviceClass.Keyboard, CancellationToken.None);

        Assert.Equal(denied ? DeviceEnumerationStatus.AccessDenied : DeviceEnumerationStatus.Unavailable, result.Status);
        Assert.Equal(DeviceClass.Keyboard, result.Class);
        Assert.Equal(error, result.NativeErrorCode);
        Assert.Null(result.Present);
        Assert.Null(result.ProblemCount);
        Assert.Equal(2, native.NextCalls);
        Assert.Equal(1, native.CloseCalls);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(1167, false)]
    public void DeviceStatusFailureMakesAffectedClassUnknown(uint error, bool denied)
    {
        var native = new Native { DeviceCount = 1, Problem = _ => new(null, error) };
        var result = new WindowsDeviceClassSource(native).Read(DeviceClass.Pointing, CancellationToken.None);

        Assert.Equal(denied ? DeviceEnumerationStatus.AccessDenied : DeviceEnumerationStatus.Unavailable, result.Status);
        Assert.Null(result.Present);
        Assert.Null(result.ProblemCount);
        Assert.Equal(error, result.NativeErrorCode);
        Assert.Equal(1, native.CloseCalls);
    }

    [Fact]
    public void InvalidProblemResultCannotInventAZeroProblemCount()
    {
        var native = new Native { DeviceCount = 1, Problem = _ => new(null) };
        var result = new WindowsDeviceClassSource(native).Read(DeviceClass.Display, CancellationToken.None);

        Assert.Equal(DeviceEnumerationStatus.InvalidData, result.Status);
        Assert.Null(result.Present);
        Assert.Null(result.ProblemCount);
        Assert.Equal(1, native.CloseCalls);
    }

    [Theory]
    [InlineData(-1, 5, true)]
    [InlineData(-1, 0, false)]
    [InlineData(0, 0, false)]
    public void MissingHandleNeverProvesAbsence(int handle, uint code, bool denied)
    {
        var native = new Native { OpenResult = new(new nint(handle), code) };
        var result = new WindowsDeviceClassSource(native).Read(DeviceClass.Camera, CancellationToken.None);

        Assert.Equal(denied ? DeviceEnumerationStatus.AccessDenied : DeviceEnumerationStatus.InvalidData, result.Status);
        Assert.Null(result.Present);
        Assert.Equal(0, native.NextCalls);
        Assert.Equal(0, native.CloseCalls);
    }

    [Fact]
    public void NonWindowsSourceIsUnavailableWithoutNativeCalls()
    {
        var native = new Native { IsSupported = false };
        var result = new WindowsDeviceClassSource(native).Read(DeviceClass.Camera, CancellationToken.None);

        Assert.Equal(DeviceEnumerationStatus.Unavailable, result.Status);
        Assert.Null(result.Present);
        Assert.Equal(0, native.OpenCalls);
        Assert.Equal(0, native.CloseCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(99)]
    public void UnownedClassCannotOpenAnArbitraryNativeSelection(int deviceClass)
    {
        var native = new Native();
        var result = new WindowsDeviceClassSource(native).Read((DeviceClass)deviceClass, CancellationToken.None);

        Assert.Equal(DeviceEnumerationStatus.InvalidData, result.Status);
        Assert.Equal(0, native.OpenCalls);
    }

    [Fact]
    public void PreCancelledReadMakesNoNativeCalls()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var native = new Native();

        Assert.Throws<OperationCanceledException>(() => new WindowsDeviceClassSource(native)
            .Read(DeviceClass.Camera, cancellation.Token));
        Assert.Equal(0, native.OpenCalls);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("next")]
    [InlineData("problem")]
    [InlineData("close")]
    public void CancellationAtEveryOwnedBoundaryReleasesTheHandle(string cancelAt)
    {
        using var cancellation = new CancellationTokenSource();
        var native = new Native { DeviceCount = 2, OnCall = call => { if (call == cancelAt) { cancellation.Cancel(); } } };

        Assert.Throws<OperationCanceledException>(() => new WindowsDeviceClassSource(native)
            .Read(DeviceClass.Camera, cancellation.Token));
        Assert.Equal(1, native.CloseCalls);
        Assert.InRange(native.NextCalls, 0, cancelAt == "close" ? 3 : 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedCleanupIsReportedAndOverridesCancellation(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var native = new Native
        {
            CloseError = 5,
            OnCall = call => { if (cancel && call == "close") { cancellation.Cancel(); } },
        };
        var source = new WindowsDeviceClassSource(native);

        if (cancel)
        {
            Assert.Throws<DeviceEnumerationCleanupException>(() => source.Read(DeviceClass.Camera, cancellation.Token));
        }
        else
        {
            Assert.False(source.Read(DeviceClass.Camera, cancellation.Token).CleanupComplete);
        }
        Assert.Equal(1, native.CloseCalls);
    }

    [Fact]
    public void CleanupExceptionCannotBecomeSuccessOrExposeItsText()
    {
        const string canary = "SYNTHETIC-PRIVATE-DEVICE-NAME";
        var native = new Native { OnCall = call => { if (call == "close") { throw new InvalidOperationException(canary); } } };
        var result = new WindowsDeviceClassSource(native).Read(DeviceClass.Camera, CancellationToken.None);

        Assert.False(result.CleanupComplete);
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public void SourceExceptionIsSanitizedAndOwnedHandleIsReleased()
    {
        const string canary = "SYNTHETIC-PRIVATE-SSID-MAC-PATH";
        var native = new Native { Read = _ => throw new InvalidOperationException(canary) };
        var result = new WindowsDeviceClassSource(native).Read(DeviceClass.Camera, CancellationToken.None);

        Assert.Equal(DeviceEnumerationStatus.InvalidData, result.Status);
        Assert.Null(result.Present);
        Assert.Equal(1, native.CloseCalls);
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(0x81, true)]
    [InlineData(0xC3, true)]
    [InlineData(4, false)]
    [InlineData(8, false)]
    [InlineData(0x8C, false)]
    public void TouchAndPenSessionVariantsDoNotBecomeFunctionTests(int flags, bool touch)
    {
        var native = new Native { DigitizerFlags = flags };
        var result = new WindowsDeviceClassSource(native).Read(DeviceClass.Touch, CancellationToken.None);

        Assert.Equal(DeviceEnumerationStatus.Available, result.Status);
        Assert.Equal(touch, result.Present);
        Assert.Null(result.ProblemCount);
        Assert.Null(result.NativeErrorCode);
        Assert.Equal(0, native.OpenCalls);
        Assert.Equal(0, native.CloseCalls);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0x40, false)]
    [InlineData(0x80, false)]
    [InlineData(0x10, true)]
    [InlineData(0x100, true)]
    [InlineData(-1, true)]
    public void MissingOrUnrecognizedTouchSessionValuesRemainUnknown(int flags, bool invalid)
    {
        var result = WindowsDeviceClassSource.NormalizeDigitizer(flags);

        Assert.Equal(invalid ? DeviceEnumerationStatus.InvalidData : DeviceEnumerationStatus.Unavailable, result.Status);
        Assert.Null(result.Present);
        Assert.Null(result.ProblemCount);
    }

    [Fact]
    public void ExactSelectionsSeparateCameraFromScannersAndCaptureFromRender()
    {
        var expected = new Dictionary<DeviceClass, DeviceClassSelection>
        {
            [DeviceClass.Bluetooth] = new(new("0850302a-b344-4fda-9be9-90576b8d46f0"), true),
            [DeviceClass.Audio] = new(new("e6327cad-dcec-4949-ae8a-991e976a79d2"), true),
            [DeviceClass.Camera] = new(new("e5323777-f976-4f5b-9b55-b94699c46e44"), true),
            [DeviceClass.Microphone] = new(new("2eef81be-33fa-4800-9670-1cd474972c3f"), true),
            [DeviceClass.Display] = new(new("4d36e96e-e325-11ce-bfc1-08002be10318"), false),
            [DeviceClass.Keyboard] = new(new("4d36e96b-e325-11ce-bfc1-08002be10318"), false),
            [DeviceClass.Pointing] = new(new("4d36e96f-e325-11ce-bfc1-08002be10318"), false),
            [DeviceClass.UsbController] = new(new("3abf6f2d-71c4-462a-8a92-1e6861e6af27"), true),
        };
        foreach (var (deviceClass, selection) in expected)
        {
            Assert.Equal(selection, WindowsDeviceClassNativeApi.Selection(deviceClass));
            var native = new Native { DeviceCount = 1 };
            Assert.True(new WindowsDeviceClassSource(native).Read(deviceClass, CancellationToken.None).Present);
            Assert.Equal(deviceClass, native.OpenedClass);
        }
        Assert.Equal(IntPtr.Size == 8 ? 32 : 28, Marshal.SizeOf<WindowsDeviceClassNativeApi.DeviceInfoData>());
    }

    private sealed class Native : IDeviceClassNativeApi
    {
        public bool IsSupported { get; init; } = true;
        public int DeviceCount { get; init; }
        public DeviceClassNativeOpen OpenResult { get; init; } = new(new nint(42));
        public Func<uint, DeviceClassNativeRow>? Read { get; init; }
        public Func<uint, DeviceClassNativeProblem> Problem { get; init; } = _ => new(false);
        public Action<string>? OnCall { get; init; }
        public uint CloseError { get; init; }
        public int DigitizerFlags { get; init; }
        public int OpenCalls { get; private set; }
        public int NextCalls { get; private set; }
        public int ProblemCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public DeviceClass? OpenedClass { get; private set; }

        public DeviceClassNativeOpen Open(DeviceClass deviceClass)
        {
            OpenCalls++;
            OpenedClass = deviceClass;
            OnCall?.Invoke("open");
            return OpenResult;
        }

        public DeviceClassNativeRow Next(nint handle, uint index)
        {
            Assert.Equal(OpenResult.Handle, handle);
            NextCalls++;
            OnCall?.Invoke("next");
            return Read?.Invoke(index) ?? (index < DeviceCount ? new(0, index) : new(259));
        }

        public DeviceClassNativeProblem ReadProblem(uint deviceNode)
        {
            ProblemCalls++;
            OnCall?.Invoke("problem");
            return Problem(deviceNode);
        }

        public uint Close(nint handle)
        {
            Assert.Equal(OpenResult.Handle, handle);
            CloseCalls++;
            OnCall?.Invoke("close");
            return CloseError;
        }

        public int ReadDigitizerFlags() => DigitizerFlags;
    }
}
