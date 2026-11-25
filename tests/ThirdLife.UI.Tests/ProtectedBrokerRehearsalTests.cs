using System.IO;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ThirdLife.Actions.Registry;
using ThirdLife.Broker.Protocol;
using ThirdLife.Broker.Protocol.Transport;
using ThirdLife.Core.Jobs;
using ThirdLife.Inventory.Windows;
using ThirdLife.UI.BrokerClient;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class ProtectedBrokerRehearsalTests
{
    [Fact]
    [Trait("Category", "ActiveMachine")]
    public void InstalledCandidateMatchesTheReviewedDigestAndRejectsDirectWriteAccess()
    {
        Assert.False(WindowsBrokerPeer.CurrentIdentity().IsElevated);
        Assert.Equal(WindowsPlatformEligibility.Supported, WindowsPlatform.CheckCurrent());

        var reviewed = Environment.GetEnvironmentVariable("THIRDLIFE_TL0311_REVIEWED_BROKER_SHA256");
        Assert.NotNull(reviewed);
        Assert.Matches("^[0-9a-f]{64}$", reviewed);
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ThirdLife", "Broker");
        var binary = Path.Combine(folder, "ThirdLife.Broker.exe");
        var metadata = Path.Combine(folder, "broker.sha256");
        using (var data = File.OpenRead(binary))
        {
            Assert.Equal(reviewed, Convert.ToHexStringLower(SHA256.HashData(data)));
        }

        Assert.Equal(Encoding.ASCII.GetBytes(reviewed), File.ReadAllBytes(metadata));
        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            using var write = File.OpenHandle(binary, FileMode.Open, FileAccess.Write);
        });
        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            using var write = File.OpenHandle(metadata, FileMode.Open, FileAccess.Write);
        });

        using var launchGuard = InstalledBroker.TryOpen();
        Assert.NotNull(launchGuard);
        Assert.Equal(binary, launchGuard.Executable, ignoreCase: true);
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public void InstalledBrokerCompletesOneNoOpThroughExplicitConsent() =>
        Rehearse("accept", BrokerBatchStatus.Completed);

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public void DecliningTheInstalledBrokerLeavesNoTerminalAction() =>
        Rehearse("decline", BrokerBatchStatus.Declined);

    private static void Rehearse(string consent, BrokerBatchStatus expected)
    {
        Assert.Equal(consent, Environment.GetEnvironmentVariable("THIRDLIFE_TL0311_CONSENT_REHEARSAL"));
        Assert.True(Environment.UserInteractive);
        Assert.False(WindowsBrokerPeer.CurrentIdentity().IsElevated);
        Assert.Equal(WindowsPlatformEligibility.Supported, WindowsPlatform.CheckCurrent());

        ShellTestHost.Run(() =>
        {
            BrokerBatchResult? result = null;
            ExceptionDispatchInfo? failure = null;
            var action = ActionRegistry.CreateNoOp(Guid.NewGuid());
            var window = new Window
            {
                Title = "ThirdLife broker verification",
                Width = 420,
                Height = 150,
                Content = new TextBlock
                {
                    Text = "Windows will ask for consent to run one test action that makes no machine changes.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(20),
                },
            };
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(75));
            window.ContentRendered += RunBrokerAsync;
            window.ShowDialog();
            failure?.Throw();

            Assert.False(WindowsBrokerPeer.CurrentIdentity().IsElevated);
            Assert.NotNull(result);
            Assert.Equal(expected, result.Status);
            if (expected == BrokerBatchStatus.Completed)
            {
                var terminal = Assert.Single(result.Results);
                Assert.Equal(action.ActionId, terminal.ActionId);
                Assert.Equal(BrokerResultStatus.Applied, terminal.Status);
            }
            else
            {
                Assert.Empty(result.Results);
            }

            async void RunBrokerAsync(object? sender, EventArgs args)
            {
                window.ContentRendered -= RunBrokerAsync;
                try
                {
                    var envelope = new BrokerEnvelope(Guid.NewGuid(), BrokerNonce.Create(),
                        TimeProvider.System.GetUtcNow().AddSeconds(70));
                    var request = new BrokerRequest(envelope, new JobId("job-tl0311-rehearsal"),
                        Guid.NewGuid(), new string('a', 64), new string('b', 64), [action]);
                    result = await new BrokerBatchClient().RunAsync(request, cancellationToken: cancellation.Token);
                }
                catch (Exception exception)
                {
                    failure = ExceptionDispatchInfo.Capture(exception);
                }
                finally
                {
                    window.Close();
                }
            }
        }, TimeSpan.FromSeconds(85));
    }
}
