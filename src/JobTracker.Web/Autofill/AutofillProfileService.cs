using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobTracker.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Web.Autofill;

public sealed class AutofillProfileService(
    ApplicationDbContext database,
    IDataProtectionProvider protection,
    TimeProvider time)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector protector = protection.CreateProtector("JobTracker.AutofillProfile.v1");

    public async Task<ApplicantProfile?> GetAsync(string ownerId, CancellationToken cancellationToken)
    {
        var record = await database.AutofillProfiles.AsNoTracking()
            .SingleOrDefaultAsync(profile => profile.OwnerId == ownerId, cancellationToken);
        return record is null ? null : Read(record);
    }

    public async Task<bool> SaveAsync(string ownerId, ApplicantProfile profile, CancellationToken cancellationToken)
    {
        foreach (var role in profile.Experience.Where(role => role.Current))
        {
            role.EndMonth = null;
        }

        var record = await database.AutofillProfiles.FindAsync([ownerId], cancellationToken);
        if (record is null)
        {
            record = new AutofillProfile { OwnerId = ownerId };
            database.AutofillProfiles.Add(record);
        }
        record.ProtectedProfile = protector.Protect(JsonSerializer.Serialize(profile, JsonOptions));
        return await UpdateAsync(record, cancellationToken);
    }

    public async Task DeleteAsync(string ownerId, CancellationToken cancellationToken)
    {
        var record = await database.AutofillProfiles.FindAsync([ownerId], cancellationToken);
        if (record is not null)
        {
            database.AutofillProfiles.Remove(record);
            await database.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<string?> CreatePairingCodeAsync(
        ApplicationUser user, string challenge, CancellationToken cancellationToken)
    {
        if (challenge.Length != 64 || !challenge.All(char.IsAsciiHexDigitLower)
            || !CanConnect(user))
        {
            return null;
        }

        var record = await database.AutofillProfiles.FindAsync([user.Id], cancellationToken);
        if (record is null)
        {
            return null;
        }

        var code = CreateToken();
        if (record.SecurityStamp != user.SecurityStamp)
        {
            record.AccessHash = null;
            record.AccessExpiresAt = null;
        }
        record.PairingHash = Hash(code);
        record.PairingChallenge = challenge;
        record.PairingExpiresAt = time.GetUtcNow().AddMinutes(5);
        record.SecurityStamp = user.SecurityStamp;
        return await UpdateAsync(record, cancellationToken) ? code : null;
    }

    public async Task<string?> ExchangeAsync(string? code, string? verifier, CancellationToken cancellationToken)
    {
        if (!IsToken(code) || !IsToken(verifier))
        {
            return null;
        }

        var now = time.GetUtcNow();
        var codeHash = Hash(code!);
        var challenge = Hash(verifier!);
        var record = await database.AutofillProfiles.SingleOrDefaultAsync(profile =>
            profile.PairingHash == codeHash && profile.PairingChallenge == challenge
            && profile.PairingExpiresAt > now, cancellationToken);
        if (record is null || !await CanReadAsync(record, cancellationToken))
        {
            return null;
        }

        var accessToken = CreateToken();
        record.AccessHash = Hash(accessToken);
        record.AccessExpiresAt = now.AddHours(8);
        ClearPairing(record);
        return await UpdateAsync(record, cancellationToken) ? accessToken : null;
    }

    public async Task<ApplicantProfile?> GetConnectedProfileAsync(string? token, CancellationToken cancellationToken)
    {
        if (!IsToken(token))
        {
            return null;
        }

        var now = time.GetUtcNow();
        var hash = Hash(token!);
        var record = await database.AutofillProfiles.AsNoTracking()
            .SingleOrDefaultAsync(profile => profile.AccessHash == hash && profile.AccessExpiresAt > now,
                cancellationToken);
        return record is not null && await CanReadAsync(record, cancellationToken) ? Read(record) : null;
    }

    public async Task RevokeAsync(string ownerId, CancellationToken cancellationToken)
    {
        if (database.Database.IsRelational())
        {
            await database.AutofillProfiles.Where(profile => profile.OwnerId == ownerId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(profile => profile.PairingHash, (string?)null)
                    .SetProperty(profile => profile.PairingChallenge, (string?)null)
                    .SetProperty(profile => profile.PairingExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(profile => profile.AccessHash, (string?)null)
                    .SetProperty(profile => profile.AccessExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(profile => profile.Version, Guid.NewGuid()), cancellationToken);
            return;
        }

        var record = await database.AutofillProfiles.FindAsync([ownerId], cancellationToken);
        if (record is not null)
        {
            ClearPairing(record);
            record.AccessHash = null;
            record.AccessExpiresAt = null;
            await UpdateAsync(record, cancellationToken);
        }
    }

    private async Task<bool> CanReadAsync(AutofillProfile record, CancellationToken cancellationToken)
    {
        var user = await database.Users.AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == record.OwnerId, cancellationToken);
        return user is not null && CanConnect(user) && user.SecurityStamp == record.SecurityStamp;
    }

    private bool CanConnect(ApplicationUser user) =>
        user.EmailConfirmed && !(user.LockoutEnd > time.GetUtcNow());

    private ApplicantProfile? Read(AutofillProfile record) =>
        JsonSerializer.Deserialize<ApplicantProfile>(protector.Unprotect(record.ProtectedProfile), JsonOptions);

    private async Task<bool> UpdateAsync(AutofillProfile record, CancellationToken cancellationToken)
    {
        record.Version = Guid.NewGuid();
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private static void ClearPairing(AutofillProfile record)
    {
        record.PairingHash = null;
        record.PairingChallenge = null;
        record.PairingExpiresAt = null;
    }

    private static string CreateToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static bool IsToken(string? value) => value?.Length == 43
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
