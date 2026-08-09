using System.Net;
using System.Net.Http.Headers;
using JobTracker.Web.Extraction;

namespace JobTracker.UnitTests;

public sealed class SafeJobPostingFetcherTests
{
    [Fact]
    public async Task RedirectToPrivateAddress_IsRejectedBeforeSecondRequest()
    {
        var handler = new SequenceHandler(
            _ => Redirect("http://127.0.0.1/private"));
        var fetcher = Fetcher(handler);

        var result = await fetcher.FetchAsync("https://jobs.example.test/opening");

        Assert.Equal(JobPostingImportFailure.BlockedDestination, result.Failure);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task TooManyRedirects_AreRejected()
    {
        var handler = new SequenceHandler(
            _ => Redirect("/two"),
            _ => Redirect("/three"));
        var fetcher = Fetcher(handler, new JobPostingFetchOptions(TimeSpan.FromSeconds(1), 1, 2_000));

        var result = await fetcher.FetchAsync("https://jobs.example.test/one");

        Assert.Equal(JobPostingImportFailure.TooManyRedirects, result.Failure);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task NonHtmlAndOversizedResponses_AreRejected()
    {
        var nonHtml = Fetcher(new SequenceHandler(_ => Response("application/pdf", "pdf")));
        var oversized = Fetcher(
            new SequenceHandler(_ => Response("text/html", new string('x', 101))),
            new JobPostingFetchOptions(TimeSpan.FromSeconds(1), 1, 100));

        var nonHtmlResult = await nonHtml.FetchAsync("https://jobs.example.test/file");
        var oversizedResult = await oversized.FetchAsync("https://jobs.example.test/large");

        Assert.Equal(JobPostingImportFailure.UnsupportedContentType, nonHtmlResult.Failure);
        Assert.Equal(JobPostingImportFailure.ResponseTooLarge, oversizedResult.Failure);
    }

    [Fact]
    public async Task HttpsToHttpDowngrade_IsRejected()
    {
        var handler = new SequenceHandler(
            _ => Redirect("http://jobs.example.test/insecure"));
        var fetcher = Fetcher(handler);

        var result = await fetcher.FetchAsync("https://jobs.example.test/secure");

        Assert.Equal(JobPostingImportFailure.BlockedDestination, result.Failure);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SlowResponse_IsCancelledByOverallTimeout()
    {
        var handler = new SequenceHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Response("text/html", "never");
        });
        var fetcher = Fetcher(
            handler,
            new JobPostingFetchOptions(TimeSpan.FromMilliseconds(30), 1, 2_000));

        var result = await fetcher.FetchAsync("https://jobs.example.test/slow");

        Assert.Equal(JobPostingImportFailure.TimedOut, result.Failure);
    }

    [Fact]
    public async Task SafeHtmlRequest_ForwardsNoCredentialsCookiesOrReferrer()
    {
        HttpRequestMessage? captured = null;
        var handler = new SequenceHandler(request =>
        {
            captured = request;
            return Response("text/html", "<html><body>Job Title: Test Engineer</body></html>");
        });
        var fetcher = Fetcher(handler);

        var result = await fetcher.FetchAsync("https://jobs.example.test/opening");

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Null(captured.Headers.Authorization);
        Assert.Null(captured.Headers.Referrer);
        Assert.False(captured.Headers.Contains("Cookie"));
        Assert.Contains("JobApplicationTracker/1.0", captured.Headers.UserAgent.ToString(), StringComparison.Ordinal);
    }

    private static SafeJobPostingFetcher Fetcher(
        HttpMessageHandler handler,
        JobPostingFetchOptions? options = null)
    {
        var policy = new PublicUrlSafetyPolicy(new PublicResolver());
        return new SafeJobPostingFetcher(
            new HttpClient(handler),
            policy,
            options ?? new JobPostingFetchOptions(TimeSpan.FromSeconds(1), 3, 2_000));
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private static HttpResponseMessage Response(string contentType, string content)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content),
        };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return response;
    }

    private sealed class PublicResolver : IHostAddressResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult<IPAddress[]>([IPAddress.Parse("93.184.216.34")]);
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> responses;

        public SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
            : this(responses.Select<Func<HttpRequestMessage, HttpResponseMessage>, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>(
                response => (request, _) => Task.FromResult(response(request))).ToArray())
        {
        }

        public SequenceHandler(
            params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] responses)
        {
            this.responses = new Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>(responses);
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.NotEmpty(responses);
            return responses.Dequeue()(request, cancellationToken);
        }
    }
}
