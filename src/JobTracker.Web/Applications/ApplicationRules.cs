namespace JobTracker.Web.Applications;

public static class ApplicationRules
{
    public const int MaximumBulkSelectionCount = 200;
    public const int MaximumNoteLength = 2_000;
    public const int FollowUpSuggestionAfterDays = 14;
    public const int GhostingConfirmationAfterDays = 30;
    public const int RecentApplicationsDisplayCount = 8;
    public const int RecentActivityDisplayCount = 10;
}
