using System.Net;
using System.Net.Sockets;

namespace JobTracker.Web.Extraction;

public interface IHostAddressResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class SystemHostAddressResolver : IHostAddressResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

public sealed class JobPostingUrlException(
    JobPostingImportFailure failure,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public JobPostingImportFailure Failure { get; } = failure;
}

public sealed class PublicUrlSafetyPolicy(IHostAddressResolver resolver)
{
    public async Task<Uri> ValidateAsync(
        string sourceUrl,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(sourceUrl?.Trim(), UriKind.Absolute, out var uri))
        {
            throw new JobPostingUrlException(
                JobPostingImportFailure.InvalidUrl,
                "Enter a complete public HTTP or HTTPS URL.");
        }

        await ValidateAsync(uri, cancellationToken);
        return uri;
    }

    public async Task<IReadOnlyList<IPAddress>> ValidateAsync(
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        ValidateShape(uri);
        return await ResolvePublicAddressesAsync(uri.Host, cancellationToken);
    }

    public async Task<IReadOnlyList<IPAddress>> ResolvePublicAddressesAsync(
        string host,
        CancellationToken cancellationToken = default)
    {
        var normalizedHost = host.Trim().TrimEnd('.');
        if (normalizedHost.Length == 0
            || string.Equals(normalizedHost, "localhost", StringComparison.OrdinalIgnoreCase)
            || normalizedHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || normalizedHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || normalizedHost.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            throw Blocked();
        }

        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(normalizedHost, out var literal)
                ? [literal]
                : await resolver.ResolveAsync(normalizedHost, cancellationToken);
        }
        catch (SocketException exception)
        {
            throw new JobPostingUrlException(
                JobPostingImportFailure.CouldNotResolve,
                "The job-site address could not be resolved.",
                exception);
        }

        if (addresses.Length == 0)
        {
            throw new JobPostingUrlException(
                JobPostingImportFailure.CouldNotResolve,
                "The job-site address could not be resolved.");
        }

        if (addresses.Any(address => !IsPublicAddress(address)))
        {
            throw Blocked();
        }

        return addresses;
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var first = bytes[0];
            var second = bytes[1];
            var third = bytes[2];
            return first != 0
                && first != 10
                && first != 127
                && first < 224
                && !(first == 100 && second is >= 64 and <= 127)
                && !(first == 169 && second == 254)
                && !(first == 172 && second is >= 16 and <= 31)
                && !(first == 192 && second == 0 && third is 0 or 2)
                && !(first == 192 && second == 88 && third == 99)
                && !(first == 192 && second == 168)
                && !(first == 198 && second is 18 or 19)
                && !(first == 198 && second == 51 && third == 100)
                && !(first == 203 && second == 0 && third == 113);
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.IsIPv6SiteLocal
            || address.IsIPv6Teredo
            || address.IsIPv6UniqueLocal)
        {
            return false;
        }

        // Only globally routable IPv6 space is eligible, with documentation and
        // transition ranges excluded because they can encapsulate unsafe targets.
        return (bytes[0] & 0xE0) == 0x20
            && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8)
            && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x02)
            && !(bytes[0] == 0x20 && bytes[1] == 0x02)
            && !(bytes[0] == 0x3F && (bytes[1] & 0xF0) == 0xF0);
    }

    private static void ValidateShape(Uri uri)
    {
        if (!uri.IsAbsoluteUri
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.HostNameType == UriHostNameType.Unknown
            || uri.Host.Length == 0)
        {
            throw new JobPostingUrlException(
                JobPostingImportFailure.InvalidUrl,
                "Enter a complete public HTTP or HTTPS URL.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
        {
            throw Blocked();
        }
    }

    private static JobPostingUrlException Blocked() => new(
        JobPostingImportFailure.BlockedDestination,
        "That address points to a network destination the importer is not allowed to contact.");
}
