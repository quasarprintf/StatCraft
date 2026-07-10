using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace StatCraft.Services
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
            var dir = Path.GetDirectoryName(_keyFilePath);
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
            using var aes = Aes.Create();
            aes.Key = _key!;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            var ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

            var result = new byte[aes.IV.Length + ciphertext.Length];
            aes.IV.CopyTo(result, 0);
            ciphertext.CopyTo(result, aes.IV.Length);
            return result;
        }

        public string Decrypt(byte[] ciphertext)
        {
            using var aes = Aes.Create();
            aes.Key = _key!;

            var iv = new byte[aes.IV.Length];
            var encrypted = new byte[ciphertext.Length - iv.Length];
            System.Array.Copy(ciphertext, 0, iv, 0, iv.Length);
            System.Array.Copy(ciphertext, iv.Length, encrypted, 0, encrypted.Length);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var plaintextBytes = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
    }
}
