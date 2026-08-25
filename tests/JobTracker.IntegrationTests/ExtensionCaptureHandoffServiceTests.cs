using System.Text.Json;
using JobTracker.Web.Data;
using JobTracker.Web.Extraction;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.IntegrationTests;

public sealed class ExtensionCaptureHandoffServiceTests
{
    [Fact]
    public async Task Capture_IsEncryptedAtRestAndCanOnlyBeRedeemedOnce()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"extension-handoff-{Guid.NewGuid()}")
            .Options;
        await using var database = new ApplicationDbContext(options);
        var time = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 25, 4, 0, 0, TimeSpan.Zero));
        var service = Service(database, time);
        var marker = $"private-capture-{Guid.NewGuid():N}";
        var payload = Payload(marker);

        var created = await service.CreateAsync(payload);

        Assert.Equal(ExtensionCaptureHandoffStatus.Success, created.Status);
        Assert.NotNull(created.Token);
        Assert.Equal(43, created.Token.Length);
        var stored = await database.ExtensionCaptureHandoffs.SingleAsync();
        Assert.DoesNotContain(marker, stored.ProtectedPayload, StringComparison.Ordinal);
        Assert.NotEqual(created.Token, stored.TokenHash);

        var redeemed = await service.RedeemAsync(created.Token);
        var replayed = await service.RedeemAsync(created.Token);

        Assert.Equal(ExtensionCaptureHandoffStatus.Success, redeemed.Status);
        Assert.NotNull(redeemed.Extraction);
        Assert.Contains(marker, redeemed.Extraction.OriginalText, StringComparison.Ordinal);
        Assert.Equal(ExtensionCaptureHandoffStatus.Unavailable, replayed.Status);
        Assert.Empty(database.ExtensionCaptureHandoffs);
    }

    [Fact]
    public async Task ExpiredCapture_IsDeletedAndCannotBeRedeemed()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"expired-extension-handoff-{Guid.NewGuid()}")
            .Options;
        await using var database = new ApplicationDbContext(options);
        var time = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 25, 4, 0, 0, TimeSpan.Zero));
        var service = Service(database, time);
        var created = await service.CreateAsync(Payload("expiring-capture"));

        time.Advance(TimeSpan.FromMinutes(11));
        var redeemed = await service.RedeemAsync(created.Token!);

        Assert.Equal(ExtensionCaptureHandoffStatus.Unavailable, redeemed.Status);
        Assert.Empty(database.ExtensionCaptureHandoffs);
    }

    private static ExtensionCaptureHandoffService Service(
        ApplicationDbContext database,
        TimeProvider timeProvider) => new(
        database,
        new BrowserExtensionImportService(new PastedJobTextExtractor()),
        new EphemeralDataProtectionProvider(),
        timeProvider);

    private static string Payload(string marker) => JsonSerializer.Serialize(new
    {
        version = 1,
        pageUrl = $"https://jobs.example.test/{marker}",
        roleTitle = "Private Handoff Engineer",
        companyName = "Synthetic Token Labs",
        sourceText = $"Private Handoff Engineer\nSynthetic Token Labs\n{marker}\nA sufficiently detailed private browser capture.",
    });

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration) => utcNow = utcNow.Add(duration);
    }
}
