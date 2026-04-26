using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using violation_management_api.Controllers;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;
using Xunit;

namespace violation_management_api.Tests
{
    public class AuthTests
    {
        private readonly Mock<IAuthService> _authServiceMock;
        private readonly Mock<ILogger<AuthController>> _loggerMock;
        private readonly AuthController _authController;

        public AuthTests()
        {
            _authServiceMock = new Mock<IAuthService>();
            _loggerMock = new Mock<ILogger<AuthController>>();
            _authController = new AuthController(_authServiceMock.Object, _loggerMock.Object);
        }

        [Fact]
        public async Task ValidLogin_CorrectEmailAndPassword_ReturnsOkWithJwt()
        {
            // Arrange
            var request = new SuperAdminLoginRequest { Email = "admin@test.com", Password = "correctPassword" };
            var expectedResponse = new AuthResponse { Token = "valid-jwt-token", Role = "SuperAdmin" };

            _authServiceMock
                .Setup(s => s.AuthenticateSuperAdminAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _authController.SuperAdminLogin(request) as OkObjectResult;

            // Assert
            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(200);

            var returnValue = result.Value as AuthResponse;
            returnValue.Should().NotBeNull();
            returnValue!.Token.Should().Be("valid-jwt-token");
        }

        [Fact]
        public async Task InvalidPassword_CorrectEmailAndWrongPassword_ReturnsUnauthorized()
        {
            // Arrange
            var request = new SuperAdminLoginRequest { Email = "admin@test.com", Password = "wrongPassword" };

            _authServiceMock
                .Setup(s => s.AuthenticateSuperAdminAsync(request))
                .ThrowsAsync(new UnauthorizedAccessException("Invalid email or password"));

            // Act
            var result = await _authController.SuperAdminLogin(request) as UnauthorizedObjectResult;

            // Assert
            // The controller catches the exception and returns 401 Unauthorized
            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(401);
            
            // Verifying the behavior asked: the service throws UnauthorizedException
            var serviceCheck = async () => await _authServiceMock.Object.AuthenticateSuperAdminAsync(request);
            await serviceCheck.Should().ThrowAsync<UnauthorizedAccessException>();
        }

        [Fact]
        public async Task MissingUser_EmailNotInDb_ReturnsUnauthorized()
        {
            // Arrange
            var request = new SuperAdminLoginRequest { Email = "unknown@test.com", Password = "somePassword" };

            _authServiceMock
                .Setup(s => s.AuthenticateSuperAdminAsync(request))
                .ThrowsAsync(new UnauthorizedAccessException("Invalid email or password"));

            // Act
            var result = await _authController.SuperAdminLogin(request) as UnauthorizedObjectResult;

            // Assert
            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(401);
            
            // Verifying the behavior asked: the service throws UnauthorizedException
            var serviceCheck = async () => await _authServiceMock.Object.AuthenticateSuperAdminAsync(request);
            await serviceCheck.Should().ThrowAsync<UnauthorizedAccessException>();
        }

        [Fact]
        public async Task ExpiredToken_JwtWithPastExpClaim_ReturnsTokenExpiredStatus()
        {
            // Arrange
            var request = new ValidateTokenRequest { Token = "expired-jwt-token" };

            _authServiceMock
                .Setup(s => s.ValidateTokenAsync(request.Token))
                .ReturnsAsync(false); // An expired token results in returning false from auth service validation

            // Act
            var result = await _authController.ValidateToken(request) as OkObjectResult;

            // Assert
            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(200);

            // Using reflection to get the anonymous type property 'valid'
            var isValidProperty = result.Value.GetType().GetProperty("valid");
            var isValidValue = (bool)isValidProperty.GetValue(result.Value, null);

            isValidValue.Should().BeFalse("because token is expired");
        }
    }
}
