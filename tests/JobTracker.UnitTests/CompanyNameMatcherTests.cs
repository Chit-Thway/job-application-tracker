using JobTracker.Web.Extraction;

namespace JobTracker.UnitTests;

public sealed class CompanyNameMatcherTests
{
    [Theory]
    [InlineData("Accenture", "accenture", CompanyNameMatchKind.Exact)]
    [InlineData("Accenture Pty Ltd", "Accenture", CompanyNameMatchKind.Exact)]
    [InlineData("Accenture Australia", "Accenture", CompanyNameMatchKind.MeaningfulPhrase)]
    [InlineData("Accenture", "Accenture Australia Pty Ltd", CompanyNameMatchKind.MeaningfulPhrase)]
    [InlineData("Air", "Air Liquide", CompanyNameMatchKind.None)]
    [InlineData("Core", "Core Physiotherapy", CompanyNameMatchKind.None)]
    [InlineData("Open Point", "OpenAI", CompanyNameMatchKind.None)]
    public void Match_UsesNormalizedMeaningfulWholeNamePhrases(
        string extractedName,
        string existingName,
        CompanyNameMatchKind expected)
    {
        Assert.Equal(expected, CompanyNameMatcher.Match(extractedName, existingName));
    }
}
