using Microsoft.AspNetCore.WebUtilities;
using System.Security.Cryptography;
using System.Text;

namespace JobTracker.Web.Identity;

public static class InvitationCode
{
    private const string Prefix = "jit_";

    public static string Create() =>
        Prefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim()));
        return Convert.ToHexString(bytes);
    }
}
