using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Finalization;
using ThirdLife.Inventory.Windows;
using Xunit.Abstractions;
using Kind = ThirdLife.Core.Finalization.WorkshopArtifactKind;
using Status = ThirdLife.Core.Finalization.WorkshopArtifactStatus;

namespace ThirdLife.Inventory.Tests;

public sealed class WorkshopArtifactDetectorTests(ITestOutputHelper output)
{
    [Fact]
    public async Task OnlyExactConfiguredNamesAreQueriedAndResultContainsNoNames()
    {
        var queries = new List<(Kind, string)>();
        var detector = new WorkshopArtifactDetector((kind, name, _) =>
        {
            if (name is null) { return Status.Unavailable; }
            queries.Add((kind, name));
            return name switch
            {
                "WorkshopAccountA" => Status.AccessDenied,
                "WorkshopAccountB" => Status.AbsentInCheckedScope,
                "WorkshopShare" => Status.Present,
                _ => throw new InvalidOperationException(),
            };
        });

        var findings = await detector.DetectAsync(["WorkshopAccountA", "WorkshopAccountB"], ["WorkshopShare"]);

        Assert.Equal([(Kind.ConfiguredLocalAccounts, "WorkshopAccountA"), (Kind.ConfiguredLocalAccounts, "WorkshopAccountB"),
            (Kind.ConfiguredSmbShares, "WorkshopShare")], queries);
        Assert.Equal(Status.AccessDenied, State(findings, Kind.ConfiguredLocalAccounts));
        Assert.Equal(Status.Present, State(findings, Kind.ConfiguredSmbShares));
        var scan = new WorkshopArtifactScan(1, DateTimeOffset.UtcNow, "evidence-1", WorkshopArtifactScan.CurrentDetectorRevision,
            ProvenanceKind.SyntheticFixture, findings);
        var json = JsonSerializer.Serialize(scan);
        Assert.DoesNotContain("WorkshopAccountA", json);
        Assert.DoesNotContain("WorkshopAccountB", json);
        Assert.DoesNotContain("WorkshopShare", json);
        var reloaded = Assert.IsType<WorkshopArtifactScan>(JsonSerializer.Deserialize<WorkshopArtifactScan>(json));
        Assert.Equal(findings, reloaded.Findings);
    }

    [Fact]
    public async Task MissingListsRemainUnknownAndNoAccountOrShareLookupRuns()
    {
        var detector = new WorkshopArtifactDetector((kind, name, _) =>
        {
            Assert.Null(name);
            Assert.DoesNotContain(kind, new[] { Kind.ConfiguredLocalAccounts, Kind.ConfiguredSmbShares });
            return Status.AbsentInCheckedScope;
        });

        var findings = await detector.DetectAsync();

        Assert.Equal(Status.TargetsNotConfigured, State(findings, Kind.ConfiguredLocalAccounts));
        Assert.Equal(Status.TargetsNotConfigured, State(findings, Kind.ConfiguredSmbShares));
    }

