using System.Security.Cryptography;
using System.Text;
using SpenceAI.Application.Common.Interfaces;

namespace SpenceAI.Infrastructure.Services;

public class EncryptionService : IEncryptionService
{
    private const string FallbackPassphrase = "SpenceAI-LocalSettings-Key-v1";
    private const int IvSize = 16;

    private readonly byte[] _key;

    public EncryptionService()
    {
        _key = DeriveKey(GetMachineSecret());
    }

    public string Encrypt(string plainText)
    {
        ArgumentException.ThrowIfNullOrEmpty(plainText);

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[IvSize + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, IvSize);
        Buffer.BlockCopy(cipherBytes, 0, result, IvSize, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        ArgumentException.ThrowIfNullOrEmpty(cipherText);

        var fullCipher = Convert.FromBase64String(cipherText);
        if (fullCipher.Length <= IvSize)
        {
            throw new CryptographicException("Invalid ciphertext.");
        }

        using var aes = Aes.Create();
        aes.Key = _key;

        var iv = new byte[IvSize];
        var cipher = new byte[fullCipher.Length - IvSize];
        Buffer.BlockCopy(fullCipher, 0, iv, 0, IvSize);
        Buffer.BlockCopy(fullCipher, IvSize, cipher, 0, cipher.Length);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }

    private static string GetMachineSecret()
    {
        var machineName = Environment.MachineName;
        var userName = Environment.UserName;
        var combined = $"{machineName}:{userName}";

        return string.IsNullOrWhiteSpace(combined) || combined == ":"
            ? FallbackPassphrase
            : combined;
    }

    private static byte[] DeriveKey(string secret)
    {
        var salt = Encoding.UTF8.GetBytes(FallbackPassphrase);
        return Rfc2898DeriveBytes.Pbkdf2(
            secret,
            salt,
            iterations: 100_000,
            HashAlgorithmName.SHA256,
            32);
    }
}
