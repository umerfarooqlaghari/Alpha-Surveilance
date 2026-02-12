using System;
using System.Security.Cryptography;

// Generate AES-256 Key (32 bytes) and IV (16 bytes)
var key = new byte[32]; // 256 bits
var iv = new byte[16];  // 128 bits

using (var rng = RandomNumberGenerator.Create())
{
    rng.GetBytes(key);
    rng.GetBytes(iv);
}

Console.WriteLine("=== AES-256 Encryption Keys ===");
Console.WriteLine($"Key (Base64): {Convert.ToBase64String(key)}");
Console.WriteLine($"IV (Base64): {Convert.ToBase64String(iv)}");
Console.WriteLine("\nAdd these to appsettings.json under 'Encryption' section");
