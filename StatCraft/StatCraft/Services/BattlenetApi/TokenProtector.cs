using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace StatCraft.Services.BattlenetApi
{
    public class TokenProtector
    {
        private readonly string _keyFilePath;
        private byte[]? _key;

        public TokenProtector(string keyFilePath)
        {
            _keyFilePath = keyFilePath;
        }

        public void Initialize()
        {
            string? dir = Path.GetDirectoryName(_keyFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(_keyFilePath))
            {
                _key = File.ReadAllBytes(_keyFilePath);
                return;
            }

            _key = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(_keyFilePath, _key);
        }

        public byte[] Encrypt(string plaintext)
        {
            using Aes aes = Aes.Create();
            aes.Key = _key!;
            aes.GenerateIV();

            using ICryptoTransform encryptor = aes.CreateEncryptor();
            byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            byte[] ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

            byte[] result = new byte[aes.IV.Length + ciphertext.Length];
            aes.IV.CopyTo(result, 0);
            ciphertext.CopyTo(result, aes.IV.Length);
            return result;
        }

        public string Decrypt(byte[] ciphertext)
        {
            using Aes aes = Aes.Create();
            aes.Key = _key!;

            byte[] iv = new byte[aes.IV.Length];
            byte[] encrypted = new byte[ciphertext.Length - iv.Length];
            System.Array.Copy(ciphertext, 0, iv, 0, iv.Length);
            System.Array.Copy(ciphertext, iv.Length, encrypted, 0, encrypted.Length);
            aes.IV = iv;

            using ICryptoTransform decryptor = aes.CreateDecryptor();
            byte[] plaintextBytes = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
    }
}
