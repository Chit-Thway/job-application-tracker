namespace JobTracker.Web.Extraction;

public enum JobPostingImportFailure
{
    None,
    InvalidUrl,
    BlockedDestination,
    CouldNotResolve,
    RequestFailed,
    TimedOut,
    TooManyRedirects,
    ResponseTooLarge,
    UnsupportedContentType,
    EmptyContent,
}

public sealed record JobPostingFetchResult(
    JobPostingImportFailure Failure,
    Uri? RequestedUri,
    Uri? FinalUri,
    string? Html)
{
    public bool IsSuccess => Failure == JobPostingImportFailure.None;

    public static JobPostingFetchResult Failed(JobPostingImportFailure failure) =>
        new(failure, null, null, null);
}

public interface IJobPostingFetcher
{
    Task<JobPostingFetchResult> FetchAsync(
        string sourceUrl,
        CancellationToken cancellationToken = default);
}

public sealed record JobPostingUrlImportResult(
    JobPostingImportFailure Failure,
    Guid? DraftId)
{
    public bool IsSuccess => Failure == JobPostingImportFailure.None && DraftId is not null;
}

public sealed record JobPostingFetchOptions(
    TimeSpan Timeout,
    int MaximumRedirects,
    int MaximumResponseBytes)
{
    public static JobPostingFetchOptions Default { get; } =
        new(TimeSpan.FromSeconds(10), 3, 1_000_000);
}
