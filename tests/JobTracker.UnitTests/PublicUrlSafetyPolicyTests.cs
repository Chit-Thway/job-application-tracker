using System.Net;
using JobTracker.Web.Extraction;

namespace JobTracker.UnitTests;

public sealed class PublicUrlSafetyPolicyTests
{
    [Theory]
    [InlineData("file:///etc/passwd", JobPostingImportFailure.InvalidUrl)]
    [InlineData("ftp://example.com/job", JobPostingImportFailure.InvalidUrl)]
    [InlineData("https://user:secret@example.com/job", JobPostingImportFailure.BlockedDestination)]
    [InlineData("https://example.com:8443/job", JobPostingImportFailure.BlockedDestination)]
    [InlineData("http://localhost/job", JobPostingImportFailure.BlockedDestination)]
    [InlineData("http://127.0.0.1/job", JobPostingImportFailure.BlockedDestination)]
    [InlineData("http://2130706433/job", JobPostingImportFailure.BlockedDestination)]
    [InlineData("http://169.254.169.254/latest/meta-data", JobPostingImportFailure.BlockedDestination)]
    [InlineData("http://10.2.3.4/job", JobPostingImportFailure.BlockedDestination)]
    [InlineData("http://172.20.1.2/job", JobPostingImportFailure.BlockedDestination)]
    [InlineData("http://192.168.1.2/job", JobPostingImportFailure.BlockedDestination)]
    [InlineData("http://[::1]/job", JobPostingImportFailure.BlockedDestination)]
    [InlineData("http://[fc00::1]/job", JobPostingImportFailure.BlockedDestination)]
    [InlineData("http://[fe80::1]/job", JobPostingImportFailure.BlockedDestination)]
    [InlineData("http://[::ffff:127.0.0.1]/job", JobPostingImportFailure.BlockedDestination)]
    public async Task UnsafeUrls_AreRejected(string url, JobPostingImportFailure expected)
    {
        var policy = new PublicUrlSafetyPolicy(new FakeResolver());

        var exception = await Assert.ThrowsAsync<JobPostingUrlException>(() => policy.ValidateAsync(url));

        Assert.Equal(expected, exception.Failure);
    }

    [Fact]
    public async Task HostWithAnyPrivateDnsAnswer_IsRejected()
    {
        var resolver = new FakeResolver(
            ("mixed.example.test", [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("10.0.0.8")]));
        var policy = new PublicUrlSafetyPolicy(resolver);

        var exception = await Assert.ThrowsAsync<JobPostingUrlException>(
            () => policy.ValidateAsync("https://mixed.example.test/jobs/1"));

        Assert.Equal(JobPostingImportFailure.BlockedDestination, exception.Failure);
    }

    [Fact]
    public async Task PublicHttpAndHttpsDestinations_AreAccepted()
    {
        var resolver = new FakeResolver(
            ("jobs.example.test", [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("2606:4700:4700::1111")]));
        var policy = new PublicUrlSafetyPolicy(resolver);

        var https = await policy.ValidateAsync("https://jobs.example.test/roles/graduate");
        var http = await policy.ValidateAsync("http://jobs.example.test/roles/graduate");

        Assert.Equal("jobs.example.test", https.Host);
        Assert.Equal(Uri.UriSchemeHttp, http.Scheme);
    }

    private sealed class FakeResolver(params (string Host, IPAddress[] Addresses)[] entries)
        : IHostAddressResolver
    {
        private readonly Dictionary<string, IPAddress[]> addresses = entries.ToDictionary(
            entry => entry.Host,
            entry => entry.Addresses,
            StringComparer.OrdinalIgnoreCase);

        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(
                addresses.TryGetValue(host, out var result)
                    ? result
                    : [IPAddress.Parse("93.184.216.34")]);
    }
}
