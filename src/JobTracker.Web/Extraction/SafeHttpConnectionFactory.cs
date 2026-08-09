using System.Net;
using System.Net.Sockets;

namespace JobTracker.Web.Extraction;

public interface IPublicAddressConnector
{
    Task<Stream> ConnectAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken);
}

public sealed class SystemPublicAddressConnector : IPublicAddressConnector
{
    public async Task<Stream> ConnectAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };

        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

public sealed record SafeHttpConnectionOptions(TimeSpan AddressAttemptDelay)
{
    public static SafeHttpConnectionOptions Default { get; } =
        new(TimeSpan.FromMilliseconds(250));
}

public sealed class SafeHttpConnectionFactory(
    PublicUrlSafetyPolicy safetyPolicy,
    IPublicAddressConnector connector,
    SafeHttpConnectionOptions options)
{
    public async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await safetyPolicy.ResolvePublicAddressesAsync(
            context.DnsEndPoint.Host,
            cancellationToken);
        return await ConnectToFirstAvailableAsync(
            addresses,
            context.DnsEndPoint.Port,
            cancellationToken);
    }

    public async Task<Stream> ConnectToFirstAvailableAsync(
        IReadOnlyList<IPAddress> validatedPublicAddresses,
        int port,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65_535);

        if (validatedPublicAddresses.Count == 0)
        {
            throw new HttpRequestException("No validated public address was available.");
        }

        if (validatedPublicAddresses.Any(address => !PublicUrlSafetyPolicy.IsPublicAddress(address)))
        {
            throw new ArgumentException(
                "Every connection candidate must be a validated public address.",
                nameof(validatedPublicAddresses));
        }

        if (options.AddressAttemptDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException("The address-attempt delay cannot be negative.");
        }

        using var attemptsCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        var attempts = InterleaveAddressFamilies(validatedPublicAddresses)
            .Select((address, index) => ConnectAfterDelayAsync(
                address,
                port,
                index,
                attemptsCancellation.Token))
            .ToList();
        var failures = new List<Exception>(attempts.Count);

        try
        {
            while (attempts.Count > 0)
            {
                var completed = await Task.WhenAny(attempts);
                attempts.Remove(completed);

                try
                {
                    var winner = await completed;
                    await CancelAndDisposeOtherAttemptsAsync(attemptsCancellation, attempts);
                    attempts.Clear();
                    return winner;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is SocketException
                    or IOException
                    or OperationCanceledException)
                {
                    failures.Add(exception);
                }
            }
        }
        finally
        {
            await CancelAndDisposeOtherAttemptsAsync(attemptsCancellation, attempts);
        }

        throw new HttpRequestException(
            "No validated public address accepted the connection.",
            failures.Count == 0 ? null : new AggregateException(failures));
    }

    private static IReadOnlyList<IPAddress> InterleaveAddressFamilies(
        IReadOnlyList<IPAddress> addresses)
    {
        var preferredFamily = addresses[0].AddressFamily;
        var preferred = new Queue<IPAddress>(
            addresses.Where(address => address.AddressFamily == preferredFamily));
        var alternate = new Queue<IPAddress>(
            addresses.Where(address => address.AddressFamily != preferredFamily));
        var ordered = new List<IPAddress>(addresses.Count);

        while (preferred.Count > 0 || alternate.Count > 0)
        {
            if (preferred.TryDequeue(out var preferredAddress))
            {
                ordered.Add(preferredAddress);
            }

            if (alternate.TryDequeue(out var alternateAddress))
            {
                ordered.Add(alternateAddress);
            }
        }

        return ordered;
    }

    private async Task<Stream> ConnectAfterDelayAsync(
        IPAddress address,
        int port,
        int attemptIndex,
        CancellationToken cancellationToken)
    {
        if (attemptIndex > 0)
        {
            await Task.Delay(options.AddressAttemptDelay * attemptIndex, cancellationToken);
        }

        return await connector.ConnectAsync(address, port, cancellationToken);
    }

    private static async Task CancelAndDisposeOtherAttemptsAsync(
        CancellationTokenSource attemptsCancellation,
        IReadOnlyCollection<Task<Stream>> attempts)
    {
        await attemptsCancellation.CancelAsync();

        foreach (var attempt in attempts)
        {
            try
            {
                var stream = await attempt;
                await stream.DisposeAsync();
            }
            catch (Exception exception) when (exception is SocketException
                or IOException
                or OperationCanceledException)
            {
                // Failed and cancelled losing attempts have no result to clean up.
            }
        }
    }
}
