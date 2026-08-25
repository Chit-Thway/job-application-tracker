using System.Security.Cryptography;
using System.Text;
using JobTracker.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Web.Extraction;

public enum ExtensionCaptureHandoffStatus
{
    Success,
    InvalidCapture,
    Unavailable,
}

public sealed record CreatedExtensionCaptureHandoff(
    ExtensionCaptureHandoffStatus Status,
    string? Token,
    DateTimeOffset? ExpiresAt,
    string? Error);

public sealed record RedeemedExtensionCaptureHandoff(
    ExtensionCaptureHandoffStatus Status,
    PastedJobExtraction? Extraction,
    string? Error);

public sealed class ExtensionCaptureHandoffService(
    ApplicationDbContext database,
    BrowserExtensionImportService imports,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private const string ProtectorPurpose = "JobTracker.ExtensionCaptureHandoff.v1";
    private readonly IDataProtector protector =
        dataProtectionProvider.CreateProtector(ProtectorPurpose);

    public async Task<CreatedExtensionCaptureHandoff> CreateAsync(
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        var imported = imports.Import(payloadJson);
        if (!imported.IsSuccess)
        {
            return new CreatedExtensionCaptureHandoff(
                ExtensionCaptureHandoffStatus.InvalidCapture,
                null,
                null,
                imported.Error);
        }

        var now = timeProvider.GetUtcNow();
        await RemoveExpiredAsync(now, cancellationToken);

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var handoff = new ExtensionCaptureHandoff
        {
            TokenHash = Hash(token),
            ProtectedPayload = protector.Protect(payloadJson),
            CreatedAt = now,
            ExpiresAt = now.Add(Lifetime),
        };

        database.ExtensionCaptureHandoffs.Add(handoff);
        await database.SaveChangesAsync(cancellationToken);

        return new CreatedExtensionCaptureHandoff(
            ExtensionCaptureHandoffStatus.Success,
            token,
            handoff.ExpiresAt,
            null);
    }

    public async Task<RedeemedExtensionCaptureHandoff> RedeemAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidToken(token))
        {
            return Unavailable();
        }

        var now = timeProvider.GetUtcNow();
        await RemoveExpiredAsync(now, cancellationToken);

        var tokenHash = Hash(token);
        var handoff = await database.ExtensionCaptureHandoffs.SingleOrDefaultAsync(
            item => item.TokenHash == tokenHash,
            cancellationToken);
        if (handoff is null || handoff.ExpiresAt <= now)
        {
            return Unavailable();
        }

        database.ExtensionCaptureHandoffs.Remove(handoff);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Unavailable();
        }

        string payloadJson;
        try
        {
            payloadJson = protector.Unprotect(handoff.ProtectedPayload);
        }
        catch (CryptographicException)
        {
            return Unavailable();
        }

        var imported = imports.Import(payloadJson);
        return imported.IsSuccess
            ? new RedeemedExtensionCaptureHandoff(
                ExtensionCaptureHandoffStatus.Success,
                imported.Extraction,
                null)
            : new RedeemedExtensionCaptureHandoff(
                ExtensionCaptureHandoffStatus.InvalidCapture,
                null,
                imported.Error);
    }

    private async Task RemoveExpiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var expired = await database.ExtensionCaptureHandoffs
            .Where(item => item.ExpiresAt <= now)
            .ToListAsync(cancellationToken);
        if (expired.Count == 0)
        {
            return;
        }

        database.ExtensionCaptureHandoffs.RemoveRange(expired);
        await database.SaveChangesAsync(cancellationToken);
    }

    private static bool IsValidToken(string token) =>
        token.Length == 43
        && token.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static RedeemedExtensionCaptureHandoff Unavailable() => new(
        ExtensionCaptureHandoffStatus.Unavailable,
        null,
        "That browser capture expired or was already used. Return to the job page and capture it again.");
}