    [Theory]
    [InlineData("example\\WorkshopAccount")]
    [InlineData("WorkshopAccount@example.invalid")]
    [InlineData("Works*")]
    [InlineData(" WorkshopAccount")]
    public async Task AccountTargetsRejectQualifiedOrPatternNamesWithoutEchoingInput(string name)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => new WorkshopArtifactDetector().DetectAsync([name], null));
        Assert.DoesNotContain(name, error.Message);
    }

    [Fact]
    public async Task TargetListsHaveAStableCapAndRejectDuplicateNames()
    {
        var detector = new WorkshopArtifactDetector();
        await Assert.ThrowsAsync<ArgumentException>(() => detector.DetectAsync(null, ["Sample", "SAMPLE"]));
        await Assert.ThrowsAsync<ArgumentException>(() => detector.DetectAsync(null,
            Enumerable.Range(0, WorkshopArtifactDetector.MaximumConfiguredTargets + 1).Select(index => $"Share{index}")));
    }

    [Fact]
    public async Task CancellationAndConcurrentRetryKeepAtMostOneOwnedScan()
    {
        using var signal = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var detector = new WorkshopArtifactDetector((_, _, _) =>
        {
            started.TrySetResult();
            signal.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
            released.TrySetResult();
            return Status.Present;
        });

        var scan = detector.DetectAsync(cancellation.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
            Assert.All(await detector.DetectAsync(), finding => Assert.Equal(Status.Unavailable, finding.Status));
        }
        finally
        {
            signal.Set();
            await released.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void FixedRootsDetectExactArtifactsAndPreserveSensitiveDecoys()
    {
        using var fixture = new ArtifactFixture();
        var battery = fixture.CreateFolder("ThirdLife", "SetupCore", "BatteryReports");
        var reports = fixture.CreateFolder("ThirdLife", "SetupCore", "WorkshopReports");
        var decoy = fixture.CreateFile(reports, "private-recipient-note.txt");
        fixture.CreateFile(reports, "workshop-non-generated.txt");
        fixture.CreateFolder("Downloads", $"ThirdLife-package-{Guid.NewGuid():N}");

        Assert.Equal(Status.AbsentInCheckedScope, fixture.Inspect(Kind.BatteryReportResidue));
        Assert.Equal(Status.AbsentInCheckedScope, fixture.Inspect(Kind.WorkshopReportCopies));
        Assert.Equal(Status.AbsentInCheckedScope, fixture.Inspect(Kind.PackageStagingResidue));

        var raw = fixture.CreateFile(battery, $"report-{Guid.NewGuid():N}.xml");
        var report = fixture.CreateFile(reports, $"workshop-{Guid.NewGuid():N}.txt");
        var packageStage = fixture.CreateFolder($"ThirdLife-package-{Guid.NewGuid():N}");

        Assert.Equal(Status.Present, fixture.Inspect(Kind.BatteryReportResidue));
        Assert.Equal(Status.Present, fixture.Inspect(Kind.WorkshopReportCopies));
        Assert.Equal(Status.Present, fixture.Inspect(Kind.PackageStagingResidue));
        Assert.All(new[] { decoy, raw, report }, file => Assert.Equal("Synthetic sensitive decoy", File.ReadAllText(file)));
        Assert.True(Directory.Exists(packageStage));
    }

    [Fact]
    public void MissingAppDirectoriesStayMissingAndTraversalIsRejected()
    {
        using var fixture = new ArtifactFixture();
        Assert.Equal(Status.AbsentInCheckedScope, fixture.Inspect(Kind.WorkshopReportCopies));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.Root));
        Assert.Equal(Status.UnsafePathOrEntry, WindowsWorkshopArtifactSource.InspectRegisteredPath(
            fixture.Root + "\\..\\" + Path.GetFileName(fixture.Root), Kind.WorkshopReportCopies, CancellationToken.None));
    }

    [Fact]
    public void CollectionStopsAtTheEntryLimitEvenWhenNoNameMatches()
    {
        using var fixture = new ArtifactFixture();
        var reports = fixture.CreateFolder("ThirdLife", "SetupCore", "WorkshopReports");
        for (var index = 0; index <= WindowsWorkshopArtifactSource.MaximumDirectoryEntries; index++)
        {
            fixture.CreateFile(reports, $"decoy-{index}.txt");
        }

        Assert.Equal(Status.ScanLimitReached, fixture.Inspect(Kind.WorkshopReportCopies));
    }

    [Fact]
    public void JunctionAncestorAndMatchingEntryStayOutOfScope()
    {
        using var fixture = new ArtifactFixture();
        var sensitive = fixture.CreateFolder("Recipient");
        var decoy = fixture.CreateFile(sensitive, "private-recipient-note.txt");
        var junction = fixture.CreateFolder("ThirdLife");
        BatteryReportWorkspaceTests.CreateJunction(junction, sensitive);
        var packageName = fixture.CreateFolder($"ThirdLife-package-{Guid.NewGuid():N}");
        BatteryReportWorkspaceTests.CreateJunction(packageName, sensitive);

        Assert.Equal(Status.UnsafePathOrEntry, fixture.Inspect(Kind.WorkshopReportCopies));
        Assert.Equal(Status.UnsafePathOrEntry, fixture.Inspect(Kind.PackageStagingResidue));
        Assert.Equal("Synthetic sensitive decoy", File.ReadAllText(decoy));
    }

    [Fact]
    public void DeniedDirectoryIsNeverReportedAsEmpty()
    {
        using var fixture = new ArtifactFixture();
        var directory = new DirectoryInfo(fixture.CreateFolder("ThirdLife", "SetupCore", "WorkshopReports"));
        var original = directory.GetAccessControl(AccessControlSections.Access);
        using var identity = WindowsIdentity.GetCurrent();
        var denied = directory.GetAccessControl(AccessControlSections.Access);
        denied.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ListDirectory, AccessControlType.Deny));
        directory.SetAccessControl(denied);
        try
        {
            Assert.Equal(Status.AccessDenied, fixture.Inspect(Kind.WorkshopReportCopies));
        }
        finally
        {
            directory.SetAccessControl(original);
        }
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task ActiveMachineUsesNativeReadOnlySourcesWithSyntheticAbsentNames()
    {
        Assert.Equal(WindowsPlatformEligibility.Supported, WindowsPlatform.CheckCurrent());
        var syntheticAccount = string.Concat("tl-", Guid.NewGuid().ToString("N").AsSpan(0, 17));
        var syntheticShare = string.Concat("tl-", Guid.NewGuid().ToString("N"));
        var findings = await new WorkshopArtifactDetector().DetectAsync([syntheticAccount], [syntheticShare]);
        Assert.Equal(Enum.GetValues<Kind>(), findings.Select(finding => finding.Kind));
        Assert.DoesNotContain(findings, finding => finding.Status is Status.Present &&
            finding.Kind is Kind.ConfiguredLocalAccounts or Kind.ConfiguredSmbShares);
        Assert.DoesNotContain(findings, finding => finding.Status is Status.TargetsNotConfigured or Status.TimedOut);
        foreach (var finding in findings)
        {
            output.WriteLine($"{finding.Kind} {finding.Status}");
        }
    }

    private static Status State(IReadOnlyList<WorkshopArtifactFinding> findings, Kind kind) =>
        Assert.Single(findings, finding => finding.Kind == kind).Status;

    private sealed class ArtifactFixture : IDisposable
    {
        private readonly List<string> _directories = [];
        private readonly List<string> _files = [];
        public string Root { get; } = Directory.CreateTempSubdirectory("ThirdLife-Artifacts-").FullName;

        public string CreateFolder(params string[] parts)
        {
            var path = Root;
            foreach (var part in parts)
            {
                path = Path.Combine(path, part);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                    _directories.Add(path);
                }
            }

            return path;
        }

        public string CreateFile(string directory, string filename)
        {
            var path = Path.Combine(directory, filename);
            File.WriteAllText(path, "Synthetic sensitive decoy");
            _files.Add(path);
            return path;
        }

        public Status Inspect(Kind kind) => WindowsWorkshopArtifactSource.InspectRegisteredPath(Root, kind, CancellationToken.None);

        public void Dispose()
        {
            foreach (var path in _files) { File.Delete(path); }
            for (var index = _directories.Count - 1; index >= 0; index--)
            {
                Directory.Delete(_directories[index], recursive: false);
            }

            Directory.Delete(Root, recursive: false);
        }
    }
}
