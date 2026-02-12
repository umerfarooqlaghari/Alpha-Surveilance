using System;
using System.IO;
using BCrypt.Net;

class Program
{
    static void Main()
    {
        string password = "132VanDijk@!";
        string hash = BCrypt.Net.BCrypt.HashPassword(password, 11);
        File.WriteAllText("hash.txt", hash);
        Console.WriteLine("Done");
    }
}
