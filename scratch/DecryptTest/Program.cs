using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DecryptTest
{
    class Program
    {
        static void Main(string[] args)
        {
            string keyB64 = "7xLQ/syxeVOQNNBfHrVj7Slw71OepMdPT13AVj5GDp4=";
            string ivB64 = "wsx0yZ9zSARVA6PrifH2tg==";
            string cipherTextB64 = "lTuiRnPrGsjtmjba/XNGWueBKzQ87u27R6x7dIEB2lKJqlrTBVTKlBZCnMD4rBdwNFzYe/35i1d4fV2XCrwK50iqH58VydO+5MQoQmf09Pc=";

            try
            {
                byte[] key = Convert.FromBase64String(keyB64);
                byte[] iv = Convert.FromBase64String(ivB64);
                byte[] cipherBytes = Convert.FromBase64String(cipherTextB64);

                using var aes = Aes.Create();
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var decryptor = aes.CreateDecryptor();
                byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                string plainText = Encoding.UTF8.GetString(plainBytes);

                Console.WriteLine($"Decrypted: {plainText}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Decryption failed: {ex.Message}");
            }
        }
    }
}
