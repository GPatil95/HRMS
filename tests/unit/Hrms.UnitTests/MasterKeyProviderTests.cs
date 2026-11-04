using Hrms.Application.Abstractions;
using Hrms.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Hrms.UnitTests;

public class MasterKeyProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hrms-mk-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly string ValidKey = Convert.ToBase64String(RandomBytes());
    private static byte[] RandomBytes() => System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    private static PlatformMasterKeyProvider Provider(SecretStoreOptions o) =>
        new(Options.Create(o), logger: null);

    [Fact]
    public void Inline_key_resolves_with_stable_fingerprint()
    {
        var p = Provider(new SecretStoreOptions { MasterKey = ValidKey });
        var info = p.Key;
        info.Key.Length.ShouldBe(32);
        info.Source.ShouldBe("Secrets:MasterKey");
        info.Fingerprint.ShouldBe(PlatformMasterKeyProvider.Fingerprint(info.Key));
        info.Fingerprint.Length.ShouldBe(12);
    }

    [Fact]
    public void Both_sources_configured_fails_fast()
    {
        var file = Path.Combine(_dir, "master.key");
        var p = Provider(new SecretStoreOptions { MasterKey = ValidKey, MasterKeyFile = file });
        var ex = Should.Throw<MasterKeyException>(() => _ = p.Key);
        ex.Message.ShouldContain("exactly one authoritative master-key source");
    }

    [Fact]
    public void Configured_key_file_missing_fails_without_generating()
    {
        var file = Path.Combine(_dir, "master.key");
        var p = Provider(new SecretStoreOptions { MasterKeyFile = file });
        Should.Throw<MasterKeyException>(() => _ = p.Key);
        File.Exists(file).ShouldBeFalse(); // never silently generated
    }

    [Fact]
    public void Key_file_with_valid_key_resolves()
    {
        var file = Path.Combine(_dir, "master.key");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(file, ValidKey + "\n");

        var info = Provider(new SecretStoreOptions { MasterKeyFile = file }).Key;
        info.Source.ShouldBe(file);
        info.Key.ShouldBe(Convert.FromBase64String(ValidKey));
    }

    [Fact]
    public void Invalid_base64_fails_with_clear_error()
    {
        var p = Provider(new SecretStoreOptions { MasterKey = "not-base64!!" });
        Should.Throw<MasterKeyException>(() => _ = p.Key);
    }

    [Fact]
    public void Wrong_length_key_fails_with_clear_error()
    {
        var p = Provider(new SecretStoreOptions { MasterKey = Convert.ToBase64String(new byte[16]) });
        var ex = Should.Throw<MasterKeyException>(() => _ = p.Key);
        ex.Message.ShouldContain("32 bytes");
    }

    [Fact]
    public void Empty_inline_key_is_treated_as_unset()
    {
        var file = Path.Combine(_dir, "master.key");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(file, ValidKey);
        var info = Provider(new SecretStoreOptions { MasterKey = "   ", MasterKeyFile = file }).Key;
        info.Source.ShouldBe(file);
    }

    [Fact]
    public void Initialize_creates_key_once_and_never_overwrites()
    {
        var file = Path.Combine(_dir, "master.key");
        PlatformMasterKeyProvider.InitializeKeyFile(file).ShouldBeTrue();
        var original = File.ReadAllText(file);
        Convert.FromBase64String(original.Trim()).Length.ShouldBe(32);

        PlatformMasterKeyProvider.InitializeKeyFile(file).ShouldBeFalse();
        File.ReadAllText(file).ShouldBe(original); // second run leaves the key untouched
    }

    [Fact]
    public void Api_and_cli_share_the_same_provider_fingerprint()
    {
        var file = Path.Combine(_dir, "master.key");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(file, ValidKey);
        var a = Provider(new SecretStoreOptions { MasterKeyFile = file }).Key;
        var b = Provider(new SecretStoreOptions { MasterKeyFile = file }).Key;
        a.Fingerprint.ShouldBe(b.Fingerprint);
    }

    [Fact]
    public void ResolveKeyFileTarget_prefers_configured_file()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Secrets:MasterKeyFile", "/tmp/custom.key")])
            .Build();
        PlatformMasterKeyProvider.ResolveKeyFileTarget(cfg).ShouldBe("/tmp/custom.key");
    }

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
}
