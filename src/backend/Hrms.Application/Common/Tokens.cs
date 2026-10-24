using System.Security.Cryptography;
using Hrms.Application.Auditing;

namespace Hrms.Application.Common;

public static class SecureTokens
{
    public static string Create(int bytes = 32) => Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));

    public static string Hash(string token) => AuditHashing.Sha256(token);

    public static string CreatePassword()
    {
        // Database role passwords: 32 random bytes, URL-safe, no characters needing escaping in connection strings.
        return Create(32);
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static class Guard
{
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
