using JobTracker.Web.Data;

namespace JobTracker.Web.Applications;

public static class ApplicationSortOptions
{
    public const string Newest = "newest";
    public const string Oldest = "oldest";
    public const string RoleTitle = "title";
    public const string CompanyName = "company";

    public static bool IsSupported(string? value) => value is
        Newest or Oldest or RoleTitle or CompanyName;
}

public sealed record ApplicationSearch(
    string? Search,
    Guid? CompanyId,
    PipelineStage? Stage,
    ApplicationOutcome? Outcome,
    bool? IsSavedForever,
    bool? IsDeletionScheduled,
    DateOnly? AppliedFrom,
    DateOnly? AppliedTo,
    string Sort);

public sealed record ApplicationListItem(
    Guid Id,
    string RoleTitle,
    string? CompanyName,
    DateOnly AppliedOn,
    PipelineStage Stage,
    ApplicationOutcome Outcome,
    string? SourceUrl,
    string? ApplicationPortalUrl,
    bool IsSavedForever,
    DateTimeOffset? DeletionScheduledAt,
    DateTimeOffset? CreatedAt = null);

public sealed record ApplicationDetails(
    Guid Id,
    Guid? CompanyId,
    string? CompanyName,
    string RoleTitle,
    DateOnly AppliedOn,
    PipelineStage Stage,
    ApplicationOutcome Outcome,
    string? SourceUrl,
    string? ApplicationPortalUrl,
    string? SourceText,
    string? DescriptionText,
    string? ExtractionMetadataJson,
    string? Notes,
    bool IsSavedForever,
    DateTimeOffset? DeletionScheduledAt);

public sealed record ApplicationInput(
    Guid? CompanyId,
    string RoleTitle,
    DateOnly AppliedOn,
    string? SourceUrl,
    string? ApplicationPortalUrl,
    string? DescriptionText,
    string? Notes,
    bool IsSavedForever);

public enum ApplicationWriteResult
{
    Success,
    NotFound,
    InvalidCompany,
    InvalidRetentionState,
    LimitReached,
}

public sealed record BulkApplicationResult(int MatchedCount, int ChangedCount);

public sealed record BulkApplicationDeleteItem(
    Guid Id,
    string RoleTitle,
    string? CompanyName,
    DateOnly AppliedOn);
