using System.Security.Cryptography;
using System.Text;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Services;

public class EncryptionService : IEncryptionService
{
    private readonly byte[] _key;
    private readonly byte[] _iv;

    public EncryptionService(IConfiguration configuration)
    {
        var encryptionKey = configuration["Encryption:Key"];
        var encryptionIV = configuration["Encryption:IV"];

        if (string.IsNullOrEmpty(encryptionKey) || string.IsNullOrEmpty(encryptionIV))
        {
            throw new InvalidOperationException("Encryption configuration is missing. Please check appsettings.json");
        }

        _key = Convert.FromBase64String(encryptionKey);
        _iv = Convert.FromBase64String(encryptionIV);

        if (_key.Length != 32) // AES-256 requires 32 bytes
        {
            throw new InvalidOperationException("Encryption key must be 32 bytes (256 bits) for AES-256");
        }

        if (_iv.Length != 16) // AES requires 16 bytes IV
        {
            throw new InvalidOperationException("Encryption IV must be 16 bytes (128 bits)");
        }
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            throw new ArgumentException("Plain text cannot be null or empty");
        }

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        return Convert.ToBase64String(cipherBytes);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            throw new ArgumentException("Cipher text cannot be null or empty");
        }

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var cipherBytes = Convert.FromBase64String(cipherText);
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
