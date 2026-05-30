using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using AlphaSurveilance.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using violation_management_api.Controllers;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services;
using violation_management_api.Services.Interfaces;
using Xunit;

namespace violation_management_api.Tests
{
    /// <summary>
    /// Tests for AuthController, JwtService, and AuthService.
    ///
    /// Sections
    /// ────────────────────────────────────────────────────────────────────
    ///  A. Controller routing (mock IAuthService) — HTTP status codes
    ///  B. JwtService — claim content, token structure, ValidateToken
    ///  C. AuthService — real InMemory DB: password check, inactive guard, tenant guard
    /// </summary>
    public class AuthTests
    {
        // ── Section A helpers ──────────────────────────────────────────────────
        private readonly Mock<IAuthService> _authServiceMock = new();
        private readonly Mock<ILogger<AuthController>> _loggerMock = new();
        private AuthController BuildController()
            => new(_authServiceMock.Object, _loggerMock.Object);

        // ════════════════════════════════════════════════════════════════════
        // A. Controller routing
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task ValidLogin_CorrectEmailAndPassword_ReturnsOkWithJwt()
        {
            var request = new SuperAdminLoginRequest { Email = "admin@test.com", Password = "correctPassword" };
            _authServiceMock.Setup(s => s.AuthenticateSuperAdminAsync(request))
                            .ReturnsAsync(new AuthResponse { Token = "valid-jwt-token", Role = "SuperAdmin" });

            var result = await BuildController().SuperAdminLogin(request) as OkObjectResult;

            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(200);
            (result.Value as AuthResponse)!.Token.Should().Be("valid-jwt-token");
        }

        [Fact]
        public async Task InvalidPassword_ReturnsUnauthorized_With401()
        {
            var request = new SuperAdminLoginRequest { Email = "admin@test.com", Password = "wrong" };
            _authServiceMock.Setup(s => s.AuthenticateSuperAdminAsync(request))
                            .ThrowsAsync(new UnauthorizedAccessException("Invalid email or password"));

            var result = await BuildController().SuperAdminLogin(request) as UnauthorizedObjectResult;

            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(401);
            var errorProp = result.Value!.GetType().GetProperty("error");
            (errorProp!.GetValue(result.Value) as string).Should().Contain("Invalid email or password");
        }

        [Fact]
        public async Task MissingUser_UnknownEmail_Returns401()
        {
            var request = new SuperAdminLoginRequest { Email = "nobody@test.com", Password = "pass" };
            _authServiceMock.Setup(s => s.AuthenticateSuperAdminAsync(request))
                            .ThrowsAsync(new UnauthorizedAccessException("Invalid email or password"));

            var result = await BuildController().SuperAdminLogin(request) as UnauthorizedObjectResult;

            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(401);
        }

        [Fact]
        public async Task ServiceThrowsGenericException_Returns500()
        {
            // An unexpected error (DB down, etc.) must return 500, not bubble up.
            var request = new SuperAdminLoginRequest { Email = "a@b.com", Password = "p" };
            _authServiceMock.Setup(s => s.AuthenticateSuperAdminAsync(request))
                            .ThrowsAsync(new Exception("connection refused"));

            var result = await BuildController().SuperAdminLogin(request) as ObjectResult;

            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(500);
        }

        [Fact]
        public async Task ExpiredToken_ValidateToken_Returns200WithValidFalse()
        {
            var request = new ValidateTokenRequest { Token = "expired-jwt-token" };
            _authServiceMock.Setup(s => s.ValidateTokenAsync(request.Token)).ReturnsAsync(false);

            var result = await BuildController().ValidateToken(request) as OkObjectResult;

            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(200);
            var isValidProp = result.Value!.GetType().GetProperty("valid");
            ((bool)isValidProp!.GetValue(result.Value)!).Should().BeFalse();
        }

        // ════════════════════════════════════════════════════════════════════
        // B. JwtService — real implementation, in-memory config
        // ════════════════════════════════════════════════════════════════════

