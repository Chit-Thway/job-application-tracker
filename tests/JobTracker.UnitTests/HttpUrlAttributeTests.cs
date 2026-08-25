using JobTracker.Web.Models;

namespace JobTracker.UnitTests;

public sealed class HttpUrlAttributeTests
{
    private readonly HttpUrlAttribute attribute = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://accenture.example.test/candidate/home")]
    [InlineData("http://careers.example.test/status")]
    public void OptionalHttpOrHttpsUrl_IsAccepted(string? value)
    {
        Assert.True(attribute.IsValid(value));
    }

    [Theory]
    [InlineData("portal.example.test/status")]
    [InlineData("ftp://example.test/status")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://")]
    public void NonHttpOrIncompleteUrl_IsRejected(string value)
    {
        Assert.False(attribute.IsValid(value));
    }
}
