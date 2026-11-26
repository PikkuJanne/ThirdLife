using System.Net;

namespace ThirdLife.Packages.Tests;

public sealed class WindowsInstallerPackageRuntimeTests
{
    [Fact]
    [Trait("Category", "Extended")]
    public async Task InterruptedInstallerStreamRemovesPartialStagingAndReportsNetworkLoss()
    {
        const int reviewedLength = 7_933_952;
        const int partialLength = 128 * 1024;
        const string readFailure = "Fixture read failed with private connection details.";
        using var staging = PackageStagingDirectory.Create(reviewedLength);
        var installerPath = Path.Combine(staging.Path, "npp.8.9.9.1.Installer.x64.msi");
        long stagedLength = 0;
        using var stream = new InterruptedStream(new byte[partialLength], () =>
        {
            stagedLength = new FileInfo(installerPath).Length;
            return new IOException(readFailure);
        });
        using var handler = new FixtureDownloadHandler(stream, reviewedLength);
        using var client = new HttpClient(handler, disposeHandler: false);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            var failure = await Assert.ThrowsAsync<PackageNetworkException>(() =>
                WindowsInstallerPackageRuntime.AcquireAsync(client, staging, deadline.Token));

            Assert.Equal(partialLength, stagedLength);
            Assert.Equal("The package download connection was interrupted.", failure.Message);
            Assert.Null(failure.InnerException);
            Assert.Equal(1, handler.Requests);
            Assert.False(File.Exists(installerPath));
            Assert.False(Directory.Exists(staging.Path));
        }
        finally
        {
            // Clean only the exact test-owned file if the runtime cleanup regresses.
            if (File.Exists(installerPath))
            {
                File.Delete(installerPath);
            }
        }
    }

    private sealed class FixtureDownloadHandler(Stream stream, long reviewedLength) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("github.com", request.RequestUri?.Host);
            Requests++;

            var content = new StreamContent(stream);
            content.Headers.ContentLength = reviewedLength;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class InterruptedStream(byte[] partialBytes, Func<IOException> fail) : MemoryStream(partialBytes, writable: false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
            Position == Length ? ValueTask.FromException<int>(fail()) : base.ReadAsync(buffer, cancellationToken);
    }
}
