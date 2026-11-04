using ClosedXML.Excel;
using Hrms.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using OtpNet;

namespace Hrms.Infrastructure.Security;

public sealed class TotpService : ITotpService
{
    public string GenerateSecret() => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

    public string BuildProvisioningUri(string issuer, string account, string secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&digits=6&period=30";

    public bool Verify(string secret, string code) =>
        code is { Length: 6 } && new Totp(Base32Encoding.ToBytes(secret)).VerifyTotp(code, out _, new VerificationWindow(1, 1));
}

public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    public string Protect(string purpose, string plaintext) => provider.CreateProtector(purpose).Protect(plaintext);

    public string? Unprotect(string purpose, string protectedValue)
    {
        try
        {
            return provider.CreateProtector(purpose).Unprotect(protectedValue);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    public string ProtectTimed(string purpose, string plaintext, TimeSpan lifetime) =>
        provider.CreateProtector(purpose).ToTimeLimitedDataProtector().Protect(plaintext, lifetime);

    public string? UnprotectTimed(string purpose, string protectedValue)
    {
        try
        {
            return provider.CreateProtector(purpose).ToTimeLimitedDataProtector().Unprotect(protectedValue);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}

public sealed class ClosedXmlExcelWriter : IExcelWriter
{
    public byte[] Write(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(sheetName);
        for (var c = 0; c < headers.Count; c++)
        {
            ws.Cell(1, c + 1).Value = headers[c];
        }

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Count; c++)
            {
                // Formula-injection guard: values starting with =,+,-,@ are written as text with a leading apostrophe.
                var v = row[c] ?? string.Empty;
                ws.Cell(r, c + 1).Value = v.Length > 0 && "=+-@\t\r".Contains(v[0], StringComparison.Ordinal) ? "'" + v : v;
            }

            r++;
        }

        ws.Row(1).Style.Font.Bold = true;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
