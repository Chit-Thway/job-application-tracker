using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using JobTracker.Web.Extraction;

namespace JobTracker.UnitTests;

public sealed class SafeHttpConnectionFactoryTests
{
    [Fact]
    public async Task StalledIpv6_DoesNotPreventWorkingIpv4Connection()
    {
        var ipv6 = IPAddress.Parse("2606:4700:4700::1111");
        var ipv4 = IPAddress.Parse("93.184.216.34");
        var connector = new ControlledConnector(ipv6, ipv4);
        var factory = Factory(connector, TimeSpan.FromMilliseconds(15));
        var stopwatch = Stopwatch.StartNew();

        await using var result = await factory.ConnectToFirstAvailableAsync(
            [ipv6, ipv4],
            443,
            CancellationToken.None);

        stopwatch.Stop();
        Assert.Same(connector.Ipv4Stream, result);
        Assert.True(connector.Ipv6AttemptWasCancelled);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task AllConnectionFailures_ReturnRequestFailure()
    {
        var connector = new AlwaysFailingConnector();
        var factory = Factory(connector, TimeSpan.Zero);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            factory.ConnectToFirstAvailableAsync(
                [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("1.1.1.1")],
                443,
                CancellationToken.None));

        Assert.Contains("No validated public address accepted", exception.Message);
        Assert.Equal(2, connector.AttemptCount);
    }

    private static SafeHttpConnectionFactory Factory(
        IPublicAddressConnector connector,
        TimeSpan attemptDelay)
    {
        var policy = new PublicUrlSafetyPolicy(new UnusedResolver());
        return new SafeHttpConnectionFactory(
            policy,
            connector,
            new SafeHttpConnectionOptions(attemptDelay));
    }

    private sealed class ControlledConnector(IPAddress stalledIpv6, IPAddress workingIpv4)
        : IPublicAddressConnector
    {
        public MemoryStream Ipv4Stream { get; } = new();

        public bool Ipv6AttemptWasCancelled { get; private set; }

        public async Task<Stream> ConnectAsync(
            IPAddress address,
            int port,
            CancellationToken cancellationToken)
        {
            Assert.Equal(443, port);

            if (address.Equals(stalledIpv6))
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Ipv6AttemptWasCancelled = true;
                    throw;
                }
            }

            Assert.Equal(workingIpv4, address);
            return Ipv4Stream;
        }
    }

    private sealed class AlwaysFailingConnector : IPublicAddressConnector
    {
        public int AttemptCount { get; private set; }

        public Task<Stream> ConnectAsync(
            IPAddress address,
            int port,
            CancellationToken cancellationToken)
        {
            AttemptCount++;
            return Task.FromException<Stream>(new SocketException((int)SocketError.HostUnreachable));
        }
    }

    private sealed class UnusedResolver : IHostAddressResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The address-racing tests provide validated addresses directly.");
    }
}
