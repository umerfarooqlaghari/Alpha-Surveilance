using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using BCrypt.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace alpha_surveilance_bff.Services
{
    public class AuthService(
        IConfiguration config,
        IAmazonSimpleEmailService sesClient,
        IMemoryCache cache,
        ILogger<AuthService> logger)
    {
        private const string OtpCachePrefix = "Auth_Otp_";

        public bool ValidateCredentials(string email, string password)
        {
            var adminEmail = config["Admin:Email"];
            var passwordHash = config["Admin:PasswordHash"];

            if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(passwordHash))
            {
                logger.LogError("Admin credentials not configured.");
                return false;
            }

            if (!email.Equals(adminEmail, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Invalid email attempt: {Email}", email);
                return false;
            }

            // Verify Password using BCrypt
            if (!BCrypt.Net.BCrypt.Verify(password, passwordHash))
            {
                logger.LogWarning("Invalid password for admin.");
                return false;
            }

            return true;
        }

        public async Task<string> GenerateAndSendOtpAsync(string email)
        {
            // 1. Generate 6-digit code
            var otp = Random.Shared.Next(100000, 999999).ToString();

            // 2. Store in Memory Cache (5 minutes expiry)
            cache.Set(OtpCachePrefix + email, otp, TimeSpan.FromMinutes(5));

            // 3. Send via SES
            try
            {
                var sendRequest = new SendEmailRequest
                {
                    Source = config["Admin:Email"], // Sending from the admin email itself (must be verified in SES)
                    Destination = new Destination { ToAddresses = new List<string> { email } },
                    Message = new Message
                    {
                        Subject = new Content("Alpha Surveillance: Admin Login Code"),
                        Body = new Body
                        {
                            Html = new Content
                            {
                                Data = $"<h3>Your Login Code:</h3><h1 style='color:red'>{otp}</h1><p>Valid for 5 minutes.</p>"
                            }
                        }
                    }
                };

                // DEV LOG: Always show OTP in console
                logger.LogInformation("=================================================");
                logger.LogInformation("🔐 DEV OTP Code: {Otp}", otp);
                logger.LogInformation("=================================================");

                await sesClient.SendEmailAsync(sendRequest);
                logger.LogInformation("OTP sent to {Email}", email);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send SES email.");
                // For dev/demo, we might output the code to logs if email fails
                logger.LogWarning("Dev Backup: OTP is {Otp}", otp);
            }

            return "OTP Sent";
        }

        public bool VerifyOtp(string email, string otp)
        {
            if (cache.TryGetValue(OtpCachePrefix + email, out string? storedOtp))
            {
                if (storedOtp == otp)
                {
                    cache.Remove(OtpCachePrefix + email); // Consume OTP
                    return true;
                }
            }
            return false;
        }

        public string GenerateJwtToken(string email)
        {
            var key = Encoding.ASCII.GetBytes(config["Jwt:SecretKey"] ?? throw new InvalidOperationException("JWT Secret missing"));
            var issuer = config["Jwt:Issuer"];
            var audience = config["Jwt:Audience"];
            var expiry = double.Parse(config["Jwt:ExpiryMinutes"] ?? "60");

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, email),
                    new Claim(ClaimTypes.Role, "Admin")
                }),
                Expires = DateTime.UtcNow.AddMinutes(expiry),
                Issuer = issuer,
                Audience = audience,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        // Helper for dev only - to be removed
        public string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password, 11);
        }
    }
}
