using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace JobTracker.Web.Extraction;

public sealed class SafeJobPostingFetcher(
    HttpClient httpClient,
    PublicUrlSafetyPolicy safetyPolicy,
    JobPostingFetchOptions options) : IJobPostingFetcher
{
    private static readonly HashSet<HttpStatusCode> RedirectStatuses =
    [
        HttpStatusCode.MovedPermanently,
        HttpStatusCode.Redirect,
        HttpStatusCode.RedirectMethod,
        HttpStatusCode.TemporaryRedirect,
        HttpStatusCode.PermanentRedirect,
    ];

    public async Task<JobPostingFetchResult> FetchAsync(
        string sourceUrl,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);

        try
        {
            var requestedUri = await safetyPolicy.ValidateAsync(sourceUrl, timeout.Token);
            var currentUri = requestedUri;

            for (var redirectCount = 0; ; redirectCount++)
            {
                await safetyPolicy.ValidateAsync(currentUri, timeout.Token);
                using var request = CreateRequest(currentUri);
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);

                if (RedirectStatuses.Contains(response.StatusCode))
                {
                    if (redirectCount >= options.MaximumRedirects)
                    {
                        return JobPostingFetchResult.Failed(JobPostingImportFailure.TooManyRedirects);
                    }

                    var redirectUri = ResolveRedirect(currentUri, response.Headers.Location);
                    if (currentUri.Scheme == Uri.UriSchemeHttps
                        && redirectUri.Scheme == Uri.UriSchemeHttp)
                    {
                        return JobPostingFetchResult.Failed(JobPostingImportFailure.BlockedDestination);
                    }

                    currentUri = redirectUri;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return JobPostingFetchResult.Failed(JobPostingImportFailure.RequestFailed);
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (!string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
                {
                    return JobPostingFetchResult.Failed(JobPostingImportFailure.UnsupportedContentType);
                }

                if (response.Content.Headers.ContentLength > options.MaximumResponseBytes)
                {
                    return JobPostingFetchResult.Failed(JobPostingImportFailure.ResponseTooLarge);
                }

                var bytes = await ReadBoundedAsync(response.Content, timeout.Token);
                if (bytes is null)
                {
                    return JobPostingFetchResult.Failed(JobPostingImportFailure.ResponseTooLarge);
                }

                var html = Decode(bytes, response.Content.Headers.ContentType?.CharSet);
                if (string.IsNullOrWhiteSpace(html))
                {
                    return JobPostingFetchResult.Failed(JobPostingImportFailure.EmptyContent);
                }

                return new JobPostingFetchResult(
                    JobPostingImportFailure.None,
                    requestedUri,
                    currentUri,
                    html);
            }
        }
        catch (JobPostingUrlException exception)
        {
            return JobPostingFetchResult.Failed(exception.Failure);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return JobPostingFetchResult.Failed(JobPostingImportFailure.TimedOut);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return JobPostingFetchResult.Failed(JobPostingImportFailure.RequestFailed);
        }
    }

    private static HttpRequestMessage CreateRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
        request.Headers.UserAgent.ParseAdd("JobApplicationTracker/1.0");
        return request;
    }

    private static Uri ResolveRedirect(Uri currentUri, Uri? location)
    {
        if (location is null
            || !Uri.TryCreate(currentUri, location, out var resolved))
        {
            throw new JobPostingUrlException(
                JobPostingImportFailure.RequestFailed,
                "The job site returned an invalid redirect.");
        }

        return resolved;
    }

    private async Task<byte[]?> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[16_384];

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return destination.ToArray();
            }

            if (destination.Length + read > options.MaximumResponseBytes)
            {
                return null;
            }

            destination.Write(buffer, 0, read);
        }
    }

    private static string Decode(byte[] bytes, string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset.Trim(' ', '\"')).GetString(bytes);
            }
            catch (ArgumentException)
            {
                // Invalid or unsupported page charsets fall back to UTF-8.
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