        private static JwtService BuildJwtService(int expirationMinutes = 60)
        {
            var cfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:SecretKey"]         = "test-secret-key-must-be-at-least-32-chars-long!",
                    ["Jwt:Issuer"]            = "alpha-test-issuer",
                    ["Jwt:Audience"]          = "alpha-test-audience",
                    ["Jwt:ExpirationMinutes"] = expirationMinutes.ToString()
                })
                .Build();
            return new JwtService(cfg, Mock.Of<ILogger<JwtService>>());
        }

        private static JwtSecurityToken ParseToken(string raw)
            => new JwtSecurityTokenHandler().ReadJwtToken(raw);

        [Fact]
        public void JwtService_GenerateToken_ContainsSubClaim()
        {
            var userId = Guid.NewGuid();
            var token = BuildJwtService().GenerateToken(userId, "a@b.com", "SuperAdmin", null);
            ParseToken(token).Claims
                .Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == userId.ToString());
        }

        [Fact]
        public void JwtService_GenerateToken_ContainsEmailClaim()
        {
            var token = BuildJwtService().GenerateToken(Guid.NewGuid(), "jane@example.com", "SuperAdmin", null);
            ParseToken(token).Claims
                .Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "jane@example.com");
        }

        [Fact]
        public void JwtService_GenerateToken_ContainsRoleClaim()
        {
            var token = BuildJwtService().GenerateToken(Guid.NewGuid(), "a@b.com", "TenantAdmin", null);
            ParseToken(token).Claims
                .Should().Contain(c => c.Type == "role" && c.Value == "TenantAdmin",
                    "role claim must be 'role', not the long URI form");
        }

        [Fact]
        public void JwtService_GenerateToken_NoTenantIdClaim_WhenTenantIdIsNull()
        {
            var token = BuildJwtService().GenerateToken(Guid.NewGuid(), "a@b.com", "SuperAdmin", null);
            ParseToken(token).Claims
                .Should().NotContain(c => c.Type == "tenantId",
                    "SuperAdmin tokens must not contain a tenantId claim");
        }

        [Fact]
        public void JwtService_GenerateToken_ContainsTenantIdClaim_WhenTenantIdProvided()
        {
            var tenantId = Guid.NewGuid();
            var token = BuildJwtService().GenerateToken(Guid.NewGuid(), "a@b.com", "TenantAdmin", tenantId);
            ParseToken(token).Claims
                .Should().Contain(c => c.Type == "tenantId" && c.Value == tenantId.ToString());
        }

        [Fact]
        public void JwtService_GenerateToken_IsValidlySignedJwt()
        {
            // Token must be a 3-part (header.payload.signature) JWT string.
            var token = BuildJwtService().GenerateToken(Guid.NewGuid(), "a@b.com", "SuperAdmin", null);
            token.Split('.').Should().HaveCount(3, "a JWT must have exactly 3 dot-separated parts");
        }

        [Fact]
        public void JwtService_ValidateToken_ReturnsPrincipal_ForFreshToken()
        {
            var jwtSvc = BuildJwtService();
            var userId = Guid.NewGuid();
            var raw = jwtSvc.GenerateToken(userId, "a@b.com", "SuperAdmin", null);

            var principal = jwtSvc.ValidateToken(raw);

            principal.Should().NotBeNull();
            principal!.Claims.Should().Contain(c =>
                c.Type == JwtRegisteredClaimNames.Sub && c.Value == userId.ToString());
        }

        [Fact]
        public void JwtService_ValidateToken_ReturnsNull_ForGarbageString()
        {
            var principal = BuildJwtService().ValidateToken("not.a.jwt");
            principal.Should().BeNull("invalid tokens must return null, not throw");
        }

        [Fact]
        public void JwtService_ValidateToken_ReturnsNull_ForTokenSignedWithDifferentKey()
        {
            // Build token with key A, validate with service using key B.
            var cfgA = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:SecretKey"]         = "secret-key-AAAA-at-least-32-chars-long",
                    ["Jwt:Issuer"]            = "alpha-test-issuer",
                    ["Jwt:Audience"]          = "alpha-test-audience",
                    ["Jwt:ExpirationMinutes"] = "60"
                }).Build();
            var cfgB = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:SecretKey"]         = "secret-key-BBBB-at-least-32-chars-long",
                    ["Jwt:Issuer"]            = "alpha-test-issuer",
                    ["Jwt:Audience"]          = "alpha-test-audience",
                    ["Jwt:ExpirationMinutes"] = "60"
                }).Build();
            var svcA = new JwtService(cfgA, Mock.Of<ILogger<JwtService>>());
            var svcB = new JwtService(cfgB, Mock.Of<ILogger<JwtService>>());

            var token = svcA.GenerateToken(Guid.NewGuid(), "a@b.com", "SuperAdmin", null);
            var principal = svcB.ValidateToken(token);

            principal.Should().BeNull("a token signed with key A must be rejected by a validator using key B");
        }

        // ════════════════════════════════════════════════════════════════════
        // C. AuthService — real InMemory DB
        // ════════════════════════════════════════════════════════════════════

        private const string TestPassword = "Correct-Password-123!";

        private static AppViolationDbContext BuildDb()
            => new(new DbContextOptionsBuilder<AppViolationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        private static User SeedSuperAdmin(
            AppViolationDbContext db,
            string email = "sa@alpha.com",
            bool isActive = true,
            Guid? tenantId = null)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,           // null → SuperAdmin
                Email = email,
                FullName = "Test Admin",
                PhoneNumber = "+1234567890",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(TestPassword),
                IsActive = isActive,
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(user);
            db.SaveChanges();
            return user;
        }

        private static AuthService BuildAuthService(AppViolationDbContext db)
            => new(db, Mock.Of<IJwtService>(j =>
                    j.GenerateToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>()
                    ) == "mocked-token"),
                Mock.Of<ILogger<AuthService>>());

        [Fact]
        public async Task AuthService_ValidCredentials_ReturnsAuthResponse()
        {
            var db = BuildDb();
            SeedSuperAdmin(db);

            var svc = BuildAuthService(db);
            var response = await svc.AuthenticateSuperAdminAsync(
                new SuperAdminLoginRequest { Email = "sa@alpha.com", Password = TestPassword });

            response.Should().NotBeNull();
            response.Token.Should().Be("mocked-token");
            response.Role.Should().Be("SuperAdmin");
            response.User.Should().NotBeNull();
            response.User!.Email.Should().Be("sa@alpha.com");
        }

        [Fact]
        public async Task AuthService_WrongPassword_ThrowsUnauthorized()
        {
            var db = BuildDb();
            SeedSuperAdmin(db);

            var svc = BuildAuthService(db);
            await svc.Invoking(s => s.AuthenticateSuperAdminAsync(
                    new SuperAdminLoginRequest { Email = "sa@alpha.com", Password = "WRONG" }))
                .Should().ThrowAsync<UnauthorizedAccessException>();
        }

        [Fact]
        public async Task AuthService_InactiveUser_ThrowsUnauthorized_WithInactiveMessage()
        {
            var db = BuildDb();
            SeedSuperAdmin(db, isActive: false);

            var svc = BuildAuthService(db);
            await svc.Invoking(s => s.AuthenticateSuperAdminAsync(
                    new SuperAdminLoginRequest { Email = "sa@alpha.com", Password = TestPassword }))
                .Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("*inactive*");
        }

        [Fact]
        public async Task AuthService_UnknownEmail_ThrowsUnauthorized()
        {
            var db = BuildDb();
            SeedSuperAdmin(db); // seeded as sa@alpha.com

            var svc = BuildAuthService(db);
            await svc.Invoking(s => s.AuthenticateSuperAdminAsync(
                    new SuperAdminLoginRequest { Email = "nobody@example.com", Password = TestPassword }))
                .Should().ThrowAsync<UnauthorizedAccessException>();
        }

        [Fact]
        public async Task AuthService_TenantUserCallingSuper_ThrowsUnauthorized()
        {
            // A user that belongs to a tenant (TenantId != null) is NOT a SuperAdmin.
            // Calling the SuperAdmin login endpoint must reject them.
            var db = BuildDb();
            SeedSuperAdmin(db, tenantId: Guid.NewGuid()); // has a TenantId → not SuperAdmin

            var svc = BuildAuthService(db);
            await svc.Invoking(s => s.AuthenticateSuperAdminAsync(
                    new SuperAdminLoginRequest { Email = "sa@alpha.com", Password = TestPassword }))
                .Should().ThrowAsync<UnauthorizedAccessException>(
                    "a tenant-scoped user must not be able to log in via the SuperAdmin endpoint");
        }

        [Fact]
        public async Task AuthService_SuccessfulLogin_UpdatesLastLoginAt()
        {
            var db = BuildDb();
            var user = SeedSuperAdmin(db);
            user.LastLoginAt.Should().BeNull("should be null before first login");

            await BuildAuthService(db).AuthenticateSuperAdminAsync(
                new SuperAdminLoginRequest { Email = "sa@alpha.com", Password = TestPassword });

            db.Users.Single().LastLoginAt.Should().NotBeNull(
                "LastLoginAt must be stamped on every successful login — " +
                "removing this breaks the admin audit trail");
        }
    }
}
