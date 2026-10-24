using Hrms.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Hrms.Application.Identity;

public interface IPasswordPolicy
{
    IReadOnlyList<string> Validate(string password, string? email);
}

public sealed class PasswordPolicy(IOptions<IdentityOptions> options) : IPasswordPolicy
{
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password1234", "123456789012", "qwertyuiop12", "letmein12345", "welcome12345", "administrator", "passw0rd1234", "iloveyou1234",
    };

    public IReadOnlyList<string> Validate(string password, string? email)
    {
        var o = options.Value;
        var errors = new List<string>();
        if (string.IsNullOrEmpty(password) || password.Length < o.PasswordMinLength)
        {
            errors.Add($"Password must be at least {o.PasswordMinLength} characters.");
        }

        if (password.Length > 128)
        {
            errors.Add("Password must be at most 128 characters.");
        }

        if (o.RequireDigit && !password.Any(char.IsDigit))
        {
            errors.Add("Password must contain a digit.");
        }

        if (o.RequireLetter && !password.Any(char.IsLetter))
        {
            errors.Add("Password must contain a letter.");
        }

        if (Common.Contains(password))
        {
            errors.Add("Password is too common.");
        }

        var local = email?.Split('@')[0];
        if (!string.IsNullOrWhiteSpace(local) && local.Length >= 4 && password.Contains(local, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Password must not contain your email name.");
        }

        return errors;
    }
}
