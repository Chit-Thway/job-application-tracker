namespace JobTracker.Web.Extraction;

public sealed class JobPostingUrlImportService(
    IJobPostingFetcher fetcher,
    JobPostingHtmlExtractor htmlExtractor,
    ExtractionDraftService drafts)
{
    public async Task<JobPostingUrlImportResult> ImportAsync(
        string sourceUrl,
        DateOnly defaultAppliedOn,
        CancellationToken cancellationToken = default)
    {
        var fetched = await fetcher.FetchAsync(sourceUrl, cancellationToken);
        if (!fetched.IsSuccess
            || fetched.RequestedUri is null
            || fetched.FinalUri is null
            || fetched.Html is null)
        {
            return new JobPostingUrlImportResult(fetched.Failure, null);
        }

        try
        {
            var extraction = htmlExtractor.Extract(
                fetched.RequestedUri,
                fetched.FinalUri,
                fetched.Html);
            var draftId = await drafts.CreateUrlDraftAsync(
                extraction,
                defaultAppliedOn,
                cancellationToken);
            return new JobPostingUrlImportResult(JobPostingImportFailure.None, draftId);
        }
        catch (JobPostingUrlException exception)
        {
            return new JobPostingUrlImportResult(exception.Failure, null);
        }
    }
}
