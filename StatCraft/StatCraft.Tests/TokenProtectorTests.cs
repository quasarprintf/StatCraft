using StatCraft.Services;

namespace StatCraft.Tests;

public class TokenProtectorTests : IDisposable
{
    private readonly string _keyFilePath;

    public TokenProtectorTests()
    {
        _keyFilePath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".key");
    }

    [Fact]
    public void EncryptThenDecrypt_ReturnsOriginalPlaintext()
    {
        TokenProtector protector = new TokenProtector(_keyFilePath);
        protector.Initialize();

        byte[] ciphertext = protector.Encrypt("super-secret-token");
        string decrypted = protector.Decrypt(ciphertext);

        Assert.Equal("super-secret-token", decrypted);
    }

    [Fact]
    public void Encrypt_SamePlaintextTwice_ProducesDifferentCiphertext()
    {
        TokenProtector protector = new TokenProtector(_keyFilePath);
        protector.Initialize();

        byte[] first = protector.Encrypt("same-value");
        byte[] second = protector.Encrypt("same-value");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Initialize_CalledAgainFromNewInstance_ReusesPersistedKey()
    {
        TokenProtector protector1 = new TokenProtector(_keyFilePath);
        protector1.Initialize();
        byte[] ciphertext = protector1.Encrypt("token");

        TokenProtector protector2 = new TokenProtector(_keyFilePath);
        protector2.Initialize();
        string decrypted = protector2.Decrypt(ciphertext);

        Assert.Equal("token", decrypted);
    }

    public void Dispose()
    {
        if (File.Exists(_keyFilePath))
            File.Delete(_keyFilePath);
    }
}
