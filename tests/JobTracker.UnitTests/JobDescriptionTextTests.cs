using JobTracker.Web.Applications;
using JobTracker.Web.Extraction;

namespace JobTracker.UnitTests;

public sealed class JobDescriptionTextTests
{
    [Fact]
    public void CapturedMetadata_IsRemovedWhileReadableDescriptionStructureIsKept()
    {
        const string source = """
            Job title: Graduate Software Engineer
            Company: Synthetic Systems
            Location: Perth, WA
            Salary: $70,000 - $75,000 per year

            About the role

            Build dependable services for Australian customers.

            What you'll do

            - Write maintainable software
            - Work with product teams
            """;

        var description = JobDescriptionText.Extract(source);

        Assert.NotNull(description);
        Assert.StartsWith("About the role", description, StringComparison.Ordinal);
        Assert.DoesNotContain("Job title:", description, StringComparison.Ordinal);
        Assert.Contains("What you'll do", description, StringComparison.Ordinal);
        Assert.Contains("- Write maintainable software", description, StringComparison.Ordinal);
    }

    [Fact]
    public void DisplayFormatter_SeparatesHeadingsParagraphsAndLists()
    {
        const string description = """
            About the role

            Build dependable services for Australian customers.

            What you'll do

            - Write maintainable software
            - Work with product teams
            """;

        var blocks = JobDescriptionDisplay.Format(description);

        Assert.Collection(
            blocks,
            block =>
            {
                Assert.Equal(JobDescriptionBlockKind.Heading, block.Kind);
                Assert.Equal("About the role", Assert.Single(block.Lines));
            },
            block =>
            {
                Assert.Equal(JobDescriptionBlockKind.Paragraph, block.Kind);
                Assert.Equal("Build dependable services for Australian customers.", Assert.Single(block.Lines));
            },
            block =>
            {
                Assert.Equal(JobDescriptionBlockKind.Heading, block.Kind);
                Assert.Equal("What you'll do", Assert.Single(block.Lines));
            },
            block =>
            {
                Assert.Equal(JobDescriptionBlockKind.List, block.Kind);
                Assert.Equal(["Write maintainable software", "Work with product teams"], block.Lines);
            });
    }
}
